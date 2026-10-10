using System.Collections.Immutable;
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

		foreach (var role in Enum.GetValues<ThemeRole>().Where(role => role is not (ThemeRole.Background or ThemeRole.Surface or ThemeRole.Highlight)))
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
	public async Task Check_LeavesStandardColoursToTheClient()
	{
		await Assert.That(ThemePalette.Terminal.Check()).IsEmpty();
		await Assert.That(ThemePalette.Preset("nord")!.Check().Select(shortfall => shortfall.Role)).Contains(ThemeRole.Muted);
	}

	[Test]
	public async Task Base16_MapsByItsOwnGuide()
	{
		var nord = ThemePalette.Preset("NORD")!;

		await Assert.That(nord[ThemeRole.Primary]).IsEqualTo(new ThemeColor(Hex("#81a1c1"), 4));
		await Assert.That(nord[ThemeRole.Error]).IsEqualTo(new ThemeColor(Hex("#bf616a"), 1));
		await Assert.That(nord.Mode).IsEqualTo(ThemeMode.Dark);
		await Assert.That(ThemePalette.Preset("solarized-light")!.Mode).IsEqualTo(ThemeMode.Light);
		await Assert.That(nord[ThemeRole.Tertiary]).IsEqualTo(new ThemeColor(Hex("#d08770"), 3));
		await Assert.That(nord[ThemeRole.Info]).IsEqualTo(new ThemeColor(Hex("#88c0d0"), 6));
		await Assert.That(nord[ThemeRole.Highlight]).IsEqualTo(new ThemeColor(Hex("#434c5e"), 8));
		await Assert.That(nord[ThemeRole.Purple]).IsEqualTo(new ThemeColor(Hex("#b48ead"), 5));
	}

	[Test]
	public async Task EveryPreset_TellsTertiaryAndInfoApart()
	{
		foreach (var palette in ThemePalette.Presets)
			await Assert.That(palette[ThemeRole.Tertiary]).IsNotEqualTo(palette[ThemeRole.Info]);
	}

	[Test]
	public async Task EveryPreset_SetsEveryRole()
	{
		var missing = ThemePalette.Presets.SelectMany(palette => Enum.GetValues<ThemeRole>().Where(role => palette[role] is null).Select(role => $"{palette.Name}.{role}"));
		// The terminal preset stripes nothing, leaving the client's own background alone.
		await Assert.That(missing).IsEquivalentTo(["terminal.Surface"]);
	}

	[Test]
	[Arguments(ThemeRole.Red, 27)]
	[Arguments(ThemeRole.Orange, 55)]
	[Arguments(ThemeRole.Yellow, 95)]
	[Arguments(ThemeRole.Green, 145)]
	[Arguments(ThemeRole.Cyan, 200)]
	[Arguments(ThemeRole.Blue, 255)]
	[Arguments(ThemeRole.Purple, 305)]
	[Arguments(ThemeRole.Pink, 350)]
	public async Task Generate_KeepsEachHueItsName(ThemeRole role, double hue)
	{
		foreach (var mode in Enum.GetValues<ThemeMode>())
		{
			var palette = ThemePalette.Generate(Hex("#7aa2f7"), ThemeHarmony.Analogous, mode);
			var made = ColorMath.ToOklch(palette[role]!.Value.Rgb!.Value).H;
			await Assert.That(Math.Abs(((made - hue) % 360 + 540) % 360 - 180)).IsLessThanOrEqualTo(25);
		}
	}

	[Test]
	public async Task Generate_TheForegroundReadsOnTheHighlight()
	{
		foreach (var mode in Enum.GetValues<ThemeMode>())
		{
			var palette = ThemePalette.Generate(Hex("#d6577c"), ThemeHarmony.Split, mode);
			var highlight = palette[ThemeRole.Highlight]!.Value.Resolved;
			await Assert.That(ColorMath.Contrast(palette[ThemeRole.Foreground]!.Value.Resolved, highlight)).IsGreaterThanOrEqualTo(4.5);
			await Assert.That(highlight).IsNotEqualTo(palette.BackgroundColor);
		}
	}

	[Test]
	public async Task Json_WorksOutTheRolesItLeavesOut()
	{
		await Assert.That(ThemePalette.TryParse("""{"colors":{"background":"#1a1b26","foreground":"#c0caf5","primary":"#7aa2f7","muted":"#565f89","pink":null}}""", out var palette, out _)).IsTrue();

		await Assert.That(palette![ThemeRole.Pink]).IsNull();
		await Assert.That(ThemePalette.TryParse(palette.ToJson(), out var reread, out _)).IsTrue();
		await Assert.That(reread).IsEqualTo(palette).Because("pink stays unset when it is written and read back");
		await Assert.That(palette[ThemeRole.Link]).IsNotNull();
		await Assert.That(palette[ThemeRole.Subtle]).IsNotNull();
		await Assert.That(palette[ThemeRole.Highlight]).IsNotNull();
		foreach (var hue in new[] { ThemeRole.Red, ThemeRole.Orange, ThemeRole.Yellow, ThemeRole.Green, ThemeRole.Cyan, ThemeRole.Blue, ThemeRole.Purple })
			await Assert.That(ColorMath.Contrast(palette[hue]!.Value.Resolved, palette.BackgroundColor)).IsGreaterThanOrEqualTo(4.5);
	}

	[Test]
	public async Task Json_TheWorkedOutSubtleIsStillReadable()
	{
		await Assert.That(ThemePalette.TryParse("""{"colors":{"background":"#ffffff","muted":"#777777"}}""", out var palette, out _)).IsTrue();

		await Assert.That(ColorMath.Contrast(palette![ThemeRole.Subtle]!.Value.Resolved, palette.BackgroundColor)).IsGreaterThanOrEqualTo(3);
		await Assert.That(ColorMath.Contrast(palette[ThemeRole.Subtle]!.Value.Resolved, palette.BackgroundColor))
			.IsLessThan(ColorMath.Contrast(palette[ThemeRole.Muted]!.Value.Resolved, palette.BackgroundColor));
	}

	[Test]
	public async Task Json_OfStandardColours_WorksOutStandardColours()
	{
		await Assert.That(ThemePalette.TryParse("""{"colors":{"background":0,"primary":6}}""", out var palette, out _)).IsTrue();

		await Assert.That(palette![ThemeRole.Red]).IsEqualTo(ThemeColor.Standard(9));
		await Assert.That(palette[ThemeRole.Link]).IsEqualTo(ThemeColor.Standard(6));
		await Assert.That(palette[ThemeRole.Highlight]).IsEqualTo(ThemeColor.Standard(8));
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
	[Arguments("""{"colors":{"accent":"#fff"}}""", "'accent' is not a role; the roles are background, surface, foreground, primary, secondary, tertiary, muted, success, warning, error, info, subtle, link, highlight, red, orange, yellow, green, cyan, blue, purple, pink")]
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
		await Assert.That(ansi).Contains("\u001b[38;2;208;135;112m•");
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

		await Assert.That(BlockLayout.Build(box.Themed(theme), 30).Render(MarkupFormat.Html, Registry)).Contains("<div class=\"ms-themed\" style=\"--ms-border:#81a1c1;--ms-title:#b48ead;");
		await Assert.That(BlockLayout.Build(box.ThemedUnder(theme), 10).Render(MarkupFormat.Html, Registry)).Contains("--ms-border-default:#81a1c1;");
		await Assert.That(LayoutCss.Fixed).Contains("border: 1px solid var(--ms-border, var(--ms-border-default, currentColor))");
	}

	/// <summary>Nord's surface, <c>#3b4252</c>, as a truecolour background.</summary>
	private const string NordStripe = "\u001b[48;2;59;66;82m";

	private static Table Rows(params string[] names) =>
		new([new TableColumn(P("Name")), new TableColumn(P("Note"))], [.. names.Select(name => (ImmutableArray<Block>)[P(name).ToBlock(), P("x").ToBlock()])]);

	[Test]
	public async Task AStripedTable_LaysEverySecondRowOnTheSurface()
	{
		var theme = ThemePalette.Preset("nord")!.ToLayoutTheme();
		var lines = BlockLayout.Build((Rows("one", "two", "three", "four") with { Striped = true }).Themed(theme), 20)
			.Render(MarkupFormat.Ansi, Registry).Split('\n');

		// The headings and the rule, then a line a row.
		await Assert.That(lines[2]).DoesNotContain("48;2;");
		await Assert.That(lines[3]).Contains(NordStripe + "two ");
		await Assert.That(lines[4]).DoesNotContain("48;2;");
		await Assert.That(lines[5]).Contains(NordStripe + "four");
	}

	[Test]
	public async Task AStripe_RunsTheWholeWidthOfEveryLineOfItsRow()
	{
		var theme = new LayoutTheme { StripeColor = new AnsiMarkup(new AnsiStyle { Background = new AnsiColor.Rgb(1, 2, 3) }) };
		var table = new Table([new TableColumn(P("A")), new TableColumn(P("B"))],
			[[P("a").ToBlock(), P("b").ToBlock()], [P("c").ToBlock(), P("long words wrap here").ToBlock()]])
		{ Striped = true };
		var text = BlockLayout.Build(table.Themed(theme), 14);
		var lines = text.Render(MarkupFormat.Ansi, Registry).Split('\n');
		var plain = text.ToPlainText().Split('\n');

		await Assert.That(lines.Length).IsGreaterThan(4);
		foreach (var line in lines.Skip(3)) await Assert.That(line).StartsWith("\u001b[48;2;1;2;3m");
		foreach (var line in plain.Skip(3)) await Assert.That(line.Length).IsEqualTo(14);
	}

	[Test]
	public async Task StripedFields_StripeEachSecondField()
	{
		var theme = ThemePalette.Preset("nord")!.ToLayoutTheme();
		var fields = new Fields([new Field(P("Name"), P("Ann").ToBlock()), new Field(P("Race"), P("Elf").ToBlock()), new Field(P("Rank"), P("3").ToBlock())]) { Striped = true };
		var lines = BlockLayout.Build(fields.Themed(theme), 20).Render(MarkupFormat.Ansi, Registry).Split('\n');

		await Assert.That(lines[0]).DoesNotContain("48;2;");
		await Assert.That(lines[1]).Contains("48;2;59;66;82mRace");
		await Assert.That(lines[1]).Contains(NordStripe + " Elf");
		await Assert.That(lines[2]).DoesNotContain("48;2;");
	}

	[Test]
	public async Task AStripe_LeavesTheTextAndACellsOwnBackgroundAlone()
	{
		var theme = ThemePalette.Preset("nord")!.ToLayoutTheme();
		var own = MarkupText.Wrap(new AnsiMarkup(new AnsiStyle { Background = new AnsiColor.Rgb(200, 0, 0) }), "two");
		var table = new Table([new TableColumn(P("Name"))], [[P("one").ToBlock()], [own.ToBlock()]]);
		var plain = BlockLayout.Build(table, 20).ToPlainText();
		var striped = BlockLayout.Build((table with { Striped = true }).Themed(theme), 20);

		await Assert.That(striped.ToPlainText()).IsEqualTo(plain);
		await Assert.That(striped.Render(MarkupFormat.Ansi, Registry)).Contains("48;2;200;0;0mtwo");
	}

	[Test]
	public async Task Striped_WithNoStripeColour_ColoursNothing()
	{
		var table = Rows("one", "two");

		await Assert.That(BlockLayout.Build(table with { Striped = true }, 20).Render(MarkupFormat.Ansi, Registry))
			.IsEqualTo(BlockLayout.Build(table, 20).Render(MarkupFormat.Ansi, Registry));
	}

	[Test]
	public async Task Stripes_SurviveTheSerializerAndRelayout()
	{
		var theme = ThemePalette.Preset("nord")!.ToLayoutTheme();
		var built = BlockLayout.Build((Rows("one", "two") with { Striped = true }).ThemedUnder(theme), 20, fluid: true);
		var read = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(built, Registry), Registry);
		var relaid = BlockLayout.Relayout(read, 30, LayoutContext.Default);

		await Assert.That(relaid.Render(MarkupFormat.Ansi, Registry)).Contains(NordStripe + "two");
	}

	[Test]
	public async Task Html_StripesWithAClassAndTheSurfaceAsAProperty()
	{
		var theme = ThemePalette.Preset("nord")!.ToLayoutTheme();
		var html = BlockLayout.Build((Rows("one", "two") with { Striped = true }).Themed(theme), 20).Render(MarkupFormat.Html, Registry);
		var fields = BlockLayout.Build(new Fields([new Field(P("A"), P("b").ToBlock())]) { Striped = true }, 20).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("--ms-stripe:#3b4252;");
		await Assert.That(html).Contains("<table class=\"ms-table ms-striped\">");
		await Assert.That(fields).Contains("<dl class=\"ms-fields ms-striped\">");
		await Assert.That(LayoutCss.Fixed).Contains("var(--ms-stripe, var(--ms-stripe-default, rgba(127, 127, 127, 0.12)))");
	}

	[Test]
	public async Task TheSurface_IsABackgroundNearTheBackground()
	{
		var generated = ThemePalette.Generate(Hex("#7aa2f7"));
		var light = ThemePalette.Generate(Hex("#7aa2f7"), mode: ThemeMode.Light);

		await Assert.That(ThemePalette.Preset("nord")![ThemeRole.Surface]!.Value.Rgb).IsEqualTo(Hex("#3b4252"));
		await Assert.That(ColorMath.Contrast(generated[ThemeRole.Surface]!.Value.Resolved, generated.BackgroundColor)).IsBetween(1.1, 1.6);
		await Assert.That(ColorMath.Contrast(light[ThemeRole.Surface]!.Value.Resolved, light.BackgroundColor)).IsBetween(1.1, 1.6);
		await Assert.That(ColorMath.Contrast(generated[ThemeRole.Foreground]!.Value.Resolved, generated[ThemeRole.Surface]!.Value.Resolved)).IsGreaterThanOrEqualTo(4.5);
		await Assert.That(ThemePalette.Terminal[ThemeRole.Surface]).IsNull();
		await Assert.That(generated.ToLayoutTheme().StripeColor).IsEqualTo(new AnsiMarkup(new AnsiStyle { Background = new AnsiColor.Rgb(generated[ThemeRole.Surface]!.Value.Rgb!.Value.R, generated[ThemeRole.Surface]!.Value.Rgb!.Value.G, generated[ThemeRole.Surface]!.Value.Rgb!.Value.B) }));
	}

	[Test]
	public async Task EachGenre_HasALookAndColoursThatStandOut()
	{
		await Assert.That(ThemePalette.Genres.Select(genre => genre.Name))
			.IsEquivalentTo(["fantasy", "historical", "horror", "modern", "mystery", "romance", "science-fiction", "spiritual"]);
		foreach (var genre in ThemePalette.Genres)
		{
			await Assert.That(genre.Look).IsNotNull();
			await Assert.That(genre.Check()).IsEmpty();
			await Assert.That(ThemePalette.Preset(genre.Name)).IsEqualTo(genre);
		}
	}

	[Test]
	public async Task AGenre_DrawsWithItsOwnShapes()
	{
		var theme = ThemePalette.Preset("fantasy")!.ToLayoutTheme();
		var sheet = new Stack([new Bullets([P("Sword").ToBlock()]), new Gauge(3, 6) { BarWidth = 6, Show = GaugeShow.None }]).Bordered(P("Kit"));
		var text = BlockLayout.Build(sheet.Themed(theme), 20).ToPlainText();
		var ascii = BlockLayout.Build(sheet.Themed(theme), 20, context: new LayoutContext { AsciiOnly = true }).ToPlainText();

		await Assert.That(text).StartsWith("❖════╡ ❧ Kit ☙ ╞═══❖");
		await Assert.That(text).Contains("❧ Sword");
		await Assert.That(text).Contains("╞███░░░╡");
		await Assert.That(ascii).StartsWith("+======< Kit >=====+");
		await Assert.That(ascii).Contains("* Sword");
		await Assert.That(ascii).Contains("+###---+");
	}

	[Test]
	public async Task ALook_IsReadAndWrittenAsJson()
	{
		ThemePalette.TryParse("""{"preset":"fantasy","look":{"bullet":"+","border":"rounded"}}""", out var changed, out _);
		ThemePalette.TryParse("""{"preset":"fantasy","look":null}""", out var plain, out _);
		ThemePalette.TryParse(ThemePalette.Preset("horror")!.ToJson(), out var read, out _);

		await Assert.That(changed!.Look!.Bullet).IsEqualTo("+");
		await Assert.That(changed.Look.Border).IsEqualTo("rounded");
		await Assert.That(changed.Look.TitleOpen).IsEqualTo("╡ ❧ ");
		await Assert.That(plain!.Look).IsNull();
		await Assert.That(read).IsEqualTo(ThemePalette.Preset("horror"));
	}

	[Test]
	public async Task ALooksCornersAndEdges_DrawAndFallBackToAscii()
	{
		ThemePalette.TryParse("""{"look":{"border":"single","corners":["◇","◇","◇","◇"],"edge":"┄","side":"┆","separator":" · "}}""", out var palette, out _);
		var theme = palette!.ToLayoutTheme();
		var box = new Fields([new Field(P("A"), P("b").ToBlock())]).Bordered();
		var text = BlockLayout.Build(box.Themed(theme), 30).ToPlainText();
		var ascii = BlockLayout.Build(box.Themed(theme), 30, context: new LayoutContext { AsciiOnly = true }).ToPlainText();

		await Assert.That(text).IsEqualTo("◇┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄◇\n┆ A · b                      ┆\n◇┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄◇");
		await Assert.That(ascii).IsEqualTo("+----------------------------+\n| A: b                       |\n+----------------------------+");
	}

	[Test]
	[Arguments("""{"look":{"border":"wavy"}}""", "border is one of none, ascii, mush, single, double, heavy, rounded")]
	[Arguments("""{"look":{"gauge":["[","#"]}}""", "gauge is [open, filled, empty, close]")]
	[Arguments("""{"look":{"sparkle":"*"}}""", "a look has no 'sparkle'")]
	[Arguments("""{"look":{"bullet":"\u001b[31m*"}}""", "bullet is text")]
	[Arguments("""{"look":{"bullet":"\u0301"}}""", "bullet is text")]
	[Arguments("""{"look":{"rule":"\ud800"}}""", "not JSON or a theme name")]
	[Arguments("""{"look":{"title":["\n","x"]}}""", "title is [open, close]")]
	[Arguments("""{"look":{"corners":["+","+","+","++"]}}""", "corners is [top-left, top-right, bottom-left, bottom-right], each one column wide")]
	[Arguments("""{"look":{"corners":["界","+","+","+"]}}""", "corners is [top-left, top-right, bottom-left, bottom-right], each one column wide")]
	[Arguments("""{"look":{"side":"||"}}""", "side is one column wide")]
	[Arguments("""{"look":{"edge":"\t"}}""", "edge is text")]
	public async Task ABadLook_SaysWhy(string json, string error)
	{
		await Assert.That(ThemePalette.TryParse(json, out _, out var message)).IsFalse();
		await Assert.That(message).IsEqualTo(error);
	}

	[Test]
	public async Task AGeneratedTheme_IsMadeAgainForALightBackground()
	{
		var fantasy = ThemePalette.Preset("fantasy")!;
		var light = fantasy.InMode(ThemeMode.Light);
		ThemePalette.TryParse("""{"preset":"fantasy","mode":"light"}""", out var read, out _);

		await Assert.That(light.Mode).IsEqualTo(ThemeMode.Light);
		await Assert.That(light.Look).IsEqualTo(fantasy.Look);
		await Assert.That(light.Name).IsEqualTo("fantasy");
		await Assert.That(light.Check()).IsEmpty();
		await Assert.That(ColorMath.Luminance(light.BackgroundColor)).IsGreaterThan(0.8);
		await Assert.That(read).IsEqualTo(light);
		await Assert.That(ThemePalette.Preset("nord")!.InMode(ThemeMode.Light)[ThemeRole.Primary]).IsEqualTo(ThemePalette.Preset("nord")![ThemeRole.Primary]);
	}

	[Test]
	public async Task AGeneratedTheme_KeepsItsSeedThroughJson()
	{
		var palette = ThemePalette.Generate(Hex("#d08770"), ThemeHarmony.Split, ThemeMode.Dark, 0.5);
		ThemePalette.TryParse(palette.ToJson(), out var read, out _);

		await Assert.That(read).IsEqualTo(palette);
		await Assert.That(read!.InMode(ThemeMode.Light)).IsEqualTo(ThemePalette.Generate(Hex("#d08770"), ThemeHarmony.Split, ThemeMode.Light, 0.5));
	}
}
