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

    public static Configurable<bool> KINETIC_ABILITIES;
    public static Configurable<bool> MIND_BLAST;

    public static Configurable<bool> MEADOW_SLOWDOWN;
    public static Configurable<bool> INFINITE_POSSESSION;
    public static Configurable<bool> POSSESS_ANCESTORS;
    public static Configurable<bool> FORCE_MULTITARGET_POSSESSION;
    public static Configurable<bool> WORLDWIDE_MIND_CONTROL;

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    private static OpCheckBox? checkBox;

    public Options()
    {
        SELECTION_MODE = config.Bind(
            "selection_mode",
            "classic",
            new ConfigurableInfo(
                "Which mode to use for selecting creatures to possess. Classic is list-based; Ascension is akin to Saint's ascension ability.",
                new ConfigAcceptableList<string>("classic", "ascension")
            )
        );
        INVERT_CLASSIC = config.Bind(
            "invert_classic",
            false,
            new ConfigurableInfo(
                "(Classic mode only) Inverts the control inputs for selecting creatures in the Possession ability."
            )
        );
        MEADOW_SLOWDOWN = config.Bind(
            "meadow_slowdown",
            false,
            new ConfigurableInfo(
                "If in a Rain Meadow lobby, whether or not using the Possession ability will slow down time."
            )
        );
        KINETIC_ABILITIES = config.Bind(
            "kinetic_abilities",
            false,
            new ConfigurableInfo(
                "If enabled, Slugcat can move objects/corpses around instead of possessing them."
            )
        );
        MIND_BLAST = config.Bind(
            "mind_blast",
            false,
            new ConfigurableInfo(
                "If enabled, Slugcat can use all possession time for a powerful burst of energy, which can kill foes if overwhelming enough; Has a separate keybind, only visible if this setting is enabled."
            )
        );
        INFINITE_POSSESSION = config.Bind(
            "infinite_possession",
            false,
            new ConfigurableInfo(
                "Allows indefinite possession of creatures. Also prevents Inv from exploding."
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

        SELECTION_MODE.OnChange += OnSelectionModeChanged;
        MIND_BLAST.OnChange += OnMindBlastChanged;
    }

    private void OnMindBlastChanged() => Keybinds.ToggleMindBlast(MIND_BLAST.Value);
    private void OnSelectionModeChanged()
    {
        if (SELECTION_MODE.Value is "classic")
        {
            checkBox?.Reactivate();
        }
        else
        {
            checkBox?.Deactivate();
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        Main.Logger?.LogInfo($"{nameof(Options)}: Initialized REMIX menu interface.");

        Tabs = new OpTab[3];

        Tabs[0] = new OptionBuilder(this, "Client Options")
            .CreateModHeader()
            .AddComboBoxOption("Selection Mode", SELECTION_MODE, width: 120)
            .AddPadding(Vector2.up * 10)
            .AddCheckBoxOption("Invert Controls", INVERT_CLASSIC, out checkBox)
            .Build();

        Tabs[1] = new OptionBuilder(this, "Gameplay")
            .CreateModHeader()
            .AddPadding(Vector2.down * 10)
            .AddText("These options may affect how you experience the game; Use with care.", new Vector2(64f, 24f))
            .AddPadding(Vector2.up * 30)
            .AddCheckBoxOption("Meadow Slowdown", MEADOW_SLOWDOWN)
            .AddPadding(Vector2.up * 10)
            .AddCheckBoxOption("Kinetic Abilities", KINETIC_ABILITIES)
            .AddCheckBoxOption("Mind Blast", MIND_BLAST, default, MenuColorEffect.rgbDarkRed)
            .Build();

        Tabs[2] = new OptionBuilder(this, "Cheats", MenuColorEffect.rgbDarkRed)
            .CreateModHeader()
            .AddPadding(Vector2.down * 10)
            .AddText("These options are for testing purposes only; Use at your own discretion.", new Vector2(50f, 24f))
            .AddPadding(Vector2.up * 30)
            .AddCheckBoxOption("Infinite Possession", INFINITE_POSSESSION)
            .AddCheckBoxOption("Possess Anscestors", POSSESS_ANCESTORS)
            .AddCheckBoxOption("Force Multi-Target Possession", FORCE_MULTITARGET_POSSESSION)
            .AddPadding(Vector2.up * 20)
            .AddCheckBoxOption("Worldwide Mind Control", WORLDWIDE_MIND_CONTROL, default, MenuColorEffect.rgbDarkRed)
            .Build();

        OnSelectionModeChanged();
    }
}

public static class OptionBuilderExts
{
    public static OptionBuilder CreateModHeader(this OptionBuilder self)
    {
        return self.SetOrigin(new Vector2(100f, 450f))
            .AddText(Main.PLUGIN_NAME, new Vector2(50f, 32f), true, RainWorld.SaturatedGold)
            .AddText($"[v{Main.PLUGIN_VERSION}]", new Vector2(60f, 24f), false, Color.gray);
    }

    public static OptionBuilder AddCheckBoxOption(this OptionBuilder self, string text, Configurable<bool> configurable, out OpCheckBox checkBox, params Color[] colors)
    {
        Vector2 origin = self.GetOrigin();

        UIelement[] elements =
        [
            new OpLabel(origin + new Vector2(40f, 0f), new Vector2(100f, 24f), text)
            {
                description = configurable.info.description,
                alignment = FLabelAlignment.Left,
                verticalAlignment = OpLabel.LabelVAlignment.Center,
                color = OptionBuilder.GetColorOrDefault(colors, 0)
            },
            checkBox = new OpCheckBox(configurable, origin)
            {
                description = configurable.info.description,
                colorEdge = OptionBuilder.GetColorOrDefault(colors, 1),
                colorFill = OptionBuilder.GetColorOrDefault(colors, 2, MenuColorEffect.rgbBlack)
            }
        ];

        self.SetOrigin(new Vector2(origin.x, origin.y - 32f));

        self.AddElements(elements);

        return self;
    }
}