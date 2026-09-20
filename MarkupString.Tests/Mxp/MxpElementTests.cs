using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Mxp;
namespace MarkupString.Tests.Mxp;

/// <summary>
/// MXP's own elements: a tag for a client that negotiated MXP, the nearest thing a browser has, and
/// nothing at all for a client with neither — so the same text is safe to send to every one of them.
/// </summary>
/// <remarks>
/// Syntax from the <see href="https://www.zuggsoft.com/zmud/mxp.htm">MXP specification</see>.
/// </remarks>
public class MxpElementTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml().WithMxp();

	private static string Render(MarkupText text, MarkupFormat format) => text.Render(format, Registry);

	// ── Written as MXP ──────────────────────────────────────────────────────────

	[Test]
	public async Task AnElementIsWrittenAsItsTag()
	{
		await Assert.That(Render(MxpElements.Sound("door.wav"), MarkupFormat.Mxp)).IsEqualTo("<SOUND door.wav>");
		await Assert.That(Render(MxpElements.Sound("door.wav", volume: 80, loops: 2, priority: 10), MarkupFormat.Mxp))
			.IsEqualTo("<SOUND door.wav V=80 L=2 P=10>");
		await Assert.That(Render(MxpElements.Music("theme.mid", continues: true), MarkupFormat.Mxp))
			.IsEqualTo("<MUSIC theme.mid C=1>");
		await Assert.That(Render(MxpElements.Image("map.png", align: MxpAlign.Left, isMap: true), MarkupFormat.Mxp))
			.IsEqualTo("<IMAGE map.png ALIGN=LEFT ISMAP>");
		await Assert.That(Render(MxpElements.Gauge("hp", "maxhp", "Hit Points", "red"), MarkupFormat.Mxp))
			.IsEqualTo("<GAUGE hp MAX=maxhp CAPTION=\"Hit Points\" COLOR=red>");
		await Assert.That(Render(MxpElements.Stat("mana", caption: "Mana"), MarkupFormat.Mxp))
			.IsEqualTo("<STAT mana CAPTION=Mana>");
		await Assert.That(Render(MxpElements.Expire("exits"), MarkupFormat.Mxp)).IsEqualTo("<EXPIRE exits>");
		await Assert.That(Render(MxpElements.Expire(), MarkupFormat.Mxp)).IsEqualTo("<EXPIRE>");
		await Assert.That(Render(MxpElements.User(), MarkupFormat.Mxp)).IsEqualTo("<USER>");
		await Assert.That(Render(MxpElements.Password(), MarkupFormat.Mxp)).IsEqualTo("<PASSWORD>");
		await Assert.That(Render(MxpElements.NoBreak(), MarkupFormat.Mxp)).IsEqualTo("<NOBR>");
		await Assert.That(Render(MxpElements.SoftBreak(), MarkupFormat.Mxp)).IsEqualTo("<SBR>");
		await Assert.That(Render(MxpElements.Relocate("other.example", 4201, quiet: true), MarkupFormat.Mxp))
			.IsEqualTo("<RELOCATE other.example 4201 QUIET>");
	}

	[Test]
	public async Task AnElementThatWrapsContentClosesAfterIt()
	{
		var frame = MxpElements.Frame(MarkupText.Plain("Map goes here"), "map", MxpFrameAction.Open, "The Map", scrolling: false);

		await Assert.That(Render(frame, MarkupFormat.Mxp))
			.IsEqualTo("<FRAME map ACTION=OPEN TITLE=\"The Map\" SCROLLING=no>Map goes here</FRAME>");

		var variable = MxpElements.Var(MarkupText.Plain("42"), "hp", description: "Hit points");

		await Assert.That(Render(variable, MarkupFormat.Mxp))
			.IsEqualTo("<VAR hp DESC=\"Hit points\">42</VAR>");
	}

	/// <summary>
	/// MXP separates arguments with whitespace and reads a quoted value as one, so a value carrying
	/// either is quoted and its own quotes written as entities.
	/// </summary>
	[Test]
	public async Task AValueThatNeedsQuotingIsQuoted()
	{
		var stat = MxpElements.Stat("hp", caption: "He said \"hello\"");

		await Assert.That(Render(stat, MarkupFormat.Mxp)).IsEqualTo("<STAT hp CAPTION=\"He said &quot;hello&quot;\">");
	}

	// ── Written for everyone else ───────────────────────────────────────────────

	[Test]
	[Arguments("ansi")]
	[Arguments("plain")]
	[Arguments("bbcode")]
	[Arguments("pueblo")]
	public async Task AFormatWithNoMxpWritesNothing_CarrierIncluded(string format)
	{
		var line = MarkupText.Concat(MxpElements.Sound("door.wav"), MarkupText.Plain("The door creaks."));

		await Assert.That(Render(line, MarkupFormat.TryParse(format)!)).IsEqualTo("The door creaks.")
			.Because("a client that cannot read the tag must not be sent the character it rode on either");
	}

	[Test]
	public async Task AWrappingElementLeavesItsContentBehind()
	{
		var frame = MxpElements.Frame(MarkupText.Plain("Map goes here"), "map");

		await Assert.That(Render(frame, MarkupFormat.Ansi)).IsEqualTo("Map goes here");
		await Assert.That(Render(frame, MarkupFormat.Plain)).IsEqualTo("Map goes here");
	}

	[Test]
	public async Task AnElementMeasuresNothing()
	{
		var line = MarkupText.Concat(MxpElements.Sound("door.wav"), MarkupText.Plain("ab"));

		await Assert.That(DisplayWidth.Of(line.Text)).IsEqualTo(2);
	}

	// ── Written for a browser ───────────────────────────────────────────────────

	[Test]
	public async Task ABrowserGetsTheNearestThingItHas()
	{
		await Assert.That(Render(MxpElements.Image("map.png", url: "https://example.test/map.png"), MarkupFormat.Html))
			.IsEqualTo("<img class=\"ms-mxp-image\" data-mxp=\"IMAGE\" src=\"https://example.test/map.png\" alt=\"map.png\">");
		await Assert.That(Render(MxpElements.Sound("door.wav", url: "https://example.test/door.wav"), MarkupFormat.Html))
			.IsEqualTo("<audio class=\"ms-mxp-sound\" data-mxp=\"SOUND\" src=\"https://example.test/door.wav\" preload=\"none\"></audio>")
			.Because("<audio> is standard HTML, but autoplay is refused until the person has interacted with the page, so whether it sounds is the page's call");
		await Assert.That(Render(MxpElements.Gauge("hp", "maxhp", "Hit Points"), MarkupFormat.Html))
			.IsEqualTo("<span class=\"ms-mxp-gauge\" data-mxp=\"GAUGE\" data-entity=\"hp\" data-max=\"maxhp\">Hit Points</span>");
		await Assert.That(Render(MxpElements.Frame(MarkupText.Plain("x"), "map"), MarkupFormat.Html))
			.IsEqualTo("<span class=\"ms-mxp-frame\" data-mxp=\"FRAME\" data-name=\"map\">x</span>");
	}

	/// <summary>
	/// MXP's <c>FName</c> names a file in the game's own sound or image directory, which a browser cannot
	/// resolve, and an address it could be made to fetch is not one to follow blindly.
	/// </summary>
	[Test]
	[Arguments(null)]
	[Arguments("javascript:alert(1)")]
	[Arguments("/relative/door.wav")]
	public async Task ABrowserIsSentNoAddressItCannotTrust(string? url)
	{
		await Assert.That(Render(MxpElements.Sound("door.wav", url: url), MarkupFormat.Html)).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task ElementsWithNoBrowserEquivalentWriteNothing()
	{
		await Assert.That(Render(MxpElements.Expire(), MarkupFormat.Html)).IsEqualTo(string.Empty);
		await Assert.That(Render(MxpElements.Relocate("other.example", 4201), MarkupFormat.Html)).IsEqualTo(string.Empty);
		await Assert.That(Render(MxpElements.NoBreak(), MarkupFormat.Html)).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task ABrowserCanBeKeptOutOfMxpEntirely()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml().WithMxp().With(new MxpSilentEmitter(MarkupFormat.Html));
		var line = MarkupText.Concat(
			MxpElements.Image("map.png", url: "https://example.test/map.png"), MarkupText.Plain("a map"));

		await Assert.That(line.Render(MarkupFormat.Html, registry)).IsEqualTo("a map");
	}

	// ── What the client said it can render ──────────────────────────────────────

	/// <summary>
	/// MXP asks with <c>&lt;SUPPORT&gt;</c> for a reason: a client that answered <c>-image</c> should not be
	/// sent one. The set of answers belongs to a connection, so the predicate is the consumer's; refusing
	/// an element writes it the way a format without MXP writes it.
	/// </summary>
	[Test]
	public async Task AnElementTheClientRefusedIsNotWritten()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml()
			.WithMxp(element => !element.Name.Equals("IMAGE", StringComparison.OrdinalIgnoreCase));

		var line = MarkupText.Concat(MxpElements.Image("map.png"), MarkupText.Plain("A map hangs here."));

		await Assert.That(line.Render(MarkupFormat.Mxp, registry)).IsEqualTo("A map hangs here.");
		await Assert.That(MxpElements.Sound("door.wav").Render(MarkupFormat.Mxp, registry)).IsEqualTo("<SOUND door.wav>")
			.Because("only the refused element is held back");
	}

	/// <summary>
	/// The half that matters most: a frame a client cannot open would otherwise take the text inside it
	/// somewhere the player never sees.
	/// </summary>
	[Test]
	public async Task ARefusedWrappingElementKeepsItsContent()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml().WithMxp(_ => false);

		var frame = MxpElements.Frame(MarkupText.Plain("The map is here."), "map");

		await Assert.That(frame.Render(MarkupFormat.Mxp, registry)).IsEqualTo("The map is here.");
	}

	[Test]
	public async Task WithNoAnswersEveryElementIsWritten()
	{
		await Assert.That(Render(MxpElements.Image("map.png"), MarkupFormat.Mxp)).IsEqualTo("<IMAGE map.png>")
			.Because("a client that was never asked is not a client that refused");
	}

	// ── The rest ────────────────────────────────────────────────────────────────

	[Test]
	public async Task AnElementRoundTripsThroughTheSerializer()
	{
		var line = MarkupText.Concat(
			MxpElements.Gauge("hp", "maxhp", "Hit Points"),
			MxpElements.Frame(MarkupText.Plain("Map"), "map"));

		var back = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(line, Registry), Registry);

		await Assert.That(back.Render(MarkupFormat.Mxp, Registry)).IsEqualTo(Render(line, MarkupFormat.Mxp));
	}

	[Test]
	public async Task AnElementNotInTheSpecificationCanStillBeWritten()
	{
		var custom = MxpElement.Standalone("XCUSTOM", new MxpArgument(null, "one"), new MxpArgument("TWO", "2"));

		await Assert.That(Render(custom, MarkupFormat.Mxp)).IsEqualTo("<XCUSTOM one TWO=2>");
		await Assert.That(Render(custom, MarkupFormat.Html)).IsEqualTo(string.Empty);
	}

	[Test]
	[Arguments("has space")]
	[Arguments("")]
	[Arguments("1bad")]
	public async Task AnElementNameIsChecked(string name)
	{
		await Assert.That(() => MxpElement.Standalone(name)).Throws<ArgumentException>();
	}
}
