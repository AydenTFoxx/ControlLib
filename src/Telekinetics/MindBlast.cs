using System.Linq;
using System.Runtime.CompilerServices;
using ModLib.Collections;
using MoreSlugcats;
using Noise;
using RWCustom;
using UnityEngine;

namespace ControlLib.Telekinetics;

public class MindBlast : CosmeticSprite
{
    private static readonly ConditionalWeakTable<Player, MindBlast> _activeInstances = new();
    private static readonly WeakDictionary<Player, int> _playerCooldowns = [];

    private readonly Player player;
    private readonly int power;

    private readonly DynamicSoundLoop soundLoop;
    private readonly Color explodeColor = RainWorld.GoldRGB;

    private float killFac;
    private float lastKillFac;

    private LightningMachine? activateLightning;
    private RoomSettings.RoomEffect? meltEffect;

    private bool expired;
    private bool enlightenedRoom;
    private bool forcedMeltEffect;

    private float effectAdd;
    private readonly float effectInitLevel;

    public MindBlast(Player player, int power)
    {
        this.player = player;
        this.power = power;

        soundLoop = new DisembodiedDynamicSoundLoop(this)
        {
            sound = SoundID.Rock_Through_Air_LOOP,
            Pitch = 0.5f
        };

        room = player.room;

        for (int i = 0; i < room.roomSettings.effects.Count; i++)
        {
            if (room.roomSettings.effects[i].type == RoomSettings.RoomEffect.Type.VoidMelt)
            {
                meltEffect = room.roomSettings.effects[i];
                effectInitLevel = meltEffect.amount;
                return;
            }
        }
    }

    public void Interrupt()
    {
        if (room is null || killFac < 0.3f)
        {
            Main.Logger?.LogDebug($"{nameof(MindBlast)}: Got interrupted but cannot go kaboom; Ignoring. (Room is: {room} | killFac is {killFac})");

            Destroy();
            return;
        }

        AbstractPhysicalObject abstractKaboom = new(
            room.world,
            killFac < 0.55f
                ? AbstractPhysicalObject.AbstractObjectType.FirecrackerPlant
                : killFac >= 0.9f
                    ? DLCSharedEnums.AbstractObjectType.SingularityBomb
                    : AbstractPhysicalObject.AbstractObjectType.ScavengerBomb,
            null,
            room.ToWorldCoordinate(pos),
            room.world.game.GetNewID()
        );

        abstractKaboom.RealizeInRoom();

        if (abstractKaboom.realizedObject is SingularityBomb singularity)
        {
            singularity.zeroMode = true;
            singularity.explodeColor = new Color(1f, 0.2f, 0.2f);

            singularity.Explode();
        }
        else if (abstractKaboom.realizedObject is ScavengerBomb scavBomb)
        {
            scavBomb.Explode(null);
        }
        else
        {
            (abstractKaboom.realizedObject as FirecrackerPlant)?.Explode();
        }

        if (!player.dead)
        {
            if (player.stun < 80)
                player.Stun(80);

            room.AddObject(new CreatureSpasmer(player, false, 40));
        }

        Main.Logger?.LogDebug($"{nameof(MindBlast)}: Interrupted! Explosion type: {abstractKaboom.realizedObject}; Player alive? {!player.dead}");

        Destroy();
    }

    public void Explode()
    {
        if (room is null) return;

        Vector2 vector = Vector2.Lerp(pos, lastPos, 0.35f);

        room.AddObject(new Explosion.ExplosionLight(vector, 280f, 1f, 7, explodeColor));
        room.AddObject(new Explosion.ExplosionLight(vector, 230f, 1f, 3, new Color(1f, 1f, 1f)));
        room.AddObject(new Explosion.ExplosionLight(vector, 2000f, 2f, 60, explodeColor));
        room.AddObject(new ShockWave(vector, 350f, 0.485f, 300, true));
        room.AddObject(new ShockWave(vector, 2000f, 0.185f, 180, false));

        room.ScreenMovement(new Vector2?(vector), default, 0.75f);
        room.PlaySound(SoundID.SB_A14, player.mainBodyChunk, false, 1f, 0.5f + (Random.value * 0.25f));

        room.InGameNoise(new InGameNoise(vector, 9000f, player, 1f));
        room.InGameNoise(new InGameNoise(pos, 12000f, player, 1f));

        if (meltEffect is null)
        {
            meltEffect = new RoomSettings.RoomEffect(RoomSettings.RoomEffect.Type.VoidMelt, 1f, false);

            room.roomSettings.effects.Add(meltEffect);

            forcedMeltEffect = true;
        }

        for (int i = 0; i < 20; i++)
        {
            room.AddObject(new MeltLights.MeltLight(1f, room.RandomPos(), room, RainWorld.GoldRGB));
        }
        effectAdd = 1f;

        player.Stun(40);

        player.aerobicLevel = 1f;
        player.exhausted = true;

        expired = true;
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (expired)
        {
            effectAdd = Mathf.Max(0f, effectAdd - 0.016666668f);
            meltEffect?.amount = Mathf.Lerp(effectInitLevel, 1f, Custom.SCurve(effectAdd, 0.6f));

            if (effectAdd <= 0.5f && room is not null && !enlightenedRoom)
            {
                enlightenedRoom = true;

                foreach (PhysicalObject physicalObject in room.physicalObjects.SelectMany(static list => list))
                {
                    if (physicalObject == player) continue;

                    foreach (BodyChunk bodyChunk in physicalObject.bodyChunks)
                    {
                        bodyChunk.vel += Custom.RNV() * 24f;
                    }

                    room.AddObject(new KarmicShockwave(physicalObject, physicalObject.firstChunk.pos, 32, 16, 32));

                    if (physicalObject is Creature creature)
                    {
                        float blastPower = power * 0.1f / creature.Template.baseDamageResistance;

                        bool shouldDie = creature.State is HealthState healthState
                            ? blastPower * 0.5f >= healthState.health
                            : blastPower >= 2f;

                        if (shouldDie)
                        {
                            creature.SetKillTag(player.abstractCreature);

                            creature.Die();
                        }
                        else
                        {
                            creature.Stun(power);
                        }
                    }
                }
            }

            if (effectAdd <= 0f)
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

            return;
        }
        else if (activateLightning is null && killFac >= 0.25f)
        {
            activateLightning = new LightningMachine(pos, pos, new Vector2(pos.x, pos.y + 10f), 0f, false, true, 0.3f, 1f, 1f)
            {
                volume = 0.8f,
                impactType = 3,
                lightningType = 0.65f
            };
            room.AddObject(activateLightning);

            Main.Logger?.LogDebug($"{nameof(MindBlast)}: Creating lightning machine!");
        }

        if (activateLightning is not null)
        {
            float num3 = Mathf.Clamp(killFac, 0.2f, 1f);
            activateLightning.startPoint = new Vector2(Mathf.Lerp(pos.x, 150f, (num3 * 2f) - 2f), pos.y);
            activateLightning.endPoint = new Vector2(Mathf.Lerp(pos.x, 150f, (num3 * 2f) - 2f), pos.y + 10f);
            activateLightning.chance = Mathf.Lerp(0f, 0.7f, num3);
        }

        soundLoop.Volume = Mathf.InverseLerp(5f, 15f, killFac);
        soundLoop.Update();
    }

    public override void Destroy()
    {
        activateLightning?.Destroy();
        activateLightning = null;

        if (forcedMeltEffect)
        {
            room?.roomSettings.effects.Remove(meltEffect);
        }
        meltEffect = null;

        base.Destroy();

        _activeInstances.Remove(player);
        _playerCooldowns.Add(player, 80);

        Main.Logger?.LogDebug($"{nameof(MindBlast)} from {player} was destroyed! Applying cooldown.");
    }

    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        sLeaser.sprites = new FSprite[1];

        sLeaser.sprites[0] = new FSprite("Futile_White", true)
        {
            shader = rCam.game.rainWorld.Shaders["FlatLight"]
        };

        AddToContainer(sLeaser, rCam, null!);
    }

    public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContatiner) =>
        rCam.ReturnFContainer("Shortcuts").AddChild(sLeaser.sprites[0]);

    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        if (killFac > 0f)
        {
            sLeaser.sprites[0].isVisible = true;

            sLeaser.sprites[0].x = Mathf.Lerp(lastPos.x, pos.x, timeStacker) - camPos.x;
            sLeaser.sprites[0].y = Mathf.Lerp(lastPos.y, pos.y, timeStacker) - camPos.y;

            float num = Mathf.Lerp(lastKillFac, killFac, timeStacker);
            sLeaser.sprites[0].scale = Mathf.Lerp(200f, 2f, Mathf.Pow(num, 0.5f));
            sLeaser.sprites[0].alpha = Mathf.Pow(num, 3f);
        }

        if (expired)
        {
            rCam.ApplyPalette();
        }

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }

    public static void CreateInstance(Player player, int blastPower)
    {
        if (player?.room is null || HasInstance(player) || HasCooldown(player))
        {
            string reason = player?.room is null
                ? "Room or player is null"
                : HasInstance(player)
                    ? "Player already has an active instance"
                    : "Player is on cooldown";

            Main.Logger?.LogDebug($"Could not create MindBlast instance: {reason}.");
            return;
        }

        MindBlast mindBlast = new(player, blastPower);

        player.room.AddObject(mindBlast);

        _activeInstances.Add(player, mindBlast);

        Main.Logger?.LogDebug($"Created new MindBlast for {player}!");
    }

    public static bool TryGetInstance(Player player, out MindBlast instance) => _activeInstances.TryGetValue(player, out instance);

    public static bool HasInstance(Player player) => _activeInstances.TryGetValue(player, out _);

    public static bool HasCooldown(Player player) => _playerCooldowns.TryGetValue(player, out _);
}