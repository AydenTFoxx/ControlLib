using System;
using ControlLib.Meadow;
using ModLib;
using ModLib.Collections;

namespace ControlLib.Possession;

/// <summary>
/// Extension methods for retrieving the possession holders of a given creature.
/// </summary>
public static class PossessionExts
{
    /// <summary>
    /// Stores the result of previous queries for a given creature and its possessing player.
    /// </summary>
    /// <remarks>References are valid for as long as the possession lasts; Once possession ends, the given creature's key-value pair is discarded.</remarks>
    internal static WeakDictionary<Creature, Player> LocalPossessions = [];
    /// <summary>
    /// Stores all players with a <c>PossessionManager</c> instance.
    /// </summary>
    /// <remarks>This is used as a reference to determine which player is currently possessing a given creature.</remarks>
    internal static WeakDictionary<Player, PossessionManager> PossessionHolders = [];

    /// <summary>
    /// Obtains the given player's <c>PossessionManager</c> instance. If none is found, a new one is created with default values.
    /// </summary>
    /// <param name="self">The player to be queried.</param>
    /// <returns>The existing <c>PossessionManager</c> instance, or a new one if none was found.</returns>
    public static PossessionManager GetOrCreatePossessionManager(this Player self)
    {
        if (TryGetPossessionManager(self, out PossessionManager manager)) return manager;

        PossessionManager newManager = new(self);

        PossessionHolders.Add(self, newManager);

        if (Extras.IsMeadowEnabled)
        {
            try
            {
                MyRPCs.SyncPossessionManagerCreation(self);
            }
            catch (Exception ex)
            {
                Main.Logger.LogError(ex);
            }
        }

        Main.Logger.LogInfo($"New PossessionManager instance:{Environment.NewLine}{newManager}");
        return newManager;
    }

    /// <summary>
    /// Removes the player's PossessionManager instance from the cache.
    /// </summary>
    /// <param name="self">The player itself.</param>
    /// <returns>
    ///     <c>true</c> if the instance was successfully removed, <c>false</c> otherwise.
    ///     This method returns <c>false</c> if the PossessionManager instance is not found in the internal cache.
    /// </returns>
    public static bool RemovePossessionManager(this Player self) => PossessionHolders.Remove(self);

    /// <summary>
    /// Attempts to retrieve the given creature's possessing player. If none is found, <c>null</c> is returned instead.
    /// </summary>
    /// <param name="self">The creature to be queried.</param>
    /// <param name="possessor">The output value; May be a <c>Player</c> instance or <c>null</c>.</param>
    /// <returns><c>true</c> if a value was found, <c>false</c> otherwise.</returns>
    public static bool TryGetPossession(this Creature self, out Player possessor)
    {
        possessor = null!;

        if (LocalPossessions.TryGetValue(self, out Player player))
        {
            possessor = player;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to retrieve the given player's <c>PossessionManager</c> instance. If none is found, <c>null</c> is returned instead.
    /// </summary>
    /// <param name="self">The player to be queried.</param>
    /// <param name="manager">The output value; May be a <c>PossessionManager</c> instance or <c>null</c>.</param>
    /// <returns><c>true</c> if a value was found, <c>false</c> otherwise.</returns>
    public static bool TryGetPossessionManager(this Player self, out PossessionManager manager) => PossessionHolders.TryGetValue(self, out manager);

    /// <summary>
    /// Adds or removes the given creature's cached possession pair, depending on whether the possession is still valid.
    /// </summary>
    /// <param name="self">The creature to be checked.</param>
    /// <remarks>This should always be called upon updating a creature's possession state.</remarks>
    public static void UpdateCachedPossession(this Creature self)
    {
        if (LocalPossessions.TryGetValue(self, out Player possession))
        {
            if (possession.TryGetPossessionManager(out PossessionManager manager)
                && !manager.HasCreaturePossession(self))
            {
                Main.Logger.LogDebug($"- {self} is no longer being possessed by {manager.GetPlayer()}.");

                LocalPossessions.Remove(self);
            }
        }
        else
        {
            foreach (PossessionManager manager in PossessionHolders.Values)
            {
                if (manager.HasCreaturePossession(self))
                {
                    Main.Logger.LogDebug($"+ {self} is now being possessed by {manager.GetPlayer()}.");

                    LocalPossessions[self] = manager.GetPlayer();
                }
            }
        }
    }
}