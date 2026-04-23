namespace Breakdown
{
    internal static class Log
    {
        private const string Prefix = "[BreakdownRevisited] ";

        internal static void Info(string message)
            => UnityEngine.Debug.Log(Prefix + message);

        internal static void Debug(string message)
        {
            if (BreakdownMod.DebugLog)
                UnityEngine.Debug.Log(Prefix + message);
        }
    }
}
