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
        public string lastMessage = string.Empty;
        public int lastRefreshFrame = 0;
        protected InstanceID lastInstance;
        //protected bool[] showRouteTypes;
        protected int lastPathCount = 0;
        public FieldInfo mPathsInfo;
        protected Dictionary<string, UIBreakdownPanel> panels = new Dictionary<string, UIBreakdownPanel>();
        protected bool districtsNotSegments = true;

        public BreakdownThread()
        {
            Log.Info("BreakdownThread created.");
            this.mPathsInfo = typeof(PathVisualizer).GetField("m_paths", BindingFlags.NonPublic | BindingFlags.Instance);
            if (this.mPathsInfo == null)
                Log.Info("Can't get m_paths from PathVisualizer.");
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
                    Log.Info($"PathsVisible=true, panels={panels.Count}, mPathsInfo={mPathsInfo != null}");
                }
                if (this.panels.Count == 0)
                {
                    this.InitUI();
                }
                foreach (var panel in this.panels.Values)
                {
                    if (!panel.enabled)
                    {
                        this.lastRefreshFrame = 0;
                    }
                    panel.Show();
                }
                //var flags = new[] { viz.showCityServiceVehicles, viz.showCyclists, viz.showPedestrians, viz.showPrivateVehicles, viz.showPublicTransport, viz.showTrucks };
                //if (showRouteTypes == null || !Enumerable.SequenceEqual(showRouteTypes, flags))
                //{
                //    showRouteTypes = flags;
                //    lastRefreshFrame = 0;
                //}
                var paths = this.mPathsInfo.GetValue(viz) as Dictionary<InstanceID, PathVisualizer.Path>;
                if (paths == null) return;
                if (paths.Count != this.lastPathCount)
                {
                    this.lastPathCount = paths.Count;
                    //UnityEngine.Debug.Log($"new path count on {lastRefreshFrame}.");
                    if (this.lastRefreshFrame > 0)
                    {
                        this.lastRefreshFrame = -6; // give it 1/10th second to settle - avoids a double update when changing targets.
                    }
                }
                var instance = InstanceManager.instance.GetSelectedInstance();
                if (instance != this.lastInstance)
                {
                    this.lastInstance = instance;
                    //UnityEngine.Debug.Log($"new instance on {lastRefreshFrame}.");
                    foreach (var panel in this.panels.Values)
                    {
                        panel.SetTopTen(new string[0]);
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
            var pcs = this.GetPathCounts();
            var messages = pcs.OrderByDescending(x => x.count.refs).Take(10).Select(x => x.Format()).ToArray();
            Log.Debug($"ranked {pcs.Count()} OD pairs, top {messages.Length} messages built in {sw.ElapsedMilliseconds}ms");
            foreach (var panel in panels.Values)
            {
                if (panel != null)
                {
                    panel.SetTopTen(messages);
                }
            }
            var message = string.Join("\n", messages);
            if (message == string.Empty)
            {
                //UnityEngine.Debug.Log("Nothing to say.");
            }
            else if (message == lastMessage)
            {
                //UnityEngine.Debug.Log("Nothing new to say.");
            }
            //else if (lastRefreshFrame % 180 == 1)
            else
            {
                //UnityEngine.Debug.Log("\n" + message);
                lastMessage = message;
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

        public string Format()
        {
            var routeLabel = this.count.refs == 1 ? "route" : "routes";
            return $"{this.from} to {this.to} ({this.count.refs} {routeLabel})\n";
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
