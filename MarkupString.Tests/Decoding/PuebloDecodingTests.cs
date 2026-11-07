using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Display;
using MarkupString.Html;
using MarkupString.Pueblo;
using MarkupString.Pueblo.Decoding;

public class PuebloDecodingTests
{
	private static readonly DecoderRegistry Registry = VtDecoding.Terminal.WithPueblo();

	private static List<DisplayLine> Decode(string text)
	{
		var decoder = Registry.CreateDecoder();
		decoder.EnableProtocol(PuebloDecoding.Protocol);
		var lines = new List<DisplayLine>(decoder.Feed(text));
		if (decoder.HasPendingLine) lines.Add(decoder.EndLine());
		return lines;
	}

	private static IReadOnlyList<DisplaySpan> Spans(DisplayLine line) => SpanRenderer.Render(line.Content, AnsiDisplay.Default, new DisplayOptions());

	private static IEnumerable<IMarkup> Markups(DisplayLine line) => line.Content.Runs.SelectMany(run => run.Markups);

	[Test]
	public async Task MudText_TagsAreText()
	{
		var lines = Decode("<b>x</b>\n");
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("<b>x</b>");
	}

	[Test]
	public async Task Html_BreaksAtBr_NotAtLineFeeds()
	{
		var lines = Decode("</xch_mudtext>one<BR>\ntwo\nstill two<br>\n");
		await Assert.That(lines.Select(line => line.Content.ToPlainText())).IsEquivalentTo(["one", "twostill two"]);
	}

	[Test]
	public async Task Html_StylesAndLinks()
	{
		var lines = Decode("</xch_mudtext><b>bold</b> <a xch_cmd=\"look\" xch_hint=\"Look\">look</a> <a href=\"https://example.com\">site</a><br>");
		var spans = Spans(lines[0]);
		await Assert.That(spans[0].Style.Has(TextAttributes.Bold)).IsTrue();
		await Assert.That(spans[2].Style.Interaction).IsEqualTo(new Interaction(InteractionKind.Command, "look") { Hint = "Look" });
		await Assert.That(spans[4].Style.Interaction).IsEqualTo(new Interaction(InteractionKind.Url, "https://example.com"));
	}

	[Test]
	public async Task Html_Entities()
	{
		var lines = Decode("</xch_mudtext>a &lt;b&gt; &amp;&nbsp;c<br>");
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("a <b> & c");
	}

	[Test]
	public async Task MudTextRegion_KeepsItsLines()
	{
		var lines = Decode("</xch_mudtext><xch_mudtext>a\nb\n</xch_mudtext>c<br>");
		await Assert.That(lines.Select(line => line.Content.ToPlainText())).IsEquivalentTo(["a", "b", "c"]);
		await Assert.That(Markups(lines[0]).Contains(PreformattedMarkup.Instance)).IsTrue();
	}

	[Test]
	public async Task WhatThisPackageWrites_ReadsBack()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml().WithPueblo();
		var link = MarkupText.Wrap(new AnsiMarkup(new AnsiStyle { LinkUrl = "look", LinkKind = LinkKind.Command, LinkText = "Look" }), "look");
		var text = MarkupText.Concat([
			MarkupText.Sound("https://example.test/door.wav", volume: 80),
			MarkupText.Pane(MarkupText.Concat([MarkupText.Plain("North: "), link]), "map", "The Map"),
			MarkupText.Plain("\nnext & last")]);
		var wire = text.Render(MarkupFormat.Pueblo, registry);

		var lines = Decode("</xch_mudtext>" + wire);
		await Assert.That(lines.Select(line => line.Content.ToPlainText())).IsEquivalentTo(["North: look", "next & last"]);
		await Assert.That(Markups(lines[0]).OfType<SoundMarkup>().Single()).IsEqualTo(new SoundMarkup("https://example.test/door.wav", Volume: 80));
		await Assert.That(Markups(lines[0]).OfType<PaneMarkup>().First()).IsEqualTo(new PaneMarkup("map", "The Map"));
		await Assert.That(Spans(lines[0])[^1].Style.Interaction).IsEqualTo(new Interaction(InteractionKind.Command, "look") { Hint = "Look" });
		await Assert.That(Markups(lines[1]).OfType<PaneMarkup>().Any()).IsFalse();
	}

	[Test]
	public async Task Greeting()
	{
		await Assert.That(PuebloDecoding.IsGreeting("This world is Pueblo 1.10 Enhanced.")).IsTrue();
		await Assert.That(PuebloDecoding.IsGreeting("Welcome!")).IsFalse();
	}

	[Test]
	public async Task PromptLink_EndsAtItsClosingTag()
	{
		var lines = Decode("</xch_mudtext><a xch_cmd=\"look\" xch_mode=\"prompt\">look</a> plain<br>");
		var content = lines[0].Content;
		var plain = content.Text.IndexOf("plain", StringComparison.Ordinal);
		IEnumerable<IMarkup> At(int index) => content.Runs.Where(run => run.Start <= index && index < run.Start + run.Length).SelectMany(run => run.Markups);
		await Assert.That(At(plain).OfType<CommandMenuMarkup>()).IsEmpty();
		await Assert.That(At(0).OfType<CommandMenuMarkup>()).IsNotEmpty();
	}

	[Test]
	public async Task Pre_ClosedWithTheLinkAroundIt_EndsWithIt()
	{
		var lines = Decode("</xch_mudtext><send href=look><pre>x</send>a\nb<br>");
		await Assert.That(lines.Select(line => line.Content.ToPlainText())).IsEquivalentTo(["xab"]);
	}

	[Test]
	public async Task Send_WithSeveralCommands_IsAMenu()
	{
		var lines = Decode("</xch_mudtext><send href=\"buy|look\" hint=\"Buy|Look\">sword</send><br>");
		var menu = Markups(lines[0]).OfType<CommandMenuMarkup>().Single();
		await Assert.That(menu.Choices).IsEquivalentTo([new CommandChoice("Buy", "buy"), new CommandChoice("Look", "look")]);
	}
}
