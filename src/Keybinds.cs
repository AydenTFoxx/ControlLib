using System;
using ImprovedInput;
using ModLib;
using ModLib.Input;
using ModLib.Options;
using UnityEngine;

namespace ControlLib;

/// <summary>
/// Static handler for this mod's keybinds.
/// </summary>
public static class Keybinds
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    public static Keybind POSSESS { get; private set; }
    public static Keybind MIND_BLAST { get; private set; }

    public static Keybind POSSESS_ITEM { get; private set; }

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    /// <summary>
    /// Initializes all keybinds of the mod.
    /// </summary>
    public static void InitKeybinds()
    {
        POSSESS = Keybind.Register("Possess", KeyCode.V, KeyCode.Joystick1Button1);
        MIND_BLAST = Keybind.Register("Mind Blast", KeyCode.B, KeyCode.Joystick1Button2);

        POSSESS_ITEM = Keybind.Register("Possess Item", KeyCode.F, KeyCode.Joystick1Button10);

        MIND_BLAST.ToggleIICKeybind(Options.MIND_BLAST.Value);
        POSSESS_ITEM.ToggleIICKeybind(OptionUtils.IsOptionEnabled("modlib.debug"));
    }

    public static void ToggleIICKeybind(this Keybind self, bool enable)
    {
        if (!Extras.IsIICEnabled) return;

        try
        {
            ImprovedInputAccess.ToggleKeybind(self, enable);
        }
        catch (Exception ex)
        {
            Main.Logger.LogError($"Failed to toggle Mind Blast option: {ex}");
        }
    }

    private static class ImprovedInputAccess
    {
        public static void ToggleKeybind(Keybind keybind, bool enable) => ((PlayerKeybind)keybind).HideConfig = !enable;
    }
}