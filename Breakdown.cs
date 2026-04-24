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
                    _findRoutesPending = true;
                    Log.Debug($"Queuing AddAction, frame={lastRefreshFrame}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");
                    SimulationManager.instance.AddAction(() => FindRoutesOnSimThread(capturedPaths, capturedInstance));
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
                        _findCityWideRoutesPending = true;
                        Log.Debug("Queuing city-wide AddAction");
                        if (_accordionPanel != null) _accordionPanel.ShowLoading();
                        SimulationManager.instance.AddAction(() => FindCityWideRoutesOnSimThread(capturedPaths2));
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
                    bp.OnModeToggled = this.ToggleDistrictsRoads;
                    this.panels[worldItem.Name] = bp;
                    Log.Debug($"attached to {worldItem}.");
                }
                catch (Exception ex)
                {
                    Log.Info($"failed to attach to {worldItem}: {ex.Message}");
                }
            }
            Log.Debug($"InitUI done, {panels.Count} panels attached.");
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

        private void FindRoutesOnSimThread(Dictionary<InstanceID, PathVisualizer.Path> pathDict, InstanceID instance)
        {
            try
            {
                FindRoutesOnSimThreadInner(pathDict, instance);
            }
            catch (Exception ex)
            {
                Log.Info($"FindRoutesOnSimThread unhandled exception: {ex.Message}\n{ex.StackTrace}");
                _findRoutesPending = false;
            }
        }

        private void FindRoutesOnSimThreadInner(Dictionary<InstanceID, PathVisualizer.Path> pathDict, InstanceID instance)
        {
            Log.Debug($"FindRoutesOnSimThread start, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");
            if (pathDict == null) { _findRoutesPending = false; return; }

            var pathBuffer = PathManager.instance?.m_pathUnits?.m_buffer;
            if (pathBuffer == null) { _findRoutesPending = false; return; }

            int bufferSize = (int)PathManager.instance.m_pathUnits.m_size;
            this.pathCounts.Clear();  // TODO option to aggregate results.
            var sw = new Stopwatch();
            sw.Start();

            var tails = pathBuffer.GetPathTails(bufferSize);

            HashSet<uint> heads;
            try
            {
                lock (pathDict)
                {
                    heads = new HashSet<uint>(pathDict.Select(x => x.Value.m_pathUnit).Select(x => GetHead(x, tails)));
                }
            }
            catch (InvalidOperationException)
            {
                _findRoutesPending = false;
                return;
            }

            Log.Debug($"FindRoutes tails={tails.Count} heads={heads.Count} pathDict={pathDict.Count} elapsed={sw.ElapsedMilliseconds}ms");

            this.FollowRoutes(pathBuffer, bufferSize, heads, tails, this.districtsNotSegments);

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
            for (int i = 0; i < ranked.Length; i++)
            {
                string f = ranked[i].from, t = ranked[i].to;
                Color32 fc = this.GetDistrictColor(f), tc = this.GetDistrictColor(t);
                if (selectedDistrict != null && t == selectedDistrict)
                {
                    prefixes[i] = "from"; froms[i] = f; fromColors[i] = fc;
                    tos[i] = t; toColors[i] = tc;
                    rowShowBoth[i] = false;
                }
                else if (selectedDistrict != null && f == selectedDistrict && t != selectedDistrict)
                {
                    prefixes[i] = "to"; froms[i] = t; fromColors[i] = tc;
                    tos[i] = f; toColors[i] = fc;
                    rowShowBoth[i] = false;
                }
                else
                {
                    prefixes[i] = string.Empty; froms[i] = f; fromColors[i] = fc;
                    tos[i] = t; toColors[i] = tc;
                    rowShowBoth[i] = true;
                }
            }
            var tags   = ranked.Select(x => SameTag(x.from, x.to)).ToArray();
            var counts = ranked.Select(x => showCount ? x.FormatCount() : string.Empty).ToArray();
            Log.Debug($"built {froms.Length} display entries in {sw.ElapsedMilliseconds}ms");

            _pendingUIUpdate = () =>
            {
                foreach (var panel in this.panels.Values)
                {
                    if (panel != null)
                    {
                        panel.SetTopTen(prefixes, froms, fromColors, tos, toColors, tags, counts, rowShowBoth, this.districtsNotSegments, countColors);
                    }
                }
            };
            _pendingResultReady = true;
        }

        private void FindCityWideRoutesOnSimThread(Dictionary<InstanceID, PathVisualizer.Path> pathDict)
        {
            try { FindCityWideRoutesOnSimThreadInner(pathDict); }
            catch (Exception ex)
            {
                Log.Info($"FindCityWideRoutesOnSimThread exception: {ex.Message}");
                _findCityWideRoutesPending = false;
            }
        }

        private void FindCityWideRoutesOnSimThreadInner(Dictionary<InstanceID, PathVisualizer.Path> pathDict)
        {
            Log.Debug("FindCityWideRoutes start");

            var pathBuffer = PathManager.instance?.m_pathUnits?.m_buffer;
            if (pathBuffer == null) { _findCityWideRoutesPending = false; return; }

            int bufferSize = (int)PathManager.instance.m_pathUnits.m_size;
            this.pathCounts.Clear();

            var tails = pathBuffer.GetPathTails(bufferSize);

            // Pass null heads = scan every active head in the full buffer (city-wide mode)
            this.FollowRoutes(pathBuffer, bufferSize, null, tails, true);

            var sections = BuildDistrictSections();
            Log.Debug($"FindCityWideRoutes done, {sections.Length} sections");

            _pendingCityWideUIUpdate = () =>
            {
                if (_accordionPanel != null) _accordionPanel.SetData(sections);
            };
            _pendingCityWideResultReady = true;
        }

        private readonly HashSet<uint> _loopCheckBuffer = new HashSet<uint>();

        private void FollowRoutes(PathUnit[] pathBuffer, int bufferSize, HashSet<uint> heads, Dictionary<uint, uint> tails, bool useDistricts)
        {
            int headCount = 0;
            var sw = new Stopwatch();
            sw.Start();
            Log.Debug($"FollowRoutes scanning {bufferSize} buffer slots, heads={(heads != null ? heads.Count.ToString() : "ALL")}");
            for (int index = 0; index < bufferSize; index++)
            {
                if (heads != null && !heads.Contains((uint)index)) continue;
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
            // Merge A→B and B→A into a single (min,max) keyed pair
            var merged = new Dictionary<string, Dictionary<string, uint>>();
            foreach (var fromKv in this.pathCounts)
            {
                foreach (var toKv in fromKv.Value)
                {
                    string a = fromKv.Key, b = toKv.Key;
                    if (string.Compare(a, b, StringComparison.Ordinal) > 0) { var tmp = a; a = b; b = tmp; }
                    if (!merged.ContainsKey(a)) merged[a] = new Dictionary<string, uint>();
                    if (!merged[a].ContainsKey(b)) merged[a][b] = 0;
                    merged[a][b] += toKv.Value.refs;
                }
            }

            // Accumulate per-district totals and connection lists
            var totals      = new Dictionary<string, uint>();
            var connections = new Dictionary<string, List<ConnectionData>>();
            foreach (var aKv in merged)
            {
                foreach (var bKv in aKv.Value)
                {
                    string a = aKv.Key, b = bKv.Key;
                    if (a == b) continue; // skip same-district paths (avoids self-connection duplication)
                    uint   n = bKv.Value;

                    if (!totals.ContainsKey(a)) { totals[a] = 0; connections[a] = new List<ConnectionData>(); }
                    if (!totals.ContainsKey(b)) { totals[b] = 0; connections[b] = new List<ConnectionData>(); }
                    totals[a] += n;
                    totals[b] += n;
                    connections[a].Add(new ConnectionData { name = b, color = GetDistrictColor(b), routes = n });
                    connections[b].Add(new ConnectionData { name = a, color = GetDistrictColor(a), routes = n });
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

        private void ToggleDistrictsRoads()
        {
            this.districtsNotSegments = !this.districtsNotSegments;
            this.lastRefreshFrame = 0;
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

    public class PathDetails
    {
        public uint refs = 0;
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
