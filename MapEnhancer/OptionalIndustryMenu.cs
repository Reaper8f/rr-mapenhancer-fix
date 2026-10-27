using System;
using System.Reflection;
using Model.Ops;

namespace MapEnhancer;

internal sealed class OptionalIndustryMenu
{
    internal const string ModIdentifier = "crzbp/a-pretty-convenient-menu";
    internal const string DependencyExpression = ModIdentifier + " ^0.1.0";
    internal const string WindowTypeName = "PrettyConvenientMenu.UI.Windows.IndustryProductionWindow";
    private readonly Func<bool> _isEnabled;
    private readonly Func<Type?> _resolveWindowType;
    private readonly Action<string> _log;
    private readonly Func<bool> _isReady;
    private MethodInfo? _open;
    private bool _reportedFailure;

    internal OptionalIndustryMenu(Func<bool> isEnabled, Func<Type?> resolveWindowType, Action<string> log,
        Func<bool>? isReady = null)
    {
        _isEnabled = isEnabled;
        _resolveWindowType = resolveWindowType;
        _log = log;
        _isReady = isReady ?? (() => true);
    }

    internal bool IsAvailable
    {
        get
        {
            try
            {
                // Disabled DLL mods can remain in the process. Check the native loader each time.
                if (!_isEnabled()) { Reset(); return false; }
                if (!_isReady()) { Reset(); return false; }
                if (_open != null) return true;
                var type = _resolveWindowType();
                var method = type?.GetMethod("Open", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Industry) }, null);
                if (method == null || method.ReturnType != type || method.ContainsGenericParameters)
                {
                    ReportFailure("A Pretty Convenient Menu's industry window is unavailable or incompatible.");
                    return false;
                }
                _open = method;
                return true;
            }
            catch (Exception error)
            {
                ReportFailure(error.GetBaseException().Message);
                return false;
            }
        }
    }

    internal bool TryOpen(Industry? industry)
    {
        if (industry == null || !IsAvailable) return false;
        try
        {
            bool opened = _open!.Invoke(null, new object[] { industry }) != null;
            if (opened) _reportedFailure = false;
            return opened;
        }
        catch (Exception error)
        {
            ReportFailure(error.GetBaseException().Message);
            return false;
        }
    }

    internal void ReportFailure(string message)
    {
        if (_reportedFailure) return;
        _reportedFailure = true;
        try { _log("Could not open the optional industry monitor; keeping normal map click behavior. " + message); }
        catch { /* Optional diagnostics must not interfere with map input. */ }
    }

    internal void Reset()
    {
        _open = null;
        _reportedFailure = false;
    }
}
