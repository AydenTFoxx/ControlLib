using System;
using System.Collections.Generic;
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
using ModLib.Options;

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

namespace ControlLib;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public class Main : ModPlugin
{
    public const string PLUGIN_NAME = "ControlLib";
    public const string PLUGIN_GUID = "ynhzrfxn.controllib";
    public const string PLUGIN_VERSION = "0.5.0";

    internal static new ModLogger Logger { get; private set; }

    internal static new ManualLogSource LogSource
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
        private set
        {
            if (field is not null)
            {
                BepInEx.Logging.Logger.Sources.Remove(field);
            }

            field = value;
        }
    }

    private static ModLogger? RWCustomLogger;

    private static readonly Dictionary<string, ConfigValue> TempOptions = [];

#nullable disable warnings

    static Main()
    {
        TempOptions.Add("default_possession_potential", new ConfigValue(360));
        TempOptions.Add("attuned_possession_potential", new ConfigValue(480));
        TempOptions.Add("hardmode_possession_potential", new ConfigValue(240));
        TempOptions.Add("sofanthiel_possession_potential", new ConfigValue(1));

        TempOptions.Add("attuned_slugcats", new ConfigValue("Monk,Saint"));
        TempOptions.Add("hardmode_slugcats", new ConfigValue("Artificer,Hunter,Inv"));

        TempOptions.Add("mind_blast_stun_factor", new ConfigValue(600f));
        TempOptions.Add("stun_death_threshold", new ConfigValue(100));
    }

#nullable restore warnings

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

        foreach (KeyValuePair<string, ConfigValue> optionPair in TempOptions)
        {
            OptionUtils.SharedOptions.AddTemporaryOption(optionPair.Key, optionPair.Value, false);
        }

        try
        {
            if (Achievements.implementation is null)
            {
                Logger.LogMessage($"Setting AchievementsImpl instance to {nameof(Achievements.StandaloneAchievementsImpl)}");

                Achievements.implementation = new Achievements.StandaloneAchievementsImpl();
                Achievements.implementation.Initialize();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Could not replace AchievementsImpl instance: {ex}");
        }
    }

    public override void OnDisable()
    {
        if (!IsModEnabled) return;

        base.OnDisable();

        AbstractObjectTypes.UnregisterValues();

        foreach (string optionKey in TempOptions.Keys)
        {
            OptionUtils.SharedOptions.RemoveTemporaryOption(optionKey);
        }
    }

    protected override void ApplyHooks()
    {
        base.ApplyHooks();

        DeathProtectionHooks.ApplyHooks();

        PossessionHooks.ApplyHooks();

        TelekineticsHooks.ApplyHooks();

        On.RainWorld.PostModsInit += PostModsInitHook;
    }

    protected override void RemoveHooks()
    {
        base.RemoveHooks();

        DeathProtectionHooks.RemoveHooks();

        PossessionHooks.RemoveHooks();

        TelekineticsHooks.RemoveHooks();

        On.RainWorld.PostModsInit -= PostModsInitHook;

        if (RWCustomLogger is not null)
        {
            On.RWCustom.Custom.Log -= RedirectCustomLoggingHook;
            On.RWCustom.Custom.LogImportant -= RedirectImportantCustomLogsHook;
            On.RWCustom.Custom.LogWarning -= RedirectWarningCustomLogsHook;

            RWCustomLogger = null;
        }
    }

    private void PostModsInitHook(On.RainWorld.orig_PostModsInit orig, RainWorld self)
    {
        orig.Invoke(self);

        if (RainWorld.ShowLogs && !Extras.IsMeadowEnabled && RWCustomLogger is null)
        {
            RWCustomLogger = LoggingAdapter.CreateLogger(BepInEx.Logging.Logger.CreateLogSource("RWCustom"));

            On.RWCustom.Custom.Log += RedirectCustomLoggingHook;
            On.RWCustom.Custom.LogImportant += RedirectImportantCustomLogsHook;
            On.RWCustom.Custom.LogWarning += RedirectWarningCustomLogsHook;
        }
    }

    private void RedirectCustomLoggingHook(On.RWCustom.Custom.orig_Log orig, string[] values)
    {
        orig.Invoke(values);

        RWCustomLogger?.LogInfo(string.Join(" ", values));
    }

    private static void RedirectImportantCustomLogsHook(On.RWCustom.Custom.orig_LogImportant orig, string[] values)
    {
        orig.Invoke(values);

        RWCustomLogger?.LogMessage(string.Join(" ", values));
    }

    private static void RedirectWarningCustomLogsHook(On.RWCustom.Custom.orig_LogWarning orig, string[] values)
    {
        orig.Invoke(values);

        RWCustomLogger?.LogWarning(string.Join(" ", values));
    }
}