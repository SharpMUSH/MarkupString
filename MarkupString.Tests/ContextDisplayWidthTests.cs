using System.Buffers;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Mxp;
using MarkupString.Pueblo;

public class ContextDisplayWidthTests
{
	/// <summary>Resolves a built-in format or creates an untagged custom format despite its HTML text encoding.</summary>
	private static MarkupFormat Format(string name) => MarkupFormat.TryParse(name)
		?? MarkupFormat.Custom(name, TextEncoding.Html);

	[Test]
	[Arguments("plain")]
	[Arguments("ansi")]
	[Arguments("html")]
	[Arguments("mxp")]
	[Arguments("pueblo")]
	[Arguments("bbcode")]
	[Arguments("markdown")]
	[Arguments("custom")]
	public async Task SourceGeometryWinsOverTheEmittedBodyInEveryFormat(string name)
	{
		var context = new EmitContext
		{
			Format = Format(name), Registry = MarkupRegistry.Empty,
			Markups = MarkupSet.Of(new ImageMarkup("cat.png")), SourceDisplayWidth = 3,
		};
		await Assert.That(DisplayWidth.Of("<GAUGE hp MAX=maxhp>", context)).IsEqualTo(3);
	}

	[Test]
	public async Task ZeroSourceGeometryDoesNotFallBackToVisibleBodyText()
	{
		var context = new EmitContext
		{
			Format = MarkupFormat.Mxp, Registry = MarkupRegistry.Empty,
			Markups = MarkupSet.Of(new ImageMarkup("cat.png")), SourceDisplayWidth = 0,
		};
		await Assert.That(DisplayWidth.Of("abc", context)).IsEqualTo(0);
	}

	[Test]
	[Arguments("plain", "<b>&amp;</b>", 12)]
	[Arguments("plain", "\e[31mx\e[0m", 8)]
	[Arguments("ansi", "\e[31mabc\e[0m", 3)]
	[Arguments("ansi", "<b>x</b>", 8)]
	[Arguments("html", "\e[31m<b>&amp;界</b>\e[0m", 3)]
	[Arguments("mxp", "\e[31m<b>&amp;界</b>\e[0m", 3)]
	[Arguments("pueblo", "\e[31m<b>&amp;界</b>\e[0m", 3)]
	[Arguments("bbcode", "[b]x[/b]", 8)]
	[Arguments("markdown", "**x**", 5)]
	[Arguments("custom", "<b>&amp;</b>", 12)]
	public async Task MissingSourceGeometryUsesOnlyTheKnownFormatsSyntax(string name, string body, int width)
	{
		var context = new EmitContext
		{
			Format = Format(name), Registry = MarkupRegistry.Empty,
			Markups = MarkupSet.Of(new ImageMarkup("cat.png")),
		};
		await Assert.That(DisplayWidth.Of(body, context)).IsEqualTo(width);
	}

	[Test]
	[Arguments("mxp")]
	[Arguments("pueblo")]
	[Arguments("html")]
	[Arguments("bbcode")]
	[Arguments("markdown")]
	public async Task PictureEmittersReserveTheSuppliedSourceGeometry(string name)
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithMxp().WithPueblo().WithHtml();
		var format = Format(name);
		var image = new ImageMarkup("cat.png") { Row = new PictureRow(1, 2, 3) };
		var context = new EmitContext
		{
			Format = format, Registry = registry, Markups = MarkupSet.Of(image), SourceDisplayWidth = 3,
		};
		var output = new ArrayBufferWriter<char>();
		registry.FindEmitter(typeof(ImageMarkup), format)!.Emit(image, "not the original text", context, output);
		await Assert.That(new string(output.WrittenSpan)).IsEqualTo("   ");
	}
}
