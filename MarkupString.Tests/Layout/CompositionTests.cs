using System.Collections.Immutable;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

/// <summary>Themes, modifiers, gradients over text, and blocks of a game's own.</summary>
public class CompositionTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static MarkupText P(string text) => MarkupText.Plain(text);

	private static string[] Lines(Block block, int width, LayoutContext? context = null) =>
		[.. BlockLayout.Lines(block, width, context).Select(line => line.ToPlainText().TrimEnd())];

	private static IColorMarkup Stop(byte r, byte g, byte b) => AnsiMarkup.Create(foreground: new AnsiColor.Rgb(r, g, b));

	private static readonly ColorGradient RedToBlue = new([Stop(255, 0, 0), Stop(0, 0, 255)]);

	[Test]
	public async Task AnUnsetBorder_ComesFromTheTheme()
	{
		var box = P("hi").ToBlock().Bordered();

		await Assert.That(Lines(box, 6)).IsEquivalentTo(new[] { "┌────┐", "│ hi │", "└────┘" });
		await Assert.That(Lines(box.Themed(new LayoutTheme { Border = BorderStyle.Ascii }), 6)).IsEquivalentTo(new[] { "+----+", "| hi |", "+----+" });
	}

	/// <summary>A theme reaches every block inside it that sets nothing of its own, and stops there.</summary>
	[Test]
	public async Task Themed_ChangesTheLookOfWhatIsInside()
	{
		var list = new Bullets([P("one")]);
		var sheet = new Stack([list.Themed(new LayoutTheme { Bullet = P("→") }), list]);

		await Assert.That(Lines(sheet, 10)).IsEquivalentTo(new[] { "→ one", "• one" });
	}

	[Test]
	public async Task ADivider_TakesItsFramesBorder()
	{
		var box = new Stack([P("a"), new Rule(P("T")), P("b")]).Bordered(border: BorderStyle.Double);

		await Assert.That(Lines(box, 11)[2]).IsEqualTo("╠══╡ T ╞══╣");
	}

	/// <summary>The example in docs/layout.md.</summary>
	[Test]
	public async Task TheDocumentedFinger_DrawsAsShown()
	{
		var finger = new Stack(
		[
			new Flex(
			[
				P("Sex: Male\nSpecies: Human").ToBlock().Sized(BlockSize.Cells(35)),
				P("Job: Dark Warrior\nOnline: 1h").ToBlock().Sized(BlockSize.Cells(36)),
			]) { Separator = P(" | ") },
			new Rule(P("Quote")),
			P("Hooooo?"),
		]).Bordered(P("Mannaz Byron"), BorderStyle.Mush with { TitleOpen = P("<< "), TitleClose = P(" >>") });

		await Assert.That(BlockLayout.Build(finger, 78).ToPlainText()).IsEqualTo(
			"+=============================<< Mannaz Byron >>=============================+\n" +
			"| Sex: Male                           | Job: Dark Warrior                    |\n" +
			"| Species: Human                      | Online: 1h                           |\n" +
			"+=================================<< Quote >>================================+\n" +
			"| Hooooo?                                                                    |\n" +
			"+============================================================================+");
	}

	[Test]
	public async Task Aligned_PlacesTextThatSetsNoneOfItsOwn()
	{
		var stack = new Stack([P("right"), new TextBlock(P("left")) { Alignment = Alignment.Left }]).Aligned(Alignment.Right);

		await Assert.That(Lines(stack, 8)).IsEquivalentTo(new[] { "   right", "left" });
		await Assert.That(BlockLayout.Build(stack, 8).Render(MarkupFormat.Html, Registry)).Contains("<div class=\"ms-aligned\" style=\"text-align:right\">");
	}

	private static RgbColor?[] Colours(MarkupText text) =>
		[.. text.EnumerateGraphemes().Select(character =>
			character.Runs.IsDefaultOrEmpty ? null : character.Runs[0].Markups.OfType<IColorMarkup>().FirstOrDefault()?.Foreground)];

	[Test]
	public async Task Shade_Characters_RunsThroughEveryLineSkippingSpaces()
	{
		var colours = Colours(RedToBlue.Shade(P("ab\nc d")));

		await Assert.That(colours[0]).IsEqualTo(new RgbColor(255, 0, 0));
		await Assert.That(colours[2]).IsNull();
		await Assert.That(colours[4]).IsNull();
		await Assert.That(colours[5]).IsEqualTo(new RgbColor(0, 0, 255));
	}

	[Test]
	public async Task Shade_Words_GivesEachWordOneColour()
	{
		var colours = Colours(RedToBlue.Shade(P("ab cd"), GradientFlow.Words));

		await Assert.That(colours[0]).IsEqualTo(colours[1]);
		await Assert.That(colours[0]).IsEqualTo(new RgbColor(255, 0, 0));
		await Assert.That(colours[3]).IsEqualTo(new RgbColor(0, 0, 255));
	}

	[Test]
	public async Task Shade_Across_LinesTheColumnsUp()
	{
		var colours = Colours(RedToBlue.Shade(P("abc\nabc"), GradientFlow.Across));

		await Assert.That(colours[0]).IsEqualTo(colours[4]);
		await Assert.That(colours[2]).IsEqualTo(new RgbColor(0, 0, 255));
	}

	[Test]
	public async Task Shade_Down_GivesEachLineOneColour()
	{
		var colours = Colours(RedToBlue.Shade(P("ab\nab"), GradientFlow.Down));

		await Assert.That(colours[0]).IsEqualTo(colours[1]);
		await Assert.That(colours[0]).IsEqualTo(new RgbColor(255, 0, 0));
		await Assert.That(colours[3]).IsEqualTo(new RgbColor(0, 0, 255));
	}

	[Test]
	public async Task Shade_Diagonal_RunsCornerToCorner()
	{
		var colours = Colours(RedToBlue.Shade(P("ab\nab"), GradientFlow.Diagonal));

		await Assert.That(colours[0]).IsEqualTo(new RgbColor(255, 0, 0));
		await Assert.That(colours[4]).IsEqualTo(new RgbColor(0, 0, 255));
		await Assert.That(colours[1]).IsNotEqualTo(colours[3]);
	}

	[Test]
	public async Task Mirror_RunsThereAndBack()
	{
		var mirrored = RedToBlue with { Mirror = true };

		await Assert.That(mirrored.At(0)).IsEqualTo(new RgbColor(255, 0, 0));
		await Assert.That(mirrored.At(0.5)).IsEqualTo(new RgbColor(0, 0, 255));
		await Assert.That(mirrored.At(1)).IsEqualTo(new RgbColor(255, 0, 0));
		await Assert.That(mirrored.ToCss()).IsEqualTo("linear-gradient(to right in oklch, #ff0000 0%, #0000ff 50%, #ff0000 100%)");
	}

	[Test]
	public async Task Repeat_RunsTheColoursAgain()
	{
		var twice = RedToBlue with { Repeat = 2 };

		await Assert.That(twice.At(0.25)).IsEqualTo(RedToBlue.At(0.5));
		await Assert.That(twice.At(1)).IsEqualTo(new RgbColor(0, 0, 255));
		await Assert.That(twice.ToCss()).IsEqualTo("linear-gradient(to right in oklch, #ff0000 0%, #0000ff 50%, #ff0000 50%, #0000ff 100%)");
	}

	[Test]
	public async Task Shaded_ColoursTheBordersAndTextOfABlock()
	{
		var box = P("hi").ToBlock().Bordered(border: BorderStyle.Ascii).Shaded(RedToBlue);
		var ansi = BlockLayout.Build(box, 6).Render(MarkupFormat.Ansi, Registry);

		await Assert.That(BlockLayout.Build(box, 6).ToPlainText()).IsEqualTo("+----+\n| hi |\n+----+");
		await Assert.That(ansi).Contains("\u001b[38;2;255;0;0m+");
		await Assert.That(ansi).Contains("\u001b[38;2;0;0;255m+");
	}

	[Test]
	public async Task Shaded_InHtml_ClipsTheGradientToTheText()
	{
		var html = BlockLayout.Build(P("hi").ToBlock().Shaded(RedToBlue, GradientFlow.Down), 6).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<div class=\"ms-shaded\" style=\"--ms-shade:linear-gradient(to bottom, #ff0000, ");
		await Assert.That(html).Contains(";background-image:var(--ms-shade);background-image:linear-gradient(to bottom in oklch, #ff0000, #0000ff)\"><div class=\"ms-text\">hi</div></div>");
	}

	[Test]
	public async Task Colored_LaysItsColourUnderTheBlocksOwn()
	{
		var red = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 0, 0));
		var blue = MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Rgb(0, 0, 255)), "b");
		var text = BlockLayout.Build(new TextBlock(MarkupText.Concat(P("a"), blue)).Colored(red), 2);

		await Assert.That(text.Render(MarkupFormat.Ansi, Registry)).Contains("\u001b[38;2;0;0;255mb");
		await Assert.That(text.Render(MarkupFormat.Ansi, Registry)).Contains("\u001b[38;2;255;0;0ma");
		await Assert.That(text.Render(MarkupFormat.Html, Registry)).Contains("<div class=\"ms-colored\" style=\"color:#ff0000\">");
	}

	[Test]
	public async Task ModifiersAndThemes_SurviveTheSerializer()
	{
		var block = new Stack(
		[
			P("hi").ToBlock().Bordered(P("T")).Themed(new LayoutTheme { Border = BorderStyle.Heavy, Bullet = P("→") }),
			P("x").ToBlock().Shaded(RedToBlue with { Mirror = true, Repeat = 3 }, GradientFlow.Diagonal),
			P("y").ToBlock().Colored(Stop(0, 128, 0)).Aligned(Alignment.Center),
			new Flex([P("a").ToBlock().Sized(BlockSize.Percent(40), 3, 2), P("b")]),
		]);
		var text = BlockLayout.Build(block, 20);

		var json = MarkupTextSerializer.Serialize(text, Registry);
		var read = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(MarkupTextSerializer.Serialize(read, Registry)).IsEqualTo(json);
		await Assert.That(BlockLayout.Build(BlockLayout.AsBlock(read), 20).Render(MarkupFormat.Ansi, Registry))
			.IsEqualTo(text.Render(MarkupFormat.Ansi, Registry));
		await Assert.That(read.Render(MarkupFormat.Html, Registry)).IsEqualTo(text.Render(MarkupFormat.Html, Registry));
	}

	/// <summary>A block of a game's own: a dice roll.</summary>
	private sealed record Dice(ImmutableArray<int> Faces) : Block
	{
		public override void Draw(LayoutContext context, int width, IList<MarkupText> lines) =>
			lines.Add(MarkupText.Plain(string.Join(" ", Faces.Select(face => context.AsciiOnly ? $"[{face}]" : ((char)('⚀' + face - 1)).ToString()))));

		public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines) =>
			lines.Add(MarkupText.Plain("Rolled " + string.Join(", ", Faces)));
	}

	private static readonly BlockCodec DiceCodec = BlockCodec.Create<Dice>("dice",
		(dice, writer) => writer.String("f", string.Join(",", dice.Faces)),
		reader => new Dice([.. (reader.String("f") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)]));

	[Test]
	public async Task AGamesOwnBlock_DrawsForEveryReader()
	{
		var dice = new Dice([1, 6]);

		await Assert.That(Lines(dice, 10)).IsEquivalentTo(new[] { "⚀ ⚅" });
		await Assert.That(Lines(dice, 10, new LayoutContext { AsciiOnly = true })).IsEquivalentTo(new[] { "[1] [6]" });
		await Assert.That(Lines(dice.Bordered(border: BorderStyle.Ascii), 9, new LayoutContext { Linear = true })).IsEquivalentTo(new[] { "Rolled 1, 6" });
	}

	[Test]
	public async Task AGamesOwnBlock_WithNoHtml_IsShownAsItsText()
		=> await Assert.That(BlockLayout.Build(new Dice([3]).Bordered(), 10).Render(MarkupFormat.Html, Registry))
			.Contains("<fieldset class=\"ms-box ms-border-single\"><pre class=\"ms-pre\">⚂</pre></fieldset>");

	[Test]
	public async Task AGamesOwnBlock_DrawsItsOwnHtml()
	{
		var registry = Registry.WithBlockHtml<Dice>((dice, html) =>
		{
			html.Write("<span class=\"dice\">");
			html.Encode(string.Join(" ", dice.Faces));
			html.Write("</span>");
		});

		await Assert.That(BlockLayout.Build(new Dice([3, 4]), 10).Render(MarkupFormat.Html, registry)).Contains("<span class=\"dice\">3 4</span>");
	}

	[Test]
	public async Task AGamesOwnBlock_WithACodec_SurvivesTheSerializer()
	{
		var registry = Registry.With(DiceCodec);
		var text = BlockLayout.Build(new Dice([2, 5]), 10, fluid: true);

		var read = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(text, registry), registry);

		await Assert.That(BlockLayout.Relayout(read, 10, new LayoutContext { AsciiOnly = true }).ToPlainText()).IsEqualTo("[2] [5]");
	}

	/// <summary>Written without its codec, a block is kept as the text it draws.</summary>
	[Test]
	public async Task AGamesOwnBlock_WithNoCodec_IsWrittenAsItsText()
	{
		var json = MarkupTextSerializer.Serialize(BlockLayout.Build(new Dice([2]), 10, fluid: true), Registry);

		await Assert.That(json).Contains("\"n\":{\"t\":\"text\",\"c\":{\"t\":\"⚁\"");
	}

	/// <summary>Read where its codec is missing, a layout is never drawn from a tree it could not read.</summary>
	[Test]
	public async Task AnUnknownBlock_LeavesTheLayoutAsItsText()
	{
		var text = BlockLayout.Build(new Dice([4]).Bordered(border: BorderStyle.Ascii), 7, fluid: true);
		var json = MarkupTextSerializer.Serialize(text, Registry.With(DiceCodec));

		var read = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(read.Render(MarkupFormat.Html, Registry)).DoesNotContain("ms-layout");
		await Assert.That(BlockLayout.Relayout(read, 20, LayoutContext.Default).ToPlainText()).IsEqualTo(text.ToPlainText());
	}

	[Test]
	public async Task ABuiltInKind_CannotBeClaimed()
		=> await Assert.That(() => Registry.With(BlockCodec.Create<Dice>("gauge", (_, _) => { }, _ => new Dice([])))).Throws<ArgumentException>();

	[Test]
	public async Task Measure_IsTheWidestLineWhereverTheTextSits()
	{
		var measure = new TextBlock(P("one two\nthree")) { Alignment = Alignment.Right }.Measure(LayoutContext.Default, 40);

		await Assert.That(measure.Natural).IsEqualTo(7);
	}
}
