using System.Text;
using System.Text.Json;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

/// <summary>
/// The stored form is a contract: rows written by every released writer are read by every later
/// reader, and a reader that cannot keep everything in a row says so instead of handing back a value
/// that would lose it on the next save. Assertions here compare JSON, not <see cref="MarkupText"/>
/// equality, which is plain text only and cannot show that a write keeps what was read.
/// </summary>
public class WireCompatibilityTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	// ── Golden payloads ──────────────────────────────────────────────────────────

	/// <summary>
	/// Written by the released MarkupString, MarkupString.Ansi and MarkupString.Html packages 1.0.0,
	/// 2.0.0, 2.8.0 and 2.18.0 with <c>WithAnsi().WithHtml()</c>; every one of them wrote these bytes.
	/// The layout payload is 2.8.0's (the first release with block layout); 2.18.0 writes the same
	/// bytes but for the random layout id.
	/// </summary>
	public static IEnumerable<Func<string>> Released() =>
	[
		() => """{"t":"plain text"}""",
		() => "{}",
		() => """{"t":"Hello world!","p":[null,[{"k":"ansi","f":9}]],"r":[6,0,5,1,1,0]}""",
		() => """{"t":"Go north","p":[null,[{"k":"ansi","f":14},{"k":"html","h":"send","a":"href=\"north\""}]],"r":[8,1]}""",
		() => """{"t":"日本語 ünï ✓","p":[null,[{"k":"ansi","f":4}]],"r":[3,1,6,0]}""",
		() => """{"t":"n","p":[null,[{"k":"neutral"}]],"r":[1,1]}""",
		() => Layout,
	];

	private const string Layout = """{"t":"+==========< Byron >=========+\n+==========< Quote >=========+\n| Hooooo?                    |\n+============================+","p":[null,[{"k":"layout","w":30,"l":123,"h":"4d6cb6025c3413fb","id":"0e716bc9b9d807a4","n":{"t":"frame","b":{"t":"stack","ch":[{"t":"rule","ti":{"t":"Quote","p":[null,[{"k":"ansi","f":10}]],"r":[5,1]}},{"t":"text","c":{"t":"Hooooo?"}}]},"bs":{"n":"mush"},"ti":{"t":"Byron"}}}],[{"k":"ansi","f":10},{"k":"layout","w":30,"l":123,"h":"4d6cb6025c3413fb","id":"0e716bc9b9d807a4","n":{"t":"frame","b":{"t":"stack","ch":[{"t":"rule","ti":{"t":"Quote","p":[null,[{"k":"ansi","f":10}]],"r":[5,1]}},{"t":"text","c":{"t":"Hooooo?"}}]},"bs":{"n":"mush"},"ti":{"t":"Byron"}}}]],"r":[44,1,5,2,74,1]}""";

	[Test]
	[MethodDataSource(nameof(Released))]
	public async Task Released_IsComplete_AndWritesBackByteForByte(string json)
	{
		var result = MarkupTextSerializer.Inspect(json, Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Complete);
		await Assert.That(result.Diagnostics).IsEmpty();
		await Assert.That(MarkupTextSerializer.Serialize(result.Value!, Registry)).IsEqualTo(json);
	}

	[Test]
	[MethodDataSource(nameof(Released))]
	public async Task Released_InspectAndDeserialize_ReadTheSameText(string json)
		=> await Assert.That(MarkupTextSerializer.Inspect(json, Registry).Value!.Text)
			.IsEqualTo(MarkupTextSerializer.Deserialize(json, Registry).Text);

	/// <summary>Forms written before the <c>"k"</c> discriminator and the semantic colour model.</summary>
	[Test]
	[Arguments("""{"t":"x","p":[null,[{"n":1}]],"r":[1,1]}""")]
	[Arguments("""{"t":"x","p":[null,[{"f":[31]}]],"r":[1,1]}""")]
	[Arguments("""{"t":"x","p":[null,[{"k":"ansi","f":[1,34],"g":[38,5,200],"bo":0}]],"r":[1,1]}""")]
	[Arguments("""{"t":"x","p":[null,[{"k":"ansi","f":"#ff000080"}]],"r":[1,1]}""")]
	[Arguments("""{"t":"x","p":[null,[{"h":"send","a":"href=\"x\""}]],"r":[1,1]}""")]
	public async Task Legacy_IsComplete(string json)
	{
		var result = MarkupTextSerializer.Inspect(json, Registry);
		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Complete);
		await Assert.That(result.CanRewrite).IsTrue();
	}

	// ── Opaque ───────────────────────────────────────────────────────────────────

	[Test]
	public async Task UnknownKind_IsOpaque_AndWritesBackByteForByte()
	{
		const string json = """{"t":"x","p":[null,[{"k":"zzz","f":1,"future":{"x":7}}]],"r":[1,1]}""";

		var result = MarkupTextSerializer.Inspect(json, Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Opaque);
		await Assert.That(result.CanRewrite).IsTrue();
		await Assert.That(MarkupTextSerializer.Serialize(result.Value!, Registry)).IsEqualTo(json);
	}

	// ── Unsupported ──────────────────────────────────────────────────────────────

	[Test]
	[Arguments("""{"v":99,"t":"x","future":true}""")]
	[Arguments("""{"v":2,"text":"important"}""")]
	[Arguments("""{"v":"1","t":"x"}""")]
	public async Task UnknownVersion_IsUnsupported_AndNotRead(string json)
	{
		var result = MarkupTextSerializer.Inspect(json, Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Unsupported);
		await Assert.That(result.CanRewrite).IsFalse();
		await Assert.That(result.Value).IsNull();
		await Assert.That(result.Diagnostics.Single()).Contains("Wire version");
	}

	[Test]
	public async Task UnknownVersion_Deserialize_Throws()
		=> await Assert.That(() => MarkupTextSerializer.Deserialize("""{"v":2,"text":"important"}""", Registry))
			.Throws<JsonException>();

	[Test]
	public async Task UnknownVersion_Utf8Deserialize_Throws()
		=> await Assert.That(() => MarkupTextSerializer.Deserialize(Encoding.UTF8.GetBytes("""{"v":2,"t":"x"}"""), Registry))
			.Throws<JsonException>();

	[Test]
	public async Task VersionOne_IsRead()
	{
		var result = MarkupTextSerializer.Inspect("""{"v":1,"t":"x"}""", Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Complete);
		await Assert.That(result.Value!.Text).IsEqualTo("x");
		await Assert.That(MarkupTextSerializer.Deserialize("""{"v":1,"t":"x"}""", Registry).Text).IsEqualTo("x");
	}

	[Test]
	public async Task UnknownEnvelopeProperty_IsUnsupported_ButShowsTheText()
	{
		var result = MarkupTextSerializer.Inspect("""{"t":"x","future":true}""", Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Unsupported);
		await Assert.That(result.Value!.Text).IsEqualTo("x");
		await Assert.That(result.Diagnostics.Single()).Contains("\"future\"");
	}

	[Test]
	public async Task KnownKind_WithAPropertyItDoesNotKnow_IsUnsupported()
	{
		var result = MarkupTextSerializer.Inspect("""{"t":"x","p":[null,[{"k":"ansi","f":1,"future":{"x":7}}]],"r":[1,1]}""", Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Unsupported);
		await Assert.That(result.Diagnostics.Single()).Contains("/future is dropped");
	}

	[Test]
	public async Task Neutral_WithAPropertyItDoesNotKnow_IsUnsupported()
		=> await Assert.That(MarkupTextSerializer.Inspect("""{"t":"x","p":[null,[{"k":"neutral","future":1}]],"r":[1,1]}""", Registry).Status)
			.IsEqualTo(MarkupReadStatus.Unsupported);

	[Test]
	public async Task UnknownLayoutBlock_IsUnsupported()
	{
		var json = Layout.Replace("\"t\":\"rule\"", "\"t\":\"future\"");

		var result = MarkupTextSerializer.Inspect(json, Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Unsupported);
		await Assert.That(result.Diagnostics[0]).Contains("/n/b/ch/0/t changes from \"future\" to \"stack\"");
		// Deserialize still shows the text the layout stands for.
		await Assert.That(MarkupTextSerializer.Deserialize(json, Registry).Text)
			.IsEqualTo(MarkupTextSerializer.Deserialize(Layout, Registry).Text);
	}

	[Test]
	public async Task UnknownLayoutBlock_WithNoRegistryForIt_IsUnsupported_EvenWhenLayoutIsKnown()
	{
		var result = MarkupTextSerializer.Inspect(Layout.Replace("\"t\":\"text\"", "\"t\":\"widget\""), Registry);
		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Unsupported);
	}

	[Test]
	public async Task GradientStop_OfAnUnknownKind_IsUnsupported()
	{
		var red = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 0, 0));
		var lime = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(0, 255, 0));
		var gauge = new Gauge(6, 12) { BarWidth = 4, Show = GaugeShow.None, Gradient = new ColorGradient([red, lime], GradientSpace.Oklch) };
		var json = MarkupTextSerializer.Serialize(BlockLayout.Build(gauge, 20), Registry);
		await Assert.That(MarkupTextSerializer.Inspect(json, Registry).Status).IsEqualTo(MarkupReadStatus.Complete);

		var result = MarkupTextSerializer.Inspect(json.Replace("\"k\":\"ansi\",\"f\":\"#ff0000\"", "\"k\":\"zzz\",\"f\":\"#ff0000\""), Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Unsupported);
		await Assert.That(string.Join("\n", result.Diagnostics)).Contains("/st has 2 entries and would be written with 1");
	}

	[Test]
	public async Task UnusedPaletteEntry_IsUnsupported()
	{
		var result = MarkupTextSerializer.Inspect("""{"t":"x","p":[null,[{"k":"neutral"}],[{"k":"zzz"}]],"r":[1,1]}""", Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Unsupported);
		await Assert.That(result.Diagnostics.Single()).Contains("Palette entry 2");
	}

	// ── Damaged ──────────────────────────────────────────────────────────────────

	[Test]
	[Arguments("not json")]
	[Arguments("[1,2]")]
	[Arguments("\"t\"")]
	[Arguments("{\"t\":")]
	public async Task NotAnObject_IsDamaged_WithNoValue(string json)
	{
		var result = MarkupTextSerializer.Inspect(json, Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Damaged);
		await Assert.That(result.Value).IsNull();
		await Assert.That(result.Diagnostics).IsNotEmpty();
	}

	[Test]
	[Arguments("""{"t":"ab"} xyz""")]
	[Arguments("""{"t":"ab"}{"t":"cd"}""")]
	public async Task TrailingContent_IsDamaged_ButShowsTheFirstValue(string json)
	{
		var result = MarkupTextSerializer.Inspect(json, Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Damaged);
		await Assert.That(result.Value!.Text).IsEqualTo("ab");
	}

	[Test]
	[Arguments("""{"t":5}""")]
	[Arguments("""{"t":"ab","p":5,"r":7}""")]
	[Arguments("""{"t":"ab","p":[null,[{"k":"neutral"}]]}""")]
	[Arguments("""{"t":"ab","p":[1,[{"k":"neutral"}]],"r":[2,1]}""")]
	[Arguments("""{"t":"ab","p":[null,7],"r":[2,1]}""")]
	[Arguments("""{"t":"ab","p":[null,[]],"r":[2,1]}""")]
	[Arguments("""{"t":"ab","p":[null,[7]],"r":[2,1]}""")]
	[Arguments("""{"t":"ab","p":[null,[{"k":"neutral"}]],"r":[9,1]}""")]
	[Arguments("""{"t":"ab","p":[null,[{"k":"neutral"}]],"r":[2,1,1,1]}""")]
	[Arguments("""{"t":"abcd","p":[null,[{"k":"neutral"}]],"r":[2,1,2]}""")]
	[Arguments("""{"t":"ab","p":[null,[{"k":"neutral"}]],"r":[2,7]}""")]
	[Arguments("""{"t":"ab","p":[null,[{"k":"neutral"}]],"r":["x",1]}""")]
	[Arguments("""{"t":"abcdef","p":[null,[{"k":"neutral"}]],"r":[3,1,-3,1,3,1]}""")]
	[Arguments("""{"t":"ab","p":[null,[{"k":5,"f":1}]],"r":[2,1]}""")]
	public async Task Malformed_IsDamaged(string json)
	{
		var result = MarkupTextSerializer.Inspect(json, Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Damaged);
		await Assert.That(result.CanRewrite).IsFalse();
		await Assert.That(result.Value).IsNotNull();
	}

	private sealed record Boom : IMarkup;

	private sealed class ThrowingCodec : IMarkupCodec
	{
		public string Kind => "boom";
		public Type MarkupType => typeof(Boom);
		public void Write(Utf8JsonWriter writer, IMarkup markup) { }
		public IMarkup Read(JsonElement element) => throw new InvalidOperationException("no");
	}

	[Test]
	public async Task ThrowingCodec_IsDamaged_AndKeepsTheLayerOpaque()
	{
		const string json = """{"t":"x","p":[null,[{"k":"boom","q":1}]],"r":[1,1]}""";
		var registry = Registry.With(new ThrowingCodec());

		var result = MarkupTextSerializer.Inspect(json, registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Damaged);
		await Assert.That(result.Value!.Runs[0].Markups[0]).IsEqualTo((IMarkup)new UnknownMarkup("boom", """{"k":"boom","q":1}"""));
		await Assert.That(() => MarkupTextSerializer.Deserialize(json, registry)).Throws<InvalidOperationException>();
	}

	[Test]
	public async Task WorstFinding_Wins()
	{
		var result = MarkupTextSerializer.Inspect("""{"t":"ab","future":1,"p":[null,[{"k":"zzz"}]],"r":[9,1]}""", Registry);

		await Assert.That(result.Status).IsEqualTo(MarkupReadStatus.Damaged);
		await Assert.That(result.Diagnostics.Length).IsEqualTo(2);
	}

	[Test]
	public async Task Empty_IsComplete()
	{
		await Assert.That(MarkupTextSerializer.Inspect("", Registry).Value).IsSameReferenceAs(MarkupText.Empty);
		await Assert.That(MarkupTextSerializer.Inspect(ReadOnlySpan<byte>.Empty, Registry).Status).IsEqualTo(MarkupReadStatus.Complete);
	}

	[Test]
	public async Task Utf8AndString_Agree()
	{
		const string json = """{"t":"日本","future":1}""";
		var fromString = MarkupTextSerializer.Inspect(json, Registry);
		var fromBytes = MarkupTextSerializer.Inspect(Encoding.UTF8.GetBytes(json), Registry);

		await Assert.That(fromBytes.Status).IsEqualTo(fromString.Status);
		await Assert.That(fromBytes.Value!.Text).IsEqualTo(fromString.Value!.Text);
	}
}
