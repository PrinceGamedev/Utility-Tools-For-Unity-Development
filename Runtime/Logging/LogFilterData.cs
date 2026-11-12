using UnityEngine;

namespace UtilityTools.Logging
{
    /// <summary>
    /// The tags a player build keeps, written into the build by the editor (Project Settings → Utility
    /// Tools → AI Console → Logs in builds) and read by <see cref="LogFilter"/> when the player starts.
    /// It exists only while a build runs; you never create it yourself.
    /// </summary>
    public sealed class LogFilterData : ScriptableObject
    {
        internal const string ResourceName = "UtilityToolsLogFilter";

        public string[] tags = new string[0];
        public bool keepUntagged;
        public bool keepErrors = true;
    }
}
