using System;
using ImprovedInput;
using ModLib;
using ModLib.Input;
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

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    /// <summary>
    /// Initializes all keybinds of the mod.
    /// </summary>
    public static void InitKeybinds()
    {
        POSSESS = Keybind.Register("Possess", KeyCode.V, KeyCode.Joystick1Button0);
        MIND_BLAST = Keybind.Register("Mind Blast", KeyCode.B, KeyCode.Joystick1Button1);

        ToggleMindBlast(Options.MIND_BLAST.Value);
    }

    public static void ToggleMindBlast(bool enable)
    {
        if (!Extras.IsIICEnabled) return;

        try
        {
            ImprovedInputAccess.ToggleMindBlast(enable);
        }
        catch (Exception ex)
        {
            Main.Logger?.LogError($"Failed to toggle Mind Blast option: {ex}");
        }
    }

    private static class ImprovedInputAccess
    {
        public static void ToggleMindBlast(bool enable) => ((PlayerKeybind)MIND_BLAST).HideConfig = !enable;
    }
}