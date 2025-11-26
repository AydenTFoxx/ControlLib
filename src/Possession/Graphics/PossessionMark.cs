using UnityEngine;

namespace ControlLib.Possession.Graphics;

public class PossessionMark : PlayerAccessory
{
    private readonly float targetSize;
    private bool invalidated;

    public Creature Target { get; }
    public Player Owner { get; }

    public Vector2 MarkPos => new Vector2(Target.firstChunk.pos.x, Target.firstChunk.pos.y + targetSize) - camPos;

    public PossessionMark(Creature target, Player owner) : base(owner)
    {
        targetSize = target.firstChunk.rad * 8;

        Target = target;
        Owner = owner;

        FollowCreature = target;
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (invalidated)
        {
            if (alpha <= 0f)
                Destroy();
            return;
        }

        if (Target.room is not null && Target.room != room)
        {
            TryRealizeInRoom(Target.room, Target.firstChunk.pos - camPos);
        }

        if (!Target.TryGetPossession(out Player possessor) || possessor != Owner)
        {
            invalidated = true;
        }
    }

    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        sLeaser.sprites = new FSprite[2];

        sLeaser.sprites[0] = new FSprite("Futile_White", true)
        {
            shader = rCam.game.rainWorld.Shaders["FlatLight"],
            scale = 2f
        };
        sLeaser.sprites[1] = new FSprite("pixel", true)
        {
            scale = 5f
        };

        base.InitiateSprites(sLeaser, rCam);
    }

    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        this.camPos = camPos;

        targetAlpha = invalidated ? 0f : 1f;

        if (UpdateAlpha(speed: 0.1f) <= 0f) return;

        sLeaser.sprites[0].alpha = alpha * 2f * 0.2f;
        sLeaser.sprites[0].SetPosition(MarkPos);

        sLeaser.sprites[1].alpha = alpha * 2f;
        sLeaser.sprites[1].SetPosition(MarkPos);

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }
}