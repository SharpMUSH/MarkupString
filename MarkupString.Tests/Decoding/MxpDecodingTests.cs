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

		// The opener, and the close of a secure tag nothing opened.
		await Assert.That(decoder.Diagnostics.UnhandledSequences).IsEqualTo(2);
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
	public async Task Version_ReportsTheStyleVersionTheServerSet()
	{
		var options = new DecoderOptions { Identity = new ClientIdentity { Name = "Test", Version = "3" } };
		var (_, events, _) = Decode("\e[1z<VERSION 2.1>\e[1z<VERSION>", options);
		await Assert.That(events.OfType<ReplyEvent>().Single().Text)
			.IsEqualTo("\e[1z<VERSION MXP=1.0 STYLE=\"2.1\" CLIENT=\"Test\" VERSION=\"3\">\n");
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
	public async Task Image_TypeIsADirectoryUnderTheUrl()
	{
		var (lines, _, _) = Decode("\e[1z<IMAGE map.png URL=\"https://example.test/img/\" T=maps>\n\e[1z<IMAGE map.png URL=https://example.test/img T=/maps/>\n\e[1z<SOUND door.wav U=https://example.test/ T=doors>\n");
		await Assert.That(Markups(lines[0]).OfType<ImageMarkup>().Single().Source).IsEqualTo("https://example.test/img/maps/map.png");
		await Assert.That(Markups(lines[1]).OfType<ImageMarkup>().Single().Source).IsEqualTo("https://example.test/img/maps/map.png");
		await Assert.That(Markups(lines[2]).OfType<SoundMarkup>().Single().Source).IsEqualTo("https://example.test/doors/door.wav");
	}

	[Test]
	public async Task Image_TypeWithoutUrlLeavesTheName()
	{
		var (lines, _, _) = Decode("\e[1z<IMAGE https://example.test/x.png T=maps>\n");
		await Assert.That(Markups(lines[0]).OfType<ImageMarkup>().Single().Source).IsEqualTo("https://example.test/x.png");
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

	[Test]
	public async Task Gauge_OnASecureLine_RaisesAnEvent()
	{
		var (lines, events, _) = Decode("\e[1z<GAUGE hp Max=maxhp Caption=Health Color=red><STAT xp>\n");
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo(string.Empty);
		await Assert.That(events.OfType<GaugeEvent>()).IsEquivalentTo([
			new GaugeEvent("hp", "maxhp", "Health", "red", Status: false),
			new GaugeEvent("xp", null, null, null, Status: true),
		]);
	}

	[Test]
	public async Task Gauge_OnAnOpenLine_IsDropped()
	{
		var (_, events, _) = Decode("<GAUGE hp>\n");
		await Assert.That(events.OfType<GaugeEvent>()).IsEmpty();
	}

	[Test]
	public async Task FrameRedirect_SendsLinesToTheFrameUntilTop()
	{
		var (lines, _, _) = Decode("\e[1z<FRAME chat ACTION=REDIRECT>\none\ntwo\n\e[1z<FRAME _top ACTION=REDIRECT>\nthree\n");
		var shown = lines.Where(line => !line.Content.ToPlainText().Equals(string.Empty)).Select(line => (line.Content.ToPlainText(), line.Destination));
		await Assert.That(shown).IsEquivalentTo([("one", "chat"), ("two", "chat"), ("three", (string?)null)]);
	}

	[Test]
	public async Task FrameClose_EndsARedirectToIt()
	{
		var (lines, _, _) = Decode("\e[1z<FRAME chat REDIRECT>\none\n\e[1z<FRAME chat ACTION=CLOSE>\ntwo\n");
		await Assert.That(lines.Single(line => line.Content.ToPlainText() == "two").Destination).IsNull();
	}

	[Test]
	public async Task Sound_CarriesItsPriorityAndType()
	{
		var (lines, _, _) = Decode("\e[1z<SOUND hit.wav V=50 P=70 T=combat U=https://example.test/sounds>\n");
		var sound = Markups(lines[0]).OfType<SoundMarkup>().Single();
		await Assert.That(sound.Source).IsEqualTo("https://example.test/sounds/combat/hit.wav");
		await Assert.That(sound.Priority).IsEqualTo(70);
		await Assert.That(sound.Type).IsEqualTo("combat");
	}

	[Test]
	public async Task Sound_PriorityAndType_ReadBackFromWhatThisPackageWrites()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithMxp().WithMxpSecureLines();
		var sound = new SoundMarkup("https://example.test/sounds/combat/hit.wav", Volume: 50) { Priority = 70, Type = "combat" };
		var wire = MarkupText.Point(sound).Render(MarkupFormat.Mxp, registry);

		var (lines, _, _) = Decode(wire + "\n");
		await Assert.That(Markups(lines.Single()).OfType<SoundMarkup>().Single()).IsEqualTo(sound);
	}

	[Test]
	public async Task FrameRedirect_EndsWhenMxpIsTurnedOff()
	{
		var decoder = Registry.CreateDecoder();
		decoder.EnableProtocol(MxpDecoding.Protocol);
		var lines = new List<DisplayLine>(decoder.Feed("\e[1z<FRAME chat REDIRECT>\none\n"));
		decoder.DisableProtocol(MxpDecoding.Protocol);
		lines.AddRange(decoder.Feed("two\n"));
		await Assert.That(lines.Single(line => line.Content.ToPlainText() == "one").Destination).IsEqualTo("chat");
		await Assert.That(lines.Single(line => line.Content.ToPlainText() == "two").Destination).IsNull();
	}

	[Test]
	public async Task Var_KeepsTheEntityTableBounded()
	{
		var tooLong = new string('x', MxpPlugin.MaxDefinitionLength + 1);
		var (_, events, decoder) = Decode($"\e[1z<V short>ok</V>\e[1z<V long>{tooLong}</V>\n");
		await Assert.That(events.OfType<VariableEvent>().Select(e => e.Name)).IsEquivalentTo(["short"]);
		await Assert.That(decoder.Diagnostics.LimitsReached).IsGreaterThan(0);
	}

	[Test]
	public async Task Image_InCells_TooLargeGoesInlineInsteadOfThrowing()
	{
		var (lines, _, _) = Decode("x\e[1z<IMAGE cat.png URL=\"https://example.test/\" W=2147483647c H=1c>y\n");
		var image = Markups(lines[0]).OfType<ImageMarkup>().Single();
		await Assert.That(image.Row).IsNull();
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("xcat.pngy");
	}

	[Test]
	public async Task Image_InCells_AfterOneThatWentInline_StillUsesItsCells()
	{
		var (lines, _, _) = Decode("\e[1z<IMAGE a.png URL=\"https://example.test/\" W=2c H=1c>xx\e[1z<IMAGE b.png URL=\"https://example.test/\" W=2c H=1c>  \n");
		var images = Markups(lines[0]).OfType<ImageMarkup>().ToList();
		await Assert.That(images.Single(image => image.Source.EndsWith("a.png", StringComparison.Ordinal)).Row).IsNull();
		await Assert.That(images.Single(image => image.Source.EndsWith("b.png", StringComparison.Ordinal)).Row).IsNotNull();
	}

	[Test]
	public async Task Image_InCells_InlineThatWouldOverfillTheLine_LeavesTheLineAlone()
	{
		var (lines, _, decoder) = Decode("\e[1z<IMAGE cat.png URL=\"https://example.test/\" W=2c H=1c>xxxx\n", new DecoderOptions { MaxLineLength = 4 });
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("xxxx");
		await Assert.That(decoder.Diagnostics.LimitsReached).IsGreaterThan(0);
	}

	[Test]
	public async Task Image_PutsItsTypeBetweenTheUrlAndTheFile()
	{
		var (lines, _, _) = Decode("\e[1z<IMAGE map.png URL=https://example.test/images T=maps>\n");
		await Assert.That(Markups(lines[0]).OfType<ImageMarkup>().Single().Source).IsEqualTo("https://example.test/images/maps/map.png");
	}

	[Test]
	public async Task AnElementThatOpensItself_IsCutOffAtTheDepthLimit()
	{
		var (lines, _, decoder) = Decode("\e[1z<!ELEMENT loop \"<loop>x\">\n\e[1z<loop>text\n");
		await Assert.That(lines[^1].Content.ToPlainText()).IsEqualTo(new string('x', MxpPlugin.MaxExpansionDepth) + "text");
		await Assert.That(decoder.Diagnostics.LimitsReached).IsGreaterThan(0);
	}

	[Test]
	public async Task ALineOfElements_IsCappedPerLine()
	{
		var many = string.Concat(Enumerable.Repeat("<e>", MxpPlugin.MaxExpansionsPerLine + 10));
		var (_, _, decoder) = Decode("\e[1z<!ELEMENT e \"<B>\" EMPTY>\n\e[1z" + many + "\n");
		await Assert.That(decoder.Diagnostics.LimitsReached).IsEqualTo(10);
	}

	[Test]
	public async Task ABuiltInCannotBeRedefined()
	{
		var (lines, _, _) = Decode("\e[1z<!ELEMENT B \"<I>\">\n\e[1z<B>bold</B>\n");
		var style = Spans(lines[^1]).Single().Style;
		await Assert.That(style.Has(TextAttributes.Bold)).IsTrue();
		await Assert.That(style.Has(TextAttributes.Italic)).IsFalse();
	}

	[Test]
	public async Task ABuiltInEntityCannotBeRedefined()
	{
		var (lines, events, _) = Decode("\e[1z<!ENTITY lt \"x\">\n&lt;\n");
		await Assert.That(lines[^1].Content.ToPlainText()).IsEqualTo("<");
		await Assert.That(events.OfType<VariableEvent>()).IsEmpty();
	}

	[Test]
	public async Task DefinitionsAreBounded()
	{
		var decoder = Registry.CreateDecoder();
		decoder.EnableProtocol(MxpDecoding.Protocol);
		var events = new List<DecodeEvent>();
		decoder.EventRaised += events.Add;
		for (var i = 0; i < MxpPlugin.MaxEntities + 5; i++) decoder.Feed($"\e[1z<!ENTITY e{i} v>\n");
		decoder.Feed($"\e[1z<!ENTITY long \"{new string('x', MxpPlugin.MaxDefinitionLength + 1)}\">\n");
		await Assert.That(events.OfType<VariableEvent>().Count()).IsEqualTo(MxpPlugin.MaxEntities);
		await Assert.That(decoder.Diagnostics.LimitsReached).IsEqualTo(6);
	}

	[Test]
	public async Task OpenElements_ListWhatGoesOnIntoTheNextLine()
	{
		var decoder = Registry.CreateDecoder();
		decoder.EnableProtocol(MxpDecoding.Protocol);
		decoder.Feed("\e[1z<B>bold\n");
		await Assert.That(decoder.OpenElements.Count).IsEqualTo(1);
		decoder.Feed("<B>open\n");
		await Assert.That(decoder.OpenElements.Count).IsEqualTo(1);
	}

	[Test]
	public async Task EntityAddAndRemove_EditTheList()
	{
		var (_, events, _) = Decode("\e[1z<!ENTITY list a><!ENTITY list b ADD><!ENTITY list a REMOVE><!ENTITY list DELETE>\n");
		await Assert.That(events.OfType<VariableEvent>().Select(e => e.Value)).IsEquivalentTo(["a", "a|b", "b", null]);
	}

	private static readonly DecoderRegistry Showing = VtDecoding.Terminal.WithMxp(new MxpOptions { ShowRefusedTags = true });

	private static string Shown(string text)
	{
		var decoder = Showing.CreateDecoder();
		decoder.EnableProtocol(MxpDecoding.Protocol);
		return decoder.Feed(text)[0].Content.ToPlainText();
	}

	[Test]
	public async Task ShowRefusedTags_KeepsARefusedTagAsItWasTyped()
	{
		const string tag = "<SeNd   hReF = '@shutdown'\tHINT=\"a  b\"   >";
		await Assert.That(Shown("Rivane says, '" + tag + "click</sEnD  >'\n")).IsEqualTo("Rivane says, '" + tag + "click</sEnD  >'");
		await Assert.That(Shown("<!ELEMENT evil '<SEND quit>'> </SEND> and </B>\n")).IsEqualTo("<!ELEMENT evil '<SEND quit>'> </SEND> and ");
	}

	[Test]
	public async Task ShowRefusedTags_StillHonoursACloseOfWhatIsOpen()
	{
		var decoder = Showing.CreateDecoder();
		decoder.EnableProtocol(MxpDecoding.Protocol);
		var line = decoder.Feed("\e[1z<SEND>\e[0zY</SEND> said\n")[0];
		await Assert.That(line.Content.ToPlainText()).IsEqualTo("Y said");
	}

	[Test]
	[Arguments("plain ")]
	[Arguments("\e[0m")]
	[Arguments("\e]0;title\a")]
	[Arguments("\e[0z")]
	[Arguments("&amp;")]
	[Arguments("\e[1\u0018")] // a CSI cancelled by CAN
	[Arguments("\e[1\u001a")] // a CSI cancelled by SUB
	public async Task TemporarySecure_IsEndedByAnythingBeforeItsTag(string between)
	{
		var (lines, _, _) = Decode("\e[4z" + between + "<SEND HREF=\"@shutdown\">click</SEND>\n");
		await Assert.That(Spans(lines[0]).Any(span => span.Style.Interaction is not null)).IsFalse();
	}

	[Test]
	public async Task TemporarySecure_SurvivesARepeatOfItself()
	{
		var (lines, _, _) = Decode("\e[4z\e[4z<SEND HREF=\"look\">a</SEND>\n");
		await Assert.That(Spans(lines[0]).Single().Style.Interaction).IsNotNull();
	}

	[Test]
	public async Task TemporarySecure_IsEndedByLockedText()
	{
		var (lines, _, _) = Decode("\e[7zRivane says, \e[4zhello\e[0z<SEND HREF=\"@shutdown\">click</SEND>\n");
		await Assert.That(Spans(lines[0]).Any(span => span.Style.Interaction is not null)).IsFalse();
	}

	[Test]
	public async Task AnUnknownLineTagNumber_LeavesTheModeAlone()
	{
		var (secure, _, _) = Decode("\e[1z\e[99z<SEND href=\"look\">a</SEND>\n");
		await Assert.That(Spans(secure[0]).Single().Style.Interaction).IsNotNull();
		var (open, _, _) = Decode("\e[15z<SEND href=\"look\">a</SEND>\n");
		await Assert.That(Spans(open[0]).Single().Style.Interaction).IsNull();
	}

	[Test]
	public async Task LeavingOpenModeMidLine_ClosesWhatTheOpenPartOpened()
	{
		var (lines, _, _) = Decode("says '<COLOR FORE=red>' \e[1zserver text\n");
		await Assert.That(Spans(lines[0])[^1].Style.Foreground).IsNull();
	}

	[Test]
	public async Task Send_HintsFollowTheCommands()
	{
		var (one, _, _) = Decode("\e[1z<SEND href=\"look\" hint=\"Look|around\">x</SEND>\n");
		await Assert.That(Spans(one[0]).Single().Style.Interaction!.Hint).IsEqualTo("Look|around");
		var (labels, _, _) = Decode("\e[1z<SEND href=\"n|s\" hint=\"North|South\">x</SEND>\n");
		var menu = Markups(labels[0]).OfType<CommandMenuMarkup>().Single();
		await Assert.That(menu.Hint).IsNull();
		await Assert.That(menu.Choices.Select(c => c.Label)).IsEquivalentTo(["North", "South"]);
	}

	[Test]
	public async Task Supports_AnswersFromTheOptionsList()
	{
		var options = new MxpOptions { Supports = new Dictionary<string, IReadOnlyList<string>> { ["send"] = ["href", "hint"], ["b"] = [] } };
		var (supported, unsupported) = options.Answer(["send.href", "send.prompt", "send.*", "b", "image"]);
		await Assert.That(supported).IsEquivalentTo(["send.href", "send.href", "send.hint", "b"]);
		await Assert.That(unsupported).IsEquivalentTo(["send.prompt", "image"]);
	}

	[Test]
	public async Task AnEmptyElementWithNoDefinition_PrintsNothingOfItsOwn()
	{
		var (lines, _, _) = Decode("\e[1z<!ELEMENT RNum FLAG=RoomNum EMPTY>\n\e[1z<RNum>1234</RNum> here\n");
		await Assert.That(lines[^1].Content.ToPlainText()).IsEqualTo("1234 here");
	}
}
