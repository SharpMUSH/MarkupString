using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Mxp;
using MarkupString.Pueblo;
namespace MarkupString.Tests.Pueblo;

/// <summary>
/// Pueblo's own extensions: its <c>xch_</c> vocabulary, which means nothing to a browser or to an MXP
/// client, and nothing at all to a terminal.
/// </summary>
/// <remarks>
/// Names are the Pueblo client's own, from its tag and attribute tables
/// (<see href="https://github.com/uecasm/pueblo">uecasm/pueblo</see>, <c>api/ChHtmSym.cpp</c> and the
/// sound module).
/// </remarks>
public class PuebloElementTests
{
	private static readonly MarkupRegistry Registry =
		MarkupRegistry.Empty.WithAnsi().WithHtml().WithMxp().WithPueblo();

	private static string Render(MarkupText text, MarkupFormat format) => text.Render(format, Registry);

	[Test]
	public async Task AnExtensionIsWrittenAsItsTag()
	{
		await Assert.That(Render(PuebloElements.Page(), MarkupFormat.Pueblo)).IsEqualTo("<xch_page clear=\"text\">");
		await Assert.That(Render(PuebloElements.Page(PuebloClear.All), MarkupFormat.Pueblo)).IsEqualTo("<xch_page clear=\"all\">");
		await Assert.That(Render(PuebloElements.Mode(PuebloMode.PureHtml), MarkupFormat.Pueblo))
			.IsEqualTo("<img xch_mode=\"purehtml\">");
		await Assert.That(Render(PuebloElements.MudText(MarkupText.Plain("as typed")), MarkupFormat.Pueblo))
			.IsEqualTo("<xch_mudtext>as typed</xch_mudtext>");
		await Assert.That(Render(PuebloElements.Sound("door.wav", volume: 80), MarkupFormat.Pueblo))
			.IsEqualTo("<img xch_sound=\"door.wav\" xch_volume=\"80\">");
		await Assert.That(Render(PuebloElements.Alert("bell.wav"), MarkupFormat.Pueblo))
			.IsEqualTo("<img xch_alert=\"bell.wav\">");
		await Assert.That(Render(PuebloElements.Speech("Someone pages you"), MarkupFormat.Pueblo))
			.IsEqualTo("<img xch_speech=\"Someone pages you\">");
		await Assert.That(Render(PuebloElements.Prefetch("https://example.test/map.png"), MarkupFormat.Pueblo))
			.IsEqualTo("<xch_prefetch src=\"https://example.test/map.png\">");
		await Assert.That(Render(PuebloElements.Image("map.png", command: "look map", hint: "The map"), MarkupFormat.Pueblo))
			.IsEqualTo("<img src=\"map.png\" xch_cmd=\"look map\" xch_hint=\"The map\">");
	}

	[Test]
	public async Task APaneWrapsWhatGoesToIt()
	{
		var pane = PuebloElements.Pane(MarkupText.Plain("Map goes here"), "map", "The Map", scrolling: false);

		await Assert.That(Render(pane, MarkupFormat.Pueblo))
			.IsEqualTo("<xch_pane name=\"map\" panetitle=\"The Map\" scrolling=\"no\">Map goes here</xch_pane>");
	}

	[Test]
	public async Task AValueIsEncodedAsAnHtmlAttributeValue()
	{
		var image = PuebloElements.Image("map.png", command: "say \"hi\" & <bye>");

		await Assert.That(Render(image, MarkupFormat.Pueblo))
			.IsEqualTo("<img src=\"map.png\" xch_cmd=\"say &quot;hi&quot; &amp; &lt;bye&gt;\">");
	}

	/// <summary>
	/// The point of the package: Pueblo's vocabulary is Pueblo's. An MXP client shows <c>xch_</c>
	/// anything as text, and a terminal shows all of it.
	/// </summary>
	[Test]
	[Arguments("ansi")]
	[Arguments("plain")]
	[Arguments("bbcode")]
	[Arguments("mxp")]
	public async Task AFormatThatIsNotPuebloWritesNothing_CarrierIncluded(string format)
	{
		var line = MarkupText.Concat(PuebloElements.Sound("door.wav"), MarkupText.Plain("The door creaks."));

		await Assert.That(Render(line, MarkupFormat.TryParse(format)!)).IsEqualTo("The door creaks.");
	}

	[Test]
	public async Task APaneKeepsItsContentEverywhereElse()
	{
		var pane = PuebloElements.Pane(MarkupText.Plain("Map goes here"), "map");

		await Assert.That(Render(pane, MarkupFormat.Ansi)).IsEqualTo("Map goes here");
		await Assert.That(Render(pane, MarkupFormat.Mxp)).IsEqualTo("Map goes here");
	}

	[Test]
	public async Task ABrowserGetsAPictureAndAPane_AndNothingElse()
	{
		await Assert.That(Render(PuebloElements.Image("https://example.test/map.png", hint: "The map"), MarkupFormat.Html))
			.IsEqualTo("<img class=\"ms-pueblo-image\" data-pueblo=\"img\" src=\"https://example.test/map.png\" alt=\"The map\">");
		await Assert.That(Render(PuebloElements.Pane(MarkupText.Plain("x"), "map"), MarkupFormat.Html))
			.IsEqualTo("<span class=\"ms-pueblo-pane\" data-pueblo=\"xch_pane\" data-name=\"map\">x</span>");
		await Assert.That(Render(PuebloElements.Page(), MarkupFormat.Html)).IsEqualTo(string.Empty);
		await Assert.That(Render(PuebloElements.Mode(PuebloMode.PureHtml), MarkupFormat.Html)).IsEqualTo(string.Empty);
		await Assert.That(Render(PuebloElements.Sound("door.wav"), MarkupFormat.Html)).IsEqualTo(string.Empty)
			.Because("a sound names a file in the world's own directory, which a browser cannot resolve");
	}

	[Test]
	public async Task ABrowserIsSentNoAddressItCannotResolve()
	{
		await Assert.That(Render(PuebloElements.Image("map.png"), MarkupFormat.Html)).IsEqualTo(string.Empty);
		await Assert.That(Render(PuebloElements.Image("javascript:alert(1)"), MarkupFormat.Html)).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// Plain HTML stays <c>HtmlMarkup</c>'s, and styling and links stay <c>AnsiMarkup</c>'s: this package
	/// is only what Pueblo adds to HTML.
	/// </summary>
	[Test]
	public async Task TheOtherKindsAreUnaffected()
	{
		var bold = MarkupText.Wrap(AnsiMarkup.Create(bold: true), "loud");
		var link = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command), "look");
		var tag = MarkupText.Wrap(HtmlMarkup.Create("pre"), "spaced");

		await Assert.That(Render(link, MarkupFormat.Pueblo)).Contains("<A XCH_CMD=\"look\">");
		await Assert.That(Render(bold, MarkupFormat.Pueblo)).Contains("loud");
		await Assert.That(Render(tag, MarkupFormat.Pueblo)).IsEqualTo("<pre>spaced</pre>");
	}

	[Test]
	public async Task AnExtensionRoundTripsThroughTheSerializer()
	{
		var line = MarkupText.Concat(
			PuebloElements.Sound("door.wav", volume: 80),
			PuebloElements.Pane(MarkupText.Plain("Map"), "map"));

		var back = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(line, Registry), Registry);

		await Assert.That(back.Render(MarkupFormat.Pueblo, Registry)).IsEqualTo(Render(line, MarkupFormat.Pueblo));
	}

	[Test]
	public async Task AnExtensionMeasuresNothing()
	{
		var line = MarkupText.Concat(PuebloElements.Page(), MarkupText.Plain("ab"));

		await Assert.That(DisplayWidth.Of(line.Text)).IsEqualTo(2);
	}

	[Test]
	[Arguments("has space")]
	[Arguments("")]
	public async Task AnElementNameIsChecked(string name)
	{
		await Assert.That(() => PuebloElement.Standalone(name)).Throws<ArgumentException>();
	}
}
