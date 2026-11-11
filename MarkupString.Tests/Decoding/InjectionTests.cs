using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Display;
using MarkupString.Extensions.DependencyInjection;
using MarkupString.Html;
using MarkupString.Mxp;
using MarkupString.Mxp.Decoding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>The seams a host injects through, and the package that wires them into a service collection.</summary>
public class InjectionTests
{
	private const string MudLink = "\e]8;;mudclient://look\e\\look\e]8;;\e\\\n";

	private static Interaction? FirstInteraction(IReadOnlyList<DisplayLine> lines, IStyleResolver styles, DisplayOptions options) =>
		SpanRenderer.Render(lines[0].Content, styles, options)[0].Style.Interaction;

	[Test]
	public async Task UrlPolicy_AddsSchemes_ButNeverTheDangerousOnes()
	{
		var policy = UrlPolicy.WithSchemes("mudclient", "JavaScript");
		await Assert.That(policy.IsAllowed("mudclient://look")).IsTrue();
		await Assert.That(policy.IsAllowed("javascript:alert(1)")).IsFalse();
		await Assert.That(policy.IsAllowed("/relative")).IsTrue();
		await Assert.That(UrlPolicy.Safe.IsAllowed("mudclient://look")).IsFalse();
	}

	[Test]
	[Arguments("java\tscript:alert(1)")]
	[Arguments("java\nscript:alert(1)")]
	[Arguments("\u0001javascript:alert(1)")]
	[Arguments("\u0085javascript:alert(1)")]
	[Arguments(" javascript:alert(1)")]
	[Arguments("/look ")]
	public async Task UrlPolicy_RefusingControlCharacters_RefusesWhatABrowserWouldStrip(string url)
	{
		await Assert.That(UrlPolicy.Safe.RefusingControlCharacters().IsAllowed(url)).IsFalse();
		await Assert.That(UrlPolicy.WithSchemes("mudclient").RefusingControlCharacters().IsAllowed(url)).IsFalse();
	}

	[Test]
	public async Task UrlPolicy_RefusingControlCharacters_IsOptIn()
	{
		// A tab inside reads as a relative address unless the host asks for it to be refused.
		await Assert.That(UrlPolicy.Safe.RefusesControlCharacters).IsFalse();
		await Assert.That(UrlPolicy.Safe.IsAllowed("java\tscript:alert(1)")).IsTrue();

		var strict = UrlPolicy.WithSchemes("mudclient").RefusingControlCharacters();
		await Assert.That(strict.RefusesControlCharacters).IsTrue();
		await Assert.That(strict.RefusingControlCharacters()).IsSameReferenceAs(strict);
		await Assert.That(strict.IsAllowed("mudclient://look")).IsTrue();
		await Assert.That(strict.IsAllowed("https://example.com/a b")).IsTrue();
		await Assert.That(strict.IsAllowed("javascript:alert(1)")).IsFalse();
	}

	[Test]
	public async Task UrlPolicy_RefusingControlCharacters_KeepsAnMxpLinkAsText()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml();
		var link = MarkupText.Wrap(new AnsiMarkup(new AnsiStyle { LinkUrl = "java\tscript:alert(1)", LinkKind = LinkKind.Url }), "x");

		await Assert.That(link.Render(MarkupFormat.Html, registry)).Contains("href=");
		await Assert.That(link.Render(MarkupFormat.Html, registry.WithLinks(UrlPolicy.Safe.RefusingControlCharacters()))).IsEqualTo("x");
	}

	[Test]
	public async Task UrlPolicy_DecidesDecodedAndDisplayedLinks()
	{
		var policy = UrlPolicy.WithSchemes("mudclient");
		var lines = VtDecoding.Terminal.CreateDecoder(new DecoderOptions { Links = policy }).Feed(MudLink);
		var display = new DisplayOptions { Links = policy };
		await Assert.That(FirstInteraction(lines, AnsiDisplay.Default, display)).IsEqualTo(new Interaction(InteractionKind.Url, "mudclient://look"));
		await Assert.That(FirstInteraction(VtDecoding.Terminal.CreateDecoder().Feed(MudLink), AnsiDisplay.Default, display)).IsNull();
	}

	[Test]
	public async Task Diagnostics_AreReportedAsMetrics()
	{
		var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
		var meters = services.GetRequiredService<IMeterFactory>();
		var seen = new List<(long Value, string Kind)>();
		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, l) =>
		{
			if (instrument.Meter.Name == "MarkupString.Decoding" && instrument.Meter.Scope == meters) l.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<long>((_, value, tags, _) => seen.Add((value, (string)tags[0].Value!)));
		listener.Start();

		var decoder = VtDecoding.Terminal.CreateDecoder(new DecoderOptions { Meters = meters });
		decoder.Feed("\e[5;5H\e]777;x\a");

		await Assert.That(seen).IsEquivalentTo([(1L, "unhandled-sequence"), (1L, "unhandled-sequence")]);
	}

	[Test]
	public async Task AddMarkupString_RegistersEverything()
	{
		using var services = new ServiceCollection()
			.AddMarkupString(options => options.Pictures.MaxPixels = 100)
			.AddDecoders(registry => registry.WithMxp())
			.Services.BuildServiceProvider();

		var factory = services.GetRequiredService<IDecoderFactory>();
		var first = factory.CreateDecoder();
		var second = factory.CreateDecoder();
		await Assert.That(first.Registry.Has(MxpDecoding.Protocol)).IsTrue();
		await Assert.That(first.Pictures().Cache).IsSameReferenceAs(second.Pictures().Cache);
		await Assert.That(first.Pictures().Cache).IsSameReferenceAs(services.GetRequiredService<IPictureCache>());
		await Assert.That(first.Pictures().MaxPixels).IsEqualTo(100);
		await Assert.That(services.GetRequiredService<IStyleResolver>()).IsSameReferenceAs(services.GetRequiredService<DisplayRegistry>());
		await Assert.That(services.GetRequiredService<MarkupRegistry>()).IsNotNull();
	}

	[Test]
	public async Task AddMarkupString_BindsConfiguration()
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["MarkupString:LinkSchemes:0"] = "mudclient",
			["MarkupString:Display:Output"] = "CellGrid",
			["MarkupString:Display:Colors"] = "Standard",
			["MarkupString:Decoding:MaxLineLength"] = "80",
			["MarkupString:Pictures:Budget"] = "1000",
		}).Build();
		using var services = new ServiceCollection()
			.AddSingleton<IConfiguration>(configuration)
			.AddMarkupString().BindConfiguration()
			.Services.BuildServiceProvider();

		var display = services.GetRequiredService<DisplayOptions>();
		await Assert.That(display.Capabilities).IsEqualTo(OutputCapabilities.CellGrid with { Colors = ColorLevel.Standard });
		await Assert.That(((MemoryPictureCache)services.GetRequiredService<IPictureCache>()).Budget).IsEqualTo(1000);
		var decoder = services.GetRequiredService<IDecoderFactory>().CreateDecoder();
		await Assert.That(decoder.Options.MaxLineLength).IsEqualTo(80);
		var lines = decoder.Feed(MudLink);
		await Assert.That(FirstInteraction(lines, services.GetRequiredService<IStyleResolver>(), display)?.Target).IsEqualTo("mudclient://look");
	}

	private sealed class NoCache : IPictureCache
	{
		public bool TryGet(string key, [NotNullWhen(true)] out StoredPicture? picture)
		{
			picture = null;
			return false;
		}

		public void Set(StoredPicture picture)
		{
		}

		public bool Remove(string key) => false;
	}

	[Test]
	public async Task AddMarkupString_TakesAPictureCacheOfYourOwn()
	{
		using var services = new ServiceCollection().AddMarkupString().UsePictureCache<NoCache>().Services.BuildServiceProvider();
		await Assert.That(services.GetRequiredService<IDecoderFactory>().CreateDecoder().Pictures().Cache).IsTypeOf<NoCache>();
	}

	public static IEnumerable<Func<MarkupFormat>> LinkFormats() =>
		[() => MarkupFormat.Ansi, () => MarkupFormat.Html, () => MarkupFormat.Mxp, () => MarkupFormat.BBCode, () => MarkupFormat.Markdown];

	[Test]
	[MethodDataSource(nameof(LinkFormats))]
	public async Task Writers_AskTheRegistrysLinkPolicy(MarkupFormat format)
	{
		var link = MarkupText.Wrap(new AnsiMarkup(new AnsiStyle { LinkUrl = "mudclient://look", LinkKind = LinkKind.Url }), "look");
		var safe = MarkupRegistry.Empty.WithAnsi().WithMxp().WithAnsiOutput(AnsiColorDepth.TrueColor);
		var open = safe.WithLinks(UrlPolicy.WithSchemes("mudclient"));
		await Assert.That(link.Render(format, safe)).DoesNotContain("mudclient://look");
		await Assert.That(link.Render(format, open)).Contains("mudclient://look");
		await Assert.That(open.WithMxp().Links).IsSameReferenceAs(open.Links);
	}

	[Test]
	public async Task HtmlTagPolicy_AsksItsLinkPolicy()
	{
		var policy = HtmlTagPolicy.WellFormed with { UrlAttributes = HtmlTagPolicy.AddressAttributes };
		await Assert.That(policy.TryCreate("a", "href=\"mudclient://look\"", out var dropped)).IsTrue();
		await Assert.That(dropped!.Attributes).IsNull();
		await Assert.That((policy with { Links = UrlPolicy.WithSchemes("mudclient") }).TryCreate("a", "href=\"mudclient://look\"", out var kept)).IsTrue();
		await Assert.That(kept!.Attributes).Contains("mudclient://look");
	}

	[Test]
	public async Task Diagnostics_RaiseDropped()
	{
		var decoder = VtDecoding.Terminal.CreateDecoder();
		var dropped = new List<(DecodeDrop, int)>();
		decoder.Diagnostics.Dropped += (kind, count) => dropped.Add((kind, count));
		decoder.Feed("\e[5;5H\u0001");
		await Assert.That(dropped).IsEquivalentTo([(DecodeDrop.UnhandledSequence, 1), (DecodeDrop.UnhandledControl, 1)]);
	}

	private sealed class Captured : ILoggerProvider, ILogger
	{
		public List<string> Lines { get; } = [];

		public ILogger CreateLogger(string categoryName) => this;

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Lines.Add(formatter(state, exception));

		public void Dispose()
		{
		}
	}

	[Test]
	public async Task AddMarkupString_LogsWhatDecodersDrop()
	{
		var captured = new Captured();
		using var services = new ServiceCollection()
			.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(captured))
			.AddMarkupString(options => options.LinkSchemes.Add("mudclient"))
			.Services.BuildServiceProvider();
		services.GetRequiredService<IDecoderFactory>().CreateDecoder().Feed("\e[5;5H");
		await Assert.That(captured.Lines).IsEquivalentTo(["Decoder dropped 1 × UnhandledSequence"]);
		var link = MarkupText.Wrap(new AnsiMarkup(new AnsiStyle { LinkUrl = "mudclient://look", LinkKind = LinkKind.Url }), "look");
		await Assert.That(link.Render(MarkupFormat.Html, services.GetRequiredService<MarkupRegistry>())).Contains("href=\"mudclient://look\"");
	}

	[Test]
	public async Task AddMarkupString_RefusesControlCharactersInLinksWhenAsked()
	{
		using var lax = new ServiceCollection().AddMarkupString().Services.BuildServiceProvider();
		using var strict = new ServiceCollection()
			.AddMarkupString(options => options.RefuseControlCharactersInLinks = true)
			.Services.BuildServiceProvider();

		await Assert.That(lax.GetRequiredService<IUrlPolicy>().IsAllowed("java\tscript:alert(1)")).IsTrue();
		await Assert.That(strict.GetRequiredService<IUrlPolicy>().IsAllowed("java\tscript:alert(1)")).IsFalse();
		await Assert.That(strict.GetRequiredService<IUrlPolicy>().IsAllowed("https://example.com")).IsTrue();
	}
}
