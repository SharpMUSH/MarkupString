using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Display;
using MarkupString.Extensions.DependencyInjection;
using MarkupString.Mxp.Decoding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
}
