using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Permissions;
using BepInEx;
using ControlLib.Enums;
using ControlLib.Possession;
using ControlLib.Telekinetics;
using FakeAchievements;
using ModLib;
using ModLib.Logging;

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

namespace ControlLib;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public class Main : ModPlugin
{
    public const string PLUGIN_NAME = "Possessions";
    public const string PLUGIN_GUID = "ynhzrfxn.possessions";
    public const string PLUGIN_VERSION = "0.9.1";

    public static bool HasFakeAchievements;

#nullable disable warnings

    internal static new ModLogger Logger { get; private set; }

    private static List<string> unlockedAchievements;

#nullable restore warnings

    public Main()
        : base(new Options())
    {
        Logger = new FilteredLogWrapper(base.Logger);
    }

    protected override void LoadResources()
    {
        base.LoadResources();

        HasFakeAchievements = CompatibilityManager.IsModEnabled("ddemile.fake_achievements");

        if (!HasFakeAchievements) return;

        Logger.LogDebug("FakeAchievements found! Loading unlocked achievements...");

        try
        {
            unlockedAchievements = FakeAchievementsAccess.GetUnlockedAchievements();

            Logger.LogDebug($"Unlocked achievements are: [{PossessionManager.FormatPossessions(unlockedAchievements)}]");
        }
        catch (Exception ex)
        {
            Logger.LogError("Could not retrieve unlocked achievements from FakeAchievements!");
            Logger.LogError(ex);

            unlockedAchievements = [];
        }
    }

    public override void OnEnable()
    {
        if (IsModEnabled) return;

        base.OnEnable();

        Keybinds.InitKeybinds();

        AbstractObjectTypes.RegisterValues();
    }

    public override void OnDisable()
    {
        if (!IsModEnabled) return;

        base.OnDisable();

        AbstractObjectTypes.UnregisterValues();
    }

    protected override void ApplyHooks()
    {
        base.ApplyHooks();

        DeathProtectionHooks.ApplyHooks();

        PossessionHooks.ApplyHooks();

        TelekineticsHooks.ApplyHooks();
    }

    protected override void RemoveHooks()
    {
        base.RemoveHooks();

        DeathProtectionHooks.RemoveHooks();

        PossessionHooks.RemoveHooks();

        TelekineticsHooks.RemoveHooks();
    }

    internal static void CueAchievement(string achievementID, bool skipUnlockCheck = false)
    {
        achievementID = $"{PLUGIN_GUID}/{achievementID}";

        if (!skipUnlockCheck && !CanUnlockAchievement(achievementID)) return;

        try
        {
            FakeAchievementsAccess.ShowAchievement(achievementID);

            unlockedAchievements.Add(achievementID);

            Logger.LogInfo($"Unlocked achievement! [{achievementID}]");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to grant achievement [{achievementID}] to player: {ex}");
        }
    }

    internal static bool CanUnlockAchievement(string achievementID) =>
        HasFakeAchievements && Extras.GameSession is StoryGameSession && !unlockedAchievements.Contains(achievementID);

    private static class FakeAchievementsAccess
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static List<string> GetUnlockedAchievements() =>
            [.. AchievementsTracker.UnlockedAchievements.Where(static str => str.StartsWith(PLUGIN_GUID))];

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ShowAchievement(string achievementID) => AchievementsManager.GrantAchievement(achievementID);
    }
}