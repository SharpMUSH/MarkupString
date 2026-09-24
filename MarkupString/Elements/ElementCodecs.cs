using System.Text.Json;
namespace MarkupString;

/// <summary>
/// The codecs for the shared vocabulary, which is core's and so needs no registration. The serializer
/// asks here before it asks a registry.
/// </summary>
internal static class ElementCodecs
{
	/// <summary>The kind the serializer writes for <see cref="NeutralMarkup"/>, which needs no codec.</summary>
	public const string NeutralKind = "neutral";

	private static readonly IMarkupCodec[] All =
	[
		new Codec<BellMarkup>("bell", static (_, _) => { }, static _ => BellMarkup.Instance),
		new Codec<ClearScreenMarkup>("clear", static (_, _) => { }, static _ => ClearScreenMarkup.Instance),
		new Codec<PreformattedMarkup>("pre", static (_, _) => { }, static _ => PreformattedMarkup.Instance),
		new Codec<SoundMarkup>("sound",
			static (w, m) =>
			{
				w.WriteString("s", m.Source);
				if (m.Channel == SoundChannel.Music) w.WriteString("c", "music");
				if (m.Volume is { } volume) w.WriteNumber("v", volume);
				if (m.Repeats is { } repeats) w.WriteNumber("r", repeats);
				if (m.Continues) w.WriteBoolean("co", true);
			},
			static e => new SoundMarkup(
				String(e, "s") ?? string.Empty,
				Channel(e) ?? SoundChannel.Effects,
				Int(e, "v"),
				Int(e, "r"),
				e.TryGetProperty("co", out var c) && c.ValueKind == JsonValueKind.True)),
		new Codec<SoundStopMarkup>("sound-stop",
			static (w, m) =>
			{
				if (m.Channel is { } channel) w.WriteString("c", channel == SoundChannel.Music ? "music" : "effects");
			},
			static e => new SoundStopMarkup(Channel(e))),
		new Codec<ImageMarkup>("image",
			static (w, m) =>
			{
				w.WriteString("s", m.Source);
				if (m.Description is not null) w.WriteString("d", m.Description);
				if (m.Width is { } width) w.WriteNumber("w", width);
				if (m.Height is { } height) w.WriteNumber("h", height);
				if (m.Align is { } align) w.WriteString("a", align.ToString().ToLowerInvariant());
			},
			static e => new ImageMarkup(
				String(e, "s") ?? string.Empty,
				String(e, "d"),
				Int(e, "w"),
				Int(e, "h"),
				Enum.TryParse<ImageAlign>(String(e, "a"), ignoreCase: true, out var align) ? align : null)),
		new Codec<PaneMarkup>("pane",
			static (w, m) =>
			{
				w.WriteString("n", m.Name);
				if (m.Title is not null) w.WriteString("ti", m.Title);
			},
			static e => new PaneMarkup(String(e, "n") ?? string.Empty, String(e, "ti"))),
		new Codec<ExpireLinksMarkup>("expire",
			static (w, m) =>
			{
				if (m.Group is not null) w.WriteString("g", m.Group);
			},
			static e => new ExpireLinksMarkup(String(e, "g"))),
		new Codec<PrefetchMarkup>("prefetch",
			static (w, m) => w.WriteString("s", m.Source),
			static e => new PrefetchMarkup(String(e, "s") ?? string.Empty)),
		new Codec<LoginPromptMarkup>("login",
			static (w, m) => w.WriteString("f", m.Field == LoginField.Password ? "password" : "user"),
			static e => new LoginPromptMarkup(String(e, "f") == "password" ? LoginField.Password : LoginField.User)),
		new Codec<RelocateMarkup>("relocate",
			static (w, m) =>
			{
				w.WriteString("ho", m.Host);
				w.WriteNumber("po", m.Port);
				if (m.Quiet) w.WriteBoolean("q", true);
			},
			static e => new RelocateMarkup(
				String(e, "ho") ?? string.Empty,
				Int(e, "po") ?? 0,
				e.TryGetProperty("q", out var q) && q.ValueKind == JsonValueKind.True)),
		new Codec<VariableMarkup>("var",
			static (w, m) => w.WriteString("n", m.Name),
			static e => new VariableMarkup(String(e, "n") ?? string.Empty)),
		new Codec<GaugeMarkup>("gauge",
			static (w, m) =>
			{
				w.WriteString("n", m.Variable);
				w.WriteString("m", m.Maximum);
				if (m.Caption is not null) w.WriteString("ca", m.Caption);
				if (m.Color is not null) w.WriteString("co", m.Color);
			},
			static e => new GaugeMarkup(String(e, "n") ?? string.Empty, String(e, "m") ?? string.Empty, String(e, "ca"), String(e, "co"))),
		new Codec<StatusMarkup>("status",
			static (w, m) =>
			{
				w.WriteString("n", m.Variable);
				if (m.Maximum is not null) w.WriteString("m", m.Maximum);
				if (m.Caption is not null) w.WriteString("ca", m.Caption);
			},
			static e => new StatusMarkup(String(e, "n") ?? string.Empty, String(e, "m"), String(e, "ca"))),
	];

	private static readonly Dictionary<Type, IMarkupCodec> ByType = All.ToDictionary(c => c.MarkupType);
	private static readonly Dictionary<string, IMarkupCodec> ByKind = All.ToDictionary(c => c.Kind, StringComparer.Ordinal);

	public static IMarkupCodec? Find(Type markupType) => ByType.GetValueOrDefault(markupType);

	public static IMarkupCodec? Find(string kind) => ByKind.GetValueOrDefault(kind);

	/// <summary>Whether <paramref name="kind"/> is one core writes itself, and so not one to register.</summary>
	public static bool IsReserved(string kind) => kind == NeutralKind || ByKind.ContainsKey(kind);

	private static string? String(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private static int? Int(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
			? number
			: null;

	private static SoundChannel? Channel(JsonElement element) => String(element, "c") switch
	{
		"music" => SoundChannel.Music,
		"effects" => SoundChannel.Effects,
		_ => null,
	};

	private sealed class Codec<T>(string kind, Action<Utf8JsonWriter, T> write, Func<JsonElement, T> read) : IMarkupCodec
		where T : IMarkup
	{
		public string Kind => kind;

		public Type MarkupType => typeof(T);

		public void Write(Utf8JsonWriter writer, IMarkup markup) => write(writer, (T)markup);

		public IMarkup Read(JsonElement element) => read(element);
	}
}
