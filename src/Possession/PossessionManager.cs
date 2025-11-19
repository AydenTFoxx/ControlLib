using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
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
/// <param name="player">The player itself.</param>
public sealed class PossessionManager : IDisposable
{
    private static readonly Type[] BannedCreatureTypes =
    [
        typeof(Player), // For obvious reasons; Also affects slugpups, so perhaps an exception will be made for them in the future
        typeof(Overseer), // Shouldn't be allowed either, since only the Safari overseer seems to respect the player's inputs.
        // Certain Watcher creatures do not respect Safari controls at all, so they're excluded from possession for the time being.
        typeof(FireSprite),
        typeof(BoxWorm),
        typeof(BigMoth),
        typeof(Rattler)
    ];

    public int PossessionTimePotential { get; }
    public bool IsHardmodeSlugcat { get; }
    public bool IsAttunedSlugcat { get; }
    public int MaxPossessionTime { get; }

    public float Power => IsAttunedSlugcat ? 1.5f : IsHardmodeSlugcat ? 0.5f : 1f;

    private readonly WeakList<Creature> MyPossessions = [];
    private readonly WeakList<PhysicalObject> PossessedItems = [];

    private Player player;
    private PossessionTimer possessionTimer;

    private bool didReachLowPossessionTime;
    private bool disposedValue;

    public TargetSelector TargetSelector { get; private set; }

    public int PossessionCooldown { get; set; }
    public float PossessionTime
    {
        get;
        set => field = !IsOptionEnabled(Options.INFINITE_POSSESSION) ? value : MaxPossessionTime;
    }

    public bool IsPossessing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => MyPossessions.Count > 0 || PossessedItems.Count > 0;
    }

    public bool LowPossessionTime => (IsPossessing || OnMindBlastCooldown) && PossessionTime / MaxPossessionTime < 0.34f;

    public bool OnMindBlastCooldown { get; set; }
    public bool SpritesNeedReset { get; set; }

    public PossessionManager(Player player)
    {
        this.player = player;

        string attunedSlugcats = FormatSlugcatNames(GetOptionValue<string>("attuned_slugcats")); // Default: "Monk, Saint"
        string hardmodeSlugcats = FormatSlugcatNames(GetOptionValue<string>("hardmode_slugcats")); // Default: "Hunter, Artificer, Sofanthiel"

        IsAttunedSlugcat = attunedSlugcats.Contains(player.SlugCatClass.ToString());

        IsHardmodeSlugcat = hardmodeSlugcats.Contains(player.SlugCatClass.ToString());

        PossessionTimePotential = player.SlugCatClass == MoreSlugcatsEnums.SlugcatStatsName.Sofanthiel
            ? GetOptionValue<int>("sofanthiel_possession_potential") // Default: 1
            : IsAttunedSlugcat
                ? GetOptionValue<int>("attuned_possession_potential") // Default: 480
                : IsHardmodeSlugcat
                    ? GetOptionValue<int>("hardmode_possession_potential") // Default: 240
                    : GetOptionValue<int>("default_possession_potential"); // Default: 360

        int? karmaValue = player.OutsideWatcherCampaign
            ? player.room?.game.GetStorySession?.saveState.deathPersistentSaveData.karma + 1
            : (int?)(player.room?.game.GetStorySession?.saveState.deathPersistentSaveData.rippleLevel * 2f);

        if (karmaValue is not null)
        {
            if (!IsAttunedSlugcat && karmaValue >= 10)
            {
                PossessionTimePotential = IsHardmodeSlugcat
                    ? GetOptionValue<int>("default_possession_potential")
                    : GetOptionValue<int>("attuned_possession_potential");

                IsAttunedSlugcat = true;
            }
            else if (IsAttunedSlugcat && karmaValue <= 1)
            {
                PossessionTimePotential = GetOptionValue<int>("default_possession_potential");

                IsAttunedSlugcat = false;
            }
        }

        MaxPossessionTime = GetMaxPossessionForSlugcat(player, PossessionTimePotential);
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

        static int GetMaxPossessionForSlugcat(Player player, int potential)
        {
            if (player?.room is null || !player.room.game.IsStorySession) return potential;

            DeathPersistentSaveData saveData = player.room.game.GetStorySession.saveState.deathPersistentSaveData;

            int extraTime = (player.OutsideWatcherCampaign ? saveData.karma : (int)(saveData.rippleLevel * 2f)) * 40;

            if (saveData.reinforcedKarma)
                extraTime = (int)(extraTime * 1.5f);

            return potential + extraTime;
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
    public bool CanPossess() => PossessionTime > 0 && PossessionCooldown == 0 && !OnMindBlastCooldown;

    /// <summary>
    /// Determines if the player can possess the given creature.
    /// </summary>
    /// <param name="target">The creature to be tested.</param>
    /// <returns><c>true</c> if the player can use their possession ability, <c>false</c> otherwise.</returns>
    public bool CanPossessCreature(Creature target) =>
        CanPossess()
        && !IsBannedPossessionTarget(target)
        && IsPossessionValid(target);

    public bool CanPossessItem(PhysicalObject target) =>
        CanPossess()
        && target is not (null or ObjectController) and { grabbedBy.Count: 0 };

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

    public bool HasItemPossession(PhysicalObject target) => PossessedItems.Contains(target);

    /// <summary>
    /// Removes all possessions of the player. Possessed creatures will automatically stop their own possessions.
    /// </summary>
    public void ResetAllPossessions()
    {
        MyPossessions.Clear();

        for (int i = 0; i < PossessedItems.Count; i++)
        {
            if (ObjectController.TryGetController(PossessedItems[i], out ObjectController controller))
            {
                controller.Destroy();
            }
        }

        if (PossessedItems.Count > 0)
        {
            Main.Logger?.LogWarning($"{player} still has posssessed items after clearing possessions! Remaining possessions will be dropped.");
            Main.Logger?.LogWarning($"Possessed items are: {FormatPossessions(PossessedItems)}");

            PossessedItems.Clear();
        }

        DestroyFadeOutController(player);

        Main.Logger?.LogDebug($"{player} is no longer possessing anything.");
    }

    public void ResetSprites()
    {
        Main.Logger?.LogInfo($"Resetting possession sprites from {player}!");

        possessionTimer?.Destroy();
        possessionTimer = new PossessionTimer(this);

        Main.Logger?.LogInfo($"PossessionTimer is: {possessionTimer}");

        TargetSelector?.ResetSprites();
    }

    /// <summary>
    /// Initializes a new possession with the given creature as a target.
    /// </summary>
    /// <param name="target">The creature to possess.</param>
    public void StartCreaturePossession(Creature target)
    {
        if (PossessionTimePotential == 1
            && player.room is not null
            && !IsOptionEnabled(Options.INFINITE_POSSESSION)
            && !IsOptionEnabled(Options.WORLDWIDE_MIND_CONTROL))
        {
            ExplosionManager.ExplodeCreature(player, AbstractPhysicalObject.AbstractObjectType.ScavengerBomb, static (p) =>
            {
                if (p is ScavengerBomb scavBomb)
                    scavBomb.thrownClosestToCreature = scavBomb.thrownBy;
            });

            Main.Logger?.LogMessage($"{(Random.value < 0.5f ? "Game over" : "Goodbye")}, {player.SlugCatClass.ToString().Replace("Sofanthiel", "gamer")}. (Tried possessing: {target})");
            return;
        }

        MyPossessions.Add(target);

        if (target.room is not null && target.room.BeingViewed)
        {
            target.room.AddObject(new TemplarCircle(target, target.firstChunk.pos, 48f, 8f, 2f, 12, true));
            target.room.AddObject(new ShockWave(target.mainBodyChunk.pos, 100f, 0.08f, 4, false));
        }

        player.room?.PlaySound(SoundID.SS_AI_Give_The_Mark_Boom, player.mainBodyChunk.pos, 1f, 1.25f + (Random.value * 1.25f));

        player.controller = GetFadeOutController(player);

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
    public void StopCreaturePossession(Creature target)
    {
        MyPossessions.Remove(target);

        if (!IsPossessing)
        {
            DestroyFadeOutController(player);
            PossessionCooldown = 20;
        }

        if (PossessionTime <= 0)
        {
            for (int k = 0; k < 20; k++)
            {
                player.room?.AddObject(new Spark(player.mainBodyChunk.pos, Custom.RNV() * Random.value * 40f, new Color(1f, 1f, 1f), null, 30, 120));
            }
        }

        if (target.room is not null && target.room.BeingViewed)
        {
            target.room.AddObject(new ReverseShockwave(target.mainBodyChunk.pos, 64f, 0.05f, 24));
        }

        player.room?.PlaySound(SoundID.HUD_Pause_Game, player.mainBodyChunk.pos, 0.8f, 0.5f);

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

    public void StartItemPossession(PhysicalObject target)
    {
        if (PossessionTimePotential == 1
            && player.room is not null
            && !IsOptionEnabled(Options.INFINITE_POSSESSION)
            && !IsOptionEnabled(Options.WORLDWIDE_MIND_CONTROL))
        {
            ExplosionManager.ExplodeCreature(player, DLCSharedEnums.AbstractObjectType.SingularityBomb, static (p) =>
            {
                if (p is SingularityBomb singularity)
                    singularity.zeroMode = true;
            });

            Main.Logger?.LogMessage($"{(Random.value < 0.5f ? "Game over" : "Goodbye")}, {player.SlugCatClass.ToString().Replace("Sofanthiel", "gamer")}. (Tried possessing: {target})");
            return;
        }

        PossessedItems.Add(target);

        if (target.room is not null && target.room.BeingViewed)
        {
            target.room.AddObject(new TemplarCircle(target, target.firstChunk.pos, 48f, 8f, 2f, 12, true));
            target.room.AddObject(new ShockWave(target.firstChunk.pos, 100f, 0.08f, 4, false));
        }

        player.room?.PlaySound(SoundID.SS_AI_Give_The_Mark_Boom, player.mainBodyChunk.pos, 0.8f, 0.9f + Random.value);

        player.controller = GetFadeOutController(player);

        Main.Logger?.LogDebug($"{player}: Started possessing item: {target}.");
    }

    public void StopItemPossession(PhysicalObject target)
    {
        PossessedItems.Remove(target);

        if (!IsPossessing)
        {
            DestroyFadeOutController(player);
            PossessionCooldown = 20;
        }

        if (PossessionTime <= 0)
        {
            for (int k = 0; k < 20; k++)
            {
                player.room?.AddObject(new Spark(player.mainBodyChunk.pos, Custom.RNV() * Random.value * 40f, new Color(1f, 1f, 1f), null, 30, 120));
            }
        }

        if (target.room is not null && target.room.BeingViewed)
        {
            target.room.AddObject(new ReverseShockwave(target.firstChunk.pos, 64f, 0.05f, 24));
        }

        player.room?.PlaySound(SoundID.HUD_Pause_Game, player.mainBodyChunk.pos, 0.8f, 0.5f);

        Main.Logger?.LogDebug($"{player}: Stopped possessing item: {target}.");
    }

    /// <summary>
    /// Updates the player's possession behaviors and controls.
    /// </summary>
    public void Update()
    {
        if (disposedValue) return;

        if (IsOptionEnabled("modlib.debug") && IsOptionEnabled(Options.KINETIC_ABILITIES) && player.WasKeyJustPressed(Keybinds.POSSESS_ITEM, true))
        {
            Debug.StartItemPossession(player.playerState.playerNumber.ToString(), player.grasps[0] is not null and { grabbed: not ObjectController } ? "0" : "1");
            return;
        }

        if (IsOptionEnabled(Options.MIND_BLAST) && UpdateMindBlast()) return;

        if (player.Consious && player.IsKeyDown(Keybinds.POSSESS, true) && (IsPossessing || CanPossess()))
        {
            TargetSelector.Update();
        }
        else if (TargetSelector.Input.InputTime > 0)
        {
            if (TargetSelector.HasTargetCursor && !TargetSelector.Input.QueriedCursor)
                TargetSelector.QueryTargetCursor();

            if (TargetSelector.HasValidTargets)
                TargetSelector.ApplySelectedTargets();

            TargetSelector.ResetSelectorInput();
        }

        if (IsPossessing)
        {
            bool hasCreaturePossession = MyPossessions.Count > 0;

            if (player.graphicsModule is PlayerGraphics playerGraphics)
            {
                if (hasCreaturePossession)
                    playerGraphics.LookAtNothing();
                else
                    playerGraphics.LookAtObject(PossessedItems.OrderBy(po => Custom.DistNoSqrt(po.firstChunk.pos, player.mainBodyChunk.pos)).FirstOrDefault());
            }

            if (hasCreaturePossession)
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
                Main.Logger?.LogInfo($"Forcing end of possession! Ran out of time? {PossessionTime <= 0f}");

                if (player.Consious)
                {
                    if (player.SlugCatClass == MoreSlugcatsEnums.SlugcatStatsName.Sofanthiel)
                    {
                        ExplosionManager.ExplodeCreature(player, DLCSharedEnums.AbstractObjectType.SingularityBomb, static (p) =>
                        {
                            if (p is SingularityBomb singularity)
                                singularity.zeroMode = true;
                        });

                        Main.Logger?.LogMessage(Random.value < 0.5f ? "It's so over" : "Kaboom");
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

                            int stunTime = IsHardmodeSlugcat ? 80 : 40;

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

        if (didReachLowPossessionTime && !IsPossessing && PossessionCooldown == 0 && !player.submerged)
        {
            player.exhausted = IsHardmodeSlugcat || player.Malnourished || player.gourmandExhausted;

            player.airInLungs = Math.Min(player.airInLungs + (IsAttunedSlugcat ? 0.025f : 0.05f), 1f);
            player.aerobicLevel = Math.Max(player.aerobicLevel - (IsHardmodeSlugcat ? 0.05f : 0.025f), 0f);

            didReachLowPossessionTime = player.airInLungs == 1f;
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

        bool keysPressed = player.IsKeyDown(Keybinds.MIND_BLAST, true) && player.IsKeyDown(Keybinds.POSSESS, true);
        MindBlast? instance = null;

        if (keysPressed
            && player.Consious
            && PossessionCooldown == 0
            && PossessionTime == MaxPossessionTime)
        {
            TargetSelector.ExceededTimeLimit = true;

            instance = MindBlast.CreateInstance(player, Power);

            PossessionTime = 0f;

            OnMindBlastCooldown = true;
        }

        if (instance is not null || MindBlast.TryGetInstance(player, out instance))
        {
            if (player.Consious && keysPressed)
            {
                instance.pos = TargetSelector.GetTargetPos();

                PossessionCooldown = 80;
            }
            else if (!instance.Expired)
            {
                instance.Interrupt();
            }
        }

        return instance is not null;
    }

    public static FadeOutController GetFadeOutController(Player player)
    {
        Player.InputPackage input = player.GetRawInput();

        return new FadeOutController(player, input.x, player.standing ? 1 : input.y, 20);
    }

    public static void DestroyFadeOutController(Player player)
    {
        if (player.controller is FadeOutController fadeOutController)
        {
            fadeOutController.Destroy();
        }
        else
        {
            player.controller = null;
        }
    }

    /// <summary>
    /// Retrieves a <c>string</c> representation of this <c>PossessionManager</c> instance.
    /// </summary>
    /// <returns>A <c>string</c> containing the instance's values and possessions.</returns>
    public override string ToString()
    {
        return new StringBuilder($"{(disposedValue ? "[DISPOSED] " : "")}{nameof(PossessionManager)}: {player}{Environment.NewLine}")
            .AppendLine($"Potential: {PossessionTimePotential}")
            .AppendLine($"Attuned: {(IsAttunedSlugcat ? "Yes" : "No")}")
            .AppendLine($"Hardmode: {(IsHardmodeSlugcat ? "Yes" : "No")}")
            .AppendLine($"Time: {PossessionTime}/{MaxPossessionTime}")
            .AppendLine($"Cooldown: {PossessionCooldown}")
            .AppendLine($"OnMindBlastCooldown: {(OnMindBlastCooldown ? "Yes" : "No")}")
            .AppendLine($"C:[{FormatPossessions(MyPossessions)}]")
            .Append($"I:[{FormatPossessions(PossessedItems)}]")
            .ToString();
    }

    /// <summary>
    /// Formats a list all of the player's possessed creatures for logging purposes.
    /// </summary>
    /// <param name="possessions">A list of the player's possessed creatures.</param>
    /// <returns>A formatted <c>string</c> listing all of the possessed creatures' names and IDs.</returns>
    public static string FormatPossessions<T>(ICollection<T> possessions)
    {
        StringBuilder stringBuilder = new();

        foreach (T element in possessions)
        {
            stringBuilder.Append($"{element}; ");
        }

        return stringBuilder.ToString().TrimEnd(';', ' ');
    }

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

    private void Dispose(bool disposing)
    {
        if (disposedValue) return;

        if (disposing)
        {
            possessionTimer?.Destroy();
            TargetSelector?.Dispose();

            player?.RemovePossessionManager();
        }

        MyPossessions.Clear();

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