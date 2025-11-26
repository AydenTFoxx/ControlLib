using System;
using System.Linq;
using System.Security.Permissions;
using BepInEx;
using BepInEx.Logging;
using ControlLib.Enums;
using ControlLib.Possession;
using ControlLib.Telekinetics;
using Kittehface.Framework20;
using ModLib;
using ModLib.Logging;

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

namespace ControlLib;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public class Main : ModPlugin
{
    public const string PLUGIN_NAME = "ControlLib";
    public const string PLUGIN_GUID = "ynhzrfxn.controllib";
    public const string PLUGIN_VERSION = "0.6.0";

#nullable disable warnings

    internal static new ModLogger Logger { get; private set; }

#nullable restore warnings

    private static new ManualLogSource LogSource
    {
        get
        {
            if (field is null)
            {
                BepInEx.Logging.Logger.Sources.Remove(BepInEx.Logging.Logger.Sources.FirstOrDefault(s => s.SourceName.Equals(PLUGIN_NAME)));

                field = BepInEx.Logging.Logger.CreateLogSource(PLUGIN_NAME);
            }
            return field;
        }
        set
        {
            if (field is not null)
            {
                BepInEx.Logging.Logger.Sources.Remove(field);
            }

            field = value;
        }
    }

    public Main()
        : base(new Options(), LoggingAdapter.CreateLogger(LogSource, true))
    {
        Logger = base.Logger ?? new FallbackLogger(LogSource);
    }

    public override void OnEnable()
    {
        if (IsModEnabled) return;

        base.OnEnable();

        Keybinds.InitKeybinds();

        AbstractObjectTypes.RegisterValues();

        if (Achievements.implementation is null)
        {
            try
            {
                Logger.LogMessage($"Setting AchievementsImpl instance to {nameof(Achievements.StandaloneAchievementsImpl)}");

                Achievements.implementation = new Achievements.StandaloneAchievementsImpl();
                Achievements.implementation.Initialize();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Could not replace AchievementsImpl instance: {ex}");
            }
        }
    }

    public override void OnDisable()
    {
        if (!IsModEnabled) return;

        base.OnDisable();

        AbstractObjectTypes.UnregisterValues();
    }

    protected override void ApplyHooks()
    {
        base.ApplyHooks();

        DeathProtectionHooks.ApplyHooks();

        PossessionHooks.ApplyHooks();

        TelekineticsHooks.ApplyHooks();
    }

    protected override void RemoveHooks()
    {
        base.RemoveHooks();

        DeathProtectionHooks.RemoveHooks();

        PossessionHooks.RemoveHooks();

        TelekineticsHooks.RemoveHooks();
    }
}