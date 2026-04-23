using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Breakdown
{
    public static class CitiesExtensions
    {
        public static Vector3 GetSegmentLocation(this ushort segment)
            => NetManager.instance.m_segments.m_buffer[segment].m_middlePosition;
        public static byte GetDistrict(this Vector3 location)
            => DistrictManager.instance.SampleDistrict(location);
        public static string GetDistrictName(this Vector3 location)
            => GameAreaManager.instance.PointOutOfArea(location) ? "Out of town" : location.GetDistrict().GetDistrictName();
        public static string GetDistrictName(this byte districtId)
            => districtId == 0 ? "No district" : DistrictManager.instance.GetDistrictName(districtId);
        public static string GetSegmentName(this ushort segmentId)
            => NetManager.instance.GetSegmentName(segmentId);

        public static bool UnusedOrEmpty(this PathUnit unit)
            => unit.m_referenceCount == 0 || unit.m_positionCount == 0;

        public static Dictionary<uint, uint> GetPathTails(this PathUnit[] pathBuffer)
        {
            Dictionary<uint, uint> tails = new Dictionary<uint, uint>();
            foreach (var index in Enumerable.Range(0, pathBuffer.Length))
            {
                var path = pathBuffer[index];
                if (path.UnusedOrEmpty())
                {
                    continue;
                }
                if (path.m_nextPathUnit == 0)
                {
                    continue;
                }
                if (tails.ContainsKey(path.m_nextPathUnit))
                {
                    continue;
                }
                tails[path.m_nextPathUnit] = (uint)index;
            }
            return tails;
        }
    }
}
