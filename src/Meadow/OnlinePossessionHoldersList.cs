using System.Collections.Generic;
using System.Linq;
using Possessions.Possession;
using RainMeadow;

namespace Possessions.Meadow;

public class OnlinePossessionHoldersList : SerializableDictionary<OnlineEntity.EntityId, SerializableDataSnapshot, Player, PossessionManager>
{
    public OnlinePossessionHoldersList() { }

    public OnlinePossessionHoldersList(Dictionary<Player, PossessionManager> dict)
        : base(dict)
    {
    }

    public OnlinePossessionHoldersList(Dictionary<OnlineEntity.EntityId, SerializableDataSnapshot> dict)
        : base(dict)
    {
    }

    public override void CustomSerialize(Serializer serializer)
    {
        if (serializer.IsWriting)
        {
            serializer.writer.Write(collection.Count);

            foreach (KeyValuePair<OnlineEntity.EntityId, SerializableDataSnapshot> kvp in collection)
            {
                kvp.Key.CustomSerialize(serializer);

                kvp.Value.CustomSerialize(serializer);
            }
        }

        if (serializer.IsReading)
        {
            for (int i = serializer.reader.ReadInt32(); i > 0; i--)
            {
                OnlineEntity.EntityId key = new();
                SerializableDataSnapshot value = new();

                key.CustomSerialize(serializer);
                value.CustomSerialize(serializer);

                collection.Add(key, value);
            }
        }
    }

    public override Dictionary<OnlineEntity.EntityId, SerializableDataSnapshot> LocalToOnlineIds(Dictionary<Player, PossessionManager> dict)
    {
        Dictionary<OnlineEntity.EntityId, SerializableDataSnapshot> result = [];

        foreach (KeyValuePair<Player, PossessionManager> kvp in dict)
        {
            OnlineEntity.EntityId? playerId = kvp.Key.abstractCreature.GetOnlineCreature()?.id;

            if (playerId is null) continue;

            SerializableDataSnapshot snapshot = kvp.Value.GetSnapshot().ToSerializableSnapshot();

            result.Add(playerId, snapshot);
        }

        return result;
    }

    public override Dictionary<Player, PossessionManager> OnlineIdsToLocal(Dictionary<OnlineEntity.EntityId, SerializableDataSnapshot> dict)
    {
        Dictionary<Player, PossessionManager> result = [];

        foreach (KeyValuePair<OnlineEntity.EntityId, SerializableDataSnapshot> kvp in dict)
        {
            if (OnlineManager.lobby.activeEntities.OfType<OnlineCreature>().FirstOrDefault(oc => oc.id == kvp.Key)?.realizedCreature is not Player player) continue;

            result.Add(player, new PossessionManager(kvp.Value.ToDataSnapshot()));
        }

        return result;
    }
}