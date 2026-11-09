public class TaggedFormatTests
{
	[Test]
	[Arguments("plain", false)]
	[Arguments("ansi", false)]
	[Arguments("html", true)]
	[Arguments("mxp", true)]
	[Arguments("pueblo", true)]
	[Arguments("bbcode", false)]
	[Arguments("markdown", false)]
	public async Task BuiltInsDeclareTheirAngleBracketSyntax(string name, bool tagged)
		=> await Assert.That(MarkupFormat.TryParse(name)!.IsTagged).IsEqualTo(tagged);

	[Test]
	[Arguments(TextEncoding.None)]
	[Arguments(TextEncoding.StripControls)]
	[Arguments(TextEncoding.Html)]
	[Arguments(TextEncoding.HtmlLineBreaks)]
	[Arguments(TextEncoding.Markdown)]
	public async Task LiteralTextEncodingDoesNotOptACustomFormatIntoTagParsing(TextEncoding encoding)
	{
		var format = MarkupFormat.Custom("custom", encoding);
		await Assert.That(format.IsTagged).IsFalse();
		await Assert.That(Width("<b>&amp;</b>", format)).IsEqualTo(12);
	}

	[Test]
	[Arguments(false, 12)]
	[Arguments(true, 1)]
	public async Task CustomFormatsExplicitlyChooseTheTaggedWidthGrammar(bool tagged, int expected)
	{
		var format = MarkupFormat.Custom("my-format", TextEncoding.Html, isTagged: tagged);
		await Assert.That(format.IsTagged).IsEqualTo(tagged);
		await Assert.That(Width("<b>&amp;</b>", format)).IsEqualTo(expected);
	}

	[Test]
	public async Task TaggedMeansTheDocumentedAngleBracketGrammarNotArbitraryTags()
	{
		var format = MarkupFormat.Custom("angle-tags", TextEncoding.Html, isTagged: true);
		await Assert.That(Width("\e[31m<b title='>'>&amp;界</b><!--hidden-->\e[0m", format)).IsEqualTo(3);
		await Assert.That(Width("[b]x[/b]", format)).IsEqualTo(8);
		await Assert.That(Width("**x**", format)).IsEqualTo(5);
	}

	[Test]
	[Arguments("HTML")]
	[Arguments("MXP")]
	[Arguments("Pueblo")]
	[Arguments("plain")]
	public async Task TaggedCustomFormatsCannotOverrideBuiltInDeclarations(string name)
		=> await Assert.That(() => MarkupFormat.Custom(name, TextEncoding.None, isTagged: true)).Throws<ArgumentException>();

	[Test]
	public async Task TaggedCustomFormatIdentityStillUsesItsName()
	{
		var first = MarkupFormat.Custom("custom-tags", TextEncoding.Html, isTagged: true);
		var second = MarkupFormat.Custom("CUSTOM-TAGS", TextEncoding.Html, isTagged: true);
		await Assert.That(first).IsEqualTo(second);
		await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
	}

	[Test]
	[Arguments(0)]
	[Arguments(7)]
	public async Task SourceGeometryStillWinsOverCustomTaggedParsing(int width)
	{
		var context = new EmitContext
		{
			Format = MarkupFormat.Custom("custom-tags", TextEncoding.Html, isTagged: true),
			Registry = MarkupRegistry.Empty, Markups = MarkupSet.Of(new ImageMarkup("cat.png")), SourceDisplayWidth = width,
		};
		await Assert.That(DisplayWidth.Of("<b>&amp;</b>", context)).IsEqualTo(width);
	}

	private static int Width(string body, MarkupFormat format)
		=> DisplayWidth.Of(body, new EmitContext { Format = format, Registry = MarkupRegistry.Empty, Markups = MarkupSet.Of(new ImageMarkup("cat.png")) });
}
