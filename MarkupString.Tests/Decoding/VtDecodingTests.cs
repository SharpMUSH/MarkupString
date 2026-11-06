using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Display;

public class VtDecodingTests
{
	private static readonly DisplayOptions Options = new();

	private static IReadOnlyList<DisplayLine> Decode(string text, DecoderOptions? options = null) =>
		Decoder.DecodeAll(text, VtDecoding.Terminal, options);

	private static IReadOnlyList<DisplaySpan> Spans(MarkupText text) => SpanRenderer.Render(text, AnsiDisplay.Default, Options);

	private static DisplaySpan Only(string text)
	{
		var lines = Decode(text);
		var spans = Spans(lines[0].Content);
		return spans.Single(span => span.Text.Trim().Length > 0);
	}

	[Test]
	public async Task LineFeeds_SplitLines()
	{
		var lines = Decode("one\ntwo\r\nthree");
		await Assert.That(lines.Select(line => line.Content.ToPlainText())).IsEquivalentTo(["one", "two", "three"]);
		await Assert.That(lines.Select(line => line.Sequence)).IsEquivalentTo([0L, 1L, 2L]);
	}

	[Test]
	public async Task Prompt_EndsWithoutALineFeed()
	{
		var decoder = VtDecoding.Terminal.CreateDecoder();
		await Assert.That(decoder.Feed("Name: ")).IsEmpty();
		var prompt = decoder.EndPrompt();
		await Assert.That(prompt!.Kind).IsEqualTo(LineKind.Prompt);
		await Assert.That(prompt.Content.ToPlainText()).IsEqualTo("Name: ");
		await Assert.That(decoder.EndPrompt()).IsNull();
	}

	[Test]
	public async Task CarriageReturn_OverwritesTheLine()
	{
		var lines = Decode("[    ] 0%\r[##  ] 50%\r[####] 100%\n");
		await Assert.That(lines.Single().Content.ToPlainText()).IsEqualTo("[####] 100%");
	}

	[Test]
	public async Task CarriageReturn_ShorterText_KeepsTheRest()
	{
		var lines = Decode("abcdef\rXY\n");
		await Assert.That(lines.Single().Content.ToPlainText()).IsEqualTo("XYcdef");
	}

	[Test]
	public async Task EraseToEndOfLine_AfterCarriageReturn()
	{
		var lines = Decode("abcdef\rXY\e[K\n");
		await Assert.That(lines.Single().Content.ToPlainText()).IsEqualTo("XY");
	}

	[Test]
	public async Task Backspace_Overwrites()
	{
		var lines = Decode("ab\bc\n");
		await Assert.That(lines.Single().Content.ToPlainText()).IsEqualTo("ac");
	}

	[Test]
	public async Task Tab_PadsToTheNextStop()
	{
		var lines = Decode("ab\tc\n");
		await Assert.That(lines.Single().Content.ToPlainText()).IsEqualTo("ab      c");
	}

	[Test]
	public async Task Overwrite_KeepsStyleOfWhatIsLeft()
	{
		var lines = Decode("\e[31mred\e[0m\rX\n");
		var spans = Spans(lines.Single().Content);
		await Assert.That(spans.Select(span => span.Text)).IsEquivalentTo(["X", "ed"]);
		await Assert.That(spans[1].Style.Foreground).IsEqualTo(new RgbColor(170, 0, 0));
	}

	[Test]
	public async Task Sgr_StandardColour()
	{
		var span = Only("\e[1;32mgo\e[0m\n");
		await Assert.That(span.Style.Foreground).IsEqualTo(new RgbColor(0, 170, 0));
		await Assert.That(span.Style.Has(TextAttributes.Bold)).IsTrue();
	}

	[Test]
	public async Task Sgr_TrueColour_SemicolonsAndColons()
	{
		await Assert.That(Only("\e[38;2;1;2;3mx\n").Style.Foreground).IsEqualTo(new RgbColor(1, 2, 3));
		await Assert.That(Only("\e[38:2::4:5:6mx\n").Style.Foreground).IsEqualTo(new RgbColor(4, 5, 6));
		await Assert.That(Only("\e[48:2:7:8:9mx\n").Style.Background).IsEqualTo(new RgbColor(7, 8, 9));
	}

	[Test]
	public async Task Sgr_Xterm256()
	{
		await Assert.That(Only("\e[38;5;196mx\n").Style.Foreground).IsEqualTo(new RgbColor(255, 0, 0));
	}

	[Test]
	public async Task Sgr_UnderlineStyleSubparameter_IsStillUnderline()
	{
		await Assert.That(Only("\e[4:3mx\n").Style.Has(TextAttributes.Underline)).IsTrue();
		var off = Decode("\e[4m\e[4:0mx\n");
		await Assert.That(Spans(off[0].Content).Single().Style.Has(TextAttributes.Underline)).IsFalse();
	}

	[Test]
	public async Task Sgr_Conceal()
	{
		await Assert.That(Only("\e[8msecret\n").Style.Has(TextAttributes.Conceal)).IsTrue();
	}

	[Test]
	public async Task Sgr_Reset_ClearsEverything()
	{
		var lines = Decode("\e[1;31ma\e[mb\n");
		var spans = Spans(lines[0].Content);
		await Assert.That(spans[1].Text).IsEqualTo("b");
		await Assert.That(spans[1].Style).IsEqualTo(DisplayStyle.Plain);
	}

	[Test]
	public async Task Sgr_RoundTripsToAnsi()
	{
		var lines = Decode("\e[1;31mhi\e[0m there\n");
		var ansi = lines[0].Content.Render(MarkupFormat.Ansi, MarkupRegistry.Empty.WithAnsi());
		await Assert.That(ansi).IsEqualTo("\e[1;31mhi\e[0m there");
	}

	[Test]
	public async Task Sgr_StyleCarriesAcrossLines()
	{
		var lines = Decode("\e[34mone\ntwo\e[0m\n");
		await Assert.That(Spans(lines[1].Content).Single().Style.Foreground).IsEqualTo(new RgbColor(0, 0, 170));
	}

	[Test]
	public async Task Inverse_SwapsColours()
	{
		var style = Only("\e[7;31mx\n").Style;
		await Assert.That(style.Foreground).IsEqualTo(Options.DefaultBackground);
		await Assert.That(style.Background).IsEqualTo(new RgbColor(170, 0, 0));
	}

	[Test]
	public async Task Osc8_MakesALink()
	{
		var lines = Decode("see \e]8;;https://example.com\e\\here\e]8;;\e\\ ok\n");
		var spans = Spans(lines[0].Content);
		await Assert.That(spans.Select(span => span.Text)).IsEquivalentTo(["see ", "here", " ok"]);
		await Assert.That(spans[1].Style.Interaction).IsEqualTo(new Interaction(InteractionKind.Url, "https://example.com"));
		await Assert.That(spans[2].Style.Interaction).IsNull();
	}

	[Test]
	public async Task Osc8_UnsafeScheme_IsText()
	{
		var lines = Decode("\e]8;;javascript:alert(1)\e\\x\e]8;;\e\\\n");
		await Assert.That(Spans(lines[0].Content).Single().Style.Interaction).IsNull();
	}

	[Test]
	public async Task Mslp_LinkLastsAsLongAsTheUnderline()
	{
		var lines = Decode("\e]68;1;SEND;look\a\e[4mlook\e[24m here\n");
		var spans = Spans(lines[0].Content);
		await Assert.That(spans[0].Text).IsEqualTo("look");
		await Assert.That(spans[0].Style.Interaction).IsEqualTo(new Interaction(InteractionKind.Command, "look"));
		await Assert.That(spans[1].Style.Interaction).IsNull();
	}

	[Test]
	public async Task Mslp_RoundTripsThroughTheAnsiWriter()
	{
		var link = MarkupText.Wrap(new AnsiMarkup(new AnsiStyle { LinkUrl = "look", LinkKind = LinkKind.Command }), "look");
		var registry = MarkupRegistry.Empty.WithAnsiOutput(new AnsiOutputOptions { Features = TerminalFeatures.CommandLinks });
		var wire = link.Render(MarkupFormat.Ansi, registry);
		var spans = Spans(Decode(wire + "\n")[0].Content);
		await Assert.That(spans.Single().Style.Interaction).IsEqualTo(new Interaction(InteractionKind.Command, "look"));
	}

	[Test]
	public async Task DecSpecialGraphics_DrawsBoxes()
	{
		var lines = Decode("\e(0lqk\e(B ok\n");
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("┌─┐ ok");
	}

	[Test]
	public async Task ShiftOut_UsesG1()
	{
		var lines = Decode("\e)0a\u000eqx\u000fq\n");
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("a─│q");
	}

	[Test]
	public async Task Bell_IsAPoint()
	{
		var lines = Decode("ding\a\n");
		var spans = Spans(lines[0].Content);
		await Assert.That(spans[^1].Point).IsSameReferenceAs(BellMarkup.Instance);
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("ding");
	}

	[Test]
	public async Task Title_Palette_Clipboard_AreEvents()
	{
		var decoder = VtDecoding.Terminal.CreateDecoder();
		var events = new List<DecodeEvent>();
		decoder.EventRaised += events.Add;
		decoder.Feed("\e]2;The Game\a\e]4;1;rgb:ff/00/80\a\e]11;#102030\a\e]104\a\e]52;c;aGk=\a");
		await Assert.That(events).IsEquivalentTo(new DecodeEvent[]
		{
			new TitleEvent("The Game", false),
			new PaletteEvent(PaletteSlot.Index, 1, new RgbColor(255, 0, 128)),
			new PaletteEvent(PaletteSlot.Background, 0, new RgbColor(16, 32, 48)),
			new PaletteEvent(PaletteSlot.Index, -1, null),
			new ClipboardEvent("c", "hi"),
		});
	}

	[Test]
	public async Task ColorSpec_ScalesChannels()
	{
		await Assert.That(OscPlugin.ParseColor("rgb:f/8/0")).IsEqualTo(new RgbColor(255, 136, 0));
		await Assert.That(OscPlugin.ParseColor("rgb:ffff/0000/8080")).IsEqualTo(new RgbColor(255, 0, 128));
		await Assert.That(OscPlugin.ParseColor("#fff")).IsEqualTo(new RgbColor(255, 255, 255));
		await Assert.That(OscPlugin.ParseColor("red")).IsNull();
	}

	[Test]
	public async Task Answers_FromTheIdentity()
	{
		var identity = new ClientIdentity { Name = "TestClient", Version = "2.1", Columns = 100, Rows = 30, Foreground = new RgbColor(1, 2, 3) };
		var decoder = VtDecoding.Terminal.CreateDecoder(new DecoderOptions { Identity = identity });
		var replies = new List<string>();
		decoder.EventRaised += e => { if (e is ReplyEvent reply) replies.Add(reply.Text); };
		decoder.Feed("\e[c\e[>q\e[18t\e[16t\e]10;?\a\e[6n");
		await Assert.That(replies).IsEquivalentTo([
			"\e[?62;22c",
			"\eP>|TestClient 2.1\e\\",
			"\e[8;30;100t",
			"\e[6;16;8t",
			"\e]10;rgb:0101/0202/0303\e\\",
		]);
	}

	[Test]
	public async Task Answers_CursorPosition_WhenAllowed()
	{
		var identity = new ClientIdentity { Answers = ClientAnswers.Default | ClientAnswers.CursorPosition };
		var decoder = VtDecoding.Terminal.CreateDecoder(new DecoderOptions { Identity = identity });
		var replies = new List<string>();
		decoder.EventRaised += e => { if (e is ReplyEvent reply) replies.Add(reply.Text); };
		decoder.Feed("abc\e[6n");
		await Assert.That(replies).IsEquivalentTo(["\e[24;4R"]);
	}

	[Test]
	public async Task Answers_None_WhenTurnedOff()
	{
		var decoder = VtDecoding.Terminal.CreateDecoder(new DecoderOptions { Identity = new ClientIdentity { Answers = ClientAnswers.None } });
		var replies = 0;
		decoder.EventRaised += e => { if (e is ReplyEvent) replies++; };
		decoder.Feed("\e[c\e[>q\e[18t");
		await Assert.That(replies).IsEqualTo(0);
		await Assert.That(decoder.Diagnostics.UnhandledSequences).IsEqualTo(0);
	}

	[Test]
	public async Task Unknown_IsCounted()
	{
		var decoder = VtDecoding.Terminal.CreateDecoder();
		decoder.Feed("\e[5;5H\e]777;x\a");
		await Assert.That(decoder.Diagnostics.UnhandledSequences).IsEqualTo(2);
	}

	[Test]
	public async Task LineLength_BreaksTheLine()
	{
		var decoder = VtDecoding.Terminal.CreateDecoder(new DecoderOptions { MaxLineLength = 4 });
		var lines = new List<DisplayLine>(decoder.Feed("abc"));
		lines.AddRange(decoder.Feed("defghij\n"));
		await Assert.That(lines.Select(line => line.Content.ToPlainText())).IsEquivalentTo(["abcd", "efgh", "ij"]);
		await Assert.That(decoder.Diagnostics.LimitsReached).IsEqualTo(2);
	}

	[Test]
	public async Task Overwrite_InsideAWideCharacter_BlanksIt()
	{
		await Assert.That(Decode("漢字\rX\n")[0].Content.ToPlainText()).IsEqualTo("X 字");
		await Assert.That(Decode("漢字\e[2GX\n")[0].Content.ToPlainText()).IsEqualTo(" X字");
		await Assert.That(Decode("ab漢\e[2G字\n")[0].Content.ToPlainText()).IsEqualTo("a字 ");
	}

	[Test]
	public async Task Mslp_WithoutUnderline_EndsWithTheLine()
	{
		var lines = Decode("\e]68;1;SEND;look\alook\nmore\n");
		await Assert.That(Spans(lines[0].Content).Single().Style.Interaction).IsNotNull();
		await Assert.That(Spans(lines[1].Content).Single().Style.Interaction).IsNull();
	}

	[Test]
	public async Task ClearScreen_IsAPoint()
	{
		var lines = Decode("\e[2Jtop\n");
		await Assert.That(lines[0].Content.Runs[0].Markups.Innermost).IsSameReferenceAs(ClearScreenMarkup.Instance);
	}

	[Test]
	public async Task Capabilities_FromAProfile()
	{
		var capabilities = TerminalProfile.Kitty.ToOutputCapabilities(AnsiColorDepth.TrueColor);
		await Assert.That(capabilities.Pictures).IsEqualTo(PictureMethods.KittyPlaceholders | PictureMethods.HalfBlocks);
		await Assert.That(capabilities.Interactions).IsEqualTo(InteractionKinds.Url);
		await Assert.That(capabilities.ToTerminalFeatures() & TerminalFeatures.Pictures).IsEqualTo(TerminalFeatures.KittyGraphics | TerminalFeatures.BlockArt);
	}
}
