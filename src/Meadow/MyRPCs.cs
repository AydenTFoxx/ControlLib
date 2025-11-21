using ModLib.Meadow;
using RainMeadow;
using Random = UnityEngine.Random;

namespace ControlLib.Meadow;

public static class MyRPCs
{
    [SoftRPCMethod]
    public static void ApplyPossessionEffects(RPCEvent rpcEvent, OnlineCreature onlineTarget, bool isPossession)
    {
        if (onlineTarget.realizedCreature is not Creature target || target.room is null)
        {
            Main.Logger.LogWarning($"Target or room is invalid; Target: {onlineTarget.realizedCreature} | Room: {onlineTarget.realizedCreature?.room}");

            rpcEvent.Resolve(new GenericResult.Fail(rpcEvent));
            return;
        }

        if (isPossession)
        {
            target.room.AddObject(new TemplarCircle(target, target.mainBodyChunk.pos, 48f, 8f, 2f, 12, true));
            target.room.AddObject(new ShockWave(target.mainBodyChunk.pos, 100f, 0.08f, 4, false));
            target.room.PlaySound(SoundID.SS_AI_Give_The_Mark_Boom, target.mainBodyChunk, loop: false, 1f, 1.25f + (Random.value * 1.25f));
        }
        else
        {
            target.room.AddObject(new ReverseShockwave(target.mainBodyChunk.pos, 64f, 0.05f, 24));
            target.room.PlaySound(SoundID.HUD_Pause_Game, target.mainBodyChunk, loop: false, 1f, 0.5f);
        }
    }

    [SoftRPCMethod]
    public static void SetCreatureControl(RPCEvent rpcEvent, OnlineCreature onlineTarget, bool controlled)
    {
        if (onlineTarget.realizedCreature is not Creature target)
        {
            Main.Logger.LogWarning($"{onlineTarget} is not a controllable creature.");

            rpcEvent.Resolve(new GenericResult.Fail(rpcEvent));
            return;
        }

        target.abstractCreature.controlled = controlled;

        Main.Logger.LogInfo($"{target} is {(controlled ? "now" : "no longer")} being controlled by {rpcEvent.from}.");
    }

    public static void SyncCreaturePossession(Creature creature, bool isPossession)
    {
        OnlineCreature? onlineCreature = creature?.abstractCreature.GetOnlineCreature();

        if (onlineCreature is null) return;

        onlineCreature.BroadcastOnceRPCInRoom(ApplyPossessionEffects, onlineCreature, isPossession);

        OnlineManager.players.ForEach(op =>
        {
            if (op.isMe) return;

            op.SendRPCEvent(SetCreatureControl, onlineCreature, isPossession);
        });
    }
}