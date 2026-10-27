using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public enum FindObjectsInactive { Exclude, Include }
    public enum FindObjectsSortMode { None, InstanceID }

    // These doubles model only the public contracts exercised by toolbar ownership.
    public class Object
    {
        public static Object[] Existing { get; set; } = [];
        public bool Destroyed { get; set; }
        public bool Active { get; set; } = true;

        public static T[] FindObjectsByType<T>(FindObjectsInactive inactive, FindObjectsSortMode sort) where T : Object
        {
            if (inactive != FindObjectsInactive.Include || sort != FindObjectsSortMode.None)
                throw new InvalidOperationException("Toolbar discovery must include inactive windows without sorting.");
            return Existing.OfType<T>().ToArray();
        }

        public static bool operator ==(Object? left, Object? right)
        {
            bool leftNull = left is null || left.Destroyed;
            bool rightNull = right is null || right.Destroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(left, right);
        }

        public static bool operator !=(Object? left, Object? right) => !(left == right);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}

namespace UI.Map
{
    public sealed class MapWindow : UnityEngine.Object
    {
        public static int SingletonReads { get; private set; }
        public static MapWindow instance
        {
            get
            {
                SingletonReads++;
                throw new NullReferenceException("WindowManager has not been initialized.");
            }
        }

        public PopupMenu? _displayMenu;
        public ToolbarPanel? _toolbarPanel;
        public void BuildDisplayOptionsButton() { }
    }

    public sealed class PopupMenu
    {
        public int Dismissals { get; private set; }
        public void Dismiss() => Dismissals++;
    }

    public sealed class ToolbarPanel
    {
        public int Rebuilds { get; private set; }
        public bool ThrowOnRebuild { get; set; }
        public void SetNeedsRebuild()
        {
            Rebuilds++;
            if (ThrowOnRebuild) throw new InvalidOperationException("Toolbar is unavailable.");
        }
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefix : Attribute { }
}
