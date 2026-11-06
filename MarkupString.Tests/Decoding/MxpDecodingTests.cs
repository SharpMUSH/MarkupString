using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Display;
using MarkupString.Mxp;
using MarkupString.Mxp.Decoding;

public class MxpDecodingTests
{
	private static readonly DecoderRegistry Registry = VtDecoding.Terminal.WithMxp();

	private static (List<DisplayLine> Lines, List<DecodeEvent> Events, Decoder Decoder) Decode(string text, DecoderOptions? options = null)
	{
		var decoder = Registry.CreateDecoder(options);
		var events = new List<DecodeEvent>();
		decoder.EventRaised += events.Add;
		decoder.EnableProtocol(MxpDecoding.Protocol);
		var lines = new List<DisplayLine>(decoder.Feed(text));
		if (decoder.HasPendingLine) lines.Add(decoder.EndLine());
		return (lines, events, decoder);
	}

	private static IReadOnlyList<DisplaySpan> Spans(DisplayLine line) => SpanRenderer.Render(line.Content, AnsiDisplay.Default, new DisplayOptions());

	private static IEnumerable<IMarkup> Markups(DisplayLine line) => line.Content.Runs.SelectMany(run => run.Markups);

	[Test]
	public async Task OpenTags_WorkOnAnOpenLine()
	{
		var (lines, _, _) = Decode("<B>bold</B> <COLOR red>red</COLOR>\n");
		var spans = Spans(lines[0]);
		await Assert.That(spans.Select(span => span.Text)).IsEquivalentTo(["bold", " ", "red"]);
		await Assert.That(spans[0].Style.Has(TextAttributes.Bold)).IsTrue();
		await Assert.That(spans[2].Style.Foreground).IsEqualTo(new RgbColor(255, 0, 0));
	}

	[Test]
	public async Task SecureTags_AreDroppedOnAnOpenLine()
	{
		var (lines, _, decoder) = Decode("<SEND look>look</SEND>\n");
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("look");
		await Assert.That(Spans(lines[0]).Single().Style.Interaction).IsNull();
		await Assert.That(decoder.Diagnostics.UnhandledSequences).IsEqualTo(1);
	}

	[Test]
	public async Task Send_OnASecureLine_IsACommand()
	{
		var (lines, _, _) = Decode("\e[1z<SEND href=\"look sword\" hint=\"Look at it\">sword</SEND> here\n");
		var spans = Spans(lines[0]);
		await Assert.That(spans[0].Text).IsEqualTo("sword");
		await Assert.That(spans[0].Style.Interaction).IsEqualTo(new Interaction(InteractionKind.Command, "look sword") { Hint = "Look at it" });
		await Assert.That(spans[1].Style.Interaction).IsNull();
	}

	[Test]
	public async Task Send_WithoutHref_SendsItsText()
	{
		var (lines, _, _) = Decode("\e[1z<SEND>north</SEND>\n");
		await Assert.That(Spans(lines[0]).Single().Style.Interaction!.Target).IsEqualTo("north");
	}

	[Test]
	public async Task Send_TextSubstitution()
	{
		var (lines, _, _) = Decode("\e[1z<SEND \"get &text;\">sword</SEND>\n");
		await Assert.That(Spans(lines[0]).Single().Style.Interaction!.Target).IsEqualTo("get sword");
	}

	[Test]
	public async Task Send_Menu()
	{
		var (lines, _, _) = Decode("\e[1z<SEND \"buy sword|look sword\" hint=\"Sword|Buy|Look\">sword</SEND>\n");
		var interaction = Spans(lines[0]).Single().Style.Interaction!;
		await Assert.That(interaction.Target).IsEqualTo("buy sword");
		await Assert.That(interaction.Hint).IsEqualTo("Sword");
		await Assert.That(interaction.Choices!).IsEquivalentTo([new CommandChoice("Buy", "buy sword"), new CommandChoice("Look", "look sword")]);
	}

	[Test]
	public async Task Send_Prompt()
	{
		var (lines, _, _) = Decode("\e[1z<SEND \"say \" PROMPT>talk</SEND>\n");
		await Assert.That(Spans(lines[0]).Single().Style.Interaction!.Kind).IsEqualTo(InteractionKind.Prompt);
	}

	[Test]
	public async Task OpenElements_CloseAtTheEndOfTheLine()
	{
		var (lines, _, _) = Decode("<B>one\ntwo\n");
		await Assert.That(Spans(lines[1]).Single().Style.Has(TextAttributes.Bold)).IsFalse();
	}

	[Test]
	public async Task LockedLine_IsText()
	{
		var (lines, _, _) = Decode("\e[2z<B>&lt;\n<B>x</B>\n");
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("<B>&lt;");
		await Assert.That(lines[1].Content.ToPlainText()).IsEqualTo("x");
	}

	[Test]
	public async Task Entities()
	{
		var (lines, _, _) = Decode("a &lt;b&gt; &amp; &#65;&#x42; &nope; & c\n");
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("a <b> & AB &nope; & c");
	}

	[Test]
	public async Task TagsSplitAcrossFeeds()
	{
		var decoder = Registry.CreateDecoder();
		decoder.EnableProtocol(MxpDecoding.Protocol);
		decoder.Feed("<CO");
		decoder.Feed("LOR re");
		var lines = decoder.Feed("d>x</COLOR>\n");
		await Assert.That(SpanRenderer.Render(lines[0].Content, AnsiDisplay.Default, new DisplayOptions()).Single().Style.Foreground).IsEqualTo(new RgbColor(255, 0, 0));
	}

	[Test]
	public async Task Comments_AreDropped()
	{
		var (lines, _, _) = Decode("a<!-- not <B> shown -->b\n");
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("ab");
	}

	[Test]
	public async Task SgrInsideMxp_Wins()
	{
		var (lines, _, _) = Decode("<COLOR red>\e[32mgreen\e[0m</COLOR>\n");
		await Assert.That(Spans(lines[0]).Single().Style.Foreground).IsEqualTo(new RgbColor(0, 170, 0));
	}

	[Test]
	public async Task CustomElements()
	{
		var (lines, _, _) = Decode("\e[1z<!ELEMENT Exit '<SEND href=\"&dir;\"><COLOR cyan>' ATT='dir' FLAG=RoomExit>\n\e[1z<Exit north>N</Exit> S\n");
		var spans = Spans(lines[1]);
		await Assert.That(spans[0].Text).IsEqualTo("N");
		await Assert.That(spans[0].Style.Interaction!.Target).IsEqualTo("north");
		await Assert.That(spans[0].Style.Foreground).IsEqualTo(new RgbColor(0, 255, 255));
		await Assert.That(Markups(lines[1]).OfType<MxpFlagMarkup>().First().Flag).IsEqualTo("RoomExit");
		await Assert.That(spans[1].Style).IsEqualTo(DisplayStyle.Plain);
	}

	[Test]
	public async Task LineTags_MarkTheLine()
	{
		var (lines, _, _) = Decode("\e[10zThe Square\nnext\n");
		await Assert.That(Markups(lines[0]).OfType<MxpFlagMarkup>().Single().Flag).IsEqualTo(MxpFlagMarkup.RoomName);
		await Assert.That(Markups(lines[1]).Any()).IsFalse();
	}

	[Test]
	public async Task Version_And_Support_AreAnswered()
	{
		var options = new DecoderOptions { Identity = new ClientIdentity { Name = "Test", Version = "3" } };
		var (_, events, _) = Decode("\e[1z<VERSION>\e[1z<SUPPORT send image.url frob>", options);
		await Assert.That(events.OfType<ReplyEvent>().Select(reply => reply.Text)).IsEquivalentTo([
			"\e[1z<VERSION MXP=1.0 CLIENT=\"Test\" VERSION=\"3\">\n",
			"\e[1z<SUPPORTS +send +image.url -frob>\n",
		]);
	}

	[Test]
	public async Task Var_SetsAVariable_AndShowsItsText()
	{
		var (lines, events, _) = Decode("\e[1zHP: <VAR hp>42</VAR>\n");
		await Assert.That(events.OfType<VariableEvent>().Single()).IsEqualTo(new VariableEvent("hp", "42"));
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("HP: 42");
		await Assert.That(Markups(lines[0]).OfType<VariableMarkup>().Single().Name).IsEqualTo("hp");
	}

	[Test]
	public async Task Entity_Definition_IsUsedAndPublished()
	{
		var (lines, events, _) = Decode("\e[1z<!ENTITY hero \"Ada\">\nHello &hero;\n");
		await Assert.That(lines[^1].Content.ToPlainText()).IsEqualTo("Hello Ada");
		await Assert.That(events.OfType<VariableEvent>().Single()).IsEqualTo(new VariableEvent("hero", "Ada"));
	}

	[Test]
	public async Task Br_BreaksWithoutEndingTheSecureLine()
	{
		var (lines, _, _) = Decode("\e[1zone<BR><SEND x>two</SEND>\n");
		await Assert.That(lines.Select(line => line.Content.ToPlainText())).IsEquivalentTo(["one", "two"]);
		await Assert.That(Spans(lines[1]).Single().Style.Interaction).IsNotNull();
	}

	[Test]
	public async Task Reset_ClosesEverything()
	{
		var (lines, _, _) = Decode("\e[6z<B>bold\e[3zplain\n");
		var spans = Spans(lines[0]);
		await Assert.That(spans[^1].Text).IsEqualTo("plain");
		await Assert.That(spans[^1].Style.Has(TextAttributes.Bold)).IsFalse();
	}

	[Test]
	public async Task LineModes_AreIgnoredUntilMxpIsOn()
	{
		var decoder = Registry.CreateDecoder();
		var lines = decoder.Feed("\e[1z<B>x</B>\n");
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("<B>x</B>");
		await Assert.That(decoder.Diagnostics.UnhandledSequences).IsEqualTo(0);
	}

	[Test]
	public async Task WhatThisPackageWrites_ReadsBack()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithMxp().WithMxpSecureLines();
		var link = MarkupText.Wrap(new AnsiMarkup(new AnsiStyle { LinkUrl = "look", LinkKind = LinkKind.Command, LinkText = "Look" }), "look");
		var text = MarkupText.Concat([
			MarkupText.Sound("https://example.test/sounds/door.wav", volume: 80),
			MarkupText.Pane(MarkupText.Concat([MarkupText.Plain("North: "), link]), "map", "The Map")]);
		var wire = text.Render(MarkupFormat.Mxp, registry);

		var (lines, _, _) = Decode(wire + "\n");
		var line = lines.Single();
		await Assert.That(line.Content.ToPlainText()).IsEqualTo("North: look");
		await Assert.That(Markups(line).OfType<SoundMarkup>().Single()).IsEqualTo(new SoundMarkup("https://example.test/sounds/door.wav", Volume: 80));
		await Assert.That(Markups(line).OfType<PaneMarkup>().First()).IsEqualTo(new PaneMarkup("map", "The Map"));
		var spans = Spans(line);
		await Assert.That(spans[^1].Style.Interaction).IsEqualTo(new Interaction(InteractionKind.Command, "look") { Hint = "Look" });
	}

	[Test]
	public async Task Image_InCells_IsDrawnOverTheLines()
	{
		var (lines, _, _) = Decode("\e[1z<IMAGE cat.png URL=\"https://example.test/\" W=2c H=2c>  a\n  b\n");
		var first = lines[0].Content.Runs.SelectMany(run => run.Markups).OfType<ImageMarkup>().Single();
		await Assert.That(first.Source).IsEqualTo("https://example.test/cat.png");
		await Assert.That(first.Row!.Value.Row).IsEqualTo(0);
		await Assert.That(lines[1].Content.Runs.SelectMany(run => run.Markups).OfType<ImageMarkup>().Single().Row!.Value.Row).IsEqualTo(1);
		await Assert.That(lines[1].Content.ToPlainText()).IsEqualTo("  b");
	}
}
