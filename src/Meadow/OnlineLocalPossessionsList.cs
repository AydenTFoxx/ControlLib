using System.Collections.Generic;
using System.Linq;
using RainMeadow;

namespace ControlLib.Meadow;

public class OnlineLocalPossessionsList : SerializableDictionary<OnlineEntity.EntityId, ushort, Creature, Player>
{
    public OnlineLocalPossessionsList() { }

    public OnlineLocalPossessionsList(Dictionary<Creature, Player> dict)
        : base(dict)
    {
    }

    public OnlineLocalPossessionsList(Dictionary<OnlineEntity.EntityId, ushort> dict)
        : base(dict)
    {
    }

    public override void CustomSerialize(Serializer serializer)
    {
        if (serializer.IsWriting)
        {
            serializer.writer.Write(collection.Count);

            foreach (KeyValuePair<OnlineEntity.EntityId, ushort> kvp in collection)
            {
                kvp.Key.CustomSerialize(serializer);

                serializer.writer.Write(kvp.Value);
            }
        }

        if (serializer.IsReading)
        {
            for (int i = serializer.reader.ReadInt32(); i > 0; i--)
            {
                OnlineEntity.EntityId key = new();

                key.CustomSerialize(serializer);

                ushort value = serializer.reader.ReadUInt16();

                collection.Add(key, value);
            }
        }
    }

    public override Dictionary<OnlineEntity.EntityId, ushort> LocalToOnlineIds(Dictionary<Creature, Player> dict)
    {
        Dictionary<OnlineEntity.EntityId, ushort> result = [];

        foreach (KeyValuePair<Creature, Player> kvp in dict)
        {
            OnlineEntity.EntityId? creatureId = kvp.Key.abstractCreature.GetOnlineCreature()?.id;

            if (creatureId is null) continue;

            ushort? playerId = kvp.Value.abstractCreature.GetOnlineCreature()?.owner.inLobbyId;

            if (playerId is null) continue;

            result.Add(creatureId, playerId.Value);
        }

        return result;
    }

    public override Dictionary<Creature, Player> OnlineIdsToLocal(Dictionary<OnlineEntity.EntityId, ushort> dict)
    {
        Dictionary<Creature, Player> result = [];

        foreach (KeyValuePair<OnlineEntity.EntityId, ushort> kvp in dict)
        {
            Creature? crit = OnlineManager.lobby.activeEntities.OfType<OnlineCreature>().FirstOrDefault(oc => oc.id == kvp.Key)?.realizedCreature;

            if (crit is null) continue;

            OnlinePlayer? onlinePlayer = OnlineManager.players.FirstOrDefault(op => op.inLobbyId == kvp.Value);

            if (onlinePlayer is null || OnlineManager.lobby.activeEntities.OfType<OnlineCreature>().FirstOrDefault(oc => oc.owner == onlinePlayer)?.realizedCreature is not Player player) continue;

            result[crit] = player;
        }

        return result;
    }
}