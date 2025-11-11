using System.Linq;
using System.Runtime.CompilerServices;
using ControlLib.Possession;
using ModLib.Options;
using MoreSlugcats;
using Noise;
using RWCustom;
using UnityEngine;

namespace ControlLib.Telekinetics;

public class MindBlast : CosmeticSprite
{
    private static readonly ConditionalWeakTable<Player, MindBlast> _activeInstances = new();

    private readonly Player player;
    private readonly DynamicSoundLoop soundLoop;
    private readonly Color explodeColor;

    private float killFac;
    private float lastKillFac;

    private LightningMachine? activateLightning;
    private FadingMeltLights? fadingMeltLights;

    private bool enlightenedRoom;

    public bool Expired { get; private set; }
    public float Power { get; }

    private float FadeProgress => fadingMeltLights?.FadeProgress ?? 0f;

    private MindBlast(Player player, float power)
    {
        this.player = player;
        Power = Mathf.Clamp(power, -2f, 2f);

        soundLoop = new ChunkDynamicSoundLoop(player.mainBodyChunk)
        {
            sound = SoundID.Rock_Through_Air_LOOP,
            Volume = 1f,
            Pitch = 0.5f
        };

        explodeColor = power < 1f
            ? RainWorld.AntiGold.rgb
            : power > 1f
                ? RainWorld.RippleGold
                : RainWorld.GoldRGB;

        room = player.room;
    }

    public void Interrupt()
    {
        if (room is null || killFac < 0.3f)
        {
            Main.Logger?.LogDebug($"{nameof(MindBlast)}: Got interrupted but cannot go kaboom; Ignoring. (Room is: {room} | killFac is {killFac})");

            Destroy();
            return;
        }

        if (killFac >= 0.5f)
        {
            AbstractPhysicalObject.AbstractObjectType bombType = killFac >= 0.95f
                ? DLCSharedEnums.AbstractObjectType.SingularityBomb
                : AbstractPhysicalObject.AbstractObjectType.ScavengerBomb;

            Main.ExplodePos(player, room.ToWorldCoordinate(pos), bombType, (p) =>
            {
                if (p is SingularityBomb singularity)
                    singularity.zeroMode = true;
            });

            room.AddObject(new FirecrackerPlant.ScareObject(pos));
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

        if (!player.dead)
        {
            player.exhausted = true;
            player.aerobicLevel = killFac;

            int stunTime = (int)(120f * killFac);

            player.Stun(stunTime);

            room.AddObject(new CreatureSpasmer(player, false, stunTime / 2));
        }

        Main.Logger?.LogDebug($"{nameof(MindBlast)}: Interrupted! Progress was: {killFac}");

        Destroy();
    }

    public void Explode()
    {
        if (room is null) return;

        room.AddObject(new Explosion.ExplosionLight(pos, 2000f * Power, 2f, 60, explodeColor));

        room.AddObject(new ShockWave(pos, 275f * Power, 0.2425f * Power, (int)(200 * Power), true));
        room.AddObject(new ShockWave(pos, 1250f * Power, 0.0925f * Power, (int)(180 * Power), false));

        room.AddObject(fadingMeltLights = new FadingMeltLights(room));

        room.ScreenMovement(pos, default, 0.35f * Power);
        room.PlaySound(SoundID.SB_A14, pos, 1f, Power + Random.Range(-0.5f, 0.5f));

        room.InGameNoise(new InGameNoise(pos, 4500f * Power, player, 1f));

        player.aerobicLevel = 1f;
        player.exhausted = true;

        if (player.TryGetPossessionManager(out PossessionManager manager))
        {
            manager.PossessionTime = -120;
            manager.PossessionCooldown = 200;
        }

        Expired = true;

        DeathProtection.CreateInstance(player, (p) => p.canJump > 0, player.abstractCreature.pos);
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (Expired)
        {
            if (room is null) return;

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

            if (FadeProgress <= 0.5f && !enlightenedRoom)
            {
                enlightenedRoom = true;

                float stunFactor = OptionUtils.GetOptionValue<float>("mind_blast_stun_factor");
                int stunDeathThreshold = OptionUtils.GetOptionValue<int>("stun_death_threshold");

                foreach (PhysicalObject physicalObject in room.physicalObjects.SelectMany(static list => list))
                {
                    if (!RippleLayerCheck(physicalObject.abstractPhysicalObject, player.abstractPhysicalObject)) continue;

                    float stunPower = -(Vector2.Distance(physicalObject.firstChunk.pos, pos) - stunFactor) * Power;
                    float velocity = stunPower * 0.2f;

                    if (physicalObject is Creature crit)
                    {
                        if (crit.Template.baseDamageResistance <= 0.1f)
                        {
                            Main.Logger?.LogDebug($"Die! {crit} (Too weak to withstand MindBlast)");

                            stunPower = int.MaxValue;
                        }
                        else
                        {
                            Main.Logger?.LogDebug($"Target: ({physicalObject}); Stun power: {stunPower} | Velocity: {velocity}");
                        }
                    }
                    else if (physicalObject is Oracle oracle)
                    {
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
                        || creature is Player or { dead: true }) continue;

                    bool isPlayerFriend = IsPlayerFriend(creature.abstractCreature);

                    int blastPower = (int)(stunPower * (creature.Template.baseDamageResistance * 0.1f) * (isPlayerFriend ? 0.5f : Power));

                    creature.Stun(blastPower);
                    creature.Deafen(blastPower);

                    if (isPlayerFriend) continue;

                    if (room.game.IsArenaSession)
                        creature.SetKillTag(player.abstractCreature);

                    if (creature.stun >= stunDeathThreshold)
                    {
                        room.AddObject(new KarmicShockwave(creature, creature.firstChunk.pos, 64, 24, 48));

                        creature.Die();
                    }
                    else
                    {
                        room.AddObject(new ReverseShockwave(creature.firstChunk.pos, 24f, 0.1f, 80));

                        creature.Violence(creature.firstChunk, null, creature.firstChunk, null, Creature.DamageType.Explosion, blastPower * (0.1f * Power), blastPower * Power);
                    }
                }

                if (ModManager.Watcher)
                {
                    int locustCount = 0;

                    foreach (LocustSystem locustSystem in room.updateList.OfType<LocustSystem>())
                    {
                        locustSystem.AddAvoidancePoint(pos, 480f * Power, (int)(200 * Power));
                        locustSystem.KillInRadius(pos, 360 * Power);

                        int locustsFound = locustSystem.cloudLocusts.Count + locustSystem.groundLocusts.Count;

                        locustCount += locustsFound;

                        Main.Logger?.LogDebug($"Locusts found: {locustsFound}");

                        foreach (LocustSystem.GroundLocust locust in locustSystem.groundLocusts)
                        {
                            if (locust.Poisoned)
                            {
                                locust.alive = false;
                                continue;
                            }

                            locust.scared = true;
                        }

                        for (int j = 0; j < locustSystem.cloudLocusts.Count; j++)
                        {
                            LocustSystem.CloudLocust locust = locustSystem.cloudLocusts[j];

                            locust.alive = !Custom.DistLess(locust.pos, pos, stunFactor * 0.5f * Power);

                            locustSystem.cloudLocusts[j] = locust;
                        }
                    }

                    if (locustCount > 0)
                    {
                        Main.Logger?.LogDebug($"Total locusts: {locustCount}");

                        player.repelLocusts = (int)(stunFactor * Power * 0.1f * locustCount);

                        Main.Logger?.LogDebug($"Locust repellant duration: {player.repelLocusts}");
                    }
                }

                room.PlaySound(SoundID.Firecracker_Bang, pos, 1f, (Random.value * 1.5f) + (Random.value * Power));
                room.PlaySound(SoundID.SS_AI_Give_The_Mark_Boom, pos, 1f, Random.value + (Random.value * Power));
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
            killFac = 1f;

            Explode();
        }
        else if (activateLightning is null && killFac >= 0.15f)
        {
            activateLightning = new LightningMachine(pos, pos, new Vector2(pos.x, pos.y + 10f), 0f, false, true, 0.5f, 0.5f * Power, 1f)
            {
                volume = 0.8f,
                impactType = 3,
                lightningType = 0.1f
            };

            room.AddObject(activateLightning);

            Main.Logger?.LogDebug($"{nameof(MindBlast)}: Creating lightning machine!");
        }

        if (activateLightning is not null)
        {
            float num3 = Mathf.Clamp(killFac, 0.2f, 1f);
            activateLightning.startPoint = new Vector2(Mathf.Lerp(pos.x, 150f, (num3 * 2f) - 2f), pos.y);
            activateLightning.endPoint = new Vector2(Mathf.Lerp(pos.x, 150f, (num3 * 2f) - 2f), pos.y + 10f);
            activateLightning.chance = Custom.SCurve(0.7f, num3);
        }

        soundLoop.Volume = Mathf.Lerp(15f, 5f, killFac);

        soundLoop.Update();
    }

    public override void Destroy()
    {
        activateLightning?.Destroy();
        activateLightning = null;

        fadingMeltLights?.Destroy();
        fadingMeltLights = null;

        base.Destroy();

        _activeInstances.Remove(player);

        if (player.TryGetPossessionManager(out PossessionManager manager))
        {
            manager.TargetSelector?.ResetSelectorInput(true);
        }

        Main.Logger?.LogDebug($"{nameof(MindBlast)} from {player} was destroyed!");
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

            Custom.Log("Ascend saint pebbles - MindBlast Edition");

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

            Custom.Log("Ascend saint moon - MindBlast Edition");

            if (oracle.oracleBehavior is SLOracleBehaviorHasMark oracleBehavior)
            {
                oracleBehavior.dialogBox.Interrupt("...", 1);
                oracleBehavior.currentConversation?.Destroy();
            }
        }
    }

    private bool IsPlayerFriend(AbstractCreature creature)
    {
        if (creature.realizedCreature is not IReactToSocialEvents) return false;

        bool result = room.game.session.creatureCommunities.LikeOfPlayer(creature.creatureTemplate.communityID, room.world?.RegionNumber ?? 0, player.playerState.playerNumber) >= 0.5f;

        Main.Logger?.LogDebug($"# Community of {creature} likes player? {result}");

        if (!result)
        {
            result = creature.abstractAI?.RealAI?.friendTracker?.friend == player;

            Main.Logger?.LogDebug($"# Is {creature} friend of player? {result}");
        }

        return result;
    }

    public static void CreateInstance(Player player, float power)
    {
        if (player?.room is null || HasInstance(player))
        {
            string reason = player?.room is null
                ? "Room or player is null"
                : "Player already has an active instance";

            Main.Logger?.LogDebug($"Could not create MindBlast instance: {reason}.");
            return;
        }

        MindBlast mindBlast = new(player, power);

        player.room.AddObject(mindBlast);

        _activeInstances.Add(player, mindBlast);

        Main.Logger?.LogDebug($"Created new MindBlast for {player}!");
    }

    public static bool TryGetInstance(Player player, out MindBlast instance) => _activeInstances.TryGetValue(player, out instance);

    public static bool HasInstance(Player player) => _activeInstances.TryGetValue(player, out _);

    private static bool RippleLayerCheck(AbstractPhysicalObject x, AbstractPhysicalObject y) =>
        x.rippleLayer == y.rippleLayer || x.rippleBothSides || y.rippleBothSides;
}