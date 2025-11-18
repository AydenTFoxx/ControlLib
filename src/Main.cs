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
using Mono.Cecil.Cil;
using MonoMod.Cil;

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

    internal static new IMyLogger? Logger { get; private set; }

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

    private static IMyLogger? RWCustomLogger;

    private static readonly Dictionary<string, ConfigValue> TempOptions = [];

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

    public Main()
        : base(new Options(), new LogWrapper(LoggingAdapter.CreateLogger(LogSource)))
    {
        Logger = base.Logger;
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

#if DEBUG
        try
        {
            if (Achievements.implementation is null)
            {
                Logger?.LogMessage($"Setting AchievementsImpl instance to {nameof(Achievements.StandaloneAchievementsImpl)}");

                Achievements.implementation = new Achievements.StandaloneAchievementsImpl();
                Achievements.implementation.Initialize();
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"Could not replace AchievementsImpl instance: {ex}");
        }
#endif
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
            IL.RWCustom.Custom.LogImportant -= Extras.WrapILHook(RedirectImportantCustomLogsILHook);
            IL.RWCustom.Custom.LogWarning -= Extras.WrapILHook(RedirectWarningCustomLogsILHook);

            On.RWCustom.Custom.Log -= RedirectCustomLoggingHook;
        }
    }

    private void PostModsInitHook(On.RainWorld.orig_PostModsInit orig, RainWorld self)
    {
        orig.Invoke(self);

        if (RainWorld.ShowLogs && !Extras.IsMeadowEnabled)
        {
            RWCustomLogger = LoggingAdapter.CreateLogger(BepInEx.Logging.Logger.CreateLogSource("RWCustom"));

            IL.RWCustom.Custom.LogImportant += Extras.WrapILHook(RedirectImportantCustomLogsILHook);
            IL.RWCustom.Custom.LogWarning += Extras.WrapILHook(RedirectWarningCustomLogsILHook);

            On.RWCustom.Custom.Log += RedirectCustomLoggingHook;
        }
    }

    private void RedirectCustomLoggingHook(On.RWCustom.Custom.orig_Log orig, string[] values)
    {
        orig.Invoke(values);

        RWCustomLogger?.LogInfo(string.Join(" ", values));
    }

    private static void RedirectImportantCustomLogsILHook(ILContext context)
    {
        ILCursor c = new(context);
        ILLabel? target = null;

        c.GotoNext(MoveType.Before,
            static x => x.MatchLdsfld(typeof(RainWorld).GetField(nameof(RainWorld.ShowLogs))),
            x => x.MatchBrfalse(out target)
        ).MoveAfterLabels();

        // Target: if (RainWorld.ShowLogs) { UnityEngine.Debug.Log(string.Join(" ", values)); }
        //                                  ^ HERE (Prepend)

        c.Emit(OpCodes.Ldc_I4_0).Emit(OpCodes.Ldarg_1).EmitDelegate(LogToCustomLogger);

        // Result: if (RainWorld.ShowLogs) { RedirectCustomLogging(false, values); UnityEngine.Debug.Log(string.Join(" ", values)); }
    }

    private static void RedirectWarningCustomLogsILHook(ILContext context)
    {
        ILCursor c = new(context);
        ILLabel? target = null;

        c.GotoNext(MoveType.Before,
            static x => x.MatchLdsfld(typeof(RainWorld).GetField(nameof(RainWorld.ShowLogs))),
            x => x.MatchBrfalse(out target)
        ).MoveAfterLabels();

        // Target: if (RainWorld.ShowLogs) { UnityEngine.Debug.Log(string.Join(" ", values)); }
        //                                  ^ HERE (Prepend)

        c.Emit(OpCodes.Ldc_I4_1).Emit(OpCodes.Ldarg_1).EmitDelegate(LogToCustomLogger);

        // Result: if (RainWorld.ShowLogs) { RedirectCustomLogging(true, values); UnityEngine.Debug.LogWarning(string.Join(" ", values)); }
    }

    private static void LogToCustomLogger(bool isWarning, params string[] values)
    {
        if (RWCustomLogger is null) return;

        if (isWarning)
            RWCustomLogger.LogWarning(string.Join(" ", values));
        else
            RWCustomLogger.LogMessage(string.Join(" ", values));
    }
}