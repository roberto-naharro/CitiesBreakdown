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
        private int _nextColorIndex = 0;

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
                float hue = (_nextColorIndex * 0.618033988f) % 1f;
                _nextColorIndex++;
                color = HsvToColor32(hue, 0.65f, 0.95f);
                _districtColors[name] = color;
            }
            return color;
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
                _pathsVisibleLogged = false;
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
                var instance = InstanceManager.instance.GetSelectedInstance();
                if (instance != this.lastInstance)
                {
                    this.lastInstance = instance;
                    //UnityEngine.Debug.Log($"new instance on {lastRefreshFrame}.");
                    foreach (var panel in this.panels.Values)
                    {
                        panel.SetTopTen(new string[0], new Color32[0], new bool[0], new string[0]);
                    }
                    this.lastRefreshFrame = 0;
                }
                if (this.lastRefreshFrame++ % 60 == 0)
                {
                    this.FindRoutes(paths);
                }
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
                //typeof(CitizenWorldInfoPanel),
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
                    this.panels[worldItem.Name] = wip.component.AddUIComponent(typeof(UIBreakdownPanel)) as UIBreakdownPanel;
                    Log.Debug($"attached to {worldItem}.");
                }
                catch (Exception ex)
                {
                    Log.Info($"failed to attach to {worldItem}: {ex.Message}");
                }
            }
            Log.Debug($"InitUI done, {panels.Count} panels attached.");
        }

        public void FindRoutes(Dictionary<InstanceID, PathVisualizer.Path> pathDict)
        {
            if (pathDict == null)
            {
                return;
            }

            //UnityEngine.Debug.Log($"{PathManager.instance.m_pathUnitCount}");
            var pathBuffer = PathManager.instance?.m_pathUnits?.m_buffer;

            if (pathBuffer == null)
            {
                return;
            }

            int bufferSize = (int)PathManager.instance.m_pathUnits.m_size;
            this.pathCounts.Clear();  // TODO option to aggregate results.
            var sw = new Stopwatch();
            sw.Start();

            var tails = pathBuffer.GetPathTails(bufferSize);
            //UnityEngine.Debug.Log($"{tails.Keys.Count}, {actualTails}, {dups}, {sw.ElapsedMilliseconds}");

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
                return;
            }

            Log.Debug($"FindRoutes tails={tails.Count} heads={heads.Count} pathDict={pathDict.Count} elapsed={sw.ElapsedMilliseconds}ms");

            this.FollowRoutes(pathBuffer, bufferSize, heads, tails);

            sw.Reset();
            sw.Start();
            var ranked = this.GetPathCounts()
                .OrderBy(x => RoutePriority(x.from, x.to))
                .ThenByDescending(x => x.count.refs)
                .Take(10)
                .ToArray();
            var names = ranked.Select(x => x.from).ToArray();
            var colors = ranked.Select(x => this.GetDistrictColor(x.from)).ToArray();
            var sameDistrict = ranked.Select(x => x.from == x.to).ToArray();
            var counts = ranked.Select(x => x.FormatCount()).ToArray();
            Log.Debug($"ranked {ranked.Length} OD pairs, top {names.Length} messages built in {sw.ElapsedMilliseconds}ms");
            foreach (var panel in panels.Values)
            {
                if (panel != null)
                {
                    panel.SetTopTen(names, colors, sameDistrict, counts);
                }
            }
        }

        private void FollowRoutes(PathUnit[] pathBuffer, int bufferSize, HashSet<uint> heads, Dictionary<uint, uint> tails)
        {
            int headCount = 0;
            var sw = new Stopwatch();
            sw.Start();
            Log.Debug($"FollowRoutes scanning {bufferSize} buffer slots for {heads.Count} heads");
            foreach (var index in Enumerable.Range(0, bufferSize))
            {
                if (!heads.Contains((uint)index) || tails.ContainsKey((uint)index))
                {
                    continue;
                }

                var path = pathBuffer[index];

                if (path.UnusedOrEmpty())
                {
                    continue;
                }
                headCount++;

                ushort firstSeg, lastSeg;
                firstSeg = path.m_position00.m_segment;

                var loopCheck = new HashSet<uint>();
                var unit = (uint)index;
                float pathLength = 0;
                ushort segmentCount = 0;
                while (unit > 0 && !loopCheck.Contains(unit))
                {
                    loopCheck.Add(unit);
                    path = pathBuffer[unit];
                    unit = path.m_nextPathUnit;
                    pathLength += path.m_length;
                    segmentCount += path.m_positionCount;
                }
                path.GetLastPosition(out var lastPosition);
                lastSeg = lastPosition.m_segment;

                string first, last;
                if (this.districtsNotSegments)
                {
                    first = firstSeg.GetSegmentLocation().GetDistrictName();
                    last = lastSeg.GetSegmentLocation().GetDistrictName();
                }
                else
                {
                    first = firstSeg.GetSegmentName();
                    last = lastSeg.GetSegmentName();
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

        private static int RoutePriority(string from, string to)
        {
            if (from == "Out of town" || to == "Out of town") return 0;
            if (from == "No district" || to == "No district") return 1;
            return 2;
        }

        private static uint GetHead(uint start, Dictionary<uint, uint> tails)
        {
            var loopCheck = new HashSet<uint>();
            var current = start;
            while (tails.ContainsKey(current) && !loopCheck.Contains(current))
            {
                loopCheck.Add(current);
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
