using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Display;
using MarkupString.Extensions.DependencyInjection;
using MarkupString.Mxp.Decoding;
using Microsoft.Extensions.DependencyInjection;

namespace MarkupString.Tests.Display;

/// <summary>Effects read out of text and decoder events, and handed to handlers.</summary>
public class OutputEffectTests
{
	private sealed record ChimeMarkup : IPointMarkup;

	private static MarkupText Point(IPointMarkup point) => MarkupText.Wrap(point, point.Carrier);

	[Test]
	public async Task Of_ListsEachPoint_InOrder()
	{
		var text = MarkupText.Concat(
		[
			MarkupText.Plain("a"), MarkupText.Sound("door.wav"), MarkupText.Plain("b"), Point(BellMarkup.Instance),
			Point(new ExpireLinksMarkup("exits")), Point(ClearScreenMarkup.Instance), Point(new SoundStopMarkup(SoundChannel.Music)),
			Point(new RelocateMarkup("h", 23)), Point(new LoginPromptMarkup(LoginField.User)), Point(new PrefetchMarkup("map.png")),
		]);

		await Assert.That(OutputEffects.Of(text)).IsEquivalentTo(new OutputEffect[]
		{
			new SoundMarkup("door.wav"), BellMarkup.Instance, new ExpireLinksMarkup("exits"), ClearScreenMarkup.Instance,
			new SoundStopMarkup(SoundChannel.Music), new RelocateMarkup("h", 23), new LoginPromptMarkup(LoginField.User),
			new PrefetchMarkup("map.png"),
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Of_CountsEveryCarrier()
	{
		var bells = MarkupText.Concat([Point(BellMarkup.Instance), Point(BellMarkup.Instance), MarkupText.Plain("x")]);

		await Assert.That(OutputEffects.Of(bells).Count).IsEqualTo(2);
	}

	[Test]
	public async Task Of_LeavesSoundsOut_ForAnOutputWithoutSound()
	{
		var text = MarkupText.Concat([MarkupText.Sound("door.wav"), Point(BellMarkup.Instance)]);

		await Assert.That(OutputEffects.Of(text, OutputCapabilities.PlainText)).IsEquivalentTo(new OutputEffect[] { BellMarkup.Instance });
		await Assert.That(OutputEffects.Of(text, OutputCapabilities.Browser).Count).IsEqualTo(2);
	}

	[Test]
	public async Task Of_GivesAVariable_TheWholeTextItCovers()
	{
		var hp = new VariableMarkup("hp");
		var text = MarkupText.Concat(
		[
			MarkupText.Plain("HP: "),
			MarkupText.Wrap(hp, MarkupText.Concat([MarkupText.Plain("4"), MarkupText.Wrap(new HighlightMarkup(new RgbColor(1, 2, 3)), "2")])),
		]);

		await Assert.That(OutputEffects.Of(text)).IsEquivalentTo(new OutputEffect[] { new VariableEvent("hp", "42") });
	}

	[Test]
	public async Task Of_PassesAPointItDoesNotKnow_AsItsOwnCase()
	{
		var chime = new ChimeMarkup();
		var effect = OutputEffects.Of(Point(chime)).Single();

		await Assert.That(effect.Value).IsSameReferenceAs(chime);
		await Assert.That(Describe(effect)).IsEqualTo("point ChimeMarkup");
	}

	/// <summary>A host's switch: the specific cases first, the open ones last.</summary>
	private static string Describe(OutputEffect effect) => effect switch
	{
		BellMarkup => "bell",
		SoundMarkup sound => "sound " + sound.Source,
		TitleEvent title => "title " + title.Title,
		VariableEvent variable => $"variable {variable.Name}={variable.Value}",
		IPointMarkup point => "point " + point.GetType().Name,
		DecodeEvent decoded => "event " + decoded.GetType().Name,
		_ => "none",
	};

	[Test]
	public async Task ASwitch_ReachesEachCase_HoweverTheEffectWasMade()
	{
		await Assert.That(Describe(BellMarkup.Instance)).IsEqualTo("bell");
		await Assert.That(Describe(OutputEffect.Of((IPointMarkup)BellMarkup.Instance))).IsEqualTo("bell");
		await Assert.That(Describe(new SoundMarkup("door.wav"))).IsEqualTo("sound door.wav");
		await Assert.That(Describe(new TitleEvent("Hall", false))).IsEqualTo("title Hall");
		await Assert.That(Describe(OutputEffect.Of((DecodeEvent)new TitleEvent("Hall", false)))).IsEqualTo("title Hall");
		await Assert.That(Describe(new ReplyEvent("x"))).IsEqualTo("event ReplyEvent");
		await Assert.That(Describe(default)).IsEqualTo("none");
	}

	[Test]
	public async Task DecodedOutput_GivesTheSameEffects_AsMarkupFrames()
	{
		var effects = new List<OutputEffect>();
		var decoder = DecoderRegistry.Empty.WithVt().WithMxp().CreateDecoder();
		decoder.EventRaised += e => effects.Add(OutputEffect.Of(e));
		decoder.EnableProtocol(MxpDecoding.Protocol);

		foreach (var line in decoder.Feed("\e]2;The Hall\aDing\a\e[1z<SOUND FName=\"door.wav\">\n")) effects.AddRange(OutputEffects.Of(line.Content));

		await Assert.That(effects).Contains(new TitleEvent("The Hall", false));
		await Assert.That(effects).Contains(BellMarkup.Instance);
		await Assert.That(effects.Select(e => e.Value).OfType<SoundMarkup>().Single().Source).IsEqualTo("door.wav");
	}

	private sealed class Recorder(string name, Func<OutputEffect, bool> takes, List<string> log) : IEffectHandler
	{
		public bool Handle(OutputEffect effect)
		{
			log.Add(name);
			return takes(effect);
		}
	}

	[Test]
	public async Task Dispatcher_AsksTheNewestHandlerFirst_AndReportsWhatNoneTook()
	{
		var log = new List<string>();
		var unhandled = new List<OutputEffect>();
		var dispatcher = new EffectDispatcher(
		[
			new Recorder("old", static e => e is BellMarkup, log),
			new Recorder("new", static e => e is TitleEvent, log),
		]);
		dispatcher.Unhandled += unhandled.Add;

		await Assert.That(dispatcher.Dispatch(new TitleEvent("t", false))).IsTrue();
		await Assert.That(dispatcher.Dispatch(BellMarkup.Instance)).IsTrue();
		await Assert.That(dispatcher.Dispatch(ClearScreenMarkup.Instance)).IsFalse();
		await Assert.That(log).IsEquivalentTo(["new", "new", "old", "new", "old"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(unhandled).IsEquivalentTo(new OutputEffect[] { ClearScreenMarkup.Instance });
	}

	private sealed class TitleHandler : IEffectHandler
	{
		public string? Title { get; private set; }

		public bool Handle(OutputEffect effect)
		{
			if (effect is not TitleEvent title) return false;
			Title = title.Title;
			return true;
		}
	}

	[Test]
	public async Task AddMarkupString_RegistersTheDispatcher_WithTheHandlersAdded()
	{
		var handler = new TitleHandler();
		using var provider = new ServiceCollection().AddMarkupString().AddEffectHandler(handler).Services.BuildServiceProvider();

		await Assert.That(provider.GetRequiredService<EffectDispatcher>().Dispatch(new TitleEvent("Hall", false))).IsTrue();
		await Assert.That(handler.Title).IsEqualTo("Hall");
	}
}
