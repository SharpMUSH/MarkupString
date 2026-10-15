using CsCheck;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using MarkupString.Ansi;
using MarkupString.Html;
namespace MarkupString.Tests.Ansi;

/// <summary>
/// <see cref="MarkupFormat.Markdown"/> for running text: GitHub-flavoured emphasis, links and pictures from the
/// Ansi fold, everything else dropped with its text kept, and every character that would start Markdown
/// syntax escaped so that stored text reads back as itself.
/// </summary>
/// <remarks>
/// Where the exact characters are not the point, the output is read back with Markdig, a GitHub-flavoured
/// reader, so a test says what a wiki shows rather than how it was spelled.
/// </remarks>
public class MarkdownRenderTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static string Render(MarkupText text) => text.Render(MarkupFormat.Markdown, Registry);

	private static readonly AnsiMarkup Bold = AnsiMarkup.Create(bold: true);
	private static readonly AnsiMarkup Italic = AnsiMarkup.Create(italic: true);

	// ── The format ──────────────────────────────────────────────────────────────

	[Test]
	public async Task TheFormatIsBuiltIn()
	{
		await Assert.That(MarkupFormat.TryParse("Markdown")).IsSameReferenceAs(MarkupFormat.Markdown);
		await Assert.That(MarkupFormat.Markdown.Encoding).IsEqualTo(TextEncoding.Markdown);
		await Assert.That(() => MarkupFormat.Custom("markdown", TextEncoding.None)).Throws<ArgumentException>();
	}

	[Test]
	public async Task PlainTextPassesThrough()
		=> await Assert.That(Render(MarkupText.Plain("Hello, world."))).IsEqualTo("Hello, world.");

	[Test]
	public async Task ARegistryWithoutAnsiStillEscapes()
		=> await Assert.That(MarkupText.Plain("*x*").Render(MarkupFormat.Markdown, MarkupRegistry.Empty)).IsEqualTo("\\*x\\*");

	// ── Escaping ────────────────────────────────────────────────────────────────

	[Test]
	[Arguments("a*b", "a\\*b")]
	[Arguments("snake_case", "snake\\_case")]
	[Arguments("`code`", "\\`code\\`")]
	[Arguments("[link](x)", "\\[link\\](x)")]
	[Arguments("![img](x)", "!\\[img\\](x)")]
	[Arguments("<b>", "\\<b>")]
	[Arguments("a|b", "a\\|b")]
	[Arguments("~~gone~~", "\\~\\~gone\\~\\~")]
	[Arguments("back\\slash", "back\\\\slash")]
	[Arguments("&amp; &#35;", "\\&amp; \\&#35;")]
	[Arguments("Tom & Jerry", "Tom & Jerry")]
	[Arguments("issue #5 > 4 - 3 + 2 = 1", "issue #5 > 4 - 3 + 2 = 1")]
	public async Task MetacharactersAreEscapedWhereverTheyStand(string text, string expected)
		=> await Assert.That(Render(MarkupText.Plain(text))).IsEqualTo(expected);

	[Test]
	[Arguments("# Heading", "\\# Heading")]
	[Arguments("> quote", "\\> quote")]
	[Arguments("- item", "\\- item")]
	[Arguments("+ item", "\\+ item")]
	[Arguments("===", "\\===")]
	[Arguments("---", "\\---")]
	[Arguments("1. first", "1\\. first")]
	[Arguments("12) twelfth", "12\\) twelfth")]
	[Arguments("3.14 is pi", "3.14 is pi")]
	[Arguments("  - indented", "\\- indented")]
	public async Task LineStartMarkersAreEscapedAtTheStartOfALine(string text, string expected)
		=> await Assert.That(Render(MarkupText.Plain(text))).IsEqualTo(expected);

	[Test]
	public async Task EveryLineIsALineStart()
		=> await Assert.That(Render(MarkupText.Plain("a\n# b\r\n- c\r1. d"))).IsEqualTo("a\n\\# b\n\\- c\n1\\. d");

	[Test]
	public async Task ACrlfSplitBetweenTwoRunsIsOneLineEnding()
		=> await Assert.That(Render(MarkupText.Concat([MarkupText.Wrap(Bold, "a\r"), MarkupText.Plain("\n- b")]))).IsEqualTo("**a**\n\\- b");

	[Test]
	public async Task ControlCharactersAreDropped()
		=> await Assert.That(Render(MarkupText.Plain("a\u0001b\u001b[1mc\u007f\td"))).IsEqualTo("ab\\[1mc\td");

	/// <summary>
	/// Indentation would make code of a line, or put it inside a list item, and a reader shows none of it, so a
	/// line's own is dropped, however many stretches of text it is spread over. Whitespace after text on the
	/// line is kept.
	/// </summary>
	[Test]
	public async Task IndentationIsNotKept()
	{
		await Assert.That(Render(MarkupText.Plain("    code?\n\tand this\n   three\n    \nfour"))).IsEqualTo("code?\nand this\nthree\n\nfour");
		var text = MarkupText.Concat([MarkupText.Plain("a\n  "), MarkupText.Wrap(Bold, "    "), MarkupText.Plain("  "), MarkupText.Bell(), MarkupText.Plain(" # b")]);
		await Assert.That(Render(text)).IsEqualTo("a\n\\# b");
		await Assert.That(Render(MarkupText.Concat([MarkupText.Plain("a"), MarkupText.Wrap(Bold, "    - b")]))).IsEqualTo("a    **- b**");
	}

	/// <summary>On its own, with nothing known of what is before it, a stretch may start partway along a line.</summary>
	[Test]
	public async Task EncodeTextKeepsOneSpaceOfIndentation()
	{
		var buffer = new System.Buffers.ArrayBufferWriter<char>();
		MarkupTextRenderer.EncodeText("    - x\n  y", TextEncoding.Markdown, buffer);
		await Assert.That(new string(buffer.WrittenSpan)).IsEqualTo(" \\- x\ny");
	}

	[Test]
	public async Task EncodeTextIsTheSameEncoding()
	{
		var buffer = new System.Buffers.ArrayBufferWriter<char>();
		MarkupTextRenderer.EncodeText("# *a*", TextEncoding.Markdown, buffer);
		await Assert.That(new string(buffer.WrittenSpan)).IsEqualTo("\\# \\*a\\*");
	}

	// ── Styling ─────────────────────────────────────────────────────────────────

	[Test]
	public async Task GitHubsStylingIsWritten()
	{
		await Assert.That(Render(MarkupText.Wrap(Bold, "b"))).IsEqualTo("**b**");
		await Assert.That(Render(MarkupText.Wrap(Italic, "i"))).IsEqualTo("*i*");
		await Assert.That(Render(MarkupText.Wrap(AnsiMarkup.Create(strikeThrough: true), "s"))).IsEqualTo("~~s~~");
		await Assert.That(Render(MarkupText.Wrap(AnsiMarkup.Create(bold: true, italic: true, strikeThrough: true), "x"))).IsEqualTo("~~***x***~~");
	}

	[Test]
	public async Task WhatMarkdownCannotSayIsDroppedAndTheTextKept()
	{
		var markup = AnsiMarkup.Create(
			foreground: new AnsiColor.Standard(1, false), background: new AnsiColor.Rgb(0, 0, 255),
			underlined: true, faint: true, blink: true, overlined: true, inverted: true);
		await Assert.That(Render(MarkupText.Wrap(markup, "plain"))).IsEqualTo("plain");
	}

	[Test]
	public async Task HtmlTagsForStylingFold()
	{
		var text = MarkupText.Concat([
			MarkupText.Wrap(HtmlMarkup.Create("strong"), "b"), MarkupText.Plain(" "),
			MarkupText.Wrap(HtmlMarkup.Create("em"), "i"), MarkupText.Plain(" "),
			MarkupText.Wrap(HtmlMarkup.Create("del"), "s"), MarkupText.Plain(" "),
			MarkupText.Wrap(HtmlMarkup.Create("span", "class=\"x\""), "kept")]);
		await Assert.That(Render(text)).IsEqualTo("**b** *i* ~~s~~ kept");
	}

	[Test]
	public async Task DelimitersStayOffTheWhitespaceAtEitherEnd()
	{
		var text = MarkupText.Concat([MarkupText.Plain("a"), MarkupText.Wrap(Bold, " bold "), MarkupText.Plain("b")]);
		await Assert.That(Render(text)).IsEqualTo("a **bold** b");
		await Assert.That(ToHtml(Render(text))).IsEqualTo("<p>a <strong>bold</strong> b</p>");
	}

	[Test]
	public async Task NothingButWhitespaceIsNotWrapped()
		=> await Assert.That(Render(MarkupText.Concat([MarkupText.Plain("a"), MarkupText.Wrap(Bold, "  "), MarkupText.Plain("b")]))).IsEqualTo("a  b");

	[Test]
	public async Task StylingThatCarriesOnIsWrittenOnceRoundBothRuns()
	{
		var text = MarkupText.Concat([MarkupText.Wrap(Bold, "a "), MarkupText.Wrap(Bold, MarkupText.Wrap(Italic, "b")), MarkupText.Plain(" c")]);
		await Assert.That(Render(text)).IsEqualTo("**a *b*** c");
		await Assert.That(ToHtml(Render(text))).IsEqualTo("<p><strong>a <em>b</em></strong> c</p>");
	}

	[Test]
	public async Task NestedStylingReadsBackNested()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(strikeThrough: true),
			MarkupText.Concat([MarkupText.Plain("x "), MarkupText.Wrap(Bold, MarkupText.Concat([MarkupText.Plain("y "), MarkupText.Wrap(Italic, "z")])), MarkupText.Plain(" w")]));
		await Assert.That(ToHtml(Render(text))).IsEqualTo("<p><del>x <strong>y <em>z</em></strong> w</del></p>");
	}

	[Test]
	public async Task StylingThatEndsInsideAnotherStillPairsUp()
	{
		// Bold and italic, then italic alone: the bold closes, and the italic is closed and opened again round it.
		var text = MarkupText.Wrap(Italic, MarkupText.Concat([MarkupText.Wrap(Bold, "a"), MarkupText.Plain("b")]));
		await Assert.That(ToHtml(Render(text))).IsEqualTo("<p><em><strong>a</strong></em><em>b</em></p>");
	}

	[Test]
	public async Task StylingClosesBeforeALineEndingAndOpensAfterIt()
	{
		var text = MarkupText.Wrap(Bold, "one\n\ntwo");
		await Assert.That(Render(text)).IsEqualTo("**one**\n\n**two**");
		await Assert.That(ToHtml(Render(text))).IsEqualTo("<p><strong>one</strong></p>\n<p><strong>two</strong></p>");
	}

	[Test]
	public async Task EscapedTextInsideStylingStaysLiteral()
	{
		var text = MarkupText.Wrap(Bold, "*not italic*");
		await Assert.That(Render(text)).IsEqualTo("**\\*not italic\\***");
		await Assert.That(ToHtml(Render(text))).IsEqualTo("<p><strong>*not italic*</strong></p>");
	}

	// ── Links ───────────────────────────────────────────────────────────────────

	[Test]
	public async Task AUrlLinkIsALink()
		=> await Assert.That(Render(MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "https://example.com/a?b=1&c=2"), "example")))
			.IsEqualTo("[example](https://example.com/a?b=1&c=2)");

	[Test]
	public async Task AnAddressIsEncodedSoTheLinkEndsWhereItShould()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "https://x.test/a b(c)|d<e>"), "x");
		await Assert.That(Render(text)).IsEqualTo("[x](https://x.test/a%20b%28c%29%7Cd%3Ce%3E)");
		await Assert.That(ToHtml(Render(text))).IsEqualTo("<p><a href=\"https://x.test/a%20b%28c%29%7Cd%3Ce%3E\">x</a></p>");
	}

	[Test]
	public async Task ACommandLinkIsItsText()
		=> await Assert.That(Render(MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "+who", linkKind: LinkKind.Command, linkText: "Who?"), "who")))
			.IsEqualTo("who");

	[Test]
	public async Task AnUnsafeAddressIsItsText()
		=> await Assert.That(Render(MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "javascript:alert(1)"), "click"))).IsEqualTo("click");

	[Test]
	public async Task StylingInsideALinkStaysInside()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "https://x.test"),
			MarkupText.Concat([MarkupText.Plain("go "), MarkupText.Wrap(Bold, "now")]));
		await Assert.That(Render(text)).IsEqualTo("[go **now**](https://x.test)");
		await Assert.That(ToHtml(Render(text))).IsEqualTo("<p><a href=\"https://x.test\">go <strong>now</strong></a></p>");
	}

	// ── The shared vocabulary ───────────────────────────────────────────────────

	[Test]
	public async Task APictureIsAnImage()
	{
		await Assert.That(Render(MarkupText.Image("map.png", "A map of the city", width: 200))).IsEqualTo("![A map of the city](map.png)");
		await Assert.That(Render(MarkupText.Image("https://example.test/a b.png"))).IsEqualTo("https://example.test/a b.png");
		await Assert.That(Render(MarkupText.Image("map.png", "A [map]"))).IsEqualTo("![A \\[map\\]](map.png)");
	}

	[Test]
	public async Task APictureAtAnAddressAPageCannotLoadIsItsDescription()
		=> await Assert.That(Render(MarkupText.Image("file:///etc/passwd", "secrets"))).IsEqualTo("secrets");

	[Test]
	public async Task APictureInsideALinkIsALinkedImage()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "https://x.test/city"), MarkupText.Image("map.png", "A map"));
		await Assert.That(Render(text)).IsEqualTo("[![A map](map.png)](https://x.test/city)");
	}

	[Test]
	public async Task PointsAreNothing()
	{
		var line = MarkupText.Concat([
			MarkupText.Sound("door.wav"), MarkupText.Music("theme.mid"), MarkupText.StopSound(), MarkupText.ClearScreen(),
			MarkupText.Bell(), MarkupText.Plain("The door creaks.")]);
		await Assert.That(Render(line)).IsEqualTo("The door creaks.");
	}

	/// <summary>
	/// A point inside styled text writes nothing, and the styling either side of it is closed against it, as
	/// against anything written by an emitter of its own, so nothing is left open.
	/// </summary>
	[Test]
	public async Task AStyledPointLeavesNothingOpen()
	{
		var line = MarkupText.Wrap(Bold, MarkupText.Concat([MarkupText.Plain("You hear "), MarkupText.Sound("door.wav"), MarkupText.Plain(" a door")]));
		await Assert.That(Render(line)).IsEqualTo("**You hear**  **a door**");
		await Assert.That(ToHtml(Render(line))).IsEqualTo("<p><strong>You hear</strong>  <strong>a door</strong></p>");
	}

	[Test]
	public async Task TextTheVocabularyMarksIsKept()
	{
		var line = MarkupText.Concat([MarkupText.Pane(MarkupText.Plain("hi"), "chat"), MarkupText.Plain(" "), MarkupText.Variable(MarkupText.Plain("10"), "hp")]);
		await Assert.That(Render(line)).IsEqualTo("hi 10");
	}

	// ── Reading it back ─────────────────────────────────────────────────────────

	/// <summary>Pieces that are Markdown syntax somewhere, and the whitespace that decides where.</summary>
	private static readonly string[] Pieces =
	[
		"a", "Zed", "42", "1.", "3)", " ", "  ", "    ", "\t", "\n", "\n\n", "\r\n",
		"*", "**", "_", "__", "`", "```", "~", "~~", "[", "]", "(", ")", "![", "<", ">", "<b>", "&", "&amp;", "&#35;",
		"#", "##", "-", "---", "+", "=", "===", "|", "\\", "!", ":", "http://x.test", "www.x.test", "é", "日本",
	];

	/// <summary>
	/// Whatever plain text is stored, a GitHub-flavoured reader shows exactly its words: none of it is read as
	/// syntax. Whitespace is compared as a reader shows it, a run of it as one space.
	/// </summary>
	[Test]
	public void PlainTextReadsBackAsItself()
	{
		var gen = Gen.OneOfConst(Pieces).Array[0, 12].Select(string.Concat);
		Check.Sample(gen, text =>
		{
			var markdown = Render(MarkupText.Plain(text));
			var shown = Words(Markdown.ToPlainText(markdown, Pipeline));
			if (shown != Words(text)) throw new InvalidOperationException($"{Show(text)} -> {Show(markdown)} reads as {Show(shown)}");
		}, seed: "000000000000", iter: 5000);
	}

	/// <summary>
	/// The same, for text styled in stretches a space apart: the words come back, whatever the delimiters.
	/// </summary>
	/// <remarks>
	/// The space is Markdown's limit, not this emitter's: a delimiter between punctuation and a letter, as in
	/// <c>**Note:**text</c>, is not read as one by any GitHub-flavoured reader.
	/// </remarks>
	[Test]
	public void StyledTextReadsBackAsItsWords()
	{
		var styles = new[] { Bold, Italic, AnsiMarkup.Create(strikeThrough: true), AnsiMarkup.Create(bold: true, italic: true) };
		var gen = Gen.Select(Gen.OneOfConst(Pieces).Array[1, 4].Select(string.Concat), Gen.Int[-1, styles.Length - 1])
			.Array[1, 5]
			.Select(parts => MarkupText.Join(MarkupText.Plain(" "), parts.Select(part => part.Item2 < 0 ? MarkupText.Plain(part.Item1) : MarkupText.Wrap(styles[part.Item2], part.Item1))));
		Check.Sample(gen, text =>
		{
			var markdown = Render(text);
			var shown = Words(Markdown.ToPlainText(markdown, Pipeline));
			if (shown != Words(text.ToPlainText())) throw new InvalidOperationException($"{Show(text.ToPlainText())} -> {Show(markdown)} reads as {Show(shown)}");
		}, seed: "000000000000", iter: 5000);
	}

	internal static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
		.UsePipeTables()
		.UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
		.Build();

	internal static string ToHtml(string markdown) => Markdown.ToHtml(markdown, Pipeline).TrimEnd('\n');

	/// <summary>Text as a reader shows it: control characters gone and every run of whitespace one space.</summary>
	private static string Words(string text) =>
		string.Join(' ', new string([.. text.Where(c => !char.IsControl(c) || char.IsWhiteSpace(c))])
			.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

	private static string Show(string text) => text.Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
}
