using System;
using System.Collections.Generic;
using System.Security.Permissions;
using BepInEx;
using ControlLib.Possession;
using ModLib;
using ModLib.Logging;
using ModLib.Options;
using MoreSlugcats;
using UnityEngine;

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

namespace ControlLib;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public class Main : ModPlugin
{
    public const string PLUGIN_NAME = "ControlLib";
    public const string PLUGIN_GUID = "ynhzrfxn.controllib";
    public const string PLUGIN_VERSION = "0.4.5";

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

        TempOptions.Add("mind_blast_stun_factor", new ConfigValue(600f));
        TempOptions.Add("stun_death_threshold", new ConfigValue(100));
    }

    public Main()
        : base(new Options())
    {
        Logger = new LogWrapper(base.Logger);
    }

    public override void OnEnable()
    {
        if (IsModEnabled) return;

        base.OnEnable();

        Keybinds.InitKeybinds();

        foreach (KeyValuePair<string, ConfigValue> optionPair in TempOptions)
        {
            OptionUtils.SharedOptions.AddTemporaryOption(optionPair.Key, optionPair.Value, false);
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

    public static void ExplodePlayer(Player player, AbstractPhysicalObject.AbstractObjectType bombType, Action<PhysicalObject>? onRealizedCallback = null) =>
        ExplodePos(player, player.abstractCreature.pos, bombType, onRealizedCallback);

    public static void ExplodePos(Player player, WorldCoordinate pos, AbstractPhysicalObject.AbstractObjectType bombType, Action<PhysicalObject>? onRealizedCallback = null)
    {
        AbstractPhysicalObject abstractBomb = new(
            player.abstractCreature.world,
            bombType,
            null,
            pos,
            player.abstractCreature.world.game.GetNewID()
        );

        abstractBomb.RealizeInRoom();

        PhysicalObject? realizedBomb = abstractBomb.realizedObject;

        if (realizedBomb is null)
        {
            Logger?.LogWarning($"Failed to realize explosion for {player}! Destroying abstract object.");

            abstractBomb.Destroy();
            return;
        }

        if (realizedBomb is Weapon weapon)
        {
            weapon.thrownBy = player;
        }

        realizedBomb.CollideWithObjects = false;

        onRealizedCallback?.Invoke(realizedBomb);

        if (realizedBomb is ScavengerBomb scavBomb)
        {
            scavBomb.Explode(scavBomb.thrownClosestToCreature?.mainBodyChunk);
        }
        else if (realizedBomb is SingularityBomb singularity)
        {
            if (singularity.zeroMode)
                singularity.explodeColor = new Color(1f, 0.2f, 0.2f);

            singularity.Explode();
        }
        else
        {
            Logger?.LogWarning($"{realizedBomb} is not a supported kaboom type; Destroying object.");

            realizedBomb.Destroy();
            abstractBomb.Destroy();
        }
    }
}