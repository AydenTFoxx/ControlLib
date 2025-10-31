using System.Collections.Generic;
using System.Security.Permissions;
using BepInEx;
using BepInEx.Logging;
using ControlLib.Possession;
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
    public const string PLUGIN_VERSION = "0.4.3";

    internal static new IMyLogger? Logger { get; private set; }

    private static readonly Dictionary<string, ConfigValue> TempOptions = [];

    static Main()
    {
        TempOptions.Add("default_possession_potential", new ConfigValue(360));
        TempOptions.Add("attuned_possession_potential", new ConfigValue(480));
        TempOptions.Add("hardmode_possession_potential", new ConfigValue(240));
        TempOptions.Add("sofanthiel_possession_potential", new ConfigValue(1));

        TempOptions.Add("attuned_slugcats", new ConfigValue("Monk,Saint"));
        TempOptions.Add("hardmode_slugcats", new ConfigValue("Artificer,Hunter,Inv"));
    }

    public Main()
        : base(new Options())
    {
        LogLevel maxLevel = OptionUtils.IsOptionEnabled("modlib.debug")
            ? LogLevel.All
            : LogLevel.Warning;

        Logger = new LogWrapper(base.Logger, maxLevel);
    }

    public override void OnEnable()
    {
        if (IsModEnabled) return;

        base.OnEnable();

        Keybinds.InitKeybinds();

        foreach (KeyValuePair<string, ConfigValue> optionPair in TempOptions)
        {
            OptionUtils.SharedOptions.AddTemporaryOption(optionPair.Key, optionPair.Value);
        }
    }

    public override void OnDisable()
    {
        if (!IsModEnabled) return;

        base.OnDisable();

        foreach (string optionKey in TempOptions.Keys)
        {
            OptionUtils.SharedOptions.RemoveTemporaryOption(optionKey);
        }
    }

    protected override void ApplyHooks()
    {
        base.ApplyHooks();

        PossessionHooks.ApplyHooks();
    }

    protected override void RemoveHooks()
    {
        base.RemoveHooks();

        PossessionHooks.RemoveHooks();
    }
}