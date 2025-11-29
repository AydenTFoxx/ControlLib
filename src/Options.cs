using Menu;
using Menu.Remix.MixedUI;
using ModLib.Options;
using UnityEngine;

namespace ControlLib;

/// <summary>
/// Holds definitions and raw values of the mod's REMIX options.
/// </summary>
/// <seealso cref="ServerOptions"/>
public class Options : OptionInterface
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    [ClientOption] public static Configurable<string> SELECTION_MODE;
    [ClientOption] public static Configurable<bool> INVERT_CLASSIC;
    [ClientOption] public static Configurable<int> WEAPON_ROTATION_SPEED;

    public static Configurable<bool> KINETIC_ABILITIES;
    public static Configurable<bool> MIND_BLAST;
    public static Configurable<bool> MIND_BLAST_PROTECTION;
    public static Configurable<bool> DANGER_MIND_BLAST;

    public static Configurable<bool> MULTIPLAYER_SLOWDOWN;
    public static Configurable<bool> INFINITE_POSSESSION;
    public static Configurable<bool> POSSESS_ANCESTORS;
    public static Configurable<bool> FORCE_MULTITARGET_POSSESSION;
    public static Configurable<bool> WORLDWIDE_MIND_CONTROL;

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    public Options()
    {
        SELECTION_MODE = config.Bind(
            "selection_mode",
            "ascension",
            new ConfigurableInfo(
                "Which mode to use for selecting creatures to possess. Classic is list-based; Ascension is akin to Saint's ascension ability.",
                new ConfigAcceptableList<string>("ascension", "classic")
            )
        );
        INVERT_CLASSIC = config.Bind(
            "invert_classic",
            false,
            new ConfigurableInfo(
                "(Requires Selection Mode: Classic) Inverts the directional inputs for selecting creatures to possess."
            )
        );
        MULTIPLAYER_SLOWDOWN = config.Bind(
            "multiplayer_slowdown",
            false,
            new ConfigurableInfo(
                "When in multiplayer (online or local), determines if using the Possession ability will slow down time."
            )
        );
        KINETIC_ABILITIES = config.Bind(
            "kinetic_abilities",
            true,
            new ConfigurableInfo(
                "If enabled, Slugcat can \"possess\" carryable items, moving them with its mind. Items can also be thrown or dropped, and have the same behavior as if Slugcat had performed these actions."
            )
        );
        MIND_BLAST = config.Bind(
            "mind_blast",
            false,
            new ConfigurableInfo(
                "A powerful burst of energy requiring full possession time, which stuns or even kills foes around its target position; Has a dedicated keybind. (Default: B)"
            )
        );
        MIND_BLAST_PROTECTION = config.Bind(
            "mind_blast_protection",
            true,
            new ConfigurableInfo(
                "If enabled, using Mind Blast grants a brief moment of immortality, which prevents unfair deaths until the player regains control of Slugcat."
            )
        );
        DANGER_MIND_BLAST = config.Bind(
            "danger_mind_blast",
            true,
            new ConfigurableInfo(
                "If enabled, using Mind Blast when grabbed by a predator triggers its explosion instantly. Has the same window of time as throwing a weapon to get free (1.5s)."
            )
        );
        WEAPON_ROTATION_SPEED = config.Bind(
            "weapon_rotation_speed",
            8,
            new ConfigurableInfo(
                "(Requires Kinetic Abilities) The speed at which weapons will rotate when possessed by the player; If set to 0, weapons instead point towards their last directional input.",
                new ConfigAcceptableRange<int>(0, 20)
            )
        );
        INFINITE_POSSESSION = config.Bind(
            "infinite_possession",
            false,
            new ConfigurableInfo(
                "Allows indefinite possession of creatures. Also prevents ??? from exploding."
            )
        );
        POSSESS_ANCESTORS = config.Bind(
            "possess_ancestors",
            false,
            new ConfigurableInfo(
                "If enabled, multi-target possessions will also target anscestors, e.g. \"White Lizard\" will target all lizard types."
            )
        );
        FORCE_MULTITARGET_POSSESSION = config.Bind(
            "force_multitarget_possession",
            false,
            new ConfigurableInfo(
                "If enabled, possessions will by default target all creatures of that same type; Saint's Ascended Possession will only target one creature at a time instead."
            )
        );
        WORLDWIDE_MIND_CONTROL = config.Bind(
            "worldwide_mind_control",
            false,
            new ConfigurableInfo(
                "The Hive Mind must consume all things, living or otherwise."
            )
        );

        MIND_BLAST.OnChange += OnMindBlastChanged;
    }

    public override void Initialize()
    {
        base.Initialize();

        Main.Logger.LogInfo($"{nameof(Options)}: Initialized REMIX menu interface.");

        Tabs = new OpTab[3];

        Tabs[0] = new OptionBuilder(this, "Client Options")
            .CreateModHeader()
            .AddComboBoxOption("Selection Mode", SELECTION_MODE, width: 120)
            .AddCheckBoxOption("Invert Controls", INVERT_CLASSIC, out OpCheckBox checkBoxIC)
            .Build();

        SELECTION_MODE.BoundUIconfig.OnValueChanged += BuildToggleAction(checkBoxIC, "classic");

        Tabs[1] = new OptionBuilder(this, "Gameplay")
            .CreateModHeader()
            .AddPadding(Vector2.down * 10)
            .AddText("These options may affect how you experience the game; Use with care.", new Vector2(64f, 24f))
            .AddPadding(Vector2.up * 30)
            .AddCheckBoxOption("Multiplayer Slowdown", MULTIPLAYER_SLOWDOWN)
            .AddPadding(Vector2.up * 10)
            .AddCheckBoxOption("Kinetic Abilities", KINETIC_ABILITIES)
            .AddPadding(Vector2.up * 10)
            .AddSliderOption("Weapon Rotation Speed", WEAPON_ROTATION_SPEED, out OpSlider sliderWRS, multi: 4f)
            .AddPadding(Vector2.up * 20)
            .AddCheckBoxOption("Mind Blast", MIND_BLAST, default, RainWorld.GoldRGB)
            .AddCheckBoxOption("Mind Blast Protection", MIND_BLAST_PROTECTION, out OpCheckBox checkBoxMBP, default, RainWorld.SaturatedGold)
            .AddCheckBoxOption("Danger-Aware Mind Blast", DANGER_MIND_BLAST, out OpCheckBox checkBoxDMB, default, RainWorld.SaturatedGold)
            .Build();

        KINETIC_ABILITIES.BoundUIconfig.OnValueChanged += BuildToggleAction(sliderWRS, "true");

        MIND_BLAST.BoundUIconfig.OnValueChanged += BuildToggleAction(checkBoxMBP, "true");
        MIND_BLAST.BoundUIconfig.OnValueChanged += BuildToggleAction(checkBoxDMB, "true");

        Tabs[2] = new OptionBuilder(this, "Cheats", MenuColorEffect.rgbDarkRed)
            .CreateModHeader()
            .AddPadding(Vector2.down * 10)
            .AddText("These options are for testing purposes only; Use at your own discretion.", new Vector2(64f, 24f))
            .AddPadding(Vector2.up * 30)
            .AddCheckBoxOption("Infinite Possession", INFINITE_POSSESSION)
            .AddCheckBoxOption("Possess Anscestors", POSSESS_ANCESTORS)
            .AddCheckBoxOption("Force Multi-Target Possession", FORCE_MULTITARGET_POSSESSION)
            .AddPadding(Vector2.up * 20)
            .AddCheckBoxOption("Worldwide Mind Control", WORLDWIDE_MIND_CONTROL, default, MenuColorEffect.rgbDarkRed)
            .Build();
    }

    private static OnValueChangeHandler BuildToggleAction(UIconfig target, string enableValue)
    {
        ToggleAction(null, target.value, "");

        return ToggleAction;

        void ToggleAction(UIconfig? _, string value, string oldValue)
        {
            target.greyedOut = value != enableValue;
        }
    }

    private static void OnMindBlastChanged() => Keybinds.MIND_BLAST.ToggleIICKeybind(MIND_BLAST.Value);
}

public static class OptionBuilderExts
{
    public static OptionBuilder CreateModHeader(this OptionBuilder self) =>
        self.SetOrigin(new Vector2(100f, 500f))
            .AddText(Main.PLUGIN_NAME, new Vector2(64f, 0f), true, RainWorld.RippleGold)
            .AddText($"[v{Main.PLUGIN_VERSION}]", new Vector2(100f, 32f), false, Color.gray)
            .ResetOrigin();
}