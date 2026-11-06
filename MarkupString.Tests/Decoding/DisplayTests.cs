using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Display;

public class DisplayTests
{
	private static readonly AnsiMarkup Red = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false));
	private static readonly AnsiMarkup Link = new(new AnsiStyle { LinkUrl = "look", LinkKind = LinkKind.Command });
	private static readonly DisplayOptions Browser = new();

	[Test]
	public async Task Resolve_Plain_ForNoMarkup()
	{
		await Assert.That(AnsiDisplay.Default.Resolve(null, Browser)).IsEqualTo(DisplayStyle.Plain);
	}

	[Test]
	public async Task Resolve_PlainTextOutput_DropsEverything()
	{
		var options = new DisplayOptions { Capabilities = OutputCapabilities.PlainText };
		var style = AnsiDisplay.Default.Resolve(MarkupSet.Of([Red, Link]), options);
		await Assert.That(style).IsEqualTo(DisplayStyle.Plain);
	}

	[Test]
	public async Task Resolve_ReducesColours_ToWhatTheOutputShows()
	{
		var orange = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(250, 130, 10), background: new AnsiColor.Rgb(10, 10, 120));
		var sixteen = new DisplayOptions { Capabilities = OutputCapabilities.Browser with { Colors = ColorLevel.Standard } };
		var xterm = new DisplayOptions { Capabilities = OutputCapabilities.Browser with { Colors = ColorLevel.Xterm256 } };

		var reduced = AnsiDisplay.Default.Resolve(MarkupSet.Of(orange), sixteen);
		await Assert.That(reduced.Foreground).IsEqualTo(new RgbColor(255, 85, 85));
		await Assert.That(reduced.Background).IsEqualTo(new RgbColor(0, 0, 170));

		var cube = AnsiDisplay.Default.Resolve(MarkupSet.Of(orange), xterm);
		await Assert.That(cube.Foreground).IsEqualTo(new RgbColor(255, 135, 0));
		await Assert.That(cube.Background).IsEqualTo(new RgbColor(0, 0, 135));

		var full = AnsiDisplay.Default.Resolve(MarkupSet.Of(orange), Browser);
		await Assert.That(full.Foreground).IsEqualTo(new RgbColor(250, 130, 10));
	}

	[Test]
	public async Task Resolve_LinkGroup_NamesTheInteraction()
	{
		var style = AnsiDisplay.Default.Resolve(MarkupSet.Of([new LinkGroupMarkup("inner"), Link, new LinkGroupMarkup("outer")]), Browser);
		await Assert.That(style.Interaction).IsEqualTo(new Interaction(InteractionKind.Command, "look") { Group = "inner" });
		await Assert.That(AnsiDisplay.Default.Resolve(MarkupSet.Of(new LinkGroupMarkup("alone")), Browser).Interaction).IsNull();
	}

	[Test]
	public async Task Resolve_PromptBecomesCommand_WhenPromptsAreNotShown()
	{
		var menu = new CommandMenuMarkup([new CommandChoice("look", "look")], Prompt: false);
		var options = new DisplayOptions { Capabilities = OutputCapabilities.Browser with { Interactions = InteractionKinds.Command } };
		var style = AnsiDisplay.Default.Resolve(MarkupSet.Of(menu), options);
		await Assert.That(style.Interaction!.Kind).IsEqualTo(InteractionKind.Command);
		await Assert.That(style.Interaction.Choices).IsNull();
	}

	[Test]
	public async Task Resolve_Menu_KeepsChoices()
	{
		var menu = new CommandMenuMarkup([new CommandChoice("Buy", "buy sword"), new CommandChoice("Look", "look sword")], "Sword");
		var style = AnsiDisplay.Default.Resolve(MarkupSet.Of(menu), Browser);
		await Assert.That(style.Interaction!.Target).IsEqualTo("buy sword");
		await Assert.That(style.Interaction.Hint).IsEqualTo("Sword");
		await Assert.That(style.Interaction.Choices!.Count).IsEqualTo(2);
	}

	[Test]
	public async Task Resolve_Highlight_WinsOverAnsiBackground()
	{
		var background = AnsiMarkup.Create(background: new AnsiColor.Standard(4, false));
		var style = AnsiDisplay.Default.Resolve(MarkupSet.Of([new HighlightMarkup(new RgbColor(255, 255, 0)), background]), Browser);
		await Assert.That(style.Background).IsEqualTo(new RgbColor(255, 255, 0));
	}

	[Test]
	public async Task Resolve_InnerColourWins()
	{
		var green = AnsiMarkup.Create(foreground: new AnsiColor.Standard(2, false));
		var style = AnsiDisplay.Default.Resolve(MarkupSet.Of([green, Red]), Browser);
		await Assert.That(style.Foreground).IsEqualTo(new RgbColor(0, 170, 0));
	}

	[Test]
	public async Task BoldIsBright()
	{
		var registry = DisplayRegistry.Core.WithAnsi(boldIsBright: true);
		var style = registry.Resolve(MarkupSet.Of(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false), bold: true)), Browser);
		await Assert.That(style.Foreground).IsEqualTo(new RgbColor(255, 85, 85));
	}

	[Test]
	public async Task Resolve_PromptMenu_IsAPrompt()
	{
		var menu = new CommandMenuMarkup([new CommandChoice("Say", "say ")], Prompt: true);
		var style = AnsiDisplay.Default.Resolve(MarkupSet.Of(menu), Browser);
		await Assert.That(style.Interaction!.Kind).IsEqualTo(InteractionKind.Prompt);
		await Assert.That(SpanRenderer.CssClasses(style)).IsEqualTo("ms-prompt");
	}

	[Test]
	public async Task Inverse_WithoutColours_IsAClass()
	{
		var options = new DisplayOptions { Capabilities = OutputCapabilities.Browser with { Colors = ColorLevel.Attributes } };
		var style = AnsiDisplay.Default.Resolve(MarkupSet.Of(AnsiMarkup.Create(inverted: true)), options);
		await Assert.That(SpanRenderer.CssClasses(style)).IsEqualTo("ms-invert");
	}

	[Test]
	public async Task Spans_MergeEqualStyles()
	{
		var text = MarkupText.Concat([MarkupText.Wrap(Red, "a"), MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Xterm(1)), "b"), MarkupText.Plain("c")]);
		var spans = SpanRenderer.Render(text, AnsiDisplay.Default, Browser);
		await Assert.That(spans.Select(span => span.Text)).IsEquivalentTo(["ab", "c"]);
	}

	[Test]
	public async Task Spans_AsciiOutput_FoldsText()
	{
		var options = new DisplayOptions { Capabilities = OutputCapabilities.Browser with { Unicode = UnicodeLevel.Ascii } };
		var spans = SpanRenderer.Render(MarkupText.Plain("café"), AnsiDisplay.Default, options);
		await Assert.That(spans.Single().Text).IsEqualTo("cafe");
	}

	[Test]
	public async Task Spans_Sound_DroppedWithoutSound()
	{
		var text = MarkupText.Concat([MarkupText.Plain("a"), MarkupText.Sound("boom.wav")]);
		var options = new DisplayOptions { Capabilities = OutputCapabilities.Browser with { Sound = false } };
		await Assert.That(SpanRenderer.Render(text, AnsiDisplay.Default, options).Count).IsEqualTo(1);
		await Assert.That(SpanRenderer.Render(text, AnsiDisplay.Default, Browser)[^1].Point).IsTypeOf<SoundMarkup>();
	}

	[Test]
	public async Task Css_And_Classes()
	{
		var style = new DisplayStyle
		{
			Foreground = new RgbColor(255, 0, 0),
			Attributes = TextAttributes.Bold | TextAttributes.Underline | TextAttributes.Strikethrough | TextAttributes.Blink,
			Interaction = new Interaction(InteractionKind.Command, "look"),
		};
		await Assert.That(SpanRenderer.Css(style)).IsEqualTo("color:#ff0000;font-weight:bold;text-decoration:underline line-through");
		await Assert.That(SpanRenderer.CssClasses(style)).IsEqualTo("ms-blink ms-command");
	}

	[Test]
	public async Task Cells_WideCharacters_TakeTwoCells()
	{
		var layout = CellRenderer.Layout(MarkupText.Plain("a漢b"), 0, AnsiDisplay.Default, Browser);
		var cells = layout.Rows.Single().Cells;
		await Assert.That(cells.Select(cell => cell.Text)).IsEquivalentTo(["a", "漢", "", "b"]);
		await Assert.That(cells[2].IsContinuation).IsTrue();
	}

	[Test]
	public async Task Cells_WrapAtTheWidth()
	{
		var layout = CellRenderer.Layout(MarkupText.Plain("abcdefg"), 3, AnsiDisplay.Default, Browser);
		await Assert.That(layout.Rows.Select(row => string.Concat(row.Cells.Select(cell => cell.Text)))).IsEquivalentTo(["abc", "def", "g"]);
		await Assert.That(layout.Rows.Select(row => row.Wrapped)).IsEquivalentTo([true, true, false]);
	}

	[Test]
	public async Task Cells_WideCharacterDoesNotSplit()
	{
		var layout = CellRenderer.Layout(MarkupText.Plain("ab漢"), 3, AnsiDisplay.Default, Browser);
		await Assert.That(layout.Rows.Select(row => string.Concat(row.Cells.Select(cell => cell.Text)))).IsEquivalentTo(["ab", "漢"]);
	}

	[Test]
	public async Task Cells_WordWrap()
	{
		var layout = CellRenderer.Layout(MarkupText.Plain("the quick fox"), 10, AnsiDisplay.Default, Browser, wordWrap: true);
		await Assert.That(layout.Rows.Select(row => string.Concat(row.Cells.Select(cell => cell.Text)))).IsEquivalentTo(["the quick ", "fox"]);
	}

	[Test]
	public async Task Cells_LineFeed_StartsARow()
	{
		var layout = CellRenderer.Layout(MarkupText.Plain("a\nb"), 0, AnsiDisplay.Default, Browser);
		await Assert.That(layout.Rows.Count).IsEqualTo(2);
		await Assert.That(layout.Rows[0].Wrapped).IsFalse();
	}

	[Test]
	public async Task Cells_Tab_AdvancesToTheStop()
	{
		var layout = CellRenderer.Layout(MarkupText.Plain("ab\tc"), 0, AnsiDisplay.Default, new DisplayOptions { TabWidth = 4 });
		await Assert.That(layout.Rows[0].Cells.Count).IsEqualTo(5);
		await Assert.That(layout.Rows[0].Cells[4].Text).IsEqualTo("c");
	}

	[Test]
	public async Task Cells_HitTest_FindsTheLink()
	{
		var text = MarkupText.Concat([MarkupText.Plain("go "), MarkupText.Wrap(Link, "漢")]);
		var layout = CellRenderer.Layout(text, 0, AnsiDisplay.Default, Browser);
		await Assert.That(layout.InteractionAt(0, 0)).IsNull();
		await Assert.That(layout.InteractionAt(0, 3)!.Target).IsEqualTo("look");
		await Assert.That(layout.InteractionAt(0, 4)!.Target).IsEqualTo("look");
		await Assert.That(layout.InteractionAt(0, 5)).IsNull();
	}

	[Test]
	public async Task Cells_PictureColumns_CountFromTheFirstShown()
	{
		var picture = new ImageMarkup("cat.png") { Row = new PictureRow(0, 2, 3) { FirstColumn = 1 } };
		var layout = CellRenderer.Layout(MarkupText.Wrap(picture, "   "), 0, AnsiDisplay.Default, Browser);
		await Assert.That(layout.Rows[0].Cells.Select(cell => cell.PictureColumn)).IsEquivalentTo([1, 2, 3]);
	}

	[Test]
	public async Task Cells_PicturesDropped_WhenNotShown()
	{
		var picture = new ImageMarkup("cat.png") { Row = new PictureRow(0, 1, 2) };
		var options = new DisplayOptions { Capabilities = OutputCapabilities.PlainText };
		var layout = CellRenderer.Layout(MarkupText.Wrap(picture, "  "), 0, AnsiDisplay.Default, options);
		await Assert.That(layout.Rows[0].Cells[0].Style.Picture).IsNull();
	}

	[Test]
	public async Task MenuMarkup_RoundTripsThroughJson()
	{
		var menu = new CommandMenuMarkup([new CommandChoice("Buy", "buy"), new CommandChoice("Look", "look")], "hint", Prompt: true);
		var text = MarkupText.Wrap(new HighlightMarkup(new RgbColor(1, 2, 3)), MarkupText.Wrap(menu, "sword"));
		var json = MarkupTextSerializer.Serialize(text);
		var back = MarkupTextSerializer.Deserialize(json);
		await Assert.That(back.Runs.Single().Markups).IsEqualTo(text.Runs.Single().Markups);
	}
}
