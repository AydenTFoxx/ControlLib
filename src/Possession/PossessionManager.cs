using System;
using System.Collections.Generic;
using System.Text;
using ControlLib.Meadow;
using ControlLib.Possession.Graphics;
using ControlLib.Telekinetics;
using ModLib;
using ModLib.Collections;
using ModLib.Input;
using ModLib.Meadow;
using MoreSlugcats;
using RWCustom;
using UnityEngine;
using Random = UnityEngine.Random;
using static ModLib.Options.OptionUtils;

namespace ControlLib.Possession;

/// <summary>
/// Stores and manages the player's possessed creatures.
/// </summary>
/// <param name="player">The player itself.</param>
public sealed class PossessionManager : IDisposable
{
    private static readonly List<Type> BannedCreatureTypes = [typeof(Player), typeof(Overseer)];

    public int PossessionTimePotential { get; }
    public bool IsHardmodeSlugcat { get; }
    public bool IsAttunedSlugcat { get; }
    public int MaxPossessionTime => PossessionTimePotential + ((player.room?.game.GetStorySession?.saveState.deathPersistentSaveData.karma ?? 0) * 40);

    private readonly WeakCollection<Creature> MyPossessions = [];
    private Player player;
    private PossessionTimer possessionTimer;

    private bool didReachLowPossessionTime;

    private bool disposedValue;

    public TargetSelector TargetSelector { get; private set; }

    public int PossessionCooldown { get; set; }
    public float PossessionTime { get; set; }

    public bool IsPossessing => MyPossessions.Count > 0;
    public bool LowPossessionTime => IsPossessing && PossessionTime / MaxPossessionTime < 0.34f;

    public PossessionManager(Player player)
    {
        this.player = player;

        string attunedSlugcats = FormatSlugcatNames(GetOptionValue<string>("attuned_slugcats"));
        string hardmodeSlugcats = FormatSlugcatNames(GetOptionValue<string>("hardmode_slugcats"));

        IsAttunedSlugcat = attunedSlugcats.Contains(player.SlugCatClass.ToString());

        IsHardmodeSlugcat = hardmodeSlugcats.Contains(player.SlugCatClass.ToString());

        PossessionTimePotential = player.SlugCatClass == MoreSlugcatsEnums.SlugcatStatsName.Sofanthiel
            ? GetOptionValue<int>("sofanthiel_possession_potential")
            : IsAttunedSlugcat
                ? GetOptionValue<int>("attuned_possession_potential")
                : IsHardmodeSlugcat
                    ? GetOptionValue<int>("hardmode_possession_potential")
                    : GetOptionValue<int>("default_possession_potential");

        PossessionTime = MaxPossessionTime;

        TargetSelector = new(player, this);

        possessionTimer = new(this);

        static string FormatSlugcatNames(string? slugcatNames)
        {
            if (string.IsNullOrWhiteSpace(slugcatNames)) return string.Empty;

            StringBuilder stringBuilder = new(slugcatNames);

            stringBuilder.Replace("Hunter", "Red")
                .Replace("Monk", "Yellow")
                .Replace("Survivor", "White")
                .Replace("Inv", "Sofanthiel")
                .Replace("Enot", "Sofanthiel")
                .Replace("Gorbo", "Sofanthiel");

            return stringBuilder.ToString();
        }
    }

    /// <summary>
    /// Retrieves the player associated with this <c>PossessionManager</c> instance.
    /// </summary>
    /// <returns>The <c>Player</c> who owns this manager instance.</returns>
    public Player GetPlayer() => player;

    /// <summary>
    /// Determines if the player is allowed to start a new possession.
    /// </summary>
    /// <returns><c>true</c> if the player can use their possession ability, <c>false</c> otherwise.</returns>
    public bool CanPossessCreature() => PossessionTime > 0 && PossessionCooldown == 0;

    /// <summary>
    /// Determines if the player can possess the given creature.
    /// </summary>
    /// <param name="target">The creature to be tested.</param>
    /// <returns><c>true</c> if the player can use their possession ability, <c>false</c> otherwise.</returns>
    public bool CanPossessCreature(Creature target) =>
        CanPossessCreature()
        && !IsBannedPossessionTarget(target)
        && IsPossessionValid(target);

    public static bool IsBannedPossessionTarget(Creature target) =>
        target is null or { dead: true } or { abstractCreature.controlled: true }
        || BannedCreatureTypes.Contains(target.GetType());

    /// <summary>
    /// Validates the player's possession of a given creature.
    /// </summary>
    /// <param name="target">The creature to be tested.</param>
    /// <returns><c>true</c> if this possession is valid, <c>false</c> otherwise.</returns>
    public bool IsPossessionValid(Creature target) => player.Consious && !target.dead && target.room == player.room;

    /// <summary>
    /// Determines if the player is currently possessing the given creature.
    /// </summary>
    /// <param name="target">The creature to be tested.</param>
    /// <returns><c>true</c> if the player is possessing this creature, <c>false</c> otherwise.</returns>
    public bool HasPossession(Creature target) => MyPossessions.Contains(target);

    /// <summary>
    /// Removes all possessions of the player. Possessed creatures will automatically stop their own possessions.
    /// </summary>
    public void ResetAllPossessions()
    {
        MyPossessions.Clear();

        player.controller = null;

        Main.Logger?.LogDebug($"{player} is no longer possessing anything.");
    }

    /// <summary>
    /// Initializes a new possession with the given creature as a target.
    /// </summary>
    /// <param name="target">The creature to possess.</param>
    public void StartPossession(Creature target)
    {
        if (PossessionTimePotential == 1
            && player.room is not null
            && !IsOptionEnabled(Options.INFINITE_POSSESSION)
            && !IsOptionEnabled(Options.WORLDWIDE_MIND_CONTROL))
        {
            AbstractPhysicalObject abstractBomb = new(
                player.room.world,
                AbstractPhysicalObject.AbstractObjectType.ScavengerBomb,
                null,
                player.abstractCreature.pos,
                player.room.world.game.GetNewID()
            );

            abstractBomb.RealizeInRoom();
            (abstractBomb.realizedObject as ScavengerBomb)?.Explode(player.mainBodyChunk);

            Main.Logger?.LogMessage($"{(Random.value < 0.5f ? "Game over" : "Goodbye")}, {player.SlugCatClass.ToString().Replace("Sofanthiel", "gamer")}.");
            return;
        }

        MyPossessions.Add(target);

        if (target.room is not null && target.room.BeingViewed)
        {
            target.room.AddObject(new TemplarCircle(target, target.mainBodyChunk.pos, 48f, 8f, 2f, 12, true));
            target.room.AddObject(new ShockWave(target.mainBodyChunk.pos, 100f, 0.08f, 4, false));
            target.room.PlaySound(SoundID.SS_AI_Give_The_Mark_Boom, target.mainBodyChunk, loop: false, 1f, 1.25f + (Random.value * 1.25f));
        }

        player.controller ??= GetFadeOutController(player);

        if (Extras.IsOnlineSession)
        {
            try
            {
                if (!MeadowUtils.IsMine(target))
                    MeadowUtils.RequestOwnership(target);

                MyRPCs.SyncCreaturePossession(target, isPossession: true);
            }
            catch (Exception ex)
            {
                Main.Logger?.LogError(ex);
            }
        }

        target.UpdateCachedPossession();
        target.abstractCreature.controlled = true;

        Main.Logger?.LogDebug($"{player}: Started possessing {target}.");
    }

    /// <summary>
    /// Interrupts the possession of the given creature.
    /// </summary>
    /// <param name="target">The creature to stop possessing.</param>
    public void StopPossession(Creature target)
    {
        MyPossessions.Remove(target);
        PossessionCooldown = 20;

        if (!IsPossessing)
        {
            player.controller = null;
        }

        if (PossessionTime == 0)
        {
            for (int k = 0; k < 20; k++)
            {
                player.room?.AddObject(new Spark(player.mainBodyChunk.pos, Custom.RNV() * Random.value * 40f, new Color(1f, 1f, 1f), null, 30, 120));
            }
        }

        if (target.room is not null && target.room.BeingViewed)
        {
            target.room.AddObject(new ReverseShockwave(target.mainBodyChunk.pos, 64f, 0.05f, 24));
            target.room.PlaySound(SoundID.HUD_Pause_Game, target.mainBodyChunk, loop: false, 1f, 0.5f);
        }

        if (Extras.IsOnlineSession)
        {
            try
            {
                MyRPCs.SyncCreaturePossession(target, isPossession: false);
            }
            catch (Exception ex)
            {
                Main.Logger?.LogError(ex);
            }
        }

        target.UpdateCachedPossession();
        target.abstractCreature.controlled = false;

        Main.Logger?.LogDebug($"{player}: Stopped possessing {target}.");
    }

    /// <summary>
    /// Updates the player's possession behaviors and controls.
    /// </summary>
    public void Update()
    {
        if (IsOptionEnabled(Options.MIND_BLAST) && UpdateMindBlast()) return;

        if (!TargetSelector.HasMindBlast && player.Consious && player.IsKeyDown(Keybinds.POSSESS, true))
        {
            TargetSelector.Update();
        }
        else if (TargetSelector.Input.InputTime > 0)
        {
            TargetSelector.ResetSelectorInput();

            if (!TargetSelector.HasMindBlast)
            {
                if (TargetSelector.HasTargetCursor && !TargetSelector.Input.QueriedCursor)
                {
                    TargetSelector.QueryTargetCursor();
                }

                if (TargetSelector.HasValidTargets)
                    TargetSelector.ApplySelectedTargets();
            }
            else
            {
                TargetSelector.MoveToState(TargetSelector.TargetSelectionState.Idle);
            }

            TargetSelector.ExceededTimeLimit = false;
        }

        if (IsPossessing)
        {
            if (player.graphicsModule is PlayerGraphics playerGraphics)
            {
                playerGraphics.LookAtNothing();
            }

            player.Blink(10);

            if (!IsOptionEnabled(Options.INFINITE_POSSESSION))
            {
                PossessionTime -= IsAttunedSlugcat ? 0.25f : 0.5f;
            }

            if (LowPossessionTime)
            {
                player.aerobicLevel += 0.0125f;
                player.airInLungs -= 1f / PossessionTimePotential;

                didReachLowPossessionTime = true;
            }

            if (PossessionTime <= 0f || !player.Consious)
            {
                Main.Logger?.LogDebug($"Forcing end of possession! Ran out of time? {PossessionTime <= 0f}");

                if (player.Consious)
                {
                    if (player.SlugCatClass == MoreSlugcatsEnums.SlugcatStatsName.Sofanthiel)
                    {
                        AbstractPhysicalObject abstractBomb = new(
                            player.abstractCreature.world,
                            DLCSharedEnums.AbstractObjectType.SingularityBomb,
                            null,
                            player.abstractCreature.pos,
                            player.abstractCreature.world.game.GetNewID()
                        );

                        abstractBomb.RealizeInRoom();

                        if (abstractBomb.realizedObject is SingularityBomb singularityBomb)
                        {
                            singularityBomb.thrownBy = player;

                            singularityBomb.zeroMode = true;
                            singularityBomb.explodeColor = new Color(1f, 0.2f, 0.2f);

                            singularityBomb.Explode();
                        }

                        Main.Logger?.LogMessage(Random.value < 0.5f ? "It's so over" : "Kaboom");
                        return;
                    }

                    player.aerobicLevel = 1f;
                    player.exhausted = true;

                    if (!IsAttunedSlugcat)
                    {
                        player.lungsExhausted = true;

                        if (player.redsIllness is not null && !player.redsIllness.curedForTheCycle)
                        {
                            player.redsIllness.fitSeverity = Custom.SCurve(Mathf.Pow(Random.value, Mathf.Lerp(3.4f, 0.4f, player.redsIllness.Severity)), 0.7f);
                            player.redsIllness.fitLength = Mathf.Lerp(80f, 240f, Mathf.Pow(Random.value, Mathf.Lerp(1.6f, 0.4f, (player.redsIllness.fitSeverity + player.redsIllness.Severity) / 2f)));
                            player.redsIllness.fitSeverity = Mathf.Pow(player.redsIllness.fitSeverity, Mathf.Lerp(1.4f, 0.4f, player.redsIllness.Severity));
                            player.redsIllness.fit += 1f / player.redsIllness.fitLength;
                        }
                        else if (IsHardmodeSlugcat && Random.value < 0.5f)
                        {
                            player.SaintStagger((int)(Random.value * 12f));
                        }
                        else
                        {
                            player.Stun(35);
                        }
                    }

                    PossessionTime = -80;
                }

                PossessionCooldown = 200;

                ResetAllPossessions();
            }
        }
        else if (PossessionCooldown > 0)
        {
            PossessionCooldown--;
        }
        else if (PossessionTime < MaxPossessionTime)
        {
            PossessionTime += IsHardmodeSlugcat ? 0.25f : 0.5f;
        }
        else if (PossessionTime > MaxPossessionTime)
        {
            PossessionTime = MaxPossessionTime;
        }

        if (didReachLowPossessionTime && !IsPossessing && PossessionCooldown == 0 && !player.submerged)
        {
            player.exhausted = IsHardmodeSlugcat || player.Malnourished || player.gourmandExhausted;

            player.airInLungs = Math.Min(player.airInLungs + (IsAttunedSlugcat ? 0.025f : 0.05f), 1f);
            player.aerobicLevel = Math.Max(player.aerobicLevel - (IsHardmodeSlugcat ? 0.05f : 0.025f), 0f);

            didReachLowPossessionTime = player.airInLungs == 1f;
        }
    }

    public bool UpdateMindBlast()
    {
        if (IsPossessing || !player.Consious) return false;

        bool keysPressed = player.IsKeyDown(Keybinds.MIND_BLAST, true) && player.IsKeyDown(Keybinds.POSSESS, true);

        if (keysPressed && !LowPossessionTime && PossessionCooldown == 0)
        {
            TargetSelector.ExceededTimeLimit = true;

            MindBlast.CreateInstance(player, (int)PossessionTime);

            PossessionTime = -120f;
        }

        if (MindBlast.TryGetInstance(player, out MindBlast instance))
        {
            if (keysPressed)
            {
                instance.pos = TargetSelector.GetTargetPos();

                PossessionCooldown = 80;
            }
            else
            {
                instance.Interrupt();
            }
            return true;
        }

        return false;
    }

    public static FadeOutController GetFadeOutController(Player player)
    {
        Player.InputPackage input = player.GetRawInput();

        return new FadeOutController(input.x, player.standing ? 1 : input.y);
    }

    /// <summary>
    /// Retrieves a <c>string</c> representation of this <c>PossessionManager</c> instance.
    /// </summary>
    /// <returns>A <c>string</c> containing the instance's values and possessions.</returns>
    public override string ToString() => $"{player} :: ({FormatPossessions(MyPossessions)}) [{PossessionTime}t; {PossessionCooldown}c]";

    /// <summary>
    /// Formats a list all of the player's possessed creatures for logging purposes.
    /// </summary>
    /// <param name="possessions">A list of the player's possessed creatures.</param>
    /// <returns>A formatted <c>string</c> listing all of the possessed creatures' names and IDs.</returns>
    public static string FormatPossessions(ICollection<Creature> possessions)
    {
        StringBuilder stringBuilder = new();

        foreach (Creature creature in possessions)
        {
            stringBuilder.Append($"{creature}; ");
        }

        return stringBuilder.ToString().Trim();
    }

    public class FadeOutController(int x, int y) : Player.PlayerController
    {
        public int FadeOutX() => x = (int)Mathf.Lerp(x, 0f, 0.25f);
        public int FadeOutY() => y = (int)Mathf.Lerp(y, 0f, 0.25f);

        public override Player.InputPackage GetInput() =>
            new(gamePad: false, global::Options.ControlSetup.Preset.None, FadeOutX(), FadeOutY(), jmp: false, thrw: false, pckp: false, mp: false, crouchToggle: false);
    }

    private void Dispose(bool disposing)
    {
        if (disposedValue) return;

        if (disposing)
        {
            if (possessionTimer is not (null or { slatedForDeletetion: true }))
            {
                possessionTimer.Destroy();
            }

            TargetSelector?.Dispose();
        }

        player = null!;
        possessionTimer = null!;
        TargetSelector = null!;

        disposedValue = true;
    }

    public void Dispose()
    {
        Main.Logger?.LogDebug($"Disposing of {nameof(PossessionManager)} from {player}!");

        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}