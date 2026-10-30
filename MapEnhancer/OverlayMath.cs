using System;

namespace MapEnhancer
{
    public enum TrackOverlayMode { Routes, Grade, TrackClass, Condition, Cleanliness }

    internal static class OverlayMath
    {
        internal static int GradeBand(float percent)
        {
            if (float.IsNaN(percent) || float.IsInfinity(percent)) return -1;
            percent = Math.Abs(percent);
            return percent < 0.1f ? 0 : percent < 1f ? 1 : percent < 2f ? 2 : percent < 3f ? 3 : 4;
        }

        internal static int UnitBand(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return -1;
            return value <= 0f ? 0 : value < 0.25f ? 1 : value < 0.5f ? 2 : value < 0.75f ? 3 : 4;
        }

        internal static int ClassBand(int value) => value >= 0 && value <= 5 ? value : -1;
    }
}
