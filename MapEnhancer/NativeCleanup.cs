using System;

namespace MapEnhancer.Native;

internal static class NativeCleanup
{
    internal static void Run(Action action, Action<Exception> reportFailure)
    {
        try { action(); }
        catch (Exception error)
        {
            // Cleanup and its diagnostics must not replace the original load failure.
            try { reportFailure(error); }
            catch { }
        }
    }
}
