using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MoreSlugcats;
using Watcher;

namespace ControlLib.Possession;

public readonly struct SlugcatPotential(int potential, bool isAttuned = false, bool isHardmode = false) : IEquatable<SlugcatPotential>, IComparable<SlugcatPotential>
{
    private static readonly Dictionary<SlugcatStats.Name, SlugcatPotential> StaticPotentials = [];

    public static SlugcatPotential DefaultPotential => StaticPotentials[SlugcatStats.Name.White];
    public static SlugcatPotential AttunedPotential => StaticPotentials[SlugcatStats.Name.Yellow];
    public static SlugcatPotential HardmodePotential => StaticPotentials[SlugcatStats.Name.Red];

    static SlugcatPotential()
    {
        StaticPotentials[SlugcatStats.Name.White] = new(360);
        StaticPotentials[SlugcatStats.Name.Yellow] = new(480, isAttuned: true);
        StaticPotentials[SlugcatStats.Name.Red] = new(240, isHardmode: true);

        StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Artificer] = new(180, isHardmode: true);
        StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Gourmand] = new(320, isHardmode: true);
        StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Rivulet] = new(80);
        StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Saint] = new(520, isAttuned: true, isHardmode: true);
        StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Sofanthiel] = new(1, isHardmode: true);
        StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Spear] = new(800, isHardmode: true);
        StaticPotentials[MoreSlugcatsEnums.SlugcatStatsName.Slugpup] = new(120, isAttuned: true, isHardmode: true);

        StaticPotentials[WatcherEnums.SlugcatStatsName.Watcher] = new(400);
    }

    public readonly int Potential = potential;
    public readonly bool IsAttuned = isAttuned;
    public readonly bool IsHardmode = isHardmode;

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

    public void Deconstruct(out int potential, out bool isAttuned, out bool isHardmode)
    {
        potential = Potential;
        isAttuned = IsAttuned;
        isHardmode = IsHardmode;
    }

    public bool Equals(SlugcatPotential other) => other.Potential == Potential && other.IsAttuned == IsAttuned && other.IsHardmode == IsHardmode;

    public override bool Equals(object obj) => obj is SlugcatPotential other && Equals(other);

    public override int GetHashCode() => Potential.GetHashCode() + IsAttuned.GetHashCode() + IsHardmode.GetHashCode();

    public override string ToString() => $"{Potential} ({(IsAttuned ? "AT" : "")}{(IsAttuned && IsHardmode ? "+" : "")}{(IsHardmode ? "HM" : "")})";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(SlugcatPotential x, SlugcatPotential y)
    {
        return x.Equals(y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(SlugcatPotential x, SlugcatPotential y)
    {
        return !x.Equals(y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(SlugcatPotential x, SlugcatPotential y)
    {
        return x.CompareTo(y) > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(SlugcatPotential x, SlugcatPotential y)
    {
        return x.CompareTo(y) < 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(SlugcatPotential x, SlugcatPotential y)
    {
        return x.CompareTo(y) >= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(SlugcatPotential x, SlugcatPotential y)
    {
        return x.CompareTo(y) <= 0;
    }

    public static SlugcatPotential GetStaticPotential(SlugcatStats.Name slugcat) =>
        StaticPotentials.TryGetValue(slugcat, out SlugcatPotential potential) ? potential : DefaultPotential;

    public static bool HasStaticPotential(SlugcatStats.Name slugcat) => StaticPotentials.ContainsKey(slugcat);

    public static void SetStaticPotential(SlugcatStats.Name slugcat, int potential, bool isAttuned = false, bool isHardmode = false) =>
        SetStaticPotential(slugcat, new SlugcatPotential(potential, isAttuned, isHardmode));

    public static void SetStaticPotential(SlugcatStats.Name slugcat, SlugcatPotential potential)
    {
        StaticPotentials[slugcat] = potential;

        Main.Logger.LogDebug($"Set potential of {slugcat} to {potential}.");
    }

    public static SlugcatPotential PotentialForPlayer(Player player, out SlugcatPotential staticPotential)
    {
        staticPotential = GetStaticPotential(player.SlugCatClass);

        if (player.room is null || player.room.game.session is not StoryGameSession storySession) return staticPotential;

        DeathPersistentSaveData saveData = storySession.saveState.deathPersistentSaveData;

        bool useRipple = saveData.minimumRippleLevel > 0f;
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