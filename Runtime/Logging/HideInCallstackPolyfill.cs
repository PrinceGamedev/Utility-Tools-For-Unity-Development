#if !UNITY_2022_2_OR_NEWER
namespace UnityEngine
{
    // Unity 2022.2+ hides methods marked [HideInCallstack] from Console stack traces (so double-click
    // opens the caller, not the logger). Older editors don't know the attribute; this no-op keeps the
    // logging code compiling there.
    [System.AttributeUsage(System.AttributeTargets.Method)]
    internal sealed class HideInCallstackAttribute : System.Attribute { }
}
#endif
