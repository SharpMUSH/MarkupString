using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
namespace MarkupString;

/// <summary>
/// The wire and storage format for a <see cref="MarkupText"/>.
/// </summary>
/// <remarks>
/// <code>
/// {"t":"…","p":[null,[{"k":"ansi","f":"#ffffff","bo":1}],[{"k":"ansi","f":"#569cd6"}]],"r":[12,0,8,1,40,0]}
/// </code>
/// <list type="bullet">
///   <item><c>t</c> — the plain text, omitted when empty.</item>
///   <item><c>p</c> — the palette of distinct <see cref="MarkupSet"/>s. Index 0 is always
///   <c>null</c>, meaning "no markup". Every other entry is an array of markup objects written
///   innermost first, matching <see cref="MarkupSet"/> order. Entries are arrays even in the
///   overwhelmingly common single-markup case: two bytes per palette entry buys a reader with no
///   union type in it, and a palette holds one entry per <em>distinct</em> set, so it stays tiny.</item>
///   <item><c>r</c> — a flat <c>[length, paletteIndex, …]</c> cover of <c>t</c>. Run starts are the
///   running sum, so no start, end, or total length is stored. Gaps between runs are plain text and
///   take slot 0; adjacent slot-0 stretches fold into one entry.</item>
/// </list>
/// <c>p</c> and <c>r</c> are both omitted when nothing carries markup, so a plain attribute — the
/// common case in a game database — costs its text plus eight bytes.
/// <para>
/// Each markup object carries a <c>"k"</c> kind discriminator, written first by this class; a
/// registered <see cref="IMarkupCodec"/> writes and reads only its own properties. <c>"neutral"</c>
/// and the shared vocabulary (<see cref="SoundMarkup"/>, <see cref="ImageMarkup"/>, …) are built in and
/// need no codec. Objects written before <c>"k"</c> existed are still read: <c>"h"</c>
/// present means html, <c>"n"</c> present means neutral, and anything else is ansi. A kind no codec
/// claims becomes an <see cref="UnknownMarkup"/> holding the raw object, which is written back
/// verbatim, so a reader missing a package neither loses nor corrupts its markup.
/// </para>
/// <para>
/// The shape is deliberately not a mirror of the object model, which is why this is hand-written
/// rather than attributes on a DTO: the palette has to be built by walking the runs, and the cover
/// has to fill the gaps between them.
/// </para>
/// </remarks>
public static class MarkupTextSerializer
{
	/// <summary>The kind written for, and read back as, <see cref="NeutralMarkup.Instance"/>.</summary>
	private const string NeutralKind = ElementCodecs.NeutralKind;

	/// <summary>
	/// Leaves non-ASCII text as literal UTF-8 rather than <c>\uXXXX</c> escapes. The default encoder
	/// triples the cost of CJK and Cyrillic text, which several games are written in. "Unsafe" here
	/// means "not pre-escaped for embedding in HTML or script"; this output goes to a database, to
	/// NATS, and to a JSON parser, never into a document.
	/// </summary>
	private static readonly JsonWriterOptions WriterOptions = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		Indented = false,
	};

	// ── Writing ──────────────────────────────────────────────────────────────────

	/// <summary>Serialises <paramref name="text"/> to a JSON string using <see cref="MarkupRegistry.Default"/>.</summary>
	/// <param name="text">The text to serialise.</param>
	/// <exception cref="InvalidOperationException">A markup has no codec in the registry.</exception>
	public static string Serialize(MarkupText text) => Serialize(text, registry: null);

	/// <summary>Serialises <paramref name="text"/> to a JSON string.</summary>
	/// <param name="text">The text to serialise.</param>
	/// <param name="registry">The codecs to use; <see cref="MarkupRegistry.Default"/> when null.</param>
	/// <exception cref="InvalidOperationException">A markup has no codec in the registry.</exception>
	public static string Serialize(MarkupText text, MarkupRegistry? registry)
	{
		ArgumentNullException.ThrowIfNull(text);
		var buffer = new ArrayBufferWriter<byte>(text.Length + 64);
		Serialize(text, buffer, registry);
		return Encoding.UTF8.GetString(buffer.WrittenSpan);
	}

	/// <summary>
	/// Serialises <paramref name="text"/> as UTF-8 JSON into <paramref name="output"/> using
	/// <see cref="MarkupRegistry.Default"/>.
	/// </summary>
	/// <param name="text">The text to serialise.</param>
	/// <param name="output">The buffer the JSON is written to.</param>
	/// <exception cref="InvalidOperationException">A markup has no codec in the registry.</exception>
	public static void Serialize(MarkupText text, IBufferWriter<byte> output) => Serialize(text, output, registry: null);

	/// <summary>Serialises <paramref name="text"/> as UTF-8 JSON into <paramref name="output"/>.</summary>
	/// <param name="text">The text to serialise.</param>
	/// <param name="output">The buffer the JSON is written to.</param>
	/// <param name="registry">The codecs to use; <see cref="MarkupRegistry.Default"/> when null.</param>
	/// <exception cref="InvalidOperationException">A markup has no codec in the registry.</exception>
	public static void Serialize(MarkupText text, IBufferWriter<byte> output, MarkupRegistry? registry)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(output);
		using var writer = new Utf8JsonWriter(output, WriterOptions);
		Write(writer, text, registry);
	}

	internal static void Write(Utf8JsonWriter writer, MarkupText text, MarkupRegistry? registry)
	{
		// Text with no runs — the common case in a game database — takes neither a palette nor a
		// cover, so it is written straight out rather than building the two collections the general
		// path needs only to find them empty. The bytes are the same: {"t":"…"}, or {} when empty.
		if (text.Runs.Length == 0)
		{
			writer.WriteStartObject();
			if (text.Length > 0) writer.WriteString("t", text.Text);
			writer.WriteEndObject();
			return;
		}

		// Index 0 is the reserved "no markup" slot; distinct sets take 1..n.
		var palette = new List<MarkupSet>();
		var indexOf = new Dictionary<MarkupSet, int>();
		var cover = new List<int>(text.Runs.Length * 2 + 2);

		var position = 0;
		foreach (var run in text.Runs)
		{
			if (run.Start > position) Cover(cover, run.Start - position, 0);

			if (!indexOf.TryGetValue(run.Markups, out var index))
			{
				palette.Add(run.Markups);
				index = palette.Count; // 1-based: slot 0 is the null entry.
				indexOf[run.Markups] = index;
			}

			// Runs are never adjacent under an equal set (MarkupText coalesces them), so a run's entry
			// can never fold into the previous one and is appended directly.
			cover.Add(run.Length);
			cover.Add(index);
			position = run.End;
		}

		writer.WriteStartObject();

		if (text.Length > 0) writer.WriteString("t", text.Text);

		if (palette.Count > 0)
		{
			if (position < text.Length) Cover(cover, text.Length - position, 0);

			writer.WriteStartArray("p");
			writer.WriteNullValue();
			foreach (var markups in palette)
			{
				writer.WriteStartArray();
				foreach (var markup in markups) WriteMarkup(writer, markup, registry);
				writer.WriteEndArray();
			}
			writer.WriteEndArray();

			writer.WriteStartArray("r");
			foreach (var value in cover) writer.WriteNumberValue(value);
			writer.WriteEndArray();
		}

		writer.WriteEndObject();
	}

	/// <summary>Appends a cover entry, extending the previous one when it uses the same palette slot.</summary>
	private static void Cover(List<int> cover, int length, int index)
	{
		if (length <= 0) return;

		if (cover.Count >= 2 && cover[^1] == index)
			cover[^2] += length;
		else
		{
			cover.Add(length);
			cover.Add(index);
		}
	}

	/// <summary>
	/// Writes one markup layer. <paramref name="registry"/> falls back to
	/// <see cref="MarkupRegistry.Default"/> only where a codec is actually needed, so text carrying
	/// nothing but neutral or unknown layers serialises without one being configured.
	/// </summary>
	private static void WriteMarkup(Utf8JsonWriter writer, IMarkup markup, MarkupRegistry? registry)
	{
		// An unknown layer round-trips as the bytes it arrived as, kind discriminator and all. When
		// those bytes predate "k", the legacy inference on the next read lands on the same kind.
		if (markup is UnknownMarkup unknown)
		{
			writer.WriteRawValue(unknown.RawJson);
			return;
		}

		writer.WriteStartObject();
		if (markup is NeutralMarkup)
		{
			writer.WriteString("k", NeutralKind);
		}
		else if (markup is Layout.LayoutMarkup layout)
		{
			writer.WriteString("k", Layout.LayoutJson.Kind);
			Layout.LayoutJson.Write(writer, layout, registry);
		}
		else
		{
			var codec = ElementCodecs.Find(markup.GetType())
				?? (registry ?? MarkupRegistry.Default).FindCodec(markup.GetType())
				?? throw new InvalidOperationException(
					$"No markup codec is registered for {markup.GetType()}. Add one with MarkupRegistry.With(IMarkupCodec).");
			writer.WriteString("k", codec.Kind);
			codec.Write(writer, markup);
		}
		writer.WriteEndObject();
	}

	// ── Reading ──────────────────────────────────────────────────────────────────

	/// <summary>
	/// Reads a <see cref="MarkupText"/> back from JSON using <see cref="MarkupRegistry.Default"/>.
	/// An empty string is <see cref="MarkupText.Empty"/>.
	/// </summary>
	/// <param name="json">The JSON produced by <see cref="Serialize(MarkupText)"/>.</param>
	public static MarkupText Deserialize(string json) => Deserialize(json, registry: null);

	/// <summary>Reads a <see cref="MarkupText"/> back from JSON. An empty string is <see cref="MarkupText.Empty"/>.</summary>
	/// <param name="json">The JSON produced by <see cref="Serialize(MarkupText, MarkupRegistry?)"/>.</param>
	/// <param name="registry">The codecs to use; <see cref="MarkupRegistry.Default"/> when null.</param>
	public static MarkupText Deserialize(string json, MarkupRegistry? registry)
	{
		ArgumentNullException.ThrowIfNull(json);
		if (json.Length == 0) return MarkupText.Empty;

		using var document = JsonDocument.Parse(json);
		return ReadVersioned(document.RootElement, registry);
	}

	/// <summary>
	/// Reads a <see cref="MarkupText"/> back from UTF-8 JSON using <see cref="MarkupRegistry.Default"/>.
	/// An empty span is <see cref="MarkupText.Empty"/>.
	/// </summary>
	/// <param name="utf8Json">The UTF-8 JSON produced by <see cref="Serialize(MarkupText, IBufferWriter{byte})"/>.</param>
	/// <exception cref="JsonException">The payload has content after the JSON value.</exception>
	public static MarkupText Deserialize(ReadOnlySpan<byte> utf8Json) => Deserialize(utf8Json, registry: null);

	/// <summary>Reads a <see cref="MarkupText"/> back from UTF-8 JSON. An empty span is <see cref="MarkupText.Empty"/>.</summary>
	/// <param name="utf8Json">The UTF-8 JSON produced by <see cref="Serialize(MarkupText, IBufferWriter{byte}, MarkupRegistry?)"/>.</param>
	/// <param name="registry">The codecs to use; <see cref="MarkupRegistry.Default"/> when null.</param>
	/// <exception cref="JsonException">The payload has content after the JSON value.</exception>
	public static MarkupText Deserialize(ReadOnlySpan<byte> utf8Json, MarkupRegistry? registry)
	{
		if (utf8Json.IsEmpty) return MarkupText.Empty;

		var reader = new Utf8JsonReader(utf8Json);
		using var document = JsonDocument.ParseValue(ref reader);

		// ParseValue only consumes the one value; unlike JsonDocument.Parse(string), it does not by
		// itself reject trailing content. Draining the reader keeps both overloads agreeing on what
		// counts as valid input.
		if (reader.Read())
			throw new JsonException("Unexpected content after the JSON value.");

		return ReadVersioned(document.RootElement, registry);
	}

	/// <summary>
	/// Reads the top-level envelope after checking its wire version, which nested texts (inside a
	/// layout) do not carry: a version is a property of the stored document, not of each text in it.
	/// </summary>
	private static MarkupText ReadVersioned(JsonElement root, MarkupRegistry? registry) =>
		root.ValueKind == JsonValueKind.Object && !IsKnownVersion(root, out _) ? Fallback(root) : Read(root, registry);

	/// <summary>
	/// What a payload in a wire version this reader does not know shows as: its <c>"t"</c> as plain
	/// text. Every wire version keeps <c>"t"</c> as the plain text of the whole document so that an
	/// older reader has something to show; nothing else in the payload is read, since its meaning
	/// belongs to a version this reader does not know.
	/// </summary>
	private static MarkupText Fallback(JsonElement root) =>
		root.TryGetProperty("t", out var text) && text.ValueKind == JsonValueKind.String
			? MarkupText.Plain(text.GetString() ?? string.Empty)
			: MarkupText.Empty;

	/// <summary>
	/// Whether <paramref name="root"/> is in a wire version this reader knows. No <c>"v"</c> is
	/// version 1, the only one there is; <paramref name="version"/> is the raw value otherwise.
	/// </summary>
	private static bool IsKnownVersion(JsonElement root, out string version)
	{
		version = WireVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
		if (!root.TryGetProperty("v", out var element)) return true;
		version = element.GetRawText();
		return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) && number == WireVersion;
	}

	internal static MarkupText Read(JsonElement root, MarkupRegistry? registry) => Read(root, registry, check: null);

	/// <summary>
	/// Reads the envelope. <paramref name="check"/>, when given, is told about everything the read
	/// recovers from or leaves out; it is null on every path but <see cref="Inspect(string)"/>.
	/// </summary>
	private static MarkupText Read(JsonElement root, MarkupRegistry? registry, ReadCheck? check)
	{
		// Every shape check below treats a malformed payload as a less marked-up one rather than
		// throwing: this reads rows written by older builds and bytes off a bus, and losing colour on
		// one corrupt attribute beats failing the read that carries it.
		var hasText = root.TryGetProperty("t", out var textElement);
		var text = hasText && textElement.ValueKind == JsonValueKind.String
			? textElement.GetString() ?? string.Empty
			: string.Empty;
		if (hasText && textElement.ValueKind != JsonValueKind.String) check?.Damage($"\"t\" is {Describe(textElement)}, not a string.");

		var hasPalette = root.TryGetProperty("p", out var paletteElement);
		var hasCover = root.TryGetProperty("r", out var coverElement);
		if (!hasPalette || paletteElement.ValueKind != JsonValueKind.Array
				|| !hasCover || coverElement.ValueKind != JsonValueKind.Array)
		{
			if (hasPalette || hasCover) check?.Damage("\"p\" and \"r\" are not both arrays, so the text reads without its markup.");
			return MarkupText.Plain(text);
		}

		var palette = ReadPalette(paletteElement, registry, check);
		var used = check is null ? null : new bool[palette.Length];
		var runs = ImmutableArray.CreateBuilder<Run>();
		var position = 0;

		using var cover = coverElement.EnumerateArray().GetEnumerator();
		while (cover.MoveNext())
		{
			// A non-numeric or negative length would desynchronise every later pair, so reading stops
			// there, as it does on a truncated pair rather than reading a length as an index.
			if (!TryReadInt32(cover.Current, out var length) || length < 0)
			{
				check?.Damage($"\"r\" has {cover.Current.GetRawText()} where a length belongs; the rest of the cover is not read.");
				break;
			}
			if (!cover.MoveNext() || !TryReadInt32(cover.Current, out var index))
			{
				check?.Damage("\"r\" ends without the palette index of its last length.");
				break;
			}

			// A cover already at (or past) the end of the text has nothing left to describe, and an
			// unclipped length would let position overrun int range on the next add (two int.MaxValue
			// entries wrap it negative, which sorts runs out of order and trips the overlap check
			// below). Clipping here — rather than leaving it to the constructor — keeps position itself
			// bounded by text.Length across every iteration.
			if (position >= text.Length)
			{
				if (length > 0) check?.Damage("\"r\" covers more than the text.");
				break;
			}
			var runLength = Math.Min(length, text.Length - position);
			if (runLength < length) check?.Damage("\"r\" covers more than the text.");

			// Slot 0 is the reserved "no markup" entry, and so is any index the palette does not have:
			// both leave the stretch as a gap, which is plain text.
			if (index > 0 && index <= palette.Length)
			{
				runs.Add(new Run(position, runLength, palette[index - 1]));
				if (used is not null) used[index - 1] = true;
			}
			else if (index != 0) check?.Damage($"\"r\" names palette entry {index}, which \"p\" does not have.");
			position += runLength;
		}

		// Writing builds the palette from the runs, so an entry nothing points at is not written back.
		if (used is not null)
			for (var i = 0; i < used.Length; i++)
				if (!used[i]) check!.Loss($"Palette entry {i + 1} is not used by \"r\" and would not be written back.");

		return new MarkupText(text, runs.ToImmutable());
	}

	private static string Describe(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.Object => "an object",
		JsonValueKind.Array => "an array",
		JsonValueKind.Number => "a number",
		JsonValueKind.True or JsonValueKind.False => "a boolean",
		JsonValueKind.Null => "null",
		_ => "a string",
	};

	private static bool TryReadInt32(JsonElement element, out int value)
	{
		value = 0;
		return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
	}

	/// <summary>Reads the palette, skipping slot 0 (the reserved null entry); callers subtract one.</summary>
	private static MarkupSet[] ReadPalette(JsonElement palette, MarkupRegistry? registry, ReadCheck? check)
	{
		var entries = new List<MarkupSet>();
		var markups = new List<IMarkup>();
		var first = true;
		foreach (var entry in palette.EnumerateArray())
		{
			if (first)
			{
				first = false;
				if (entry.ValueKind != JsonValueKind.Null) check?.Damage("Palette entry 0 is not null; it is never read.");
				continue;
			}

			markups.Clear();
			if (entry.ValueKind == JsonValueKind.Array)
			{
				foreach (var markup in entry.EnumerateArray())
					if (markup.ValueKind == JsonValueKind.Object)
						markups.Add(check is null ? ReadMarkup(markup, registry) : ReadMarkup(markup, registry, check));
					else
						check?.Damage($"Palette entry {entries.Count + 1} holds {Describe(markup)} where a markup object belongs.");
			}
			else check?.Damage($"Palette entry {entries.Count + 1} is {Describe(entry)}, not an array.");
			if (markups.Count == 0 && entry.ValueKind == JsonValueKind.Array && entry.GetArrayLength() == 0)
				check?.Damage($"Palette entry {entries.Count + 1} is empty.");

			// A set must hold at least one markup; an entry that read as none is a slot no cover index
			// can usefully point at, so it takes a neutral layer rather than dropping and shifting
			// every later index.
			entries.Add(markups.Count == 0 ? MarkupSet.Of(NeutralMarkup.Instance) : MarkupSet.Of(markups));
		}
		return [.. entries];
	}

	/// <summary>
	/// Reads one markup layer. As on the write side, <paramref name="registry"/> falls back to
	/// <see cref="MarkupRegistry.Default"/> only where a codec is actually looked up.
	/// </summary>
	private static IMarkup ReadMarkup(JsonElement element, MarkupRegistry? registry) => ReadMarkup(element, registry, out _);

	/// <summary>
	/// Reads one markup layer. A codec that throws — a game's own, or one meeting a row it was never
	/// written for — leaves the layer as an <see cref="UnknownMarkup"/> holding the raw object, so the
	/// read still succeeds, the layer renders as nothing, and writing the text back keeps it.
	/// <paramref name="failure"/> is the codec's message when it threw.
	/// </summary>
	private static IMarkup ReadMarkup(JsonElement element, MarkupRegistry? registry, out string? failure)
	{
		failure = null;
		var kind = KindOf(element);
		if (kind == NeutralKind) return NeutralMarkup.Instance;
		try
		{
			if (kind == Layout.LayoutJson.Kind) return Layout.LayoutJson.Read(element, registry);
			var codec = ElementCodecs.Find(kind) ?? (registry ?? MarkupRegistry.Default).FindCodec(kind);
			return codec is null ? new UnknownMarkup(kind, element.GetRawText()) : codec.Read(element);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			failure = exception.Message;
			return new UnknownMarkup(kind, element.GetRawText());
		}
	}

	/// <summary>The kind of a markup object, inferred as described on this class for one with no <c>"k"</c>.</summary>
	private static string KindOf(JsonElement element) =>
		element.TryGetProperty("k", out var k) && k.ValueKind == JsonValueKind.String
			? k.GetString()!
			: element.TryGetProperty("h", out _) ? "html"
			: element.TryGetProperty("n", out _) ? NeutralKind
			: "ansi";

	/// <summary>
	/// <see cref="ReadMarkup(JsonElement, MarkupRegistry?)"/> for <see cref="Inspect(string)"/>: a codec
	/// that throws is reported, and every layer a codec reads is written straight back and compared
	/// with what it was read from, which is the only way to know that a codec — core's, a package's or a game's — kept everything in it.
	/// </summary>
	private static IMarkup ReadMarkup(JsonElement element, MarkupRegistry? registry, ReadCheck check)
	{
		var kind = KindOf(element);
		if (element.TryGetProperty("k", out var k) && k.ValueKind != JsonValueKind.String)
			check.Damage($"A layer's \"k\" is {Describe(k)}, not a string; it reads as \"{kind}\".");
		var markup = ReadMarkup(element, registry, out var failure);
		if (failure is not null)
		{
			check.Damage($"The \"{kind}\" codec could not read a layer ({failure}); it is kept as an unknown layer.");
			return markup;
		}

		if (markup is UnknownMarkup)
		{
			check.Opaque();
			return markup;
		}

		string written;
		try
		{
			var buffer = new ArrayBufferWriter<byte>();
			using (var writer = new Utf8JsonWriter(buffer, WriterOptions)) WriteMarkup(writer, markup, registry);
			written = Encoding.UTF8.GetString(buffer.WrittenSpan);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			check.Loss($"A \"{kind}\" layer could not be written back ({exception.Message}).");
			return markup;
		}

		using var rewritten = JsonDocument.Parse(written);
		// A legacy neutral object is recognised by its "n", which the neutral kind does not write.
		var ignore = kind == NeutralKind && !element.TryGetProperty("k", out _) ? "n" : null;
		if (WireShape.Lost(element, rewritten.RootElement, ignore) is { } path)
			check.Loss($"A \"{kind}\" layer would not be written back as read: {path}.");
		return markup;
	}

	// ── Inspecting ───────────────────────────────────────────────────────────────

	/// <summary>The wire version this build reads and writes. Version 1 writes no <c>"v"</c>.</summary>
	public const int WireVersion = 1;

	/// <summary>
	/// The wire version <paramref name="json"/> was written in, read without parsing the rest of it:
	/// 1 when it has no <c>"v"</c> or is empty, null when it is not a JSON object or its <c>"v"</c> is
	/// not a whole number.
	/// </summary>
	/// <param name="json">A stored text, in any version.</param>
	public static int? VersionOf(string json)
	{
		ArgumentNullException.ThrowIfNull(json);
		return VersionOf(Encoding.UTF8.GetBytes(json));
	}

	/// <summary>
	/// The wire version <paramref name="utf8Json"/> was written in, read without parsing the rest of
	/// it: 1 when it has no <c>"v"</c> or is empty, null when it is not a JSON object or its
	/// <c>"v"</c> is not a whole number.
	/// </summary>
	/// <param name="utf8Json">A stored text as UTF-8, in any version.</param>
	public static int? VersionOf(ReadOnlySpan<byte> utf8Json)
	{
		if (utf8Json.IsEmpty) return WireVersion;
		var reader = new Utf8JsonReader(utf8Json);
		try
		{
			if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
			while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
			{
				var isVersion = reader.ValueTextEquals("v"u8);
				if (!reader.Read()) return null;
				if (isVersion)
					return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var version) ? version : null;
				reader.Skip();
			}
			return reader.TokenType == JsonTokenType.EndObject ? WireVersion : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>
	/// Whether <paramref name="json"/> is in an earlier wire version than <see cref="WireVersion"/>, so
	/// that an upgrade step would rewrite it. False for the current version, a newer one, and a
	/// payload whose version cannot be read.
	/// </summary>
	/// <param name="json">A stored text, in any version.</param>
	public static bool NeedsUpgrade(string json) => VersionOf(json) < WireVersion;

	/// <summary>
	/// Whether <paramref name="utf8Json"/> is in an earlier wire version than <see cref="WireVersion"/>,
	/// so that an upgrade step would rewrite it. False for the current version, a newer one, and a
	/// payload whose version cannot be read.
	/// </summary>
	/// <param name="utf8Json">A stored text as UTF-8, in any version.</param>
	public static bool NeedsUpgrade(ReadOnlySpan<byte> utf8Json) => VersionOf(utf8Json) < WireVersion;

	/// <summary>The envelope properties of wire version 1.</summary>
	private static readonly string[] EnvelopeProperties = ["t", "p", "r", "v"];

	/// <summary>
	/// Reads a stored <see cref="MarkupText"/> using <see cref="MarkupRegistry.Default"/> and reports
	/// whether writing it back would keep everything the payload held. Never throws on bad input.
	/// </summary>
	/// <param name="json">The JSON produced by <see cref="Serialize(MarkupText)"/>, or anything else.</param>
	public static MarkupReadResult Inspect(string json) => Inspect(json, registry: null);

	/// <summary>
	/// Reads a stored <see cref="MarkupText"/> and reports whether writing it back would keep
	/// everything the payload held. Never throws on bad input.
	/// </summary>
	/// <param name="json">The JSON produced by <see cref="Serialize(MarkupText, MarkupRegistry?)"/>, or anything else.</param>
	/// <param name="registry">The codecs to use; <see cref="MarkupRegistry.Default"/> when null.</param>
	public static MarkupReadResult Inspect(string json, MarkupRegistry? registry)
	{
		ArgumentNullException.ThrowIfNull(json);
		return Inspect(Encoding.UTF8.GetBytes(json), registry);
	}

	/// <summary>
	/// Reads a stored <see cref="MarkupText"/> from UTF-8 JSON using <see cref="MarkupRegistry.Default"/>
	/// and reports whether writing it back would keep everything the payload held. Never throws on bad input.
	/// </summary>
	/// <param name="utf8Json">The UTF-8 JSON produced by <see cref="Serialize(MarkupText, IBufferWriter{byte})"/>, or anything else.</param>
	public static MarkupReadResult Inspect(ReadOnlySpan<byte> utf8Json) => Inspect(utf8Json, registry: null);

	/// <summary>
	/// Reads a stored <see cref="MarkupText"/> from UTF-8 JSON and reports whether writing it back
	/// would keep everything the payload held. Never throws on bad input.
	/// </summary>
	/// <remarks>
	/// An empty payload is <see cref="MarkupText.Empty"/>, as <see cref="Deserialize(string)"/> reads it.
	/// </remarks>
	/// <param name="utf8Json">The UTF-8 JSON produced by <see cref="Serialize(MarkupText, IBufferWriter{byte}, MarkupRegistry?)"/>, or anything else.</param>
	/// <param name="registry">The codecs to use; <see cref="MarkupRegistry.Default"/> when null.</param>
	public static MarkupReadResult Inspect(ReadOnlySpan<byte> utf8Json, MarkupRegistry? registry)
	{
		if (utf8Json.IsEmpty) return new MarkupReadResult(MarkupReadStatus.Complete, MarkupText.Empty, []);

		var check = new ReadCheck();
		JsonDocument document;
		var reader = new Utf8JsonReader(utf8Json);
		try
		{
			document = JsonDocument.ParseValue(ref reader);
		}
		catch (JsonException exception)
		{
			check.Damage($"The payload is not JSON ({exception.Message}).");
			return check.Result(value: null);
		}

		using (document)
		{
			try
			{
				if (reader.Read()) check.Damage("There is content after the JSON value.");
			}
			catch (JsonException)
			{
				check.Damage("There is content after the JSON value.");
			}

			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				check.Damage($"The payload is {Describe(root)}, not an object.");
				return check.Result(value: null);
			}

			// The version decides what every other property means, so it is read before any of them,
			// and a version this reader does not know is not read as one it does.
			if (!IsKnownVersion(root, out var version))
			{
				check.Unsupported($"Wire version {version} is not one this reader knows; it reads version {WireVersion}. The value is its plain text only.");
				return check.Result(Fallback(root));
			}

			foreach (var property in root.EnumerateObject())
				if (Array.IndexOf(EnvelopeProperties, property.Name) < 0)
					check.Loss($"The envelope property \"{property.Name}\" is not one this reader knows and would not be written back.");

			return check.Result(Read(root, registry, check));
		}
	}

	/// <summary>What one <see cref="Inspect(ReadOnlySpan{byte}, MarkupRegistry?)"/> found.</summary>
	private sealed class ReadCheck
	{
		private readonly List<string> _diagnostics = [];
		private MarkupReadStatus _status = MarkupReadStatus.Complete;

		public void Opaque() => Raise(MarkupReadStatus.Opaque);

		public void Damage(string diagnostic) => Add(MarkupReadStatus.Damaged, diagnostic);

		public void Unsupported(string diagnostic) => Add(MarkupReadStatus.Unsupported, diagnostic);

		/// <summary>Something well formed the reader does not keep: unsupported, not damaged.</summary>
		public void Loss(string diagnostic) => Add(MarkupReadStatus.Unsupported, diagnostic);

		private void Add(MarkupReadStatus status, string diagnostic)
		{
			_diagnostics.Add(diagnostic);
			Raise(status);
		}

		private void Raise(MarkupReadStatus status)
		{
			if (status > _status) _status = status;
		}

		public MarkupReadResult Result(MarkupText? value) => new(_status, value, [.. _diagnostics]);
	}
}
