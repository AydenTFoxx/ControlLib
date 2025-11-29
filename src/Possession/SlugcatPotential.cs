using System;
using System.Collections.Generic;
using MoreSlugcats;
using Watcher;

namespace ControlLib.Possession;

/// <summary>
///     Determines the total possession time (static or dynamic) for a given slugcat.
/// </summary>
/// <param name="potential">The total amount of possession time for this slugcat.</param>
/// <param name="isAttuned">Whether this slugcat is "Attuned"; Possession time is consumed at a slower rate, and has lesser consequences for possession time exhaustion.</param>
/// <param name="isHardmode">Whether this slugcat is "Hardmode"; Regenerates possession time at a slower rate, has more severe consequences for possession time exhaustion, and cannot possess certain creatures.</param>
public readonly struct SlugcatPotential(int potential, bool isAttuned = false, bool isHardmode = false) : IEquatable<SlugcatPotential>, IComparable<SlugcatPotential>
{
    private static readonly Dictionary<SlugcatStats.Name, SlugcatPotential> StaticPotentials = [];

    /// <summary>
    ///     The default potential value, used for any slugcat not otherwise registered. This value matches Survivor's possession time potential.
    /// </summary>
    public static SlugcatPotential DefaultPotential => StaticPotentials[SlugcatStats.Name.White];

    /// <summary>
    ///     The "standard" potential value for Attuned slugcats. This value matches Monk's possession time potential.
    /// </summary>
    public static SlugcatPotential AttunedPotential => StaticPotentials[SlugcatStats.Name.Yellow];

    /// <summary>
    ///     The "standard" potential value for Hardmode slugcats. This value matches Hunter's possession time potential.
    /// </summary>
    public static SlugcatPotential HardmodePotential => StaticPotentials[SlugcatStats.Name.Red];

    static SlugcatPotential()
    {
        StaticPotentials[SlugcatStats.Name.White] = new(360);
        StaticPotentials[SlugcatStats.Name.Yellow] = new(480, isAttuned: true);
        StaticPotentials[SlugcatStats.Name.Red] = new(240, isHardmode: true);

        if (ModManager.MSC)
        {
            StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Artificer] = new(180, isHardmode: true);
            StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Gourmand] = new(320, isHardmode: true);
            StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Rivulet] = new(80);
            StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Saint] = new(520, isAttuned: true, isHardmode: true);
            StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Sofanthiel] = new(1, isHardmode: true);
            StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Spear] = new(800, isHardmode: true);
            StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Slugpup] = new(120, isAttuned: true, isHardmode: true);
        }

        if (ModManager.Watcher)
        {
            StaticPotentials[WatcherEnums.SlugcatStatsName.Watcher] = new(400);
        }
    }

    /// <summary>
    ///     The raw potential value for this slugcat.
    /// </summary>
    public readonly int Potential = potential;

    /// <summary>
    ///     Whether or not this slugcat is "Attuned".
    /// </summary>
    public readonly bool IsAttuned = isAttuned;

    /// <summary>
    ///     Whether or not this slugcat is "Hardmode".
    /// </summary>
    public readonly bool IsHardmode = isHardmode;

    /// <inheritdoc/>
    public int CompareTo(SlugcatPotential other)
    {
        int result = Potential.CompareTo(other.Potential);

        if (result == 0)
        {
            if (IsAttuned && !other.IsAttuned)
                result++;
            if (IsHardmode && !other.IsHardmode)
                result--;
        }

        return result;
    }

    /// <summary>
    ///     Deconstructs the potential instance into its stored values.
    /// </summary>
    /// <param name="potential">The stored potential of this instance.</param>
    /// <param name="isAttuned">Whether or not the instance represents an Attuned slugcat.</param>
    /// <param name="isHardmode">Whether or not the instance represents a Hardmode slugcat.</param>
    public void Deconstruct(out int potential, out bool isAttuned, out bool isHardmode)
    {
        potential = Potential;
        isAttuned = IsAttuned;
        isHardmode = IsHardmode;
    }

    /// <inheritdoc/>
    public bool Equals(SlugcatPotential other) => other.Potential == Potential && other.IsAttuned == IsAttuned && other.IsHardmode == IsHardmode;

    /// <inheritdoc/>
    public override bool Equals(object obj) => obj is SlugcatPotential other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Potential.GetHashCode() + IsAttuned.GetHashCode() + IsHardmode.GetHashCode();

    /// <summary>
    ///     Returns a string representation of the potential values held by this instance.
    /// </summary>
    /// <returns>A string representation of the potential values held by this instance.</returns>
    public override string ToString() => $"{Potential} ({(IsAttuned ? "AT" : "")}{(IsAttuned && IsHardmode ? "+" : "")}{(IsHardmode ? "HM" : "")})";

    public static bool operator ==(SlugcatPotential x, SlugcatPotential y)
    {
        return x.Equals(y);
    }

    public static bool operator !=(SlugcatPotential x, SlugcatPotential y)
    {
        return !x.Equals(y);
    }

    public static bool operator >(SlugcatPotential x, SlugcatPotential y)
    {
        return x.CompareTo(y) > 0;
    }

    public static bool operator <(SlugcatPotential x, SlugcatPotential y)
    {
        return x.CompareTo(y) < 0;
    }

    public static bool operator >=(SlugcatPotential x, SlugcatPotential y)
    {
        return x.CompareTo(y) >= 0;
    }

    public static bool operator <=(SlugcatPotential x, SlugcatPotential y)
    {
        return x.CompareTo(y) <= 0;
    }

    /// <summary>
    ///     Retrieves the static potential of the given slugcat. If none is found, <see cref="DefaultPotential"/> is returned instead.
    /// </summary>
    /// <param name="slugcat">The slugcat to search for.</param>
    /// <returns>The potential value registered for the given slugcat, or <see cref="DefaultPotential"/> if none is found.</returns>
    public static SlugcatPotential GetStaticPotential(SlugcatStats.Name? slugcat) =>
        slugcat is not null && StaticPotentials.TryGetValue(slugcat, out SlugcatPotential potential) ? potential : DefaultPotential;

    /// <summary>
    ///     Determines if the given slugcat has been registered with its own potential value.
    /// </summary>
    /// <param name="slugcat">The slugcat to search for.</param>
    /// <returns><c>true</c> if the slugcat has a registered value, <c>false</c> otherwise.</returns>
    public static bool HasStaticPotential(SlugcatStats.Name? slugcat) => slugcat is not null && StaticPotentials.ContainsKey(slugcat);

    /// <summary>
    ///     Sets the static (or "innate") potential of a given slugcat to the specified values.
    /// </summary>
    /// <param name="slugcat">The slugcat whose potential will be set. If a value is already registered for this slugcat, it is overwritten.</param>
    /// <param name="potential">The base potential time for this slugcat.</param>
    /// <param name="isAttuned">Whether or not this slugcat is "Attuned" by default.</param>
    /// <param name="isHardmode">Whether or not this slugcat is "Hardmode" by default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="slugcat"/> is null.</exception>
    public static void SetStaticPotential(SlugcatStats.Name slugcat, int potential, bool isAttuned = false, bool isHardmode = false) =>
        SetStaticPotential(slugcat, new SlugcatPotential(potential, isAttuned, isHardmode));

    /// <summary>
    ///     Sets the static (or "innate") potential of a given slugcat to the specified <see cref="SlugcatPotential"/> instance.
    /// </summary>
    /// <param name="slugcat">The slugcat whose potential will be set. If a value is already registered for this slugcat, it is overwritten.</param>
    /// <param name="potential">The potential instance to set for this slugcat.</param>
    /// <exception cref="ArgumentNullException"><paramref name="slugcat"/> is null.</exception>
    public static void SetStaticPotential(SlugcatStats.Name slugcat, SlugcatPotential potential)
    {
        if (slugcat is null)
            throw new ArgumentNullException(nameof(slugcat));

        StaticPotentials[slugcat] = potential;

        Main.Logger.LogDebug($"Set potential of {slugcat} to {potential}.");
    }

    /// <summary>
    ///     Retrieves the dynamic potential time for a given player, taking into account the current Karma/Ripple level and whether or not karmic reinforcement is active.
    /// </summary>
    /// <param name="player">The player to be evaluated.</param>
    /// <param name="staticPotential">The static potential of the player's slugcat class.</param>
    /// <returns>The dynamic potential for the given player.</returns>
    public static SlugcatPotential PotentialForPlayer(Player player, out SlugcatPotential staticPotential)
    {
        staticPotential = GetStaticPotential(player?.SlugCatClass);

        if (player?.room is null || player.room.game.session is not StoryGameSession storySession) return staticPotential;

        DeathPersistentSaveData saveData = storySession.saveState.deathPersistentSaveData;

        bool useRipple = saveData.minimumRippleLevel >= 1f;
        int extraTime = (useRipple ? (int)(saveData.rippleLevel * 2f) - 1 : saveData.karma) * 40;

        (int potential, bool isAttuned, bool isHardmode) = staticPotential;

        if (useRipple ? saveData.rippleLevel == saveData.maximumRippleLevel : saveData.karma == saveData.karmaCap)
        {
            isAttuned = true;
            isHardmode = false;

            potential += 120;
        }
        else if (useRipple ? saveData.rippleLevel == saveData.minimumRippleLevel : saveData.karma == 0)
        {
            isAttuned = false;

            potential -= 120;
        }

        potential += extraTime;

        if (saveData.reinforcedKarma)
            potential = (int)(potential * 1.5f);

        return new SlugcatPotential(potential, isAttuned, isHardmode);
    }
}