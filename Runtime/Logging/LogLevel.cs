namespace UtilityTools.Logging
{
    /// <summary>Severity of a log. Verbose / Info / Warning are compiled out of release builds.</summary>
    public enum LogLevel
    {
        Verbose = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }
}
