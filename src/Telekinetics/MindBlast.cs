using System.Linq;
using Possessions.Possession;
using ModLib.Collections;
using ModLib.Options;
using MoreSlugcats;
using Noise;
using RWCustom;
using UnityEngine;

namespace Possessions.Telekinetics;

public class MindBlast : CosmeticSprite
{
    private const float StunFactor = 600f;
    private const int StunDeathThreshold = 100;

    private static readonly WeakDictionary<Player, MindBlast> _activeInstances = [];
    private static readonly System.Predicate<Creature> PlayerProtectionCondition = static (c) => c is Player { canJump: > 0 } or { stun: 0, Submersion: >= 0.5f };

    private readonly Player player;
    private readonly DynamicSoundLoop soundLoop;
    private readonly Color explodeColor;

    private readonly PossessionManager? manager;

    private float killFac;
    private float lastKillFac;

    private LightningMachine? activateLightning;
    private FadingMeltLights? fadingMeltLights;

    private FirecrackerPlant.ScareObject? scareObj;

    private bool enlightenedRoom;

    public bool Expired { get; private set; }
    public float Power { get; }

    private float FadeProgress => fadingMeltLights?.FadeProgress ?? 0f;

    private MindBlast(Player player, PossessionManager? manager)
    {
        this.player = player;

        if (manager is null)
            player.TryGetPossessionManager(out manager);

        this.manager = manager;

        Power = manager is not null ? manager.Power : 1f;

        if (OptionUtils.IsOptionEnabled(Options.WORLDWIDE_MIND_CONTROL))
            Power *= 3f;

        soundLoop = new ChunkDynamicSoundLoop(player.mainBodyChunk)
        {
            sound = SoundID.Rock_Through_Air_LOOP,
            Volume = 1f,
            Pitch = 0.5f
        };

        explodeColor = Power < 1f
            ? RainWorld.AntiGold.rgb
            : Power > 1f
                ? RainWorld.RippleGold
                : RainWorld.GoldRGB;

        room = player.room;
    }

    public void Interrupt()
    {
        bool worldwideMindControl = OptionUtils.IsOptionEnabled(Options.WORLDWIDE_MIND_CONTROL) || (manager is not null && manager.IsSofanthielSlugcat);

        if (room is null || (killFac < 0.3f && !worldwideMindControl))
        {
            Main.Logger.LogDebug($"{nameof(MindBlast)}: Got interrupted but cannot go kaboom; Ignoring. (Room is: {room} | killFac is {killFac})");

            Destroy();
            return;
        }

        bool dangerousKillFac = killFac >= 0.5f || worldwideMindControl;

        if (dangerousKillFac)
        {
            AbstractPhysicalObject.AbstractObjectType bombType = (worldwideMindControl ? killFac >= 0.85f : killFac >= 0.95f)
                ? DLCSharedEnums.AbstractObjectType.SingularityBomb
                : AbstractPhysicalObject.AbstractObjectType.ScavengerBomb;

            ExplosionManager.ExplodePos(player, player.room, room.ToWorldCoordinate(pos), bombType, ExplosionManager.EggSingularityCallback);
        }
        else
        {
            for (int num = Random.Range(1, 6); num >= 0; num--)
            {
                room.AddObject(new Spark(pos, Custom.RNV() * Mathf.Lerp(15f, 30f, Random.value), explodeColor, null, 7, 17));
            }

            room.AddObject(new Explosion.FlashingSmoke(pos, Custom.RNV() * 5f * Random.value, 1f, new Color(1f, 1f, 1f), explodeColor, 5));
            room.AddObject(new Explosion.ExplosionLight(pos, Mathf.Lerp(50f, 150f, Random.value), 0.5f, 4, explodeColor));

            room.PlaySound(SoundID.Firecracker_Bang, pos);
        }

        CreateFear();

        if (scareObj is not null && !worldwideMindControl)
        {
            scareObj.lifeTime = dangerousKillFac ? 400 : 600;
            scareObj.fearRange = dangerousKillFac ? 4000f : 1000f;
            scareObj.fearScavs = dangerousKillFac;
        }

        if (!player.dead)
        {
            player.exhausted = true;
            player.aerobicLevel = killFac;

            int stunTime = (int)(120f * killFac);

            player.Stun(stunTime);

            if (dangerousKillFac)
                room.AddObject(new CreatureSpasmer(player, false, stunTime / 2));
        }

        Main.Logger.LogDebug($"{nameof(MindBlast)}: Interrupted! Progress was: {killFac}");

        Destroy();
    }

    public void Explode()
    {
        if (room is null) return;

        room.AddObject(new Explosion.ExplosionLight(pos, 2000f * Power, 2f, 60, explodeColor));

        room.AddObject(new ShockWave(pos, 275f * Power, 0.2425f * Power, (int)(200 * Power), true));
        room.AddObject(new ShockWave(pos, 1250f * Power, 0.0925f * Power, (int)(180 * Power), false));

        room.AddObject(fadingMeltLights = new FadingMeltLights(room));

        room.ScreenMovement(pos, Vector2.zero, 0.5f * Power);
        room.PlaySound(SoundID.SB_A14, pos, 1f, Power + Random.Range(-0.5f, 0.5f));

        room.InGameNoise(new InGameNoise(pos, 4500f * Power, null, 1f));

        player.aerobicLevel = 1f;
        player.exhausted = true;

        if (manager is not null)
        {
            manager.PossessionTime = -120;
            manager.PossessionCooldown = 200;
        }

        killFac = 1f;
        Expired = true;

        CreateFear();

        scareObj?.lifeTime = (int)(-200 * Power);

        if (OptionUtils.IsOptionEnabled(Options.MIND_BLAST_PROTECTION))
            DeathProtection.CreateInstance(player, PlayerProtectionCondition, player.abstractCreature.pos);
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (Expired)
        {
            if (!enlightenedRoom)
            {
                Vector2 targetVel = new(0f, 2f);
                foreach (Creature creature in room.physicalObjects.SelectMany(static list => list).OfType<Creature>())
                {
                    creature.Stun(40);

                    if (creature is Player) continue;

                    foreach (BodyChunk bodyChunk in creature.bodyChunks)
                    {
                        bodyChunk.vel = Vector2.MoveTowards(bodyChunk.vel, targetVel, 20f);
                    }

                    foreach (Limb limb in creature.graphicsModule?.bodyParts?.OfType<Limb>() ?? [])
                    {
                        limb.mode = Limb.Mode.Dangle;
                    }
                }
            }

            if (this is { FadeProgress: <= 0.5f, enlightenedRoom: false })
            {
                enlightenedRoom = true;

                int blastedCrits = 0;
                foreach (PhysicalObject physicalObject in room.physicalObjects.SelectMany(static list => list))
                {
                    if (!RippleLayerCheck(physicalObject.abstractPhysicalObject, player.abstractPhysicalObject)) continue;

                    float stunPower = -(Vector2.Distance(physicalObject.firstChunk.pos, pos) - (StunFactor * Power));
                    float velocity = stunPower * (OptionUtils.IsOptionEnabled(Options.WORLDWIDE_MIND_CONTROL) ? 0.5f : 0.2f);

                    if (physicalObject is Player plr)
                    {
                        if (stunPower <= 0f || !OptionUtils.IsOptionEnabled(Options.MIND_BLAST_PROTECTION)) continue;

                        if (plr != player && (!room.game.IsArenaSession || plr.AI is not null))
                        {
                            Main.Logger.LogDebug($"Protecting other slugcat: {plr}");

                            DeathProtection.CreateInstance(plr, PlayerProtectionCondition);
                        }
                    }
                    else if (physicalObject is Creature crit)
                    {
                        if (crit.Template.baseDamageResistance <= 0.1f * Power && (manager is null || !manager.IsSofanthielSlugcat || manager.IsAttunedSlugcat))
                        {
                            Main.Logger.LogDebug($"Die! {crit} (Too weak to withstand MindBlast)");

                            stunPower = int.MaxValue;

                            if (room.game.IsArenaSession)
                                crit.SetKillTag(player.abstractCreature);

                            crit.Die();
                        }
                        else
                        {
                            Main.Logger.LogDebug($"Target: ({physicalObject}); Stun power: {stunPower} | Velocity: {velocity}");
                        }
                    }
                    else if (physicalObject is Oracle oracle)
                    {
                        if (stunPower <= 0f || (manager is not null && manager.IsSofanthielSlugcat && !manager.IsAttunedSlugcat)) continue;

                        if (room.game.IsArenaSession || room.game.GetStorySession?.saveStateNumber == MoreSlugcatsEnums.SlugcatStatsName.Saint)
                        {
                            AscendOracle(oracle);
                        }
                    }

                    if (velocity > 0f)
                    {
                        Vector2 offset = Custom.RNV() * 0.1f;
                        foreach (BodyChunk bodyChunk in physicalObject.bodyChunks)
                        {
                            Vector2 direction = Custom.DirVec(bodyChunk.pos, pos) + offset;

                            bodyChunk.vel += -direction * velocity;
                        }
                    }

                    if (stunPower <= 0f
                        || physicalObject is not Creature creature
                        || creature is Player) continue;

                    bool isPlayerFriend = IsPlayerFriend(creature.abstractCreature);

                    if (isPlayerFriend)
                    {
                        Main.Logger.LogInfo($"Protecting friend of player: {creature}");

                        DeathProtection.CreateInstance(creature, static (c) => c.stun == 0 && c.IsTileSolid(0, 0, -1));
                    }

                    if (creature.dead) continue;

                    int blastPower = (int)(stunPower * (creature.Template.baseDamageResistance * 0.1f) * (isPlayerFriend ? 0.5f : Power));

                    creature.Stun(blastPower);
                    creature.Deafen(blastPower);

                    if (isPlayerFriend) continue;

                    blastedCrits++;

                    if (creature.stun >= StunDeathThreshold)
                    {
                        room.AddObject(new KarmicShockwave(creature, creature.firstChunk.pos, 64, 24, 48));

                        if (room.game.IsArenaSession)
                            creature.SetKillTag(player.abstractCreature);

                        creature.Die();
                    }
                    else
                    {
                        room.AddObject(new ReverseShockwave(creature.firstChunk.pos, 24f, 0.1f, 80));

                        creature.Violence(room.game.IsArenaSession ? player.mainBodyChunk : creature.firstChunk, null, creature.firstChunk, null, Creature.DamageType.Explosion, blastPower * (0.1f * Power), blastPower * Power);
                    }
                }

                if (ModManager.Watcher && room.locusts is not null)
                {
                    room.locusts.AddAvoidancePoint(pos, 480f * Power, (int)(200 * Power));
                    room.locusts.AddAvoidancePoint(player.mainBodyChunk.pos, 240f * Power, (int)(100 * Power));

                    int locustCount = room.locusts.cloudLocusts.Count + room.locusts.groundLocusts.Count;

                    Main.Logger.LogDebug($"Locusts found: {locustCount}");

                    foreach (LocustSystem.GroundLocust locust in room.locusts.groundLocusts)
                    {
                        if (Custom.DistLess(locust.pos, pos, StunFactor * 0.5f * Power))
                        {
                            locust.alive = false;
                        }
                        else
                        {
                            locust.scared = true;
                        }
                    }

                    for (int i = room.locusts.cloudLocusts.Count - 1; i >= 0; i--)
                    {
                        LocustSystem.CloudLocust locust = room.locusts.cloudLocusts[i];

                        if (!Custom.DistLess(locust.pos, pos, StunFactor * 0.75f * Power)) return;

                        locust.alive = false;
                        room.locusts.cloudLocusts[i] = locust;

                        room.locusts.groundLocusts.Add(new LocustSystem.GroundLocust(locust) { Poisoned = true });
                    }

                    room.locusts.spawnMultiplier = -100f * Power; // Note: This would break vanilla Watcher; There are two hooks in TelekineticsHooks (DelayLocustSpawningILHook and DelayLocustWarmUpHook) which prevent that, with this very use case in mind.

                    if (locustCount > 0)
                    {
                        player.repelLocusts = Mathf.Max(player.repelLocusts, OptionUtils.IsOptionEnabled(Options.MIND_BLAST_PROTECTION) ? (int)(StunFactor * Power * 0.1f * locustCount) : 240);

                        Main.Logger.LogDebug($"Locust repellant duration: {player.repelLocusts}");
                    }
                }

                room.PlaySound(SoundID.Firecracker_Bang, pos, 1f, (Random.value * 1.5f) + (Random.value * Power));
                room.PlaySound(SoundID.SS_AI_Give_The_Mark_Boom, pos, 1f, Random.value + (Random.value * Power));

                if (blastedCrits >= 8 && !room.game.IsArenaSession)
                    Main.CueAchievement("judgement");
            }

            if (FadeProgress <= 0f)
            {
                Destroy();
            }
            return;
        }

        lastKillFac = killFac;
        killFac += 0.015f;

        if (killFac >= 1f)
        {
            Explode();
        }
        else if (this is { activateLightning: null, killFac: >= 0.15f })
        {
            activateLightning = new LightningMachine(pos, pos, new Vector2(pos.x, pos.y + (10f * Power)), 0f, false, true, 0.5f * Power, 0.5f * Power, 1f)
            {
                volume = 0.8f,
                impactType = 3,
                lightningType = 0.1f + (1f - Power)
            };

            room.AddObject(activateLightning);

            Main.Logger.LogDebug($"{nameof(MindBlast)}: Creating lightning machine!");
        }

        if (activateLightning is not null)
        {
            float clampedFac = Mathf.Clamp(killFac, 0.2f, 1f);

            activateLightning.startPoint = new Vector2(Mathf.Lerp(pos.x, pos.x * (150f * Power), (clampedFac * 2f) - 2f), pos.y);
            activateLightning.endPoint = new Vector2(Mathf.Lerp(pos.x, pos.x * (150f * Power), (clampedFac * 2f) - 2f), pos.y + (10f * Power));
            activateLightning.chance = Custom.SCurve(0.7f * Power, clampedFac);
        }

        soundLoop.Volume = Mathf.Lerp(15f, 5f, killFac);
        soundLoop.Update();
    }

    public override void Destroy()
    {
        if (activateLightning is not null and { slatedForDeletetion: false })
            activateLightning.Destroy();

        activateLightning = null;

        if (fadingMeltLights is not null and { slatedForDeletetion: false })
            fadingMeltLights.Destroy();

        fadingMeltLights = null;

        if (scareObj is not null and { slatedForDeletetion: false })
            scareObj.Destroy();

        scareObj = null;

        base.Destroy();

        _activeInstances.Remove(player);

        manager?.TargetSelector?.ResetSelectorInput(true);

        Main.Logger.LogDebug($"{nameof(MindBlast)} from {player} was destroyed!");
    }

    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        sLeaser.sprites = new FSprite[1];

        sLeaser.sprites[0] = new FSprite("Futile_White")
        {
            shader = rCam.game.rainWorld.Shaders["FlatLight"]
        };

        AddToContainer(sLeaser, rCam, null!);
    }

    public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContatiner) =>
        rCam.ReturnFContainer("Bloom").AddChild(sLeaser.sprites[0]);

    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        if (killFac > 0f)
        {
            sLeaser.sprites[0].isVisible = true;

            sLeaser.sprites[0].x = Mathf.Lerp(lastPos.x, pos.x, timeStacker) - camPos.x;
            sLeaser.sprites[0].y = Mathf.Lerp(lastPos.y, pos.y, timeStacker) - camPos.y;

            float num = Mathf.Lerp(lastKillFac, killFac, timeStacker);
            sLeaser.sprites[0].scale = Mathf.Lerp(150f, 2f, Mathf.Pow(num, 0.5f));
            sLeaser.sprites[0].alpha = Mathf.Pow(num, 3f);
        }

        if (Expired)
        {
            rCam.ApplyPalette();
        }

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }

    private void AscendOracle(Oracle oracle)
    {
        if (oracle.ID == MoreSlugcatsEnums.OracleID.ST)
        {
            if (oracle.Consious)
            {
                room.AddObject(new ShockWave(oracle.firstChunk.pos, 500f, 0.75f, 18, false));
                (oracle.oracleBehavior as STOracleBehavior)?.AdvancePhase();

                player.firstChunk.vel = Vector2.zero;
            }
            return;
        }

        if (room.game.session is not StoryGameSession storySession) return;

        if (oracle.ID == MoreSlugcatsEnums.OracleID.CL)
        {
            if (storySession.saveState.deathPersistentSaveData.ripPebbles) return;

            storySession.saveState.deathPersistentSaveData.ripPebbles = true;

            room.PlaySound(SoundID.SS_AI_Talk_1, player.mainBodyChunk, false, 1f, 0.4f);

            Custom.Log($"Ascend {player.SlugCatClass} pebbles - MindBlast Edition");

            if (oracle.oracleBehavior is CLOracleBehavior oracleBehavior)
            {
                oracleBehavior.dialogBox.Interrupt("...", 1);
                oracleBehavior.currentConversation?.Destroy();
            }

            oracle.health = 0f;
        }
        else if (oracle.ID == Oracle.OracleID.SL)
        {
            if (storySession.saveState.deathPersistentSaveData.ripMoon || oracle.glowers < 0 || oracle.mySwarmers.Count < 0) return;

            for (int l = 0; l < oracle.mySwarmers.Count; l++)
            {
                oracle.mySwarmers[l].ExplodeSwarmer();
            }

            storySession.saveState.deathPersistentSaveData.ripMoon = true;

            Custom.Log($"Ascend {player.SlugCatClass} moon - MindBlast Edition");

            if (oracle.oracleBehavior is SLOracleBehaviorHasMark oracleBehavior)
            {
                oracleBehavior.dialogBox.Interrupt("...", 1);
                oracleBehavior.currentConversation?.Destroy();
            }
        }
    }

    private void CreateFear()
    {
        if (scareObj is not null) return;

        scareObj = new FirecrackerPlant.ScareObject(pos, player.abstractPhysicalObject.rippleLayer)
        {
            fearRange = 6000f * Power,
            fearScavs = true
        };

        room.AddObject(scareObj);
        room.InGameNoise(new InGameNoise(pos, 6000f * Power, null, 1f));
    }

    private bool IsPlayerFriend(AbstractCreature creature)
    {
        ArtificialIntelligence? AI = creature.realizedCreature is Lizard lizor ? lizor.AI : creature.abstractAI?.RealAI;

        // Criteria to not be mind-blasted:

        // - Crit has a friend tracker
        // - Crit is friend of player

        // OR:

        // - Crit reacts to social events
        // - Crit has a generic tracker
        // - Crit does not want to attack or eat the player
        // - Crit's community is friendly to this player (rep >= 0.5)

        return AI is not null && ((AI is FriendTracker.IHaveFriendTracker && AI.friendTracker?.friend == player)
            || (AI is IReactToSocialEvents && AI.tracker is not null && !AI.DynamicRelationship(player.abstractCreature).GoForKill && room.game.session.creatureCommunities.LikeOfPlayer(creature.creatureTemplate.communityID, room.world?.RegionNumber ?? 0, player.playerState.playerNumber) >= 0.5f));
    }

    public static MindBlast CreateInstance(Player player, PossessionManager? manager, bool allowMultiple = false)
    {
        if (player?.room is null || (!allowMultiple && (HasInstance(player) || _activeInstances.Values.Any(m => m.player == player))))
        {
            string reason = player?.room is null
                ? "Room or player is null"
                : "Player already has an active instance";

            Main.Logger.LogDebug($"Could not create MindBlast instance: {reason}.");
            return null!;
        }

        MindBlast mindBlast = new(player, manager);

        player.room.AddObject(mindBlast);

        if (!allowMultiple)
            _activeInstances.Add(player, mindBlast);

        Main.Logger.LogDebug($"Created new MindBlast for {player}!");
        return mindBlast;
    }

    public static bool HasInstance(Player? player) => player is not null && _activeInstances.ContainsKey(player);

    public static bool TryGetInstance(Player? player, out MindBlast instance)
    {
        if (player is null)
        {
            instance = null!;
            return false;
        }

        return _activeInstances.TryGetValue(player, out instance);
    }

    private static bool RippleLayerCheck(AbstractPhysicalObject x, AbstractPhysicalObject y) =>
        x.rippleLayer == y.rippleLayer || x.rippleBothSides || y.rippleBothSides;
}