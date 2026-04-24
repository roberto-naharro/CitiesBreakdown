using UnityEngine;

namespace Breakdown
{
    internal static class BreakdownStyle
    {
        public static readonly Color32 MutedColor  = new Color32(160, 160, 160, 255);
        public static readonly Color32 ActiveColor = new Color32(255, 255, 255, 255);
        public static readonly Color32 BgColor     = new Color32( 20,  20,  20, 235);
        public static readonly Color32 WarnColor   = new Color32(255, 200,  50, 255);
        public static readonly Color32 AlertColor  = new Color32(255,  80,  80, 255);
        public const float TextScale = 0.7f;
    }
}
