using System;
using UnityEngine;

namespace MapEnhancer.Native;

[Serializable]
public sealed class KeyBinding
{
    public KeyCode keyCode;
    public int modifiers;

    public bool Down() => Input.GetKeyDown(keyCode) && CurrentModifiers == modifiers;

    internal static int CurrentModifiers =>
        ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) ? 1 : 0) |
        ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 2 : 0) |
        ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) ? 4 : 0);

    public override string ToString() =>
        ((modifiers & 1) != 0 ? "Ctrl+" : "") +
        ((modifiers & 2) != 0 ? "Shift+" : "") +
        ((modifiers & 4) != 0 ? "Alt+" : "") + keyCode;
}

internal static class SettingsUi
{
    private static KeyBinding? _capturing;
    public static float Scale(float value) => value;
    internal static void Reset() => _capturing = null;

    public static void DrawKeybindingSmart(KeyBinding binding, string label, object? unused, params GUILayoutOption[] options)
    {
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label(label);
            if (GUILayout.Button(ReferenceEquals(_capturing, binding) ? "Press a key (Esc cancels)" : binding.ToString(), options))
                _capturing = ReferenceEquals(_capturing, binding) ? null : binding;
        }
        Event current = Event.current;
        if (!ReferenceEquals(_capturing, binding) || current.type != EventType.KeyDown) return;
        if (current.keyCode == KeyCode.Escape) { _capturing = null; current.Use(); return; }
        if (current.keyCode == KeyCode.None || current.keyCode == KeyCode.LeftControl || current.keyCode == KeyCode.RightControl ||
            current.keyCode == KeyCode.LeftShift || current.keyCode == KeyCode.RightShift ||
            current.keyCode == KeyCode.LeftAlt || current.keyCode == KeyCode.RightAlt) return;
        binding.keyCode = current.keyCode;
        binding.modifiers = (current.control ? 1 : 0) | (current.shift ? 2 : 0) | (current.alt ? 4 : 0);
        _capturing = null;
        current.Use();
        Loader.SaveSettings();
    }

    public static bool PopupToggleGroup(ref int selected, string[] choices, object? unused, params GUILayoutOption[] options)
    {
        int next = GUILayout.SelectionGrid(selected, choices, Math.Max(1, choices.Length), options);
        if (next == selected) return false;
        selected = next;
        return true;
    }
}
