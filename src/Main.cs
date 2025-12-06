using System.Security.Permissions;
using BepInEx;
using ControlLib.Enums;
using ControlLib.Possession;
using ControlLib.Telekinetics;
using ModLib;
using ModLib.Logging;

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

namespace ControlLib;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public class Main : ModPlugin
{
    public const string PLUGIN_NAME = "Possessions";
    public const string PLUGIN_GUID = "ynhzrfxn.controllib";
    public const string PLUGIN_VERSION = "0.8.0";

#nullable disable warnings

    internal static new ModLogger Logger { get; private set; }

#nullable restore warnings

    public Main()
        : base(new Options())
    {
        Logger = new FilteredLogWrapper(base.Logger);
    }

    public override void OnEnable()
    {
        if (IsModEnabled) return;

        base.OnEnable();

        Keybinds.InitKeybinds();

        AbstractObjectTypes.RegisterValues();
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