using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

/// <summary>Palettes, how they are made, and the colours they give a layout's parts.</summary>
public class ThemeTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static MarkupText P(string text) => MarkupText.Plain(text);

	private static RgbColor Hex(string hex) => ColorMath.TryParseHex(hex, out var rgb) ? rgb : throw new ArgumentException(hex);

	[Test]
	public async Task Contrast_IsWcags()
	{
		await Assert.That(ColorMath.Contrast(new RgbColor(0, 0, 0), new RgbColor(255, 255, 255))).IsEqualTo(21).Within(0.01);
		await Assert.That(ColorMath.Contrast(Hex("#777777"), Hex("#ffffff"))).IsEqualTo(4.48).Within(0.01);
	}

	[Test]
	public async Task WithContrast_MovesOnlyAsFarAsItMust()
	{
		var background = Hex("#1e1e2e");
		var dim = Hex("#303060");
		var lifted = ColorMath.WithContrast(dim, background, 4.5);

		await Assert.That(ColorMath.Contrast(lifted, background)).IsBetween(4.5, 4.7);
		await Assert.That(ColorMath.ToOklch(lifted).H).IsEqualTo(ColorMath.ToOklch(dim).H).Within(3);
		await Assert.That(ColorMath.WithContrast(Hex("#ffffff"), background, 4.5)).IsEqualTo(Hex("#ffffff"));
	}

	[Test]
	[Arguments("#89b4fa", 12)]
	[Arguments("#1e66f5", 4)]
	[Arguments("#a6e3a1", 10)]
	[Arguments("#f38ba8", 9)]
	[Arguments("#fab387", 11)]
	[Arguments("#94e2d5", 14)]
	[Arguments("#cba6f7", 13)]
	[Arguments("#45475a", 8)]
	[Arguments("#11111b", 0)]
	[Arguments("#cdd6f4", 15)]
	[Arguments("#a9b1d6", 7)]
	public async Task StandardSlot_GoesByKindNotDistance(string hex, int slot)
	{
		await Assert.That(ColorMath.StandardSlot(Hex(hex))).IsEqualTo(slot);
	}

	[Test]
	[MethodDataSource(nameof(Harmonies))]
	public async Task Generate_MeetsItsContrastInBothModes(ThemeHarmony harmony, ThemeMode mode)
	{
		var palette = ThemePalette.Generate(Hex("#7aa2f7"), harmony, mode);

		await Assert.That(palette.Check()).IsEmpty();
		await Assert.That(palette.Mode).IsEqualTo(mode);
		foreach (var role in Enum.GetValues<ThemeRole>()) await Assert.That(palette[role]).IsNotNull();
	}

	public static IEnumerable<Func<(ThemeHarmony, ThemeMode)>> Harmonies() =>
		from harmony in Enum.GetValues<ThemeHarmony>()
		from mode in Enum.GetValues<ThemeMode>()
		select (Func<(ThemeHarmony, ThemeMode)>)(() => (harmony, mode));

	[Test]
	public async Task Generate_FullContrastReachesSevenToOne()
	{
		var palette = ThemePalette.Generate(Hex("#5e81ac"), ThemeHarmony.Triadic, ThemeMode.Light, contrast: 1);

		foreach (var role in Enum.GetValues<ThemeRole>().Where(role => role != ThemeRole.Background))
			await Assert.That(ColorMath.Contrast(palette[role]!.Value.Resolved, palette.BackgroundColor)).IsGreaterThanOrEqualTo(7);
	}

	[Test]
	public async Task Generate_HarmonyPicksTheHues()
	{
		var palette = ThemePalette.Generate(Hex("#7aa2f7"), ThemeHarmony.Complementary);
		var primary = ColorMath.ToOklch(palette[ThemeRole.Primary]!.Value.Rgb!.Value).H;
		var secondary = ColorMath.ToOklch(palette[ThemeRole.Secondary]!.Value.Rgb!.Value).H;

		await Assert.That(Math.Abs(180 - Math.Abs(primary - secondary))).IsLessThan(8);
	}

	[Test]
	public async Task AGreySeed_GivesGreyAccents()
	{
		var palette = ThemePalette.Generate(Hex("#808080"), ThemeHarmony.Triadic);

		await Assert.That(ColorMath.ToOklch(palette[ThemeRole.Primary]!.Value.Rgb!.Value).C).IsLessThan(0.01);
		await Assert.That(palette.Check()).IsEmpty();
	}

	[Test]
	public async Task Base16_MapsByItsOwnGuide()
	{
		var nord = ThemePalette.Preset("NORD")!;

		await Assert.That(nord[ThemeRole.Primary]).IsEqualTo(new ThemeColor(Hex("#81a1c1"), 4));
		await Assert.That(nord[ThemeRole.Error]).IsEqualTo(new ThemeColor(Hex("#bf616a"), 1));
		await Assert.That(nord.Mode).IsEqualTo(ThemeMode.Dark);
		await Assert.That(ThemePalette.Preset("solarized-light")!.Mode).IsEqualTo(ThemeMode.Light);
	}

	[Test]
	public async Task Json_ReadsEveryForm()
	{
		await Assert.That(ThemePalette.TryParse("nord", out var bare, out _)).IsTrue();
		await Assert.That(bare!.Name).IsEqualTo("nord");
		await Assert.That(ThemePalette.TryParse("\"dracula\"", out var quoted, out _)).IsTrue();
		await Assert.That(quoted!.Name).IsEqualTo("dracula");

		await Assert.That(ThemePalette.TryParse("""{"seed":"#7aa2f7","harmony":"triadic","mode":"light","contrast":0.5}""", out var generated, out _)).IsTrue();
		await Assert.That(generated!.Mode).IsEqualTo(ThemeMode.Light);
		await Assert.That(generated).IsEqualTo(ThemePalette.Generate(Hex("#7aa2f7"), ThemeHarmony.Triadic, ThemeMode.Light, 0.5));

		await Assert.That(ThemePalette.TryParse("""{"preset":"nord","name":"mine","colors":{"primary":"#bf616a","muted":8,"info":null,"tertiary":{"rgb":"#ffffff","slot":11}}}""", out var edited, out _)).IsTrue();
		await Assert.That(edited!.Name).IsEqualTo("mine");
		await Assert.That(edited[ThemeRole.Primary]).IsEqualTo(new ThemeColor(Hex("#bf616a"), 1));
		await Assert.That(edited[ThemeRole.Muted]).IsEqualTo(ThemeColor.Standard(8));
		await Assert.That(edited[ThemeRole.Info]).IsNull();
		await Assert.That(edited[ThemeRole.Tertiary]).IsEqualTo(new ThemeColor(Hex("#ffffff"), 11));
	}

	[Test]
	[Arguments("nowhere", "not JSON or a theme name")]
	[Arguments("\"nowhere\"", "no theme named 'nowhere'")]
	[Arguments("""{"seed":"blue"}""", "seed is a colour like #7aa2f7")]
	[Arguments("""{"seed":"#123456","preset":"nord"}""", "use one of preset, base16 and seed")]
	[Arguments("""{"colors":{"accent":"#fff"}}""", "'accent' is not a role; the roles are background, foreground, primary, secondary, tertiary, muted, success, warning, error, info")]
	[Arguments("""{"colors":{"primary":16}}""", "'16' is not a colour: use #rrggbb, a standard colour 0 to 15, or {\"rgb\":..,\"slot\":..}")]
	[Arguments("""{"border":"red"}""", "a theme has no 'border'")]
	public async Task Json_SaysWhatIsWrong(string json, string error)
	{
		await Assert.That(ThemePalette.TryParse(json, out _, out var message)).IsFalse();
		await Assert.That(message).IsEqualTo(error);
	}

	[Test]
	public async Task Json_RoundTrips()
	{
		foreach (var palette in ThemePalette.Presets.Append(ThemePalette.Generate(Hex("#bb9af7"), ThemeHarmony.Split)))
		{
			await Assert.That(ThemePalette.TryParse(palette.ToJson(), out var read, out _)).IsTrue();
			await Assert.That(read).IsEqualTo(palette);
		}
	}

	[Test]
	public async Task ThePalette_ColoursTheParts()
	{
		var theme = ThemePalette.Preset("nord")!.ToLayoutTheme();
		var box = new Stack([new Bullets([P("one")]), new Fields([new Field(P("Name"), (Block)P("Ann"))])]).Bordered(P("Sheet"));
		var ansi = BlockLayout.Build(box.Themed(theme), 20).Render(MarkupFormat.Ansi, Registry);

		await Assert.That(ansi).Contains("\u001b[38;2;129;161;193m┌");
		await Assert.That(ansi).Contains("\u001b[1;38;2;180;142;173mSheet");
		await Assert.That(ansi).Contains("\u001b[38;2;136;192;208m•");
		await Assert.That(ansi).Contains("\u001b[38;2;180;142;173mName");
		await Assert.That(BlockLayout.Build(box.Themed(theme), 20).ToPlainText()).IsEqualTo(BlockLayout.Build(box, 20).ToPlainText());
	}

	[Test]
	public async Task ASixteenColourClient_GetsTheSlot()
	{
		var theme = ThemePalette.Preset("catppuccin-mocha")!.ToLayoutTheme();
		var sixteen = MarkupRegistry.Empty.WithAnsiOutput(AnsiColorDepth.Standard);
		var output = BlockLayout.Build(P("hi").ToBlock().Bordered().Themed(theme), 6).Render(MarkupFormat.Ansi, sixteen);

		// Blue (34), where catppuccin's pastel blue is nearest a grey by distance.
		await Assert.That(output).Contains("\u001b[34m┌");
	}

	[Test]
	public async Task TheTerminalPalette_SendsStandardColoursOnly()
	{
		var output = BlockLayout.Build(P("hi").ToBlock().Bordered(P("T")).Themed(ThemePalette.Terminal.ToLayoutTheme()), 10).Render(MarkupFormat.Ansi, Registry);

		await Assert.That(output).Contains("\u001b[36m┌");
		await Assert.That(output).DoesNotContain("38;2");
	}

	[Test]
	public async Task ColourAPieceSetsItself_Wins()
	{
		var red = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 0, 0));
		var border = BorderStyle.Single with { TopLeft = MarkupText.Wrap(red, "┌") };
		var theme = new LayoutTheme { BorderColor = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(0, 0, 255)) };
		var output = BlockLayout.Build(P("hi").ToBlock().Bordered(border: border).Themed(theme), 6).Render(MarkupFormat.Ansi, Registry);

		await Assert.That(output).Contains("\u001b[38;2;255;0;0m┌");
		await Assert.That(output).Contains("\u001b[38;2;0;0;255m─");
	}

	[Test]
	public async Task AFallbackTheme_GivesWayToTheReaders()
	{
		var blue = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(0, 0, 255));
		var green = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(0, 255, 0));
		var house = new LayoutTheme { BorderColor = blue, Border = BorderStyle.Ascii };
		var reader = LayoutContext.Default with { Theme = new LayoutTheme { BorderColor = green } };
		var box = P("hi").ToBlock().Bordered();

		var under = BlockLayout.Build(box.ThemedUnder(house), 6, context: reader).Render(MarkupFormat.Ansi, Registry);
		var over = BlockLayout.Build(box.Themed(house), 6, context: reader).Render(MarkupFormat.Ansi, Registry);

		await Assert.That(under).Contains("\u001b[38;2;0;255;0m+");
		await Assert.That(over).Contains("\u001b[38;2;0;0;255m+");
	}

	[Test]
	public async Task ThemeColours_SurviveTheSerializerAndRelayout()
	{
		var theme = ThemePalette.Preset("gruvbox-dark")!.ToLayoutTheme();
		var text = BlockLayout.Build(new Tree([new TreeItem(P("root"), [new TreeItem(P("leaf"))])]).Bordered(P("T")).ThemedUnder(theme), 20, fluid: true);
		var read = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(text, Registry), Registry);
		var relaid = BlockLayout.Relayout(read, 30, LayoutContext.Default);

		await Assert.That(relaid.Render(MarkupFormat.Ansi, Registry)).IsEqualTo(BlockLayout.Build(new Tree([new TreeItem(P("root"), [new TreeItem(P("leaf"))])]).Bordered(P("T")).ThemedUnder(theme), 30, fluid: true).Render(MarkupFormat.Ansi, Registry));
		await Assert.That(relaid.Render(MarkupFormat.Ansi, Registry)).Contains("\u001b[38;2;102;92;84m└─");
	}

	[Test]
	public async Task Html_WritesThePartsAsCustomProperties()
	{
		var theme = ThemePalette.Preset("nord")!.ToLayoutTheme();
		var box = P("hi").ToBlock().Bordered();

		await Assert.That(BlockLayout.Build(box.Themed(theme), 10).Render(MarkupFormat.Html, Registry)).Contains("<div class=\"ms-themed\" style=\"--ms-border:#81a1c1;--ms-title:#b48ead;");
		await Assert.That(BlockLayout.Build(box.ThemedUnder(theme), 10).Render(MarkupFormat.Html, Registry)).Contains("--ms-border-default:#81a1c1;");
		await Assert.That(LayoutCss.Fixed).Contains("border: 1px solid var(--ms-border, var(--ms-border-default, currentColor))");
	}
}
