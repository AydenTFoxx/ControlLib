using System.Collections.Generic;
using System.Linq;
using Possessions.Possession;
using ModLib.Meadow;
using RainMeadow;

namespace Possessions.Meadow;

/// <summary>
///     Rain Meadow RPCs used for syncing the mod's behavior across multiple clients.
/// </summary>
public static class MyRPCs
{
    public static void ApplyHooks() => MeadowUtils.JoinedGameSession += SyncLocalPossessions;
    public static void RemoveHooks() => MeadowUtils.JoinedGameSession -= SyncLocalPossessions;

    /// <summary>
    ///     Initializes a new PossessionManager instance for the given player.
    /// </summary>
    /// <param name="rpcEvent">The RPC event itself.</param>
    /// <param name="playerAvatar">The player avatar who triggered this event.</param>
    [RPCMethod]
    public static void AddPossessionManager(RPCEvent rpcEvent, OnlineCreature playerAvatar)
    {
        if (playerAvatar.realizedCreature is not Player player)
        {
            Main.Logger.LogWarning($"Cannot create a PossessionManager for {playerAvatar}; Not a valid Player instance.");

            rpcEvent.Resolve(new GenericResult.Fail(rpcEvent));
            return;
        }

        Main.Logger.LogInfo($"Created new PossessionManager for {playerAvatar}: {player.GetOrCreatePossessionManager()}");
    }

    /// <summary>
    ///     Spawns possession effects for the given online possession.
    /// </summary>
    /// <param name="rpcEvent">The RPC event itself.</param>
    /// <param name="onlineTarget">The target of the possession.</param>
    /// <param name="isNewPossession">Whether the possession has just started (<c>true</c>) or just stopped (<c>false</c>).</param>
    /// <param name="depletedPossessionTime">If the possession was stopped, whether or not it was caused by a depletion of <c>PossessionTime</c>.</param>
    [RPCMethod]
    public static void ApplyPossessionEffects(RPCEvent rpcEvent, OnlinePhysicalObject onlineTarget, OnlineCreature playerAvatar, bool isNewPossession, bool depletedPossessionTime)
    {
        if (onlineTarget.apo.realizedObject?.room is null)
        {
            Main.Logger.LogWarning($"Target or room is invalid. (Target: {onlineTarget})");

            rpcEvent.Resolve(new GenericResult.Fail(rpcEvent));
            return;
        }

        if (playerAvatar.realizedCreature is not Player player)
        {
            Main.Logger.LogWarning($"Player avatar is not valid. (Player: {playerAvatar})");

            rpcEvent.Resolve(new GenericResult.Fail(rpcEvent));
            return;
        }

        PhysicalObject target = onlineTarget.apo.realizedObject;

        PossessionManager.SpawnPossessionEffects(target, player, isNewPossession, depletedPossessionTime);

        if (player.TryGetPossessionManager(out PossessionManager manager))
        {
            if (target is Creature crit && manager.CanPossessCreature(crit))
                manager.StartCreaturePossession(crit);
            else if (manager.CanPossessItem(target))
                manager.StartItemPossession(target);
            else
                Main.Logger.LogWarning($"Could not start a possession of {target}! Not a valid possession target.");
        }
    }

    [RPCMethod]
    public static void RequestPossessionsSync(RPCEvent rpcEvent, OnlinePlayer caller)
    {
        if (!OnlineManager.lobby.isOwner)
        {
            Main.Logger.LogWarning("Player is not owner of the current lobby; Cannot sync data with other clients.");

            rpcEvent.Resolve(new GenericResult.Fail(rpcEvent));
            return;
        }

        Main.Logger.LogInfo($"Syncing lobby data with {caller}.");

        caller.SendRPCEvent(SerializeLocalPossessions, new OnlineLocalPossessionsList(PossessionExts.LocalPossessions.ToDictionary()));
        caller.SendRPCEvent(SerializePossessionHolders, new OnlinePossessionHoldersList(PossessionExts.PossessionHolders.ToDictionary()));
    }

    /// <summary>
    ///     Sets an online creature as being controlled by the sender of this RPC.
    /// </summary>
    /// <param name="rpcEvent">The RPC event itself.</param>
    /// <param name="onlineTarget">The target creature for control.</param>
    /// <param name="controlled">Whether or not the given creature is being controlled by the sender.</param>
    [RPCMethod]
    public static void SetCreatureControl(RPCEvent rpcEvent, OnlineCreature onlineTarget, bool controlled)
    {
        if (onlineTarget.realizedCreature is not Creature target)
        {
            Main.Logger.LogWarning($"{onlineTarget} is not a controllable creature.");

            rpcEvent.Resolve(new GenericResult.Fail(rpcEvent));
            return;
        }

        target.abstractCreature.controlled = controlled;
        target.UpdateCachedPossession();

        Main.Logger.LogInfo($"{target} is {(controlled ? "now" : "no longer")} being controlled by {rpcEvent.from}.");
    }

    [RPCMethod]
    public static void SerializeLocalPossessions(RPCEvent rpcEvent, OnlineLocalPossessionsList possessions)
    {
        if (OnlineManager.lobby.isOwner)
        {
            Main.Logger.LogWarning("Player is owner of the current lobby; Will not sync possessions list.");

            rpcEvent.Resolve(new GenericResult.Fail(rpcEvent));
            return;
        }

        PossessionExts.LocalPossessions = [.. possessions.LocalDict];

        Main.Logger.LogInfo($"Synced local possessions from serializable list: {PossessionManager.FormatPossessions(possessions.collection)}");
    }

    [RPCMethod]
    public static void SerializePossessionHolders(RPCEvent _, OnlinePossessionHoldersList possessionHolders)
    {
        foreach (KeyValuePair<Player, PossessionManager> kvp in possessionHolders.LocalDict)
        {
            PossessionExts.PossessionHolders.Add(kvp);

            Main.Logger.LogInfo($"Adding serialized PossessionManager instance: {kvp.Value}");
        }
    }

    /// <summary>
    ///     Broadcasts RPCs to all online players of the new possession, for spawning visual effects and syncing creature behavior.
    /// </summary>
    /// <param name="target">The object being possessed.</param>
    /// <param name="caller">The player who owns the possession.</param>
    /// <param name="isNewPossession">Whether the possession has just started (<c>true</c>) or just stopped (<c>false</c>).</param>
    /// <param name="depletedPossessionTime">If the possession was stopped, whether or not it was caused by a depletion of <c>PossessionTime</c>.</param>
    public static void SyncOnlinePossession(PhysicalObject target, Player caller, bool isNewPossession, bool depletedPossessionTime)
    {
        OnlinePhysicalObject? onlineTarget = target?.abstractPhysicalObject.GetOnlineObject();
        OnlineCreature? playerAvatar = caller.abstractCreature.GetOnlineCreature();

        if (onlineTarget is null || playerAvatar is null) return;

        onlineTarget.BroadcastOnceRPCInRoom(ApplyPossessionEffects, onlineTarget, playerAvatar, isNewPossession, depletedPossessionTime);

        if (target is Creature creature)
        {
            OnlineCreature? onlineCreature = creature.abstractCreature.GetOnlineCreature();

            if (onlineCreature is null) return;

            foreach (OnlinePlayer op in OnlineManager.players)
            {
                if (op.isMe) continue;

                op.SendRPCEvent(SetCreatureControl, onlineCreature, isNewPossession);
            }
        }
    }

    /// <summary>
    ///     Sends an RPC event to all players for synchronizing the creation of a new PossessionManager instance.
    /// </summary>
    /// <param name="player">The player who triggered this event.</param>
    public static void SyncPossessionManagerCreation(Player player)
    {
        OnlineCreature? playerAvatar = player.abstractCreature.GetOnlineCreature();

        if (playerAvatar is null) return;

        foreach (OnlinePlayer op in OnlineManager.players)
        {
            if (op.isMe) continue;

            op.SendRPCEvent(AddPossessionManager, playerAvatar);
        }
    }

    private static void SyncLocalPossessions(GameSession _)
    {
        if (OnlineManager.lobby.isOwner) return;

        OnlineManager.lobby.owner.SendRPCEvent(RequestPossessionsSync, OnlineManager.mePlayer);
    }
}