using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
using Watcher;
using Random = UnityEngine.Random;
using static ModLib.Options.OptionUtils;

namespace ControlLib.Possession;

/// <summary>
/// Stores and manages the player's possessed creatures.
/// </summary>
public class PossessionManager : IDisposable
{
    private static readonly Type[] BannedCreatureTypes =
    [
        typeof(Overseer), // good lord don't
        // Certain Watcher creatures do not respect Safari controls at all, so they're excluded from possession here.
        typeof(BigMoth),
        typeof(BoxWorm),
        typeof(DrillCrab),
        typeof(FireSprite),
        typeof(Rattler)
    ];

    private readonly WeakList<Creature> MyPossessions = [];
    private readonly WeakList<PhysicalObject> PossessedItems = [];

    private readonly float quarterPossessionTime;

    private Player player;
    private PossessionTimer? possessionTimer;

    private bool didReachLowPossessionTime;
    private bool disposedValue;

    public List<CreatureTemplate.Type>? killedPossessionTypes = Main.CanUnlockAchievement("sacrilege") ? [] : null;

    public bool IsAttunedSlugcat { get; }
    public bool IsHardmodeSlugcat { get; }
    public bool IsSofanthielSlugcat { get; }

    public int MaxPossessionTime { get; }
    public float Power { get; }

    public TargetSelector? TargetSelector { get; set; }

    public int PossessionCooldown { get; set; }
    public float PossessionTime
    {
        get;
        set => field = !IsOptionEnabled(Options.INFINITE_POSSESSION) ? value : MaxPossessionTime;
    }

    public bool OnMindBlastCooldown { get; set; }
    public bool SpritesNeedReset { get; set; }

    public int ForceVisiblePips { get; set; }

    public bool IsValid => player is not null && !disposedValue;

    public bool IsPossessing => MyPossessions.Count > 0 || PossessedItems.Count > 0;
    public bool LowPossessionTime => (IsPossessing && PossessionTime <= 60) || (OnMindBlastCooldown && PossessionTime <= quarterPossessionTime);

    public PossessionManager(in ManagerDataSnapshot snapshot)
    {
        MyPossessions = snapshot.MyPossessions;
        PossessedItems = snapshot.PossessedItems;

        player = snapshot.player;

        IsAttunedSlugcat = snapshot.IsAttunedSlugcat;
        IsHardmodeSlugcat = snapshot.IsHardmodeSlugcat;
        IsSofanthielSlugcat = snapshot.IsSofanthielSlugcat;

        Power = IsAttunedSlugcat ? 1.5f : IsHardmodeSlugcat ? 0.75f : 1f;

        if (IsSofanthielSlugcat)
            Power -= 0.25f;

        MaxPossessionTime = snapshot.MaxPossessionTime;
        PossessionTime = snapshot.PossessionTime;
        PossessionCooldown = snapshot.PossessionCooldown;

        quarterPossessionTime = Mathf.Min(MaxPossessionTime / 0.25f, 120f);

        OnMindBlastCooldown = snapshot.OnMindBlastCooldown;
        SpritesNeedReset = snapshot.SpritesNeedReset;

        TargetSelector = snapshot.TargetSelector ?? new TargetSelector(player, this);
        possessionTimer = snapshot.possessionTimer ?? new PossessionTimer(this);
    }

    public PossessionManager(Player player)
    {
        this.player = player;

        SlugcatPotential dynamicPotential = SlugcatPotential.PotentialForPlayer(player);

        IsAttunedSlugcat = dynamicPotential.IsAttuned;
        IsHardmodeSlugcat = dynamicPotential.IsHardmode;
        IsSofanthielSlugcat = dynamicPotential.IsSofanthiel;

        Power = (IsAttunedSlugcat ? 1.5f : IsHardmodeSlugcat ? 0.75f : 1f) + (IsSofanthielSlugcat ? -0.25f : SlugcatPotential.GetStaticPotential(player.SlugCatClass).Potential / dynamicPotential.Potential);

        MaxPossessionTime = dynamicPotential.Potential;
        PossessionTime = MaxPossessionTime;

        quarterPossessionTime = Mathf.Min(MaxPossessionTime / 0.25f, 120f);

        TargetSelector = new TargetSelector(player, this);
        possessionTimer = new PossessionTimer(this);
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanPossess() => this is { PossessionTime: > 0, PossessionCooldown: 0, OnMindBlastCooldown: false };

    /// <summary>
    /// Determines if the player can possess the given creature.
    /// </summary>
    /// <param name="target">The creature to be tested.</param>
    /// <returns><c>true</c> if the player can use their possession ability, <c>false</c> otherwise.</returns>
    public bool CanPossessCreature(Creature target) =>
        target != player
        && CanPossess()
        && !IsBannedPossessionTarget(target)
        && IsPossessionValid(target);

    public bool CanPossessItem(PhysicalObject target) =>
        CanPossess()
        && target is not (null or ObjectController) and { grabbedBy.Count: 0 };

    public static bool IsBannedPossessionTarget(Creature target) =>
        target is null or Player { AI: not null } or { dead: true } or { abstractCreature.controlled: true }
        || BannedCreatureTypes.Contains(target.GetType());

    /// <summary>
    /// Validates the player's possession of a given creature.
    /// </summary>
    /// <param name="target">The creature to be tested.</param>
    /// <returns><c>true</c> if this possession is valid, <c>false</c> otherwise.</returns>
    public bool IsPossessionValid(Creature target) => player is { Consious: true, room: not null } && target is { dead: false, room: not null };

    /// <summary>
    /// Determines if the player is currently possessing the given creature.
    /// </summary>
    /// <param name="target">The creature to be tested.</param>
    /// <returns><c>true</c> if the player is possessing this creature, <c>false</c> otherwise.</returns>
    public bool HasCreaturePossession(Creature target) => MyPossessions.Contains(target);

    public bool HasItemPossession(PhysicalObject target) => PossessedItems.Contains(target);

    /// <summary>
    /// Removes all possessions of the player. Possessed creatures will automatically stop their own possessions.
    /// </summary>
    public void ResetAllPossessions()
    {
        if (player.room is not null)
        {
            RoomCamera? camera = player.room.world.game.cameras.FirstOrDefault();
            if (camera is not null && MyPossessions.Any(c => c.abstractCreature == camera.followAbstractCreature))
            {
                Main.Logger.LogDebug($"Resetting camera to {player}.");

                if (camera.followAbstractCreature.Room != player.abstractCreature.Room)
                    camera.MoveCamera(player.room, 0);

                camera.followAbstractCreature = player.abstractCreature;
            }
        }

        MyPossessions.Clear();

        for (int i = PossessedItems.Count - 1; i >= 0; i--)
        {
            if (ObjectController.TryGetController(PossessedItems[i], out ObjectController controller))
            {
                controller.Destroy();
            }
        }

        if (PossessedItems.Count > 0)
        {
            Main.Logger.LogWarning($"{player} still has posssessed items after clearing possessions! Remaining possessions will be dropped.");
            Main.Logger.LogWarning($"Possessed items are: {FormatPossessions(PossessedItems)}");

            PossessedItems.Clear();
        }

        DestroyFadeOutController(player);

        if (player.room is not null)
        {
            foreach (PossessionMark mark in player.room.updateList.OfType<PossessionMark>())
            {
                if (!mark.Target.TryGetPossession(out Player possessor)
                    || !possessor.TryGetPossessionManager(out PossessionManager manager)
                    || manager == this)
                {
                    Main.Logger.LogDebug($"Destroying orphaned PossessionMark! (Owner: {mark.Owner} | Target: {mark.Target})");

                    mark.Invalidate();

                    if (possessionTimer is not null && possessionTimer.FollowMark == mark)
                    {
                        Main.Logger.LogDebug($"PossessionTimer of {player} is no longer following this Mark.");

                        possessionTimer.FollowMark = null;
                    }
                }
            }
        }

        Main.Logger.LogDebug($"{player} is no longer possessing anything.");
    }

    public void ResetSprites()
    {
        Main.Logger.LogInfo($"Resetting possession sprites from {player}!");

        possessionTimer?.Destroy();
        possessionTimer = new PossessionTimer(this);

        Main.Logger.LogInfo($"PossessionTimer is: {possessionTimer}");

        TargetSelector?.ResetSprites();
    }

    /// <summary>
    /// Initializes a new possession with the given creature as a target.
    /// </summary>
    /// <param name="target">The creature to possess.</param>
    public void StartCreaturePossession(Creature target)
    {
        if (player?.room is null || target?.room is null) return;

        if (this is { IsSofanthielSlugcat: true, MaxPossessionTime: 1 }
            && !IsOptionEnabled(Options.INFINITE_POSSESSION)
            && !IsOptionEnabled(Options.WORLDWIDE_MIND_CONTROL))
        {
            ExplosionManager.ExplodeCreature(player, AbstractPhysicalObject.AbstractObjectType.ScavengerBomb, ExplosionManager.SelfExplosionCallback);

            LogSofanthielDeath(player, "Game over", "Goodbye", $" (Tried possessing: {target})");
            return;
        }

        MyPossessions.Add(target);

        target.UpdateCachedPossession();
        target.abstractCreature.controlled = true;

        if (Extras.IsOnlineSession)
            RequestMeadowPossessionSync(target, false);

        SpawnPossessionEffects(target, player, true);

        possessionTimer?.FollowMark = new PossessionMark(target, player);

        RoomCamera? camera = player.abstractCreature.world.game.cameras.FirstOrDefault();
        if (camera is not null && camera.followAbstractCreature == player.abstractCreature)
        {
            camera.followAbstractCreature = target.abstractCreature;

            if (target.room != player.room)
                camera.MoveCamera(camera.followAbstractCreature.realizedCreature.room, 0);

            Main.Logger.LogInfo($"Camera is now following {camera.followAbstractCreature}.");
        }

        player.controller = GetFadeOutController(player);

        if (!player.room.game.IsArenaSession)
        {
            Main.CueAchievement("possessions");

            if (MyPossessions.Count + PossessedItems.Count >= 7)
                Main.CueAchievement("hive_mind");
        }

        Main.Logger.LogDebug($"({player}) Started possessing creature: {target}.");
    }

    /// <summary>
    /// Interrupts the possession of the given creature.
    /// </summary>
    /// <param name="target">The creature to stop possessing.</param>
    public void StopCreaturePossession(Creature target)
    {
        MyPossessions.Remove(target);

        RoomCamera? camera = player.abstractCreature.world.game.cameras.FirstOrDefault();
        if (camera is not null && camera.followAbstractCreature == target.abstractCreature)
        {
            Creature? nextTarget = MyPossessions.LastOrDefault();

            possessionTimer?.FollowMark = nextTarget?.room?.updateList.OfType<PossessionMark>().FirstOrDefault(m => m.Target == nextTarget);

            camera.followAbstractCreature = nextTarget?.abstractCreature ?? player.abstractCreature;

            if (camera.followAbstractCreature.realizedCreature.room is not null
                && camera.followAbstractCreature.realizedCreature.room != target.room)
            {
                Main.Logger.LogInfo($"Moving camera to follow {camera.followAbstractCreature}.");

                camera.MoveCamera(camera.followAbstractCreature.realizedCreature.room, 0);
            }

            Main.Logger.LogInfo($"Changed camera focus to {camera.followAbstractCreature}.");
        }

        if (!IsPossessing)
        {
            DestroyFadeOutController(player);
            PossessionCooldown = 20;

            (player.graphicsModule as PlayerGraphics)?.blink = 0;
        }

        SpawnPossessionEffects(target, player, false, PossessionTime <= 0f);

        if (Extras.IsOnlineSession)
            RequestMeadowPossessionSync(target, false);

        target.UpdateCachedPossession();
        target.abstractCreature.controlled = false;

        Main.Logger.LogDebug($"({player}) Stopped possessing creature: {target}.");
    }

    public void StartItemPossession(PhysicalObject target)
    {
        if (player?.room is null || target?.room is null) return;

        if (this is { IsSofanthielSlugcat: true, MaxPossessionTime: 1 }
            && !IsOptionEnabled(Options.INFINITE_POSSESSION)
            && !IsOptionEnabled(Options.WORLDWIDE_MIND_CONTROL))
        {
            ExplosionManager.ExplodeCreature(player, AbstractPhysicalObject.AbstractObjectType.ScavengerBomb, ExplosionManager.SelfExplosionCallback);

            LogSofanthielDeath(player, "Game over", "Goodbye", $" (Tried possessing: {target})");
            return;
        }

        PossessedItems.Add(target);

        SpawnPossessionEffects(target, player, true);

        if (Extras.IsOnlineSession)
            RequestMeadowPossessionSync(target, true);

        if (target != player)
            player.controller = GetFadeOutController(player);

        if (!player.room.game.IsArenaSession)
        {
            Main.CueAchievement("commando");

            if (target == player)
                Main.CueAchievement("belief");

            if (MyPossessions.Count + PossessedItems.Count >= 7)
                Main.CueAchievement("hive_mind");
        }

        Main.Logger.LogDebug($"({player}) Started possessing item: {target}.");
    }

    public void StopItemPossession(PhysicalObject target)
    {
        PossessedItems.Remove(target);

        if (!IsPossessing)
        {
            DestroyFadeOutController(player);
            PossessionCooldown = 20;
        }

        SpawnPossessionEffects(target, player, false, PossessionTime <= 0f);

        if (Extras.IsOnlineSession)
            RequestMeadowPossessionSync(target, false);

        Main.Logger.LogDebug($"({player}) Stopped possessing item: {target}.");
    }

    public void MeadowSafeUpdate()
    {
        if (disposedValue) return;

        if (player is null)
        {
            Dispose();
            return;
        }

        if (IsPossessing)
        {
            if (!IsOptionEnabled(Options.INFINITE_POSSESSION))
            {
                PossessionTime -= IsAttunedSlugcat ? 0.25f : 0.5f;
            }

            if (PossessionTime <= 0f || !player.Consious)
            {
                if (player.Consious)
                    PossessionTime = -80;

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

        if (OnMindBlastCooldown)
            OnMindBlastCooldown = LowPossessionTime || PossessionCooldown > 0;

        if (ForceVisiblePips > 0)
            ForceVisiblePips--;

        if (SpritesNeedReset && player.warpPointCooldown == 0)
        {
            ResetSprites();

            SpritesNeedReset = false;
        }
    }

    /// <summary>
    /// Updates the player's possession behaviors and controls.
    /// </summary>
    public void Update()
    {
        if (disposedValue) return;

        if (player is null)
        {
            Dispose();
            return;
        }

        if (IsOptionEnabled(Options.KINETIC_ABILITIES) && player.WasKeyJustPressed(Keybinds.POSSESS_ITEM, true) && CanPossess())
        {
            int targetID = player.grasps[0] is not null and { grabbed: not ObjectController } ? 0 : 1;

            Debug.StartItemPossession(player.playerState.playerNumber.ToString(), player.grasps[targetID]?.grabbed.abstractPhysicalObject.ID.number.ToString());
            return;
        }

        if (IsOptionEnabled(Options.MIND_BLAST) && UpdateMindBlast()) return;

        if (TargetSelector is not null)
        {
            if (player.Consious && player.IsKeyDown(Keybinds.POSSESS, true) && (IsPossessing || CanPossess()))
            {
                TargetSelector.Update();
            }
            else if (TargetSelector.Input.InputTime > 0)
            {
                if (TargetSelector is { HasTargetCursor: true, Input.QueriedCursor: false })
                    TargetSelector.QueryTargetCursor();

                if (TargetSelector.HasValidTargets)
                    TargetSelector.ApplySelectedTargets();

                TargetSelector.ResetSelectorInput();
            }
        }

        if (IsPossessing)
        {
            bool hasCreaturePossession = MyPossessions.Count > 0;

            if (player.graphicsModule is PlayerGraphics playerGraphics)
            {
                if (hasCreaturePossession || PossessedItems.Count == 0)
                    playerGraphics.LookAtNothing();
                else
                    playerGraphics.LookAtObject(PossessedItems.OrderBy(po => Custom.DistNoSqrt(po.firstChunk.pos, player.mainBodyChunk.pos)).FirstOrDefault());
            }

            if (hasCreaturePossession)
            {
                player.Blink(10);

                foreach (Creature crit in MyPossessions)
                {
                    if (crit.dead || !crit.abstractCreature.controlled || crit.abstractCreature.InDen)
                    {
                        StopCreaturePossession(crit);
                    }
                }
            }

            if (!IsOptionEnabled(Options.INFINITE_POSSESSION))
            {
                PossessionTime -= IsAttunedSlugcat ? 0.25f : 0.5f;
            }

            if (!OnMindBlastCooldown && PossessionTime <= quarterPossessionTime)
            {
                player.aerobicLevel += 0.0125f;
                player.airInLungs -= 1f / Mathf.Max(PossessionTime, 1f);

                didReachLowPossessionTime = true;
            }

            if (PossessionTime <= 0f || !player.Consious)
            {
                Main.Logger.LogInfo($"Forcing end of possession! Ran out of time? {PossessionTime <= 0f}");

                if (player.Consious)
                {
                    if (IsSofanthielSlugcat)
                    {
                        ExplosionManager.ExplodeCreature(player, DLCSharedEnums.AbstractObjectType.SingularityBomb, ExplosionManager.EggSingularityCallback);

                        LogSofanthielDeath(player, "It's so over", "Kaboom");
                        return;
                    }

                    player.aerobicLevel = 1f;
                    player.exhausted = true;

                    if (!IsAttunedSlugcat || IsHardmodeSlugcat)
                    {
                        player.lungsExhausted = true;

                        if (player.SlugCatClass == MoreSlugcatsEnums.SlugcatStatsName.Saint)
                        {
                            player.SaintStagger(120 + (int)(Random.value * 60f));
                        }
                        else
                        {
                            player.airInLungs *= 0.2f;
                            player.aerobicLevel = 1f;

                            int stunTime = IsHardmodeSlugcat ? 120 : 40;

                            if (IsHardmodeSlugcat)
                                player.room?.AddObject(new CreatureSpasmer(player, allowDead: false, stunTime));

                            player.Stun(stunTime);
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
        else if (PossessionTime < MaxPossessionTime && (!IsSofanthielSlugcat || player.FoodInStomach == player.MaxFoodInStomach))
        {
            PossessionTime += IsHardmodeSlugcat ? 0.25f : 0.5f;
        }
        else if (PossessionTime > MaxPossessionTime)
        {
            PossessionTime = MaxPossessionTime;
        }

        if (OnMindBlastCooldown)
            OnMindBlastCooldown = LowPossessionTime || PossessionCooldown > 0;

        if (IsSofanthielSlugcat && player.input[0].mp)
        {
            ForceVisiblePips = 60;
        }
        else if (ForceVisiblePips > 0)
        {
            ForceVisiblePips--;
        }

        if (this is { didReachLowPossessionTime: true, player.submerged: false, PossessionCooldown: 0, IsPossessing: false })
        {
            player.exhausted = player.Malnourished || player.gourmandExhausted;

            player.airInLungs = Math.Min(player.airInLungs + (IsAttunedSlugcat ? 0.025f : 0.05f), 1f);
            player.aerobicLevel = Math.Max(player.aerobicLevel - (IsHardmodeSlugcat ? 0.05f : 0.025f), 0f);

            didReachLowPossessionTime = player.airInLungs < 1f;
        }

        if (SpritesNeedReset && player.warpPointCooldown == 0)
        {
            ResetSprites();

            SpritesNeedReset = false;
        }
    }

    public bool UpdateMindBlast()
    {
        if (IsPossessing) return false;

        bool keysPressed = (player.Consious && player.IsKeyDown(Keybinds.MIND_BLAST, true) && player.IsKeyDown(Keybinds.POSSESS, true))
                        || (player is { dead: false, dangerGrasp: not null, dangerGraspTime: <= 60 } && IsOptionEnabled(Options.DANGER_MIND_BLAST) && player.IsKeyDown(Keybinds.MIND_BLAST, true));

        MindBlast? instance = null;

        if (keysPressed && PossessionCooldown == 0 && PossessionTime == MaxPossessionTime)
        {
            TargetSelector?.ExceededTimeLimit = true;

            PossessionTime = 0f;

            OnMindBlastCooldown = true;

            instance = MindBlast.CreateInstance(player, this);

            if (IsOptionEnabled(Options.DANGER_MIND_BLAST) && player.dangerGrasp is not null)
            {
                instance.pos = player.mainBodyChunk.pos;
                instance.Explode();
                return true;
            }
        }

        if (instance is not null || (MindBlast.TryGetInstance(player, out instance) && !instance.Expired))
        {
            if (player.Consious && keysPressed)
            {
                instance.pos = TargetSelector?.GetTargetPos() ?? player.mainBodyChunk.pos;

                PossessionCooldown = 80;
            }
            else
            {
                instance.Interrupt();
            }
        }

        return instance is not null;
    }

    /// <summary>
    /// Retrieves a <c>string</c> representation of this <c>PossessionManager</c> instance.
    /// </summary>
    /// <returns>A <c>string</c> containing the instance's values and possessions.</returns>
    public override string ToString()
    {
        return new StringBuilder($"{(disposedValue ? "[DISPOSED] " : "")}{player} ({player?.SlugCatClass}){Environment.NewLine}")
            .AppendLine($"Attuned: {(IsAttunedSlugcat ? "Yes" : "No")}")
            .AppendLine($"Hardmode: {(IsHardmodeSlugcat ? "Yes" : "No")}")
            .AppendLine($"Power: {Power}{(IsSofanthielSlugcat ? " (Sofanthiel)" : "")}")
            .AppendLine($"Time: {PossessionTime}/{MaxPossessionTime}")
            .AppendLine($"Cooldown: {PossessionCooldown}")
            .AppendLine($"OnMindBlastCooldown: {(OnMindBlastCooldown ? "Yes" : "No")}")
            .AppendLine($"C:[{FormatPossessions(MyPossessions)}]")
            .Append($"I:[{FormatPossessions(PossessedItems)}]")
            .ToString();
    }

    private void RequestMeadowPossessionSync(PhysicalObject target, bool isNewPossession)
    {
        try
        {
            if (isNewPossession && !MeadowUtils.IsMine(target))
                MeadowUtils.RequestOwnership(target);

            MyRPCs.SyncOnlinePossession(target, player, isNewPossession, !isNewPossession && PossessionTime <= 0f);
        }
        catch (Exception ex)
        {
            Main.Logger.LogError(ex);
        }
    }

    public static void DestroyFadeOutController(Player player)
    {
        if (player.controller is FadeOutController fadeOutController)
            fadeOutController.Destroy();
        else
            player.controller = null;
    }

    public static FadeOutController GetFadeOutController(Player player) =>
        new(player, player.input[0].x, player.standing ? 1 : player.input[0].y, 15);

    /// <summary>
    /// Formats a list all of the player's possessed creatures for logging purposes.
    /// </summary>
    /// <param name="possessions">A list of the player's possessed creatures.</param>
    /// <returns>A formatted <c>string</c> listing all of the possessed creatures' names and IDs.</returns>
    public static string FormatPossessions<T>(ICollection<T> possessions)
    {
        if (possessions is null or { Count: 0 })
            return string.Empty;

        StringBuilder stringBuilder = new();

        foreach (T element in possessions)
        {
            stringBuilder.Append($"{(element is not null ? element.ToString() : "NULL")}; ");
        }

        return stringBuilder.ToString().TrimEnd(';', ' ');
    }

    public static void SpawnPossessionEffects(PhysicalObject target, Creature? caller, bool isNewPossession, bool depletedPossessionTime = false)
    {
        if (target?.room is null) return;

        if (target.room.BeingViewed)
        {
            if (isNewPossession)
            {
                target.room.AddObject(new TemplarCircle(target, target.firstChunk.pos, 48f, 8f, 2f, 12, true));
                target.room.AddObject(new ShockWave(target is Creature crit ? crit.mainBodyChunk.pos : target.firstChunk.pos, 100f, 0.08f, 4, false));
            }
            else
            {
                target.room.AddObject(new ReverseShockwave(target is Creature crit ? crit.mainBodyChunk.pos : target.firstChunk.pos, 64f, 0.05f, 24));
            }
        }

        if (caller?.room is null) return;

        if (isNewPossession)
        {
            float vol = target is Creature ? 1f : 0.8f;
            float pitch = target is Creature ? 1.25f : 0.9f;

            caller.room.PlaySound(SoundID.SS_AI_Give_The_Mark_Boom, caller.mainBodyChunk.pos, vol, pitch + (Random.value * pitch));
        }
        else
        {
            if (depletedPossessionTime)
            {
                for (int k = 0; k < 20; k++)
                {
                    caller.room.AddObject(new Spark(caller.mainBodyChunk.pos, Custom.RNV() * Random.value * 40f, new Color(1f, 1f, 1f), null, 30, 120));
                }
            }
            caller.room.PlaySound(SoundID.HUD_Pause_Game, caller.mainBodyChunk.pos, 0.8f, depletedPossessionTime ? 0.35f : 0.5f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LogSofanthielDeath(Player player, string messageA, string messageB, string extras = "") =>
        Main.Logger.LogMessage($"{(Random.value < 0.5f ? messageA : messageB)}, {(player.SlugCatClass == MoreSlugcatsEnums.SlugcatStatsName.Sofanthiel ? "gamer" : player.SlugCatClass.ToString())}.{extras}");

    public class FadeOutController(Player target, int x, int y, int fadeInTime) : Player.PlayerController
    {
        private bool slatedForDeletion;

        public int FadeOutX() => x = (int)Mathf.Lerp(x, 0f, 0.25f);
        public int FadeOutY() => y = (int)Mathf.Lerp(y, 0f, 0.25f);

        public override Player.InputPackage GetInput()
        {
            if (slatedForDeletion)
            {
                fadeInTime--;
                if (fadeInTime <= 0 || !target.Consious)
                {
                    target.controller = null;
                    return target.GetRawInput();
                }
            }
            return new(gamePad: false, global::Options.ControlSetup.Preset.None, FadeOutX(), FadeOutY(), jmp: false, thrw: false, pckp: false, mp: false, crouchToggle: false);
        }

        public void Destroy() => slatedForDeletion = true;
    }

    public void Dispose()
    {
        if (disposedValue) return;

        Main.Logger.LogDebug($"Disposing of {nameof(PossessionManager)} from {player}!");

        possessionTimer?.Destroy();
        possessionTimer = null;

        TargetSelector?.Dispose();
        TargetSelector = null;

        if (player is not null)
        {
            if (player.controller is FadeOutController controller)
                controller.Destroy();

            player.RemovePossessionManager();
            player = null!;
        }

        MyPossessions.Clear();
        PossessedItems.Clear();

        disposedValue = true;
    }

    public ManagerDataSnapshot GetSnapshot() => new(this);

    public struct ManagerDataSnapshot(PossessionManager manager)
    {
        public WeakList<Creature> MyPossessions = manager.MyPossessions;
        public WeakList<PhysicalObject> PossessedItems = manager.PossessedItems;

        public Player player = manager.player;
        public PossessionTimer? possessionTimer = manager.possessionTimer;

        public bool IsAttunedSlugcat = manager.IsAttunedSlugcat;
        public bool IsHardmodeSlugcat = manager.IsHardmodeSlugcat;
        public bool IsSofanthielSlugcat = manager.IsSofanthielSlugcat;

        public int MaxPossessionTime = manager.MaxPossessionTime;

        public TargetSelector? TargetSelector = manager.TargetSelector;

        public int PossessionCooldown = manager.PossessionCooldown;
        public float PossessionTime = manager.PossessionTime;

        public bool OnMindBlastCooldown = manager.OnMindBlastCooldown;
        public bool SpritesNeedReset = manager.SpritesNeedReset;

        public readonly SerializableDataSnapshot ToSerializableSnapshot() => new(this);
    }
}