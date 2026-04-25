using ColossalFramework;
using ColossalFramework.UI;
using ICities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Breakdown
{
    public class BreakdownMod : IUserMod
    {
#if DEBUG
        public static bool DebugLog = true;
#else
        public static bool DebugLog = false;
#endif

        public string Name { get { return "Breakdown Revisited"; } }

        public string Description { get { return "Shows more route details when viewing routes."; } }

        public void OnEnabled()
        {
            Log.Info("Mod enabled.");
        }

        public void OnDisabled()
        {
            Log.Info("Mod disabled.");
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            Log.Info("OnSettingsUI called.");
            try
            {
                UIHelperBase group = helper.AddGroup("Breakdown");
                group.AddCheckbox("Debug logging", DebugLog, (bool value) => { DebugLog = value; });
                Log.Info("OnSettingsUI completed.");
            }
            catch (Exception ex)
            {
                Log.Info($"OnSettingsUI failed: {ex.Message}");
            }
        }
    }

    public class BreakdownThread : ThreadingExtensionBase
    {
        // TODO implement an 'accumulate' button to gather stats. (that's why pathCounts is here, not in FindRoutes).
        public Dictionary<string, Dictionary<string, PathDetails>> pathCounts = new Dictionary<string, Dictionary<string, PathDetails>>();
        public int lastRefreshFrame = 0;
        protected InstanceID lastInstance;
        //protected bool[] showRouteTypes;
        public FieldInfo mPathsInfo;
        protected Dictionary<string, UIBreakdownPanel> panels = new Dictionary<string, UIBreakdownPanel>();
        protected bool districtsNotSegments = true;

        private readonly Dictionary<string, Color32> _districtColors = new Dictionary<string, Color32>();
        private readonly Dictionary<string, float>   _districtHues   = new Dictionary<string, float>();
        private int _nextColorIndex = 0;

        private readonly Dictionary<ushort, string>  _nearestDistrictCache = new Dictionary<ushort, string>();
        private readonly Dictionary<string, Vector3> _districtPositions    = new Dictionary<string, Vector3>();
        private byte _cachedDistrictCount = 0;
        private int  _districtCheckFrame  = 0;

        private UIBreakdownAccordionPanel _accordionPanel;
        private volatile bool _findCityWideRoutesPending  = false;
        private volatile bool _pendingCityWideResultReady = false;
        private Action _pendingCityWideUIUpdate;
        private int _cityWideRefreshFrame = 0;
        private bool _wasPopupOpen = false;

        // Accumulated EMA store: canonical pair (pa < pb) → EmaEntry
        private readonly Dictionary<string, Dictionary<string, EmaEntry>> _accumulated =
            new Dictionary<string, Dictionary<string, EmaEntry>>();
        private bool   _showAverage    = false;
        private const double _alpha          = 0.10;
        private const double _pruneThreshold = 0.001; // prune pairs with EMA share < 0.1%

        // Route-type tooltip labels — read from traffic routes panel checkboxes at init time
        private string _labelPedestrian  = "Pedestrians";
        private string _labelCyclists    = "Cyclists";
        private string _labelPrivate     = "Private Vehicles";
        private string _labelTrucks      = "Trucks";
        private string _labelPublicTrans = "Public Transport";
        private string _labelServices    = "City Services";

        // Categorize a path by its owner InstanceID using the same 6 groups as PathVisualizer
        // (Pedestrians, Cyclists, Private, Trucks, Public Transport, City Services).
        // Mirrors the filter logic in PathVisualizer.SimulationStep / AddInstance.
        private static uint GetInstanceCategory(InstanceID id)
        {
            if (id.Vehicle != 0)
            {
                var info = VehicleManager.instance.m_vehicles.m_buffer[id.Vehicle].Info;
                if (info == null || info.m_class == null) return 2;
                switch (info.m_class.m_service)
                {
                    case ItemClass.Service.Residential:  return 2; // Private Vehicles
                    case ItemClass.Service.Industrial:
                    case ItemClass.Service.PlayerIndustry: return 3; // Trucks
                    case ItemClass.Service.PublicTransport: return 4; // Public Transport
                    case ItemClass.Service.Fishing:
                        if (info.m_vehicleAI is FishingBoatAI) return 4;
                        return 3;
                    default: return 5; // City Services
                }
            }
            if (id.CitizenInstance != 0)
            {
                var cm = CitizenManager.instance;
                uint citizenId = cm.m_instances.m_buffer[id.CitizenInstance].m_citizen;
                if (citizenId != 0)
                {
                    ushort vehicleId = cm.m_citizens.m_buffer[(int)citizenId].m_vehicle;
                    if (vehicleId != 0)
                    {
                        var vInfo = VehicleManager.instance.m_vehicles.m_buffer[vehicleId].Info;
                        if (vInfo != null && (uint)vInfo.m_vehicleType == 32) return 1; // Cyclists
                        return 2; // In non-bicycle vehicle (PathVisualizer skips these)
                    }
                }
                return 0; // Pedestrians (on foot)
            }
            return 2;
        }

        private volatile bool _findRoutesPending = false;
        private volatile bool _pendingResultReady = false;
        private Action _pendingUIUpdate;

        public BreakdownThread()
        {
            Log.Info("BreakdownThread created.");
            this.mPathsInfo = typeof(PathVisualizer).GetField("m_paths", BindingFlags.NonPublic | BindingFlags.Instance);
            if (this.mPathsInfo == null)
                Log.Info("Can't get m_paths from PathVisualizer.");
        }

        private Color32 GetDistrictColor(string name)
        {
            if (name == "Out of town")
                return new Color32(255, 255, 255, 255);
            if (name == "No district")
                return new Color32(180, 180, 180, 255);
            Color32 color;
            if (!_districtColors.TryGetValue(name, out color))
            {
                bool isNear = name.StartsWith("near ");
                string baseKey = isNear ? name.Substring(5) : name;
                float hue;
                if (!_districtHues.TryGetValue(baseKey, out hue))
                {
                    hue = (_nextColorIndex * 0.618033988f) % 1f;
                    _nextColorIndex++;
                    _districtHues[baseKey] = hue;
                }
                color = HsvToColor32(hue, isNear ? 0.35f : 0.65f, 0.95f);
                _districtColors[name] = color;
            }
            return color;
        }

        private string ResolveDistrictName(ushort segmentId)
        {
            var pos = segmentId.GetSegmentLocation();
            if (GameAreaManager.instance.PointOutOfArea(pos))
                return "Out of town";
            byte districtId = pos.GetDistrict();
            if (districtId != 0)
            {
                string distName = districtId.GetDistrictName();
                if (!_districtPositions.ContainsKey(distName))
                    _districtPositions[distName] = DistrictManager.instance.m_districts.m_buffer[districtId].m_nameLocation;
                return distName;
            }

            string cached;
            if (_nearestDistrictCache.TryGetValue(segmentId, out cached))
                return cached;

            var dm = DistrictManager.instance;
            string nearest = null;
            Vector3 nearestPos = Vector3.zero;
            float bestDist = float.MaxValue;
            for (int i = 1; i < 128; i++)
            {
                var district = dm.m_districts.m_buffer[i];
                if ((district.m_flags & District.Flags.Created) == 0) continue;
                float d = Vector3.Distance(pos, district.m_nameLocation);
                if (d < bestDist) { bestDist = d; nearest = dm.GetDistrictName((byte)i); nearestPos = district.m_nameLocation; }
            }
            string result = nearest != null ? "near " + nearest : "No district";
            _nearestDistrictCache[segmentId] = result;
            if (nearest != null && !_districtPositions.ContainsKey(nearest))
                _districtPositions[nearest] = nearestPos;
            return result;
        }

        private Vector3 GetDistrictPosition(string name)
        {
            if (name == "Out of town" || name == "No district")
                return new Vector3(float.MaxValue, 0, float.MaxValue);
            string baseKey = name.StartsWith("near ") ? name.Substring(5) : name;
            Vector3 pos;
            if (_districtPositions.TryGetValue(baseKey, out pos))
                return pos;
            var dm = DistrictManager.instance;
            for (int i = 1; i < 128; i++)
            {
                var district = dm.m_districts.m_buffer[i];
                if ((district.m_flags & District.Flags.Created) == 0) continue;
                if (dm.GetDistrictName((byte)i) == baseKey)
                {
                    _districtPositions[baseKey] = district.m_nameLocation;
                    return district.m_nameLocation;
                }
            }
            return new Vector3(float.MaxValue, 0, float.MaxValue);
        }

        private static Vector3 GetEntityPosition(InstanceID instance)
        {
            switch (instance.Type)
            {
                case InstanceType.Building:
                    return BuildingManager.instance.m_buildings.m_buffer[instance.Building].m_position;
                case InstanceType.District:
                    return DistrictManager.instance.m_districts.m_buffer[instance.District].m_nameLocation;
                case InstanceType.NetNode:
                    return NetManager.instance.m_nodes.m_buffer[instance.NetNode].m_position;
                case InstanceType.NetSegment:
                    return NetManager.instance.m_segments.m_buffer[instance.NetSegment].m_middlePosition;
                default:
                    return Vector3.zero;
            }
        }

        private static Color32 HsvToColor32(float h, float s, float v)
        {
            float sector = h * 6f;
            int hi = (int)sector % 6;
            float f = sector - (int)sector;
            float p = v * (1f - s);
            float q = v * (1f - f * s);
            float t = v * (1f - (1f - f) * s);
            float r, g, b;
            switch (hi)
            {
                case 0:  r = v; g = t; b = p; break;
                case 1:  r = q; g = v; b = p; break;
                case 2:  r = p; g = v; b = t; break;
                case 3:  r = p; g = q; b = v; break;
                case 4:  r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return new Color32((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), 255);
        }

        private bool _pathsVisibleLogged = false;

        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            var viz = Singleton<PathVisualizer>.instance; // TODO should we be checking "exists" instead of checking instance for null?
            if (viz == null || !viz.PathsVisible || this.mPathsInfo == null)
            {
                if (_pathsVisibleLogged)
                    Log.Debug($"PathsVisible=false, hiding {panels.Count} panels, pendingPending={_findRoutesPending}");
                _pathsVisibleLogged = false;
                if (_accordionPanel != null) _accordionPanel.isVisible = false;
                foreach (var panel in this.panels.Values)
                {
                    if (panel != null && panel.enabled)
                    {
                        panel.Hide();
                    }
                }
            }
            else
            {
                if (!_pathsVisibleLogged)
                {
                    _pathsVisibleLogged = true;
                    this.lastRefreshFrame = 0;
                    Log.Info($"PathsVisible=true, panels={panels.Count}, mPathsInfo={mPathsInfo != null}");
                }
                if (this.panels.Count == 0)
                {
                    this.InitUI();
                    // Sync toggle state for panels created after Average was already enabled
                    if (_showAverage)
                        foreach (var p in this.panels.Values)
                            if (p != null) p.UpdateAverageState(true);
                    if (_accumulated.Count > 0)
                        foreach (var p in this.panels.Values)
                            if (p != null) p.SetAverageAvailable(true);
                }
                //var flags = new[] { viz.showCityServiceVehicles, viz.showCyclists, viz.showPedestrians, viz.showPrivateVehicles, viz.showPublicTransport, viz.showTrucks };
                //if (showRouteTypes == null || !Enumerable.SequenceEqual(showRouteTypes, flags))
                //{
                //    showRouteTypes = flags;
                //    lastRefreshFrame = 0;
                //}
                var paths = this.mPathsInfo.GetValue(viz) as Dictionary<InstanceID, PathVisualizer.Path>;
                if (paths == null) return;

                if (_pendingResultReady)
                {
                    Log.Debug("Picking up pending result from sim thread");
                    _pendingResultReady = false;
                    if (_pendingUIUpdate != null) _pendingUIUpdate();
                    _findRoutesPending = false;
                }

                if (_districtCheckFrame++ % 120 == 0 && !_findRoutesPending)
                {
                    byte count = 0;
                    var dmBuf = DistrictManager.instance.m_districts.m_buffer;
                    for (int i = 1; i < 128; i++)
                        if ((dmBuf[i].m_flags & District.Flags.Created) != 0) count++;
                    if (count != _cachedDistrictCount)
                    {
                        _cachedDistrictCount = count;
                        _nearestDistrictCache.Clear();
                        _districtPositions.Clear();
                    }
                }

                var instance = InstanceManager.instance.GetSelectedInstance();

                if (_cityWideRefreshFrame % 120 == 0)
                    Log.Debug($"[status] mode={InfoManager.instance?.CurrentMode} instance.IsEmpty={instance.IsEmpty} accordionNull={_accordionPanel == null} cityWidePending={_findCityWideRoutesPending} pathsCount={paths?.Count}");

                if (instance != this.lastInstance)
                {
                    this.lastInstance = instance;
                    //UnityEngine.Debug.Log($"new instance on {lastRefreshFrame}.");
                    foreach (var panel in this.panels.Values)
                    {
                        panel.SetTopTen(new string[0], new string[0], new Color32[0], new string[0], new Color32[0], new string[0], new string[0], new bool[0], this.districtsNotSegments);
                    }
                    this.lastRefreshFrame = 0;
                }
                if (this.lastRefreshFrame++ % 60 == 0 && !_findRoutesPending)
                {
                    var capturedInstance = instance;
                    var capturedPaths    = paths;
                    var capturedAvg      = _showAverage;
                    _findRoutesPending = true;
                    Log.Debug($"Queuing AddAction, frame={lastRefreshFrame}, showAverage={capturedAvg}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");
                    SimulationManager.instance.AddAction(() => FindRoutesOnSimThread(capturedPaths, capturedInstance, capturedAvg));
                }

                bool inTrafficRoutesMode = InfoManager.instance != null &&
                    InfoManager.instance.CurrentMode == InfoManager.InfoMode.TrafficRoutes;
                bool noPopupOpen = true;
                foreach (var p in this.panels.Values)
                    if (p != null && p.parent != null && p.parent.isVisible) { noPopupOpen = false; break; }
                if (inTrafficRoutesMode && noPopupOpen)
                {
                    if (_accordionPanel == null) InitAccordionUI();
                    if (_wasPopupOpen)
                    {
                        // Popup just closed — discard any stale scan and force an immediate refresh
                        _findCityWideRoutesPending  = false;
                        _pendingCityWideResultReady = false;
                        _cityWideRefreshFrame = 0;
                        if (_accordionPanel != null) _accordionPanel.ShowLoading();
                    }
                    if (_pendingCityWideResultReady)
                    {
                        Log.Debug("Picking up city-wide pending result");
                        _pendingCityWideResultReady = false;
                        if (_pendingCityWideUIUpdate != null) _pendingCityWideUIUpdate();
                        _findCityWideRoutesPending = false;
                    }
                    if (_cityWideRefreshFrame++ % 300 == 0 && !_findCityWideRoutesPending)
                    {
                        var capturedPaths2 = paths;
                        var capturedAvg2   = _showAverage;
                        _findCityWideRoutesPending = true;
                        Log.Debug($"Queuing city-wide AddAction, showAverage={capturedAvg2}");
                        if (_accordionPanel != null) _accordionPanel.ShowLoading();
                        SimulationManager.instance.AddAction(() => FindCityWideRoutesOnSimThread(capturedPaths2, capturedAvg2));
                    }
                }
                else if (_accordionPanel != null)
                {
                    _accordionPanel.isVisible = false;
                }
                _wasPopupOpen = !noPopupOpen;
            }
            base.OnUpdate(realTimeDelta, simulationTimeDelta);
        }

        public override void OnReleased()
        {
            foreach (var panel in this.panels.Values)
            {
                if (panel != null)
                {
                    UnityEngine.Object.Destroy(panel);
                }
            }
            this.panels.Clear();
            if (_accordionPanel != null) { UnityEngine.Object.Destroy(_accordionPanel); _accordionPanel = null; }
            base.OnReleased();
        }

        protected void InitUI()
        {
            // TODO use reflection to find all WorldInfoPanel implementations.
            var WorldInfoPanelTypes = new[]
            {
                //typeof(AnimalWorldInfoPanel),
                typeof(CampusWorldInfoPanel),
                typeof(CitizenVehicleWorldInfoPanel),
                typeof(CitizenWorldInfoPanel),
                typeof(CityServiceVehicleWorldInfoPanel),
                typeof(CityServiceWorldInfoPanel),
                typeof(DistrictWorldInfoPanel),
                //typeof(EventBuildingWorldInfoPanel),
                //typeof(HumanWorldInfoPanel),
                typeof(IndustryWorldInfoPanel),
                //typeof(LivingCreatureWorldInfoPanel),
                //typeof(MeteorWorldInfoPanel),
                typeof(ParkWorldInfoPanel),
                typeof(PublicTransportVehicleWorldInfoPanel),
                //typeof(PublicTransportWorldInfoPanel),
                typeof(RoadWorldInfoPanel),
                //typeof(ServicePersonWorldInfoPanel),
                typeof(ShelterWorldInfoPanel),
                typeof(TouristVehicleWorldInfoPanel),
                typeof(UniqueFactoryWorldInfoPanel),
                //typeof(VehicleWorldInfoPanel),
                typeof(WarehouseWorldInfoPanel),
                typeof(ZonedBuildingWorldInfoPanel),
            };
            Log.Debug($"InitUI starting, {WorldInfoPanelTypes.Length} panel types to attach.");
            foreach (var worldItem in WorldInfoPanelTypes)
            {
                try
                {
                    var roadInfoObj = GameObject.Find($"(Library) {worldItem.Name}");
                    if (roadInfoObj == null) continue;
                    WorldInfoPanel wip = roadInfoObj.GetComponent<WorldInfoPanel>();
                    if (wip == null) continue;
                    var bp = wip.component.AddUIComponent(typeof(UIBreakdownPanel)) as UIBreakdownPanel;
                    bp.OnModeToggled    = this.ToggleDistrictsRoads;
                    bp.OnAverageToggled = this.ToggleAverageMode;
                    this.panels[worldItem.Name] = bp;
                    Log.Debug($"attached to {worldItem}.");
                }
                catch (Exception ex)
                {
                    Log.Info($"failed to attach to {worldItem}: {ex.Message}");
                }
            }
            Log.Debug($"InitUI done, {panels.Count} panels attached.");
            InitRouteTypeLabels();
        }

        private void InitRouteTypeLabels()
        {
            var obj = GameObject.Find("(Library) TrafficRoutesInfoViewPanel");
            if (obj == null) { Log.Debug("InitRouteTypeLabels: panel not found"); return; }
            foreach (var cb in obj.GetComponentsInChildren<UICheckBox>(true))
            {
                string text = cb.text;
                if (string.IsNullOrEmpty(text)) continue;
                // Actual names from TrafficRoutesInfoViewPanel.InitCheckBoxes IL
                switch (cb.name)
                {
                    case "CheckboxPedestrians":        _labelPedestrian  = text; break;
                    case "CheckboxCyclists":           _labelCyclists    = text; break;
                    case "CheckboxPirateVehicles":     _labelPrivate     = text; break; // "Pirate" is a typo in the original game code
                    case "CheckboxTrucks":             _labelTrucks      = text; break;
                    case "CheckboxPublicTransport":    _labelPublicTrans = text; break;
                    case "CheckboxCityServiceVehicles": _labelServices   = text; break;
                }
            }
            Log.Debug($"Route labels: {_labelPedestrian} | {_labelCyclists} | {_labelPrivate} | {_labelTrucks} | {_labelPublicTrans} | {_labelServices}");
        }

        private void InitAccordionUI()
        {
            Log.Info("InitAccordionUI starting");
            try
            {
                var obj = GameObject.Find("(Library) TrafficRoutesInfoViewPanel");
                if (obj == null) { Log.Info("TrafficRoutesInfoViewPanel GO not found"); return; }
                var comp = obj.GetComponent<TrafficRoutesInfoViewPanel>();
                if (comp == null) { Log.Info("No TrafficRoutesInfoViewPanel component"); return; }
                var parent = comp.component;
                _accordionPanel = parent.AddUIComponent(typeof(UIBreakdownAccordionPanel)) as UIBreakdownAccordionPanel;
                if (_accordionPanel == null) { Log.Info("Accordion AddUIComponent returned null"); return; }
                _accordionPanel.OnAverageToggled = this.ToggleAverageMode;
                _accordionPanel.relativePosition = new Vector3(0, parent.height);
                parent.eventSizeChanged += (c, v) =>
                {
                    if (_accordionPanel != null) _accordionPanel.relativePosition = new Vector3(0, parent.height);
                };
                _cityWideRefreshFrame = 299; // let Start() run one frame before first ShowLoading call
                Log.Info("InitAccordionUI done");
            }
            catch (Exception ex)
            {
                Log.Info($"InitAccordionUI failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void FindRoutesOnSimThread(Dictionary<InstanceID, PathVisualizer.Path> pathDict, InstanceID instance, bool showAverage)
        {
            try
            {
                FindRoutesOnSimThreadInner(pathDict, instance, showAverage);
            }
            catch (Exception ex)
            {
                Log.Info($"FindRoutesOnSimThread unhandled exception: {ex.Message}\n{ex.StackTrace}");
                _findRoutesPending = false;
            }
        }

        private void FindRoutesOnSimThreadInner(Dictionary<InstanceID, PathVisualizer.Path> pathDict, InstanceID instance, bool showAverage)
        {
            Log.Debug($"FindRoutesOnSimThread start, showAverage={showAverage}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");
            if (pathDict == null) { _findRoutesPending = false; return; }

            var pathBuffer = PathManager.instance?.m_pathUnits?.m_buffer;
            if (pathBuffer == null) { _findRoutesPending = false; return; }

            int bufferSize = (int)PathManager.instance.m_pathUnits.m_size;

            if (showAverage)
            {
                // Average mode: read from accumulated EMA, skip live scan entirely
                string selDist = GetSelectedDistrict(instance);
                BuildPopupEmaDisplay(instance, selDist);
                return;
            }

            this.pathCounts.Clear();
            var sw = new Stopwatch();
            sw.Start();

            var tails = pathBuffer.GetPathTails(bufferSize);

            // Map each path's head unit index → the InstanceID that owns it
            // so FollowRoutes can classify by vehicle service / citizen bike status.
            var headToInstance = new Dictionary<uint, InstanceID>();
            try
            {
                lock (pathDict)
                {
                    foreach (var kv in pathDict)
                    {
                        uint head = GetHead(kv.Value.m_pathUnit, tails);
                        if (head > 0 && !headToInstance.ContainsKey(head))
                            headToInstance[head] = kv.Key;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                _findRoutesPending = false;
                return;
            }

            Log.Debug($"FindRoutes tails={tails.Count} heads={headToInstance.Count} pathDict={pathDict.Count} elapsed={sw.ElapsedMilliseconds}ms");

            this.FollowRoutes(pathBuffer, bufferSize, headToInstance, tails, this.districtsNotSegments);

            sw.Reset();
            sw.Start();
            bool showCount = instance.Type != InstanceType.Vehicle && instance.Type != InstanceType.Citizen;
            string selectedDistrict = GetSelectedDistrict(instance);
            Vector3 entityPos = GetEntityPosition(instance);

            bool sortByCount = instance.Type == InstanceType.NetSegment || instance.Type == InstanceType.NetNode;

            PathCount[] ranked;
            if (sortByCount)
            {
                ranked = this.GetPathCounts()
                    .OrderByDescending(x => x.count.refs)
                    .Take(25)
                    .ToArray();
            }
            else
            {
                Func<PathCount, float> distFn = x =>
                {
                    if (selectedDistrict != null && x.to == selectedDistrict)
                        return Vector3.Distance(entityPos, GetDistrictPosition(x.from));
                    if (selectedDistrict != null && x.from == selectedDistrict)
                        return Vector3.Distance(entityPos, GetDistrictPosition(x.to));
                    float df = Vector3.Distance(entityPos, GetDistrictPosition(x.from));
                    float dt = Vector3.Distance(entityPos, GetDistrictPosition(x.to));
                    return (float.IsInfinity(df) || float.IsInfinity(dt)) ? float.MaxValue : Math.Min(df, dt);
                };

                // Precompute distances once per item
                var allByDist = this.GetPathCounts()
                    .Select(x => new { pc = x, dist = distFn(x) })
                    .OrderBy(x => x.dist)
                    .ThenByDescending(x => x.pc.count.refs)
                    .ToList();

                // High-traffic entries always shown (up to 15); normal entries fill remaining slots
                var highTraffic = allByDist.Where(x => x.pc.count.refs > 10).ToList();
                var normal      = allByDist.Where(x => x.pc.count.refs <= 10).ToList();
                int highCount   = Math.Min(highTraffic.Count, 15);

                ranked = highTraffic.Take(highCount)
                    .Concat(normal.Take(15 - highCount))
                    .OrderBy(x => x.dist)
                    .Select(x => x.pc)
                    .ToArray();
            }

            var countColors = ranked.Select(x =>
                x.count.refs > 15 ? BreakdownStyle.AlertColor :
                x.count.refs > 10 ? BreakdownStyle.WarnColor  :
                BreakdownStyle.MutedColor).ToArray();

            var prefixes    = new string[ranked.Length];
            var froms       = new string[ranked.Length];
            var fromColors  = new Color32[ranked.Length];
            var tos         = new string[ranked.Length];
            var toColors    = new Color32[ranked.Length];
            var rowShowBoth = new bool[ranked.Length];
            var tags        = new string[ranked.Length];
            var counts      = new string[ranked.Length];
            var tooltips    = new string[ranked.Length];
            for (int i = 0; i < ranked.Length; i++)
            {
                var fmt        = FormatRouteRow(ranked[i].from, ranked[i].to, selectedDistrict);
                prefixes[i]    = fmt.prefix;
                froms[i]       = fmt.from;
                fromColors[i]  = fmt.fromColor;
                tos[i]         = fmt.to;
                toColors[i]    = fmt.toColor;
                rowShowBoth[i] = fmt.showBoth;
                tags[i]        = fmt.tag;
                counts[i]      = showCount ? ranked[i].FormatCount() : string.Empty;
                tooltips[i]    = BuildTooltip(ranked[i].count);
            }
            Log.Debug($"built {froms.Length} display entries in {sw.ElapsedMilliseconds}ms");

            _pendingUIUpdate = () =>
            {
                foreach (var panel in this.panels.Values)
                {
                    if (panel != null)
                    {
                        panel.SetTopTen(prefixes, froms, fromColors, tos, toColors, tags, counts, rowShowBoth, this.districtsNotSegments, countColors, tooltips);
                    }
                }
            };
            _pendingResultReady = true;
        }

        private void FindCityWideRoutesOnSimThread(Dictionary<InstanceID, PathVisualizer.Path> pathDict, bool showAverage)
        {
            try { FindCityWideRoutesOnSimThreadInner(pathDict, showAverage); }
            catch (Exception ex)
            {
                Log.Info($"FindCityWideRoutesOnSimThread exception: {ex.Message}");
                _findCityWideRoutesPending = false;
            }
        }

        private void FindCityWideRoutesOnSimThreadInner(Dictionary<InstanceID, PathVisualizer.Path> pathDict, bool showAverage)
        {
            Log.Debug("FindCityWideRoutes start");

            var pathBuffer = PathManager.instance?.m_pathUnits?.m_buffer;
            if (pathBuffer == null) { _findCityWideRoutesPending = false; return; }

            int bufferSize = (int)PathManager.instance.m_pathUnits.m_size;
            this.pathCounts.Clear();

            var tails = pathBuffer.GetPathTails(bufferSize);

            // Build a complete head→InstanceID map by scanning all active vehicles and citizens.
            // This enables per-category breakdown in the accordion tooltips.
            var allHeadToInstance = new Dictionary<uint, InstanceID>();
            var vm = VehicleManager.instance;
            int vmSize = (int)vm.m_vehicles.m_size;
            for (int i = 1; i < vmSize; i++)
            {
                uint pathUnit = vm.m_vehicles.m_buffer[i].m_path;
                if (pathUnit == 0) continue;
                uint head = GetHead(pathUnit, tails);
                if (head > 0 && !allHeadToInstance.ContainsKey(head))
                {
                    var id = InstanceID.Empty;
                    id.Vehicle = (ushort)i;
                    allHeadToInstance[head] = id;
                }
            }
            var cm = CitizenManager.instance;
            int cmSize = (int)cm.m_instances.m_size;
            for (int i = 1; i < cmSize; i++)
            {
                uint pathUnit = cm.m_instances.m_buffer[i].m_path;
                if (pathUnit == 0) continue;
                uint head = GetHead(pathUnit, tails);
                if (head > 0 && !allHeadToInstance.ContainsKey(head))
                {
                    var id = InstanceID.Empty;
                    id.CitizenInstance = (ushort)i;
                    allHeadToInstance[head] = id;
                }
            }
            Log.Debug($"FindCityWideRoutes: reverse map={allHeadToInstance.Count}");

            this.FollowRoutes(pathBuffer, bufferSize, allHeadToInstance, tails, true);

            UpdateEma();

            var sections = showAverage ? BuildDistrictSectionsEma() : BuildDistrictSections();
            Log.Debug($"FindCityWideRoutes done, {sections.Length} sections, showAverage={showAverage}");

            bool hasEmaData = _accumulated.Count > 0;
            _pendingCityWideUIUpdate = () =>
            {
                if (_accordionPanel != null) _accordionPanel.SetData(sections);
                if (hasEmaData)
                    foreach (var p in panels.Values)
                        if (p != null) p.SetAverageAvailable(true);
            };
            _pendingCityWideResultReady = true;
        }

        private readonly HashSet<uint> _loopCheckBuffer = new HashSet<uint>();

        private void FollowRoutes(PathUnit[] pathBuffer, int bufferSize, Dictionary<uint, InstanceID> headToInstance, Dictionary<uint, uint> tails, bool useDistricts)
        {
            int headCount = 0;
            var sw = new Stopwatch();
            sw.Start();
            Log.Debug($"FollowRoutes scanning {bufferSize} buffer slots, heads={(headToInstance != null ? headToInstance.Count.ToString() : "ALL")}");
            for (int index = 0; index < bufferSize; index++)
            {
                if (headToInstance != null && !headToInstance.ContainsKey((uint)index)) continue;
                if (tails.ContainsKey((uint)index)) continue;

                var path = pathBuffer[index];

                if (path.UnusedOrEmpty())
                {
                    continue;
                }
                headCount++;

                ushort firstSeg, lastSeg;
                firstSeg = path.m_position00.m_segment;

                _loopCheckBuffer.Clear();
                var unit = (uint)index;
                float pathLength = 0;
                ushort segmentCount = 0;
                while (unit > 0 && !_loopCheckBuffer.Contains(unit))
                {
                    _loopCheckBuffer.Add(unit);
                    path = pathBuffer[unit];
                    unit = path.m_nextPathUnit;
                    pathLength += path.m_length;
                    segmentCount += path.m_positionCount;
                }
                path.GetLastPosition(out var lastPosition);
                lastSeg = lastPosition.m_segment;

                string first, last;
                if (useDistricts)
                {
                    first = this.ResolveDistrictName(firstSeg);
                    last = this.ResolveDistrictName(lastSeg);
                }
                else
                {
                    first = firstSeg.GetSegmentName();
                    last = lastSeg.GetSegmentName();
                    if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(last)) continue;
                }

                this.AddPath(first, last, segmentCount, pathLength,
                    path.m_laneTypes, path.m_pathFindFlags, path.m_referenceCount, (byte)path.m_simulationFlags, path.m_speed, path.m_vehicleTypes);

                if (headToInstance != null)
                {
                    InstanceID id;
                    if (headToInstance.TryGetValue((uint)index, out id))
                    {
                        var detail = this.pathCounts[first][last];
                        switch (GetInstanceCategory(id))
                        {
                            case 0: detail.catPedestrian++; break;
                            case 1: detail.catCyclists++;   break;
                            case 3: detail.catTrucks++;     break;
                            case 4: detail.catPublic++;     break;
                            case 5: detail.catServices++;   break;
                            default: detail.catPrivate++;   break;
                        }
                    }
                }
            }
            Log.Debug($"FollowRoutes processed {headCount} heads in {sw.ElapsedMilliseconds}ms");
        }

        public void AddPath(string from, string to, ushort segments, float length,
            byte laneTypes, byte pathFindFlags, byte referenceCount, byte simulationFlags,
            byte speed, uint vehicleTypes)
        {
            if (!this.pathCounts.ContainsKey(from))
            {
                this.pathCounts[from] = new Dictionary<string, PathDetails>();
            }
            if (!this.pathCounts[from].ContainsKey(to))
            {
                this.pathCounts[from][to] = new PathDetails();
            }
            this.pathCounts[from][to].refs++;
            this.pathCounts[from][to].length.Add(length);
            this.pathCounts[from][to].segments.Add(segments);
            this.pathCounts[from][to].laneTypes.Add(laneTypes);
            this.pathCounts[from][to].pathFindFlags.Add(pathFindFlags);
            this.pathCounts[from][to].referenceCount.Add(referenceCount);
            this.pathCounts[from][to].simulationFlags.Add(simulationFlags);
            this.pathCounts[from][to].speed.Add(speed);
            this.pathCounts[from][to].vehicleTypes.Add(vehicleTypes);
        }

        public IEnumerable<PathCount> GetPathCounts()
        {
            foreach (var fromCount in this.pathCounts)
            {
                foreach (var toCount in fromCount.Value)
                {
                    yield return new PathCount() { from = fromCount.Key, to = toCount.Key, count = toCount.Value };
                }
            }
        }

        private DistrictSection[] BuildDistrictSections()
        {
            var totals      = new Dictionary<string, uint>();
            var connections = new Dictionary<string, List<ConnectionData>>();
            var processed   = new HashSet<string>();

            foreach (var fromKv in this.pathCounts)
            {
                string a = fromKv.Key;
                foreach (var toKv in fromKv.Value)
                {
                    string b = toKv.Key;
                    if (a == b) continue;

                    // Canonical pair key so A↔B is processed once regardless of direction
                    string pairKey = string.Compare(a, b, StringComparison.Ordinal) < 0
                        ? a + "" + b : b + "" + a;
                    if (processed.Contains(pairKey)) continue;
                    processed.Add(pairKey);

                    PathDetails dAB = toKv.Value;
                    PathDetails dBA = null;
                    Dictionary<string, PathDetails> bDict;
                    if (this.pathCounts.TryGetValue(b, out bDict)) bDict.TryGetValue(a, out dBA);

                    uint n = dAB.refs + (dBA != null ? dBA.refs : 0);
                    if (n == 0) continue;

                    uint catPed  = dAB.catPedestrian + (dBA != null ? dBA.catPedestrian : 0);
                    uint catCyc  = dAB.catCyclists   + (dBA != null ? dBA.catCyclists   : 0);
                    uint catPriv = dAB.catPrivate     + (dBA != null ? dBA.catPrivate    : 0);
                    uint catTrk  = dAB.catTrucks      + (dBA != null ? dBA.catTrucks     : 0);
                    uint catPub  = dAB.catPublic      + (dBA != null ? dBA.catPublic     : 0);
                    uint catSvc  = dAB.catServices    + (dBA != null ? dBA.catServices   : 0);

                    string tip = BuildConnectionTooltip(catPed, catCyc, catPriv, catTrk, catPub, catSvc);

                    if (!totals.ContainsKey(a)) { totals[a] = 0; connections[a] = new List<ConnectionData>(); }
                    if (!totals.ContainsKey(b)) { totals[b] = 0; connections[b] = new List<ConnectionData>(); }
                    totals[a] += n;
                    totals[b] += n;
                    connections[a].Add(new ConnectionData { name = b, color = GetDistrictColor(b), routes = n, tooltip = tip });
                    connections[b].Add(new ConnectionData { name = a, color = GetDistrictColor(a), routes = n, tooltip = tip });
                }
            }

            return totals
                .Where(x => !x.Key.StartsWith("near ") && x.Key != "No district" && x.Key != "Out of town")
                .OrderByDescending(x => x.Value)
                .Take(UIBreakdownAccordionPanel.MaxSections)
                .Select(x => new DistrictSection
                {
                    name        = x.Key,
                    color       = GetDistrictColor(x.Key),
                    totalRoutes = x.Value,
                    connections = connections[x.Key]
                        .OrderByDescending(c => c.routes)
                        .Take(UIBreakdownAccordionPanel.MaxConnections)
                        .ToArray()
                })
                .ToArray();
        }

        private string BuildConnectionTooltip(uint catPed, uint catCyc, uint catPriv, uint catTrk, uint catPub, uint catSvc)
        {
            var lines = new List<string>(6);
            if (catPed  > 0) lines.Add($"{_labelPedestrian}: {catPed}");
            if (catCyc  > 0) lines.Add($"{_labelCyclists}: {catCyc}");
            if (catPriv > 0) lines.Add($"{_labelPrivate}: {catPriv}");
            if (catTrk  > 0) lines.Add($"{_labelTrucks}: {catTrk}");
            if (catPub  > 0) lines.Add($"{_labelPublicTrans}: {catPub}");
            if (catSvc  > 0) lines.Add($"{_labelServices}: {catSvc}");
            return lines.Count > 0 ? string.Join("\n", lines.ToArray()) : null;
        }

        private string BuildTooltip(PathDetails count)
        {
            if (count == null || count.refs == 0) return null;
            var lines = new List<string>(6);
            if (count.catPedestrian > 0) lines.Add($"{_labelPedestrian}: {count.catPedestrian}");
            if (count.catCyclists   > 0) lines.Add($"{_labelCyclists}: {count.catCyclists}");
            if (count.catPrivate    > 0) lines.Add($"{_labelPrivate}: {count.catPrivate}");
            if (count.catTrucks     > 0) lines.Add($"{_labelTrucks}: {count.catTrucks}");
            if (count.catPublic     > 0) lines.Add($"{_labelPublicTrans}: {count.catPublic}");
            if (count.catServices   > 0) lines.Add($"{_labelServices}: {count.catServices}");
            return lines.Count > 0 ? string.Join("\n", lines.ToArray()) : null;
        }

        private void ToggleDistrictsRoads()
        {
            this.districtsNotSegments = !this.districtsNotSegments;
            this.lastRefreshFrame = 0;
        }

        private void ToggleAverageMode()
        {
            if (!_showAverage && _accumulated.Count == 0) return;  // no EMA data yet
            _showAverage = !_showAverage;
            this.lastRefreshFrame = 0;  // immediate popup refresh
            // _cityWideRefreshFrame is NOT reset: it only increments when no popup is open,
            // so resetting it would freeze at 0 and flood the log.
            foreach (var panel in this.panels.Values)
                if (panel != null) panel.UpdateAverageState(_showAverage);
            if (_accordionPanel != null) _accordionPanel.UpdateAverageState(_showAverage);

            // Immediate accordion refresh from already-cached data — no scan needed.
            // _findCityWideRoutesPending = false means no sim-thread scan is active, so
            // reading pathCounts / _accumulated on the main thread is safe here.
            if (_accordionPanel != null && !_findCityWideRoutesPending)
            {
                var sections = _showAverage ? BuildDistrictSectionsEma() : BuildDistrictSections();
                _accordionPanel.SetData(sections);
            }
        }

        // Build popup display from accumulated EMA, filtered by selectedDistrict.
        // Uses the same row-format rules as live mode (same-district tag, from/to prefix,
        // pass-through arrow) but with muted colors and percentages instead of counts.
        private void BuildPopupEmaDisplay(InstanceID instance, string selectedDistrict)
        {
            bool showCount = instance.Type != InstanceType.Vehicle && instance.Type != InstanceType.Citizen;

            // Collect matching EMA rows
            var rows = new List<EmaPopupRow>(32);
            if (selectedDistrict != null)
            {
                foreach (var paKv in _accumulated)
                {
                    string pa = paKv.Key;
                    foreach (var pbKv in paKv.Value)
                    {
                        string pb = pbKv.Key;
                        if (pa == selectedDistrict || pb == selectedDistrict)
                        {
                            // Mirror live mode: selectedDistrict is the destination, other is the origin
                            string other = pa == selectedDistrict ? pb : pa;
                            rows.Add(new EmaPopupRow { from = other, to = selectedDistrict, entry = pbKv.Value });
                        }
                    }
                }
            }
            else
            {
                // No specific district: show top global pairs (pass-through — both shown with arrow)
                foreach (var paKv in _accumulated)
                    foreach (var pbKv in paKv.Value)
                        rows.Add(new EmaPopupRow { from = paKv.Key, to = pbKv.Key, entry = pbKv.Value });
            }
            rows.Sort((a, b) => b.entry.ema.CompareTo(a.entry.ema));

            int take = Math.Min(rows.Count, 25);
            var prefixes    = new string[take];
            var froms       = new string[take];
            var fromColors  = new Color32[take];
            var tos         = new string[take];
            var toColors    = new Color32[take];
            var rowBoth     = new bool[take];
            var tags        = new string[take];
            var counts      = new string[take];
            var countColors = new Color32[take];
            var tooltips    = new string[take];

            for (int i = 0; i < take; i++)
            {
                var row   = rows[i];
                double pct = row.entry.ema * 100.0;
                var fmt    = FormatRouteRow(row.from, row.to, selectedDistrict);
                prefixes[i]    = fmt.prefix;
                froms[i]       = fmt.from;
                fromColors[i]  = fmt.fromColor;
                tos[i]         = fmt.to;
                toColors[i]    = fmt.toColor;
                rowBoth[i]     = fmt.showBoth;
                tags[i]        = fmt.tag;
                counts[i]      = showCount ? $"({FormatPct(pct)}%)" : string.Empty;
                countColors[i] = pct > 15 ? BreakdownStyle.AlertColor : pct > 10 ? BreakdownStyle.WarnColor : BreakdownStyle.MutedColor;
                tooltips[i]    = BuildConnectionTooltipEma(row.entry);
            }

            _pendingUIUpdate = () =>
            {
                foreach (var panel in this.panels.Values)
                    if (panel != null)
                        panel.SetTopTen(prefixes, froms, fromColors, tos, toColors, tags, counts, rowBoth,
                            this.districtsNotSegments, countColors, tooltips);
            };
            _pendingResultReady = true;
        }

        private void UpdateEma()
        {
            // First pass: compute grand total routes across all canonical pairs (including same-district)
            double grandTotal = 0;
            var processed = new HashSet<string>();
            foreach (var fromKv in this.pathCounts)
            {
                string a = fromKv.Key;
                foreach (var toKv in fromKv.Value)
                {
                    string b = toKv.Key;
                    if (a == b) { grandTotal += toKv.Value.refs; continue; }
                    bool aFirst = string.Compare(a, b, StringComparison.Ordinal) < 0;
                    string pa = aFirst ? a : b, pb = aFirst ? b : a;
                    string pairKey = pa + "" + pb;
                    if (!processed.Add(pairKey)) continue;

                    PathDetails dPA = null, dPB = null;
                    Dictionary<string, PathDetails> paDict, pbDict;
                    if (this.pathCounts.TryGetValue(pa, out paDict)) paDict.TryGetValue(pb, out dPA);
                    if (this.pathCounts.TryGetValue(pb, out pbDict)) pbDict.TryGetValue(pa, out dPB);
                    grandTotal += (dPA != null ? dPA.refs : 0) + (dPB != null ? dPB.refs : 0);
                }
            }
            if (grandTotal == 0) return;

            // Second pass: update EMA for each canonical pair (including same-district as [a][a])
            processed.Clear();
            foreach (var fromKv in this.pathCounts)
            {
                string a = fromKv.Key;
                foreach (var toKv in fromKv.Value)
                {
                    string b = toKv.Key;
                    if (a == b)
                    {
                        if (!processed.Add(a + "" + a)) continue;
                        var d = toKv.Value;
                        double sPct = d.refs / grandTotal;
                        double sFPed = d.refs > 0 ? d.catPedestrian / (double)d.refs : 0;
                        double sFCyc = d.refs > 0 ? d.catCyclists   / (double)d.refs : 0;
                        double sFPrv = d.refs > 0 ? d.catPrivate    / (double)d.refs : 0;
                        double sFTrk = d.refs > 0 ? d.catTrucks     / (double)d.refs : 0;
                        double sFPub = d.refs > 0 ? d.catPublic     / (double)d.refs : 0;
                        double sFSvc = d.refs > 0 ? d.catServices   / (double)d.refs : 0;
                        if (!_accumulated.ContainsKey(a)) _accumulated[a] = new Dictionary<string, EmaEntry>();
                        EmaEntry se;
                        if (!_accumulated[a].TryGetValue(a, out se))
                            _accumulated[a][a] = new EmaEntry { ema = sPct, emaCatPedestrian = sFPed, emaCatCyclists = sFCyc, emaCatPrivate = sFPrv, emaCatTrucks = sFTrk, emaCatPublic = sFPub, emaCatServices = sFSvc };
                        else
                        {
                            se.ema              = _alpha * sPct  + (1 - _alpha) * se.ema;
                            se.emaCatPedestrian = _alpha * sFPed + (1 - _alpha) * se.emaCatPedestrian;
                            se.emaCatCyclists   = _alpha * sFCyc + (1 - _alpha) * se.emaCatCyclists;
                            se.emaCatPrivate    = _alpha * sFPrv + (1 - _alpha) * se.emaCatPrivate;
                            se.emaCatTrucks     = _alpha * sFTrk + (1 - _alpha) * se.emaCatTrucks;
                            se.emaCatPublic     = _alpha * sFPub + (1 - _alpha) * se.emaCatPublic;
                            se.emaCatServices   = _alpha * sFSvc + (1 - _alpha) * se.emaCatServices;
                        }
                        continue;
                    }
                    bool aFirst = string.Compare(a, b, StringComparison.Ordinal) < 0;
                    string pa = aFirst ? a : b, pb = aFirst ? b : a;
                    string pairKey = pa + "" + pb;
                    if (!processed.Add(pairKey)) continue;

                    PathDetails dPA = null, dPB = null;
                    Dictionary<string, PathDetails> paDict, pbDict;
                    if (this.pathCounts.TryGetValue(pa, out paDict)) paDict.TryGetValue(pb, out dPA);
                    if (this.pathCounts.TryGetValue(pb, out pbDict)) pbDict.TryGetValue(pa, out dPB);

                    uint n    = (dPA != null ? dPA.refs : 0) + (dPB != null ? dPB.refs : 0);
                    uint cPed = (dPA != null ? dPA.catPedestrian : 0) + (dPB != null ? dPB.catPedestrian : 0);
                    uint cCyc = (dPA != null ? dPA.catCyclists   : 0) + (dPB != null ? dPB.catCyclists   : 0);
                    uint cPrv = (dPA != null ? dPA.catPrivate    : 0) + (dPB != null ? dPB.catPrivate    : 0);
                    uint cTrk = (dPA != null ? dPA.catTrucks     : 0) + (dPB != null ? dPB.catTrucks     : 0);
                    uint cPub = (dPA != null ? dPA.catPublic     : 0) + (dPB != null ? dPB.catPublic     : 0);
                    uint cSvc = (dPA != null ? dPA.catServices   : 0) + (dPB != null ? dPB.catServices   : 0);

                    double pct  = n    / grandTotal;
                    double fPed = n > 0 ? cPed / (double)n : 0;
                    double fCyc = n > 0 ? cCyc / (double)n : 0;
                    double fPrv = n > 0 ? cPrv / (double)n : 0;
                    double fTrk = n > 0 ? cTrk / (double)n : 0;
                    double fPub = n > 0 ? cPub / (double)n : 0;
                    double fSvc = n > 0 ? cSvc / (double)n : 0;

                    if (!_accumulated.ContainsKey(pa)) _accumulated[pa] = new Dictionary<string, EmaEntry>();
                    EmaEntry entry;
                    if (!_accumulated[pa].TryGetValue(pb, out entry))
                    {
                        _accumulated[pa][pb] = new EmaEntry
                        {
                            ema = pct, emaCatPedestrian = fPed, emaCatCyclists = fCyc,
                            emaCatPrivate = fPrv, emaCatTrucks = fTrk, emaCatPublic = fPub, emaCatServices = fSvc
                        };
                    }
                    else
                    {
                        entry.ema              = _alpha * pct  + (1 - _alpha) * entry.ema;
                        entry.emaCatPedestrian = _alpha * fPed + (1 - _alpha) * entry.emaCatPedestrian;
                        entry.emaCatCyclists   = _alpha * fCyc + (1 - _alpha) * entry.emaCatCyclists;
                        entry.emaCatPrivate    = _alpha * fPrv + (1 - _alpha) * entry.emaCatPrivate;
                        entry.emaCatTrucks     = _alpha * fTrk + (1 - _alpha) * entry.emaCatTrucks;
                        entry.emaCatPublic     = _alpha * fPub + (1 - _alpha) * entry.emaCatPublic;
                        entry.emaCatServices   = _alpha * fSvc + (1 - _alpha) * entry.emaCatServices;
                    }
                }
            }

            // Prune pairs whose share has decayed below the threshold
            var toPrunePa = new List<string>();
            foreach (var paKv in _accumulated)
            {
                var toPrunePb = new List<string>();
                foreach (var pbKv in paKv.Value)
                    if (pbKv.Value.ema < _pruneThreshold) toPrunePb.Add(pbKv.Key);
                foreach (string pb in toPrunePb) paKv.Value.Remove(pb);
                if (paKv.Value.Count == 0) toPrunePa.Add(paKv.Key);
            }
            foreach (string pa in toPrunePa) _accumulated.Remove(pa);

            Log.Debug($"UpdateEma: {_accumulated.Count} pa-keys, grandTotal={grandTotal:F0}");
        }

        private DistrictSection[] BuildDistrictSectionsEma()
        {
            var totals      = new Dictionary<string, double>();
            var connections = new Dictionary<string, List<ConnectionData>>();

            foreach (var paKv in _accumulated)
            {
                string pa = paKv.Key;
                foreach (var pbKv in paKv.Value)
                {
                    string   pb      = pbKv.Key;
                    if (pa == pb) continue;  // same-district pairs don't appear in the accordion
                    EmaEntry e       = pbKv.Value;
                    double   pct     = e.ema * 100.0;
                    string   disp    = $"({FormatPct(pct)}%)";
                    uint     sortKey = (uint)Math.Round(e.ema * 1000000);
                    string   tip     = BuildConnectionTooltipEma(e);

                    if (!totals.ContainsKey(pa)) { totals[pa] = 0; connections[pa] = new List<ConnectionData>(); }
                    if (!totals.ContainsKey(pb)) { totals[pb] = 0; connections[pb] = new List<ConnectionData>(); }
                    totals[pa] += pct;
                    totals[pb] += pct;
                    connections[pa].Add(new ConnectionData { name = pb, color = GetDistrictColor(pb), routes = sortKey, displayRoutes = disp, tooltip = tip });
                    connections[pb].Add(new ConnectionData { name = pa, color = GetDistrictColor(pa), routes = sortKey, displayRoutes = disp, tooltip = tip });
                }
            }

            return totals
                .Where(x => !x.Key.StartsWith("near ") && x.Key != "No district" && x.Key != "Out of town")
                .OrderByDescending(x => x.Value)
                .Take(UIBreakdownAccordionPanel.MaxSections)
                .Select(x => new DistrictSection
                {
                    name         = x.Key,
                    color        = GetDistrictColor(x.Key),
                    totalRoutes  = (uint)Math.Round(totals[x.Key] * 10000),
                    displayTotal = $"({FormatPct(totals[x.Key])}%)",
                    connections  = connections[x.Key]
                        .OrderByDescending(c => c.routes)
                        .Take(UIBreakdownAccordionPanel.MaxConnections)
                        .ToArray()
                })
                .ToArray();
        }

        private string BuildConnectionTooltipEma(EmaEntry e)
        {
            var lines = new List<string>(6);
            if (e.emaCatPedestrian * 100 >= 1.0) lines.Add($"{_labelPedestrian}: {FormatPct(e.emaCatPedestrian * 100)}%");
            if (e.emaCatCyclists   * 100 >= 1.0) lines.Add($"{_labelCyclists}: {FormatPct(e.emaCatCyclists   * 100)}%");
            if (e.emaCatPrivate    * 100 >= 1.0) lines.Add($"{_labelPrivate}: {FormatPct(e.emaCatPrivate    * 100)}%");
            if (e.emaCatTrucks     * 100 >= 1.0) lines.Add($"{_labelTrucks}: {FormatPct(e.emaCatTrucks     * 100)}%");
            if (e.emaCatPublic     * 100 >= 1.0) lines.Add($"{_labelPublicTrans}: {FormatPct(e.emaCatPublic     * 100)}%");
            if (e.emaCatServices   * 100 >= 1.0) lines.Add($"{_labelServices}: {FormatPct(e.emaCatServices   * 100)}%");
            return lines.Count > 0 ? string.Join("\n", lines.ToArray()) : null;
        }

        private static string FormatPct(double pct)
        {
            return pct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string GetSelectedDistrict(InstanceID instance)
        {
            if (instance.Type == InstanceType.Building)
                return BuildingManager.instance.m_buildings.m_buffer[instance.Building].m_position.GetDistrictName();
            if (instance.Type == InstanceType.District)
                return ((byte)instance.District).GetDistrictName();
            return null;
        }

        private static string SameTag(string from, string to)
        {
            if (from != to) return null;
            if (from == "Out of town" || from == "No district") return null;
            if (from.StartsWith("near ")) return null;
            return "(same district)";
        }

        // Shared row-format logic for both live and average modes.
        // Determines prefix ("to"/"from"/empty), which district is shown as "from",
        // and whether to show both endpoints with the arrow.
        private RowFormat FormatRouteRow(string f, string t, string selectedDistrict)
        {
            string tag = SameTag(f, t);
            Color32 fc = GetDistrictColor(f), tc = GetDistrictColor(t);
            if (tag != null)
                return new RowFormat { prefix = string.Empty, from = f, fromColor = fc, to = t, toColor = tc, showBoth = false, tag = tag };
            if (selectedDistrict != null && t == selectedDistrict)
                return new RowFormat { prefix = "from", from = f, fromColor = fc, to = t, toColor = tc, showBoth = false };
            if (selectedDistrict != null && f == selectedDistrict)
                return new RowFormat { prefix = "to", from = t, fromColor = tc, to = f, toColor = fc, showBoth = false };
            return new RowFormat { prefix = string.Empty, from = f, fromColor = fc, to = t, toColor = tc, showBoth = true };
        }

        private uint GetHead(uint start, Dictionary<uint, uint> tails)
        {
            _loopCheckBuffer.Clear();
            var current = start;
            while (tails.ContainsKey(current) && !_loopCheckBuffer.Contains(current))
            {
                _loopCheckBuffer.Add(current);
                current = tails[current];
            }
            return current;
        }

        private struct EmaPopupRow
        {
            public string   from;
            public string   to;
            public EmaEntry entry;
        }

        private struct RowFormat
        {
            public string  prefix;
            public string  from;
            public Color32 fromColor;
            public string  to;
            public Color32 toColor;
            public bool    showBoth;
            public string  tag;
        }
    }

    public struct PathCount
    {
        public PathDetails count;
        public string from;
        public string to;

        public override string ToString()
        {
            return $"{this.count.refs} : {this.from} -> {this.to}";
        }

        public string FormatCount()
        {
            var routeLabel = this.count.refs == 1 ? "route" : "routes";
            return $"({this.count.refs} {routeLabel})";
        }
    }

    public class EmaEntry
    {
        // Share of total city traffic for this district pair (0–1, where 1 = 100%)
        public double ema;
        // Fraction of this pair's traffic in each category (0–1 each)
        public double emaCatPedestrian;
        public double emaCatCyclists;
        public double emaCatPrivate;
        public double emaCatTrucks;
        public double emaCatPublic;
        public double emaCatServices;
    }

    public class PathDetails
    {
        public uint refs = 0;
        // Per-category route counts for the hover tooltip (same order as traffic-routes panel)
        public uint catPedestrian = 0;
        public uint catCyclists   = 0;
        public uint catPrivate    = 0;
        public uint catTrucks     = 0;
        public uint catPublic     = 0;
        public uint catServices   = 0;
        public Counts<ushort> segments = new Counts<ushort>();
        public Counts<float> length = new Counts<float>();
        public Counts<byte> laneTypes = new Counts<byte>();
        public Counts<byte> pathFindFlags = new Counts<byte>();
        public Counts<byte> referenceCount = new Counts<byte>();
        public Counts<byte> simulationFlags = new Counts<byte>();
        public Counts<byte> speed = new Counts<byte>();
        public Counts<uint> vehicleTypes = new Counts<uint>();

        public override string ToString()
        {
            return $"r:{this.refs} s:{this.segments} l:{this.length} [{this.laneTypes} {this.pathFindFlags} {this.referenceCount} {this.simulationFlags} {this.speed} {this.vehicleTypes}]";
        }
    }
}
