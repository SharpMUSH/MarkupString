using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Mxp;
using MarkupString.Pueblo;
namespace MarkupString.Tests;

/// <summary>
/// The shared vocabulary: a game puts a sound, a picture or a pane in its text once, and every format
/// writes it in its own dialect or stands something else in for it.
/// </summary>
/// <remarks>
/// MXP syntax from the <see href="https://www.zuggsoft.com/zmud/mxp.htm">MXP specification</see>; Pueblo's
/// from its client (<see href="https://github.com/uecasm/pueblo">uecasm/pueblo</see>: <c>ChSound.cpp</c>,
/// <c>ChPaneTag.cpp</c>, <c>ChHtmlPane.cpp</c>, <c>ChHeadElement.cpp</c>).
/// </remarks>
public class SharedVocabularyTests
{
	private static readonly MarkupRegistry Registry =
		MarkupRegistry.Empty.WithAnsi().WithHtml().WithMxp().WithPueblo();

	private static string Render(MarkupText text, MarkupFormat format) => text.Render(format, Registry);

	private static readonly MarkupText Creaks = MarkupText.Plain("The door creaks.");

	// ── One object, every format ───────────────────────────────────────────────

	[Test]
	public async Task ASoundIsWrittenOnceForEveryFormatThatHasSound()
	{
		var line = MarkupText.Concat(MarkupText.Sound("door.wav", volume: 80), Creaks);

		await Assert.That(Render(line, MarkupFormat.Mxp)).IsEqualTo("<SOUND door.wav V=80>The door creaks.");
		await Assert.That(Render(line, MarkupFormat.Pueblo))
			.IsEqualTo("<img xch_sound=\"play\" href=\"door.wav\" xch_volume=\"80\">The door creaks.");
		await Assert.That(Render(line, MarkupFormat.Html)).IsEqualTo(
			"<audio class=\"ms-sound\" data-channel=\"effects\" src=\"door.wav\" preload=\"none\" data-volume=\"80\"></audio>The door creaks.");
	}

	[Test]
	[Arguments("ansi")]
	[Arguments("plain")]
	[Arguments("bbcode")]
	public async Task AFormatWithNoSoundWritesNothing_CarrierIncluded(string format)
	{
		var line = MarkupText.Concat(MarkupText.Sound("door.wav"), Creaks);

		await Assert.That(Render(line, MarkupFormat.TryParse(format)!)).IsEqualTo("The door creaks.");
	}

	[Test]
	public async Task MusicThatLoopsLoopsEverywhere()
	{
		var music = MarkupText.Music("theme.mid", repeats: SoundMarkup.Forever, continues: true);

		await Assert.That(Render(music, MarkupFormat.Mxp)).IsEqualTo("<MUSIC theme.mid L=-1 C=1>");
		await Assert.That(Render(music, MarkupFormat.Pueblo)).IsEqualTo("<img xch_sound=\"loop\" href=\"theme.mid\">");
		await Assert.That(Render(music, MarkupFormat.Html)).IsEqualTo(
			"<audio class=\"ms-sound\" data-channel=\"music\" src=\"theme.mid\" preload=\"none\" loop=\"\" data-continues=\"true\"></audio>");
	}

	/// <summary>MXP names the file and, separately, where to download it from.</summary>
	[Test]
	public async Task MxpSplitsAnAddressIntoAFileAndWhereToFetchIt()
	{
		await Assert.That(Render(MarkupText.Sound("https://example.test/sounds/door.wav"), MarkupFormat.Mxp))
			.IsEqualTo("<SOUND door.wav U=https://example.test/sounds/>");
		await Assert.That(Render(MarkupText.Image("https://example.test/map.png"), MarkupFormat.Mxp))
			.IsEqualTo("<IMAGE map.png URL=https://example.test/>");
	}

	[Test]
	public async Task StoppingASoundNamesTheChannel()
	{
		await Assert.That(Render(MarkupText.StopSound(SoundChannel.Music), MarkupFormat.Mxp)).IsEqualTo("<MUSIC Off>");
		await Assert.That(Render(MarkupText.StopSound(), MarkupFormat.Mxp)).IsEqualTo("<SOUND Off><MUSIC Off>");
		await Assert.That(Render(MarkupText.StopSound(SoundChannel.Effects), MarkupFormat.Pueblo))
			.IsEqualTo("<img xch_sound=\"stop\" xch_device=\"wave\">");
		await Assert.That(Render(MarkupText.StopSound(), MarkupFormat.Pueblo)).IsEqualTo("<img xch_sound=\"stop\">");
		await Assert.That(Render(MarkupText.StopSound(SoundChannel.Music), MarkupFormat.Html))
			.IsEqualTo("<span class=\"ms-sound-stop\" data-channel=\"music\"></span>");
	}

	// ── A picture degrades to words ─────────────────────────────────────────────

	[Test]
	public async Task APictureIsShownWhereItCanBe_AndDescribedWhereItCannot()
	{
		var map = MarkupText.Image("map.png", "A map of the city", width: 200);

		await Assert.That(Render(map, MarkupFormat.Mxp)).IsEqualTo("<IMAGE map.png W=200>");
		await Assert.That(Render(map, MarkupFormat.Pueblo)).IsEqualTo("<img src=\"map.png\" alt=\"A map of the city\" width=\"200\">");
		await Assert.That(Render(map, MarkupFormat.Html))
			.IsEqualTo("<img class=\"ms-image\" src=\"map.png\" alt=\"A map of the city\" width=\"200\">");
		await Assert.That(Render(map, MarkupFormat.BBCode)).IsEqualTo("[img]map.png[/img]");
		await Assert.That(Render(map, MarkupFormat.Ansi)).IsEqualTo("A map of the city");
		await Assert.That(Render(map, MarkupFormat.Plain)).IsEqualTo("A map of the city");
	}

	[Test]
	public async Task APictureWithNoDescriptionLeavesItsAddress()
	{
		var map = MarkupText.Image("https://example.test/map.png");

		await Assert.That(Render(map, MarkupFormat.Ansi)).IsEqualTo("https://example.test/map.png");
		await Assert.That(map.ToPlainText()).IsEqualTo("https://example.test/map.png");
	}

	/// <summary>A link is its own markup, so a picture inside one is a picture that is a link, in every dialect.</summary>
	[Test]
	public async Task APictureInsideALinkIsALink()
	{
		var map = MarkupText.Wrap(
			AnsiMarkup.Create(linkUrl: "look map", linkKind: LinkKind.Command),
			MarkupText.Image("map.png", "A map"));

		await Assert.That(Render(map, MarkupFormat.Pueblo)).IsEqualTo("<A XCH_CMD=\"look map\"><img src=\"map.png\" alt=\"A map\"></A>");
		await Assert.That(Render(map, MarkupFormat.Mxp)).IsEqualTo("<SEND HREF=\"look map\"><IMAGE map.png></SEND>");
	}

	// ── A pane keeps its text ────────────────────────────────────────────────────

	[Test]
	public async Task APaneTakesItsTextToThePane_OrLeavesItWhereItIs()
	{
		var pane = MarkupText.Pane(MarkupText.Plain("North: the gate"), "map", "The Map");

		await Assert.That(Render(pane, MarkupFormat.Mxp))
			.IsEqualTo("<FRAME map TITLE=\"The Map\"><DEST map>North: the gate</DEST>");
		await Assert.That(Render(pane, MarkupFormat.Pueblo)).IsEqualTo(
			"<xch_pane action=\"redirect\" name=\"map\" panetitle=\"The Map\">North: the gate<xch_pane action=\"redirect\" name=\"_previous\">");
		await Assert.That(Render(pane, MarkupFormat.Html))
			.IsEqualTo("<span class=\"ms-pane\" data-pane=\"map\" data-title=\"The Map\">North: the gate</span>");
		await Assert.That(Render(pane, MarkupFormat.Ansi)).IsEqualTo("North: the gate");
	}

	// ── What only some formats have ─────────────────────────────────────────────

	[Test]
	public async Task ClearingTheScreenIsEachFormatsOwnInstruction()
	{
		var clear = MarkupText.ClearScreen();

		await Assert.That(Render(clear, MarkupFormat.Ansi)).IsEqualTo("\e[H\e[2J");
		await Assert.That(Render(clear, MarkupFormat.Pueblo)).IsEqualTo("<xch_page clear=\"text\">");
		await Assert.That(Render(clear, MarkupFormat.Html)).IsEqualTo("<span class=\"ms-clear\"></span>");
		await Assert.That(Render(clear, MarkupFormat.Mxp)).IsEqualTo(string.Empty).Because("MXP has no such instruction");
		await Assert.That(Render(clear, MarkupFormat.Plain)).IsEqualTo(string.Empty);
	}

	/// <summary>Pueblo ignores a prefetch that carries no likelihood of being used.</summary>
	[Test]
	public async Task APrefetchIsWrittenWhereAClientCanFetchAhead()
	{
		var prefetch = MarkupText.Prefetch("https://example.test/map.png");

		await Assert.That(Render(prefetch, MarkupFormat.Pueblo))
			.IsEqualTo("<xch_prefetch href=\"https://example.test/map.png\" xch_prob=\"100\">");
		await Assert.That(Render(prefetch, MarkupFormat.Html)).IsEqualTo("<link rel=\"prefetch\" href=\"https://example.test/map.png\">");
		await Assert.That(Render(prefetch, MarkupFormat.Mxp)).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task WhatOnlyMxpHasIsWrittenForMxpAndNothingElse()
	{
		var points = MarkupText.Concat([
			MarkupText.ExpireLinks("exits"),
			MarkupText.Relocate("other.example", 4201, quiet: true),
			MarkupText.LoginPrompt(LoginField.User),
			MarkupText.LoginPrompt(LoginField.Password)]);

		await Assert.That(Render(points, MarkupFormat.Mxp))
			.IsEqualTo("<EXPIRE exits><RELOCATE other.example 4201 QUIET><USER><PASSWORD>");
		await Assert.That(Render(points, MarkupFormat.Pueblo)).IsEqualTo(string.Empty);
		await Assert.That(Render(points, MarkupFormat.Ansi)).IsEqualTo(string.Empty);
		await Assert.That(Render(points, MarkupFormat.Html)).IsEqualTo("<span class=\"ms-expire\" data-group=\"exits\"></span>");
	}

	[Test]
	public async Task VariablesAndGaugesLeaveTheirTextWhereTheyAreNotUnderstood()
	{
		var hp = MarkupText.Variable(MarkupText.Plain("42"), "hp");
		var gauge = MarkupText.Gauge(MarkupText.Plain("HP 42/50"), "hp", "maxhp", "Hit Points", "red");
		var status = MarkupText.Status(MarkupText.Plain("Mana 7"), "mana", caption: "Mana");

		await Assert.That(Render(hp, MarkupFormat.Mxp)).IsEqualTo("<VAR hp>42</VAR>");
		await Assert.That(Render(gauge, MarkupFormat.Mxp)).IsEqualTo("<GAUGE hp MAX=maxhp CAPTION=\"Hit Points\" COLOR=red>");
		await Assert.That(Render(status, MarkupFormat.Mxp)).IsEqualTo("<STAT mana CAPTION=Mana>");
		await Assert.That(Render(gauge, MarkupFormat.Html)).IsEqualTo(
			"<span class=\"ms-gauge\" data-variable=\"hp\" data-maximum=\"maxhp\" data-caption=\"Hit Points\" data-color=\"red\">HP 42/50</span>");

		foreach (var format in new[] { MarkupFormat.Ansi, MarkupFormat.Pueblo, MarkupFormat.Plain })
		{
			await Assert.That(Render(MarkupText.Concat([hp, gauge, status]), format)).IsEqualTo("42HP 42/50Mana 7");
		}
	}

	// ── MXP holds each element to the client's answer ────────────────────────────

	[Test]
	public async Task MxpWritesWhatTheClientRefusedAsIfItHadNoMxp()
	{
		var refusing = MarkupRegistry.Empty.WithAnsi().WithHtml()
			.WithMxp(element => element is not ("IMAGE" or "SOUND" or "FRAME"));

		var line = MarkupText.Concat([
			MarkupText.Sound("door.wav"),
			MarkupText.Image("map.png", "A map"),
			MarkupText.Pane(MarkupText.Plain(" to the north"), "map"),
			MarkupText.Music("theme.mid")]);

		await Assert.That(line.Render(MarkupFormat.Mxp, refusing)).IsEqualTo("A map to the north<MUSIC theme.mid>");
	}

	[Test]
	public async Task TheElementsToAskAboutAreTheOnesWritten()
	{
		await Assert.That(MxpRegistration.Elements).Contains("IMAGE");
		await Assert.That(MxpRegistration.Elements).Contains("DEST");
	}

	// ── An HTML policy reaches these elements too ───────────────────────────────

	/// <summary>
	/// <c>WithHtml(policy)</c> says every tag rendered for a browser is held to it. These elements are
	/// tags, so they are held to it too: one the policy refuses is written the way a format that cannot
	/// express it writes it.
	/// </summary>
	[Test]
	public async Task AnHtmlPolicyHoldsTheseElementsToo()
	{
		var policy = HtmlTagPolicy.WellFormed with
		{
			AllowedTags = new HashSet<string> { "span" },
			UrlAttributes = HtmlTagPolicy.AddressAttributes,
		};
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml(policy);

		var line = MarkupText.Concat([
			MarkupText.Sound("door.wav"),
			MarkupText.Image("map.png", "A map"),
			MarkupText.Pane(MarkupText.Plain("gate"), "map")]);

		await Assert.That(line.Render(MarkupFormat.Html, registry))
			.IsEqualTo("A map<span class=\"ms-pane\" data-pane=\"map\">gate</span>")
			.Because("audio and img are not allowed tags: the sound goes, the picture leaves its description");
	}

	[Test]
	public async Task AnHtmlPolicyCanRefuseAnAddressWithoutRefusingTheElement()
	{
		var policy = HtmlTagPolicy.WellFormed with { UrlAttributes = HtmlTagPolicy.AddressAttributes };
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml(policy);

		await Assert.That(MarkupText.Image("javascript:alert(1)", "A map").Render(MarkupFormat.Html, registry))
			.IsEqualTo("<img class=\"ms-image\" alt=\"A map\">");
	}

	// ── A point is not text ─────────────────────────────────────────────────────

	[Test]
	public async Task APointIsLeftOutOfThePlainText_AndOutOfEquality()
	{
		var line = MarkupText.Concat([MarkupText.Sound("door.wav"), Creaks, MarkupText.ClearScreen()]);

		await Assert.That(line.ToPlainText()).IsEqualTo("The door creaks.");
		await Assert.That(line.ToString()).IsEqualTo("The door creaks.");
		await Assert.That(line).IsEqualTo(Creaks);
		await Assert.That(line.GetHashCode()).IsEqualTo(Creaks.GetHashCode());
		await Assert.That(DisplayWidth.Of(line.Text)).IsEqualTo(16);
	}

	[Test]
	public async Task APointInsideStylingLeavesNoStyledNothingBehind()
	{
		var line = MarkupText.Concat([
			MarkupText.Wrap(AnsiMarkup.Create(bold: true), MarkupText.Sound("door.wav")),
			MarkupText.Plain("x")]);

		await Assert.That(Render(line, MarkupFormat.Ansi)).IsEqualTo("x");
		await Assert.That(Render(line, MarkupFormat.Mxp)).Contains("<SOUND door.wav>");
	}

	[Test]
	public async Task TwoOfTheSamePointSideBySideAreTwoPoints()
	{
		var twice = MarkupText.Concat(MarkupText.Sound("door.wav"), MarkupText.Sound("door.wav"));

		await Assert.That(twice.Runs.Length).IsEqualTo(1).Because("equal markup side by side coalesces");
		await Assert.That(Render(twice, MarkupFormat.Mxp)).IsEqualTo("<SOUND door.wav><SOUND door.wav>");
	}

	[Test]
	public async Task AValueIsQuotedWhenMxpWouldOtherwiseSplitIt()
	{
		var status = MarkupText.Status(MarkupText.Plain("x"), "hp", caption: "He said \"hi\" <loudly>");

		await Assert.That(Render(status, MarkupFormat.Mxp)).IsEqualTo("<STAT hp CAPTION=\"He said &quot;hi&quot; &lt;loudly&gt;\">");
	}

	// ── A point marks its carrier and nothing else ──────────────────────────────

	/// <summary>
	/// Without this, a point could stand over words: the emitter writes the point and ignores the body,
	/// so the text under it would be swallowed, and a point over an astral character would fire twice.
	/// </summary>
	[Test]
	public async Task APointCannotBeWrappedAroundText()
	{
		await Assert.That(() => MarkupText.Wrap(new SoundMarkup("door.wav"), "The door creaks."))
			.Throws<ArgumentException>();
		await Assert.That(() => MarkupText.Wrap(new SoundMarkup("door.wav"), "\U0001F600")).Throws<ArgumentException>();
		await Assert.That(() => MarkupText.Wrap(BellMarkup.Instance, MarkupText.Plain("x"))).Throws<ArgumentException>();
		await Assert.That(() => MarkupText.Wrap(
			MarkupSet.Of([new SoundMarkup("door.wav"), AnsiMarkup.Create(bold: true)]), "loud")).Throws<ArgumentException>();
	}

	[Test]
	public async Task APointWrappedInStylingIsStillAPoint()
	{
		var styled = MarkupText.Wrap(AnsiMarkup.Create(bold: true), MarkupText.Sound("door.wav"));

		await Assert.That(Render(styled, MarkupFormat.Mxp)).Contains("<SOUND door.wav>");
		await Assert.That(styled.ToPlainText()).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// Two points on one carrier cannot be built, but a cover read back from somewhere else can hold
	/// them. Each is written, and neither swallows the other.
	/// </summary>
	[Test]
	public async Task EveryPointOnACarrierIsWritten()
	{
		var two = MarkupText.Wrap(
			MarkupSet.Of([new SoundMarkup("door.wav"), new SoundMarkup("bell.wav")]), MarkupText.PointCarrier);

		await Assert.That(Render(two, MarkupFormat.Mxp)).IsEqualTo("<SOUND door.wav><SOUND bell.wav>");
		await Assert.That(two.ToPlainText()).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// A cover that puts a point over real text is not a shape any format can render: the point is
	/// dropped and the text kept, rather than the text disappearing under it.
	/// </summary>
	[Test]
	public async Task AMisplacedPointIsDroppedAndItsTextKept()
	{
		var json = MarkupTextSerializer.Serialize(
			MarkupText.Concat([MarkupText.Sound("door.wav"), Creaks]), MarkupRegistry.Empty);

		// The cover, rewritten so the sound covers the sentence instead of its carrier.
		var moved = json.Replace("\"r\":[1,1,16,0]", "\"r\":[1,0,16,1]");

		var back = MarkupTextSerializer.Deserialize(moved, MarkupRegistry.Empty);

		await Assert.That(back.ToPlainText()).EndsWith("The door creaks.");
		await Assert.That(back.Runs.Any(run => run.Markups.Any(markup => markup is IPointMarkup))).IsFalse();
		await Assert.That(Render(back, MarkupFormat.Mxp)).EndsWith("The door creaks.")
			.Because("the sentence is kept; only the point that could not be written where it sat is gone");
	}

	// ── Line endings ────────────────────────────────────────────────────────────

	/// <summary>
	/// A Pueblo client renders the stream as HTML, where a newline is whitespace. PennMUSH's
	/// <c>queue_eol</c> writes <c>&lt;BR&gt;\n</c> in HTML mode, and so does this.
	/// </summary>
	[Test]
	public async Task PuebloEndsALineTheWayAnHtmlClientReadsOne()
	{
		var lines = MarkupText.Plain("north\nsouth\n");

		await Assert.That(Render(lines, MarkupFormat.Pueblo)).IsEqualTo("north<BR>\nsouth<BR>\n");
	}

	[Test]
	public async Task ABlankLineIsItsOwnBreak()
	{
		await Assert.That(Render(MarkupText.Plain("a\n\nb"), MarkupFormat.Pueblo)).IsEqualTo("a<BR>\n<BR>\nb")
			.Because("a blank line the player was meant to see is a break like any other");
	}

	[Test]
	public async Task ACarriageReturnGoesWithTheNewlineItBelongedTo()
	{
		await Assert.That(Render(MarkupText.Plain("a\r\nb"), MarkupFormat.Pueblo)).IsEqualTo("a<BR>\nb");
		await Assert.That(Render(MarkupText.Plain("a\rb"), MarkupFormat.Pueblo)).IsEqualTo("a<BR>\nb");
	}

	[Test]
	public async Task TextWithNoEndingGetsNoBreak()
	{
		await Assert.That(Render(MarkupText.Plain("north"), MarkupFormat.Pueblo)).IsEqualTo("north")
			.Because("a fragment has not ended a line, so rendering pieces and joining them adds nothing");
	}

	[Test]
	public async Task ALineInsideMarkupEndsTheSameWay()
	{
		var styled = MarkupText.Wrap(AnsiMarkup.Create(bold: true), MarkupText.Plain("north\nsouth"));

		await Assert.That(Render(styled, MarkupFormat.Pueblo)).Contains("north<BR>\nsouth");
	}

	/// <summary>
	/// The other two formats that encode as HTML keep their newlines. A page decides its own line
	/// handling in its stylesheet, and an MXP client is line-oriented and reads a newline as a break.
	/// </summary>
	[Test]
	[Arguments("html")]
	[Arguments("mxp")]
	[Arguments("ansi")]
	[Arguments("plain")]
	public async Task EveryOtherFormatLeavesTheNewlineAlone(string format)
	{
		await Assert.That(Render(MarkupText.Plain("north\nsouth"), MarkupFormat.TryParse(format)!))
			.IsEqualTo("north\nsouth");
	}

	// ── Storage ──────────────────────────────────────────────────────────────────

	/// <summary>The vocabulary is core's, so text carrying it round-trips with no package registered at all.</summary>
	[Test]
	public async Task TheVocabularyRoundTripsWithNoPackageRegistered()
	{
		var line = MarkupText.Concat([
			MarkupText.Sound("door.wav", 80, 2),
			MarkupText.Music("theme.mid", repeats: SoundMarkup.Forever, continues: true),
			MarkupText.StopSound(SoundChannel.Effects),
			MarkupText.Image("map.png", "A map", 200, 100, ImageAlign.Left),
			MarkupText.Pane(MarkupText.Plain("p"), "map", "The Map"),
			MarkupText.ClearScreen(),
			MarkupText.ExpireLinks("exits"),
			MarkupText.Prefetch("x.png"),
			MarkupText.LoginPrompt(LoginField.Password),
			MarkupText.Relocate("other.example", 4201, true),
			MarkupText.Variable(MarkupText.Plain("42"), "hp"),
			MarkupText.Gauge(MarkupText.Plain("g"), "hp", "maxhp", "HP", "red"),
			MarkupText.Status(MarkupText.Plain("s"), "mana", "maxmana", "Mana"),
			MarkupText.Bell()]);

		var back = MarkupTextSerializer.Deserialize(
			MarkupTextSerializer.Serialize(line, MarkupRegistry.Empty), MarkupRegistry.Empty);

		await Assert.That(back.Runs.SequenceEqual(line.Runs)).IsTrue();
		foreach (var format in new[] { MarkupFormat.Mxp, MarkupFormat.Pueblo, MarkupFormat.Html, MarkupFormat.Ansi })
		{
			await Assert.That(Render(back, format)).IsEqualTo(Render(line, format));
		}
	}

	// ── Arguments ────────────────────────────────────────────────────────────────

	[Test]
	public async Task OutOfRangeArgumentsAreRefused()
	{
		await Assert.That(() => MarkupText.Sound("")).Throws<ArgumentException>();
		await Assert.That(() => MarkupText.Sound("a.wav", volume: 101)).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => MarkupText.Sound("a.wav", repeats: 0)).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => MarkupText.Image("a.png", width: 0)).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => MarkupText.Relocate("h", 70000)).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => MarkupText.Pane(MarkupText.Plain("x"), "")).Throws<ArgumentException>();
	}
}
