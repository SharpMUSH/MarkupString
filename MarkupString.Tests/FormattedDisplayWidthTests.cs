using System.Buffers;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Pueblo;
using MarkupString.Layout;
using MarkupString.Mxp;

public class FormattedDisplayWidthTests
{
	[Test]
	[Arguments("", 0)]
	[Arguments("abc", 3)]
	[Arguments("\e[36mabc\e[0m", 3)]
	[Arguments("\e[38;2;0;245;183mabc\e[0m", 3)]
	[Arguments("\e[?25labc\e[?25h", 3)]
	[Arguments("\e]8;;https://example.test\aabc\e]8;;\e\\", 3)]
	[Arguments("a\eP1;2qhidden payload\e\\z", 2)]
	[Arguments("a\e_hidden payload\e\\z", 2)]
	[Arguments("a\e^hidden payload\e\\z", 2)]
	[Arguments("a\eXhidden payload\e\\z", 2)]
	[Arguments("a\e[", 1)]
	[Arguments("a\e]unterminated\nz", 2)]
	[Arguments("\u009b31mX", 4)]
	[Arguments("\U0001F468\u200D\e[31m\U0001F469\e[0m", 2)]
	[Arguments("e\e[31m\u0301界\e[0m", 3)]
	[Arguments("<b>x</b>", 8)]
	[Arguments("\ud83d\e[31m\udc68", 2)]
	public async Task AnsiMeasuresTextWithoutSequenceSyntax(string text, int width)
		=> await Assert.That(DisplayWidth.OfAnsi(text)).IsEqualTo(width);

	[Test]
	[Arguments("", 0)]
	[Arguments("abc", 3)]
	[Arguments("<b>abc</b>", 3)]
	[Arguments("<span title='a > b'>abc</span>", 3)]
	[Arguments("&amp;&lt;界", 4)]
	[Arguments("&amp;lt;", 4)]
	[Arguments("&lt;b&gt;x&lt;/b&gt;", 8)]
	[Arguments("a<!-- hidden <b>markup</b> -->z", 2)]
	[Arguments("&unknown;", 9)]
	[Arguments("<tag", 4)]
	[Arguments("&am", 3)]
	[Arguments("\e[31m<b>&lt;界</b>\e[0m", 3)]
	[Arguments("&#x1f468;&#8205;<b>&#x1f469;</b>", 2)]
	[Arguments("e<b>&#769;</b>界", 3)]
	[Arguments("a<tag\nz", 6)]
	public async Task TaggedMeasuresTextWithoutTagsSequencesOrEntitySyntax(string text, int width)
		=> await Assert.That(DisplayWidth.OfTagged(text)).IsEqualTo(width);

	[Test]
	public async Task PlainWidthStillCountsLiteralMarkup()
	{
		await Assert.That(DisplayWidth.Of("<b>x</b>")).IsEqualTo(8);
		await Assert.That(MarkupText.Plain("&amp;").DisplayWidth).IsEqualTo(5);
		await Assert.That(DisplayWidth.Of("[b]x[/b]")).IsEqualTo(8);
		await Assert.That(DisplayWidth.IndexAtWidth("<b>x</b>", 3)).IsEqualTo(3);
	}

	[Test]
	public async Task FormattedWidthIgnoresLargeControlStringPayloads()
	{
		var text = "a\e]0;" + new string('x', 100_000) + "\az";
		await Assert.That(DisplayWidth.OfAnsi(text)).IsEqualTo(2);
		await Assert.That(DisplayWidth.OfTagged(text)).IsEqualTo(2);
	}
	[Test]
	[Arguments("mxp")]
	[Arguments("pueblo")]
	[Arguments("html")]
	public async Task ImageFallbackMeasuresFormattedTextWithoutSourceMetadata(string name)
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithMxp().WithPueblo().WithHtml();
		var format = MarkupFormat.TryParse(name)!;
		var image = new ImageMarkup("cat.png") { Row = new PictureRow(1, 2, 4) };
		var context = new EmitContext { Format = format, Registry = registry, Markups = MarkupSet.Of(image) };
		var output = new ArrayBufferWriter<char>();
		registry.FindEmitter(typeof(ImageMarkup), format)!.Emit(image, "\e[36m&amp;&lt;界\e[0m", context, output);
		await Assert.That(new string(output.WrittenSpan)).IsEqualTo("    ");
	}

	[Test]
	public async Task PictureGeometrySurvivesAnInnerEmitterThatReplacesItsText()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithMxp();
		var art = MarkupText.Wrap(new GaugeMarkup("hp", "maxhp"), "abc");
		await Assert.That(DisplayWidth.OfTagged(art.Render(MarkupFormat.Mxp, registry))).IsEqualTo(0);
		var laid = BlockLayout.Build(new Figure(new ImageMarkup("cat.png"), art), 8);
		var output = laid.Render(MarkupFormat.Mxp, registry);
		await Assert.That(output.Replace("<IMAGE cat.png W=3c H=1c>", string.Empty)).IsEqualTo("        ");
	}

	[Test]
	public async Task TaggedWidthKeepsOverlongQuotedTagsLiteral()
	{
		var text = "<b title=\"" + new string('x', 9_000) + "\">z";
		await Assert.That(DisplayWidth.OfTagged(text)).IsEqualTo(9_013);
	}

	[Test]
	public async Task TaggedWidthKeepsOverlongEntitiesLiteral()
	{
		var text = "&" + new string('x', 33) + ";";
		await Assert.That(DisplayWidth.OfTagged(text)).IsEqualTo(35);
	}

	[Test]
	public async Task TaggedWidthDoesNotCountLargeComments()
	{
		var text = "a<!--" + new string('x', 100_000) + "-->z";
		await Assert.That(DisplayWidth.OfTagged(text)).IsEqualTo(2);
	}

}
