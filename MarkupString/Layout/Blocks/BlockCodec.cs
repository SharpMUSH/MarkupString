using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace MarkupString.Layout;

/// <summary>
/// Writes one kind of <see cref="Block"/> into the JSON form of a <see cref="LayoutMarkup"/> and reads
/// it back, so a layout keeps its tree through <see cref="MarkupTextSerializer"/>. The built-in blocks
/// have theirs; register one for a block of your own with <see cref="MarkupRegistry.With(BlockCodec)"/>.
/// </summary>
/// <remarks>
/// A block with no codec is written as the text it draws, so it still shows; a kind the reader has no
/// codec for leaves the whole layout unread, so its text is shown as it is and never laid out again.
/// </remarks>
public abstract class BlockCodec
{
	/// <summary>The name written for the block: letters, digits and dashes, like <c>"gauge"</c>.</summary>
	public abstract string Kind { get; }

	/// <summary>The block type this writes.</summary>
	public abstract Type BlockType { get; }

	/// <summary>Writes <paramref name="block"/>'s properties; the kind is written already.</summary>
	public abstract void Write(Block block, BlockWriter writer);

	/// <summary>Reads a block back from what <see cref="Write"/> wrote.</summary>
	public abstract Block Read(BlockReader reader);

	/// <summary>A codec from two functions.</summary>
	/// <typeparam name="T">The block type.</typeparam>
	/// <param name="kind">The name written for it.</param>
	/// <param name="write">Writes a block's properties.</param>
	/// <param name="read">Reads one back.</param>
	public static BlockCodec Create<T>(string kind, Action<T, BlockWriter> write, Func<BlockReader, T> read) where T : Block
	{
		ArgumentException.ThrowIfNullOrEmpty(kind);
		ArgumentNullException.ThrowIfNull(write);
		ArgumentNullException.ThrowIfNull(read);
		return new Delegated<T>(kind, write, read);
	}

	private sealed class Delegated<T>(string kind, Action<T, BlockWriter> write, Func<BlockReader, T> read) : BlockCodec where T : Block
	{
		public override string Kind => kind;

		public override Type BlockType => typeof(T);

		public override void Write(Block block, BlockWriter writer) => write((T)block, writer);

		public override Block Read(BlockReader reader) => read(reader);
	}
}

/// <summary>Writes a block's properties as JSON. Each method writes nothing for a value left unset or at its default.</summary>
public sealed class BlockWriter
{
	private readonly Utf8JsonWriter _json;
	private readonly MarkupRegistry? _registry;
	private readonly int _width;

	internal BlockWriter(Utf8JsonWriter json, MarkupRegistry? registry, int width)
	{
		_json = json;
		_registry = registry;
		_width = width;
	}

	/// <summary>Writes a block as an object: its kind and what its codec writes.</summary>
	internal void Object(Block block)
	{
		_json.WriteStartObject();
		if (LayoutJson.CodecFor(block.GetType(), _registry) is { } codec)
		{
			_json.WriteString("t", codec.Kind);
			codec.Write(block, this);
		}
		else
		{
			// No codec: the text it draws, which reads back as text.
			_json.WriteString("t", "text");
			Text("c", MarkupText.Join(MarkupText.NewLine, LayoutContext.Default.Lines(block, _width)));
		}
		_json.WriteEndObject();
	}

	/// <summary>Text, unless null.</summary>
	public void Text(string name, MarkupText? value)
	{
		if (value is null) return;
		_json.WritePropertyName(name);
		MarkupTextSerializer.Write(_json, value, _registry);
	}

	/// <summary>Texts, as an array.</summary>
	public void Texts(string name, IEnumerable<MarkupText> values)
	{
		_json.WriteStartArray(name);
		foreach (var value in values) MarkupTextSerializer.Write(_json, value, _registry);
		_json.WriteEndArray();
	}

	/// <summary>A child block, unless null.</summary>
	public void Block(string name, Block? value)
	{
		if (value is null) return;
		_json.WritePropertyName(name);
		Object(value);
	}

	/// <summary>Child blocks, as an array.</summary>
	public void Blocks(string name, IEnumerable<Block> values)
	{
		_json.WriteStartArray(name);
		foreach (var value in values) Object(value);
		_json.WriteEndArray();
	}

	/// <summary>Objects of your own, as an array, each written by <paramref name="item"/>.</summary>
	public void Array<T>(string name, IEnumerable<T> values, Action<T, BlockWriter> item)
	{
		ArgumentNullException.ThrowIfNull(item);
		_json.WriteStartArray(name);
		foreach (var value in values)
		{
			_json.WriteStartObject();
			item(value, this);
			_json.WriteEndObject();
		}
		_json.WriteEndArray();
	}

	/// <summary>A whole number, unless it is <paramref name="unless"/>.</summary>
	public void Int(string name, int value, int unless = 0)
	{
		if (value != unless) _json.WriteNumber(name, value);
	}

	/// <summary>A number, always.</summary>
	public void Number(string name, double value) => _json.WriteNumber(name, double.IsFinite(value) ? value : 0);

	/// <summary><see langword="true"/>, or nothing.</summary>
	public void Bool(string name, bool value)
	{
		if (value) _json.WriteBoolean(name, true);
	}

	/// <summary>A string, unless null.</summary>
	public void String(string name, string? value)
	{
		if (value is not null) _json.WriteString(name, value);
	}

	/// <summary>An enum value by its lower-case name, unless it is <paramref name="unless"/>.</summary>
	public void Enum<TEnum>(string name, TEnum value, TEnum unless = default) where TEnum : struct, Enum
	{
		if (!EqualityComparer<TEnum>.Default.Equals(value, unless)) _json.WriteString(name, value.ToString().ToLowerInvariant());
	}

	/// <summary>A markup layer, as one character carrying it.</summary>
	public void Markup(string name, IMarkup value)
	{
		ArgumentNullException.ThrowIfNull(value);
		Text(name, MarkupText.Wrap(value, "#"));
	}

	/// <summary>A gradient, unless null: its space, run, and stops.</summary>
	public void Gradient(string name, ColorGradient? gradient)
	{
		if (gradient is null) return;
		_json.WriteStartObject(name);
		_json.WriteString("sp", gradient.Space.ToString().ToLowerInvariant());
		if (gradient.Mirror) _json.WriteBoolean("mi", true);
		if (gradient.Repeat != 1) _json.WriteNumber("re", gradient.Repeat);
		_json.WriteStartArray("st");
		foreach (var stop in gradient.Stops.IsDefault ? [] : gradient.Stops)
			MarkupTextSerializer.Write(_json, MarkupText.Wrap(stop, "#"), _registry);
		_json.WriteEndArray();
		_json.WriteEndObject();
	}

	private static readonly (string Key, Func<BorderStyle, MarkupText> Get)[] BorderPieces =
	[
		("tl", b => b.TopLeft), ("t", b => b.Top), ("tr", b => b.TopRight), ("l", b => b.Left), ("r", b => b.Right),
		("bl", b => b.BottomLeft), ("b", b => b.Bottom), ("br", b => b.BottomRight), ("el", b => b.TeeLeft),
		("er", b => b.TeeRight), ("o", b => b.TitleOpen), ("c", b => b.TitleClose),
	];

	private static readonly (string Key, Func<TreeGuide, MarkupText> Get)[] GuidePieces =
	[
		("b", g => g.Branch), ("l", g => g.Last), ("p", g => g.Pipe), ("e", g => g.Blank),
	];

	/// <summary>A border, unless null: its preset's name and the pieces that differ from that preset.</summary>
	public void Border(string name, BorderStyle? border)
	{
		if (border is null) return;
		_json.WriteStartObject(name);
		_json.WriteString("n", border.Name);
		var preset = BorderStyle.Preset(border.Name) ?? BorderStyle.None;
		foreach (var (key, get) in BorderPieces)
			if (!Same(get(border), get(preset))) Text(key, get(border));
		_json.WriteEndObject();
	}

	/// <summary>A tree guide, unless null: its preset's name and the pieces that differ from that preset.</summary>
	public void Guide(string name, TreeGuide? guide)
	{
		if (guide is null) return;
		_json.WriteStartObject(name);
		_json.WriteString("n", guide.Name);
		var preset = TreeGuide.Preset(guide.Name) ?? TreeGuide.None;
		foreach (var (key, get) in GuidePieces)
			if (!Same(get(guide), get(preset))) Text(key, get(guide));
		_json.WriteEndObject();
	}

	/// <summary>A theme: what it sets.</summary>
	public void Theme(string name, LayoutTheme theme)
	{
		ArgumentNullException.ThrowIfNull(theme);
		_json.WriteStartObject(name);
		Border("bs", theme.Border);
		Guide("gd", theme.Guide);
		Text("gf", theme.GaugeFilled);
		Text("ge", theme.GaugeEmpty);
		Text("go", theme.GaugeOpen);
		Text("gc", theme.GaugeClose);
		Text("bu", theme.Bullet);
		Text("fs", theme.FieldSeparator);
		Text("hr", theme.HeaderRule);
		if (theme.BorderColor is { } cb) Markup("cb", cb);
		if (theme.TitleColor is { } ct) Markup("ct", ct);
		if (theme.HeadingColor is { } ch) Markup("ch", ch);
		if (theme.LabelColor is { } cl) Markup("cl", cl);
		if (theme.SeparatorColor is { } cs) Markup("cs", cs);
		if (theme.BulletColor is { } cu) Markup("cu", cu);
		if (theme.GuideColor is { } cg) Markup("cg", cg);
		if (theme.HeaderRuleColor is { } cr) Markup("cr", cr);
		if (theme.GaugeFilledColor is { } cf) Markup("cf", cf);
		if (theme.GaugeEmptyColor is { } ce) Markup("ce", ce);
		if (theme.StripeColor is { } cz) Markup("cz", cz);
		_json.WriteEndObject();
	}

	/// <summary>
	/// Whether <paramref name="piece"/> is the preset's own: the same characters with no markup.
	/// <see cref="MarkupText.Equals(MarkupText)"/> compares plain text only, so a coloured piece would
	/// otherwise be taken for the preset's and its colour lost.
	/// </summary>
	private static bool Same(MarkupText piece, MarkupText preset) =>
		ReferenceEquals(piece, preset)
		|| (piece.Text == preset.Text && piece.Runs.IsDefaultOrEmpty && preset.Runs.IsDefaultOrEmpty);
}

/// <summary>Reads back what a <see cref="BlockWriter"/> wrote. Each method returns null, or the default, for a value not there.</summary>
public sealed class BlockReader
{
	/// <summary>How deep blocks read back may nest, so a hostile payload cannot exhaust the stack.</summary>
	private const int MaxDepth = 64;

	private readonly JsonElement _element;
	private readonly State _state;
	private readonly int _depth;

	internal sealed class State(MarkupRegistry? registry)
	{
		public MarkupRegistry? Registry { get; } = registry;

		/// <summary>Whether a block was of a kind no codec reads, or nested too deep.</summary>
		public bool Incomplete { get; set; }
	}

	internal BlockReader(JsonElement element, State state, int depth)
	{
		_element = element;
		_state = state;
		_depth = depth;
	}

	private static readonly Block Nothing = new Stack([]);

	/// <summary>Reads the block this reader's object holds.</summary>
	internal Block Object()
	{
		if (_element.ValueKind != JsonValueKind.Object || _depth > MaxDepth) return Unread();
		var kind = String("t");
		if (kind is null || LayoutJson.CodecFor(kind, _state.Registry) is not { } codec) return Unread();
		return codec.Read(this);
	}

	private Block Unread()
	{
		_state.Incomplete = true;
		return Nothing;
	}

	private BlockReader Inner(JsonElement element) => new(element, _state, _depth + 1);

	private bool TryGet(string name, JsonValueKind kind, out JsonElement value)
	{
		value = default;
		return _element.ValueKind == JsonValueKind.Object && _element.TryGetProperty(name, out value) && value.ValueKind == kind;
	}

	/// <summary>Text.</summary>
	public MarkupText? Text(string name) =>
		TryGet(name, JsonValueKind.Object, out var value) ? MarkupTextSerializer.Read(value, _state.Registry) : null;

	/// <summary>Texts; empty when there are none.</summary>
	public ImmutableArray<MarkupText> Texts(string name) =>
		TryGet(name, JsonValueKind.Array, out var list)
			? [.. list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(item => MarkupTextSerializer.Read(item, _state.Registry))]
			: [];

	/// <summary>A child block.</summary>
	public Block? Block(string name) =>
		_element.ValueKind == JsonValueKind.Object && _element.TryGetProperty(name, out var value) ? Inner(value).Object() : null;

	/// <summary>Child blocks; empty when there are none.</summary>
	public ImmutableArray<Block> Blocks(string name) =>
		TryGet(name, JsonValueKind.Array, out var list) ? [.. list.EnumerateArray().Select(item => Inner(item).Object())] : [];

	/// <summary>Objects of your own, each read by <paramref name="item"/>; empty when there are none.</summary>
	public ImmutableArray<T> Array<T>(string name, Func<BlockReader, T> item)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (!TryGet(name, JsonValueKind.Array, out var list)) return [];
		if (_depth > MaxDepth)
		{
			_state.Incomplete = true;
			return [];
		}
		return [.. list.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object).Select(value => item(Inner(value)))];
	}

	/// <summary>A whole number.</summary>
	public int? Int(string name) =>
		TryGet(name, JsonValueKind.Number, out var value) && value.TryGetInt32(out var number) ? number : null;

	/// <summary>A whole number held to <paramref name="min"/> to <paramref name="max"/>, or <paramref name="fallback"/>.</summary>
	public int Int(string name, int fallback, int min, int max) => Math.Clamp(Int(name) ?? fallback, min, max);

	/// <summary>A finite number.</summary>
	public double? Number(string name) =>
		TryGet(name, JsonValueKind.Number, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;

	/// <summary>Whether <see langword="true"/> was written.</summary>
	public bool Bool(string name) => TryGet(name, JsonValueKind.True, out _);

	/// <summary>A string.</summary>
	public string? String(string name) => TryGet(name, JsonValueKind.String, out var value) ? value.GetString() : null;

	/// <summary>An enum value by name, or <paramref name="fallback"/>.</summary>
	public TEnum Enum<TEnum>(string name, TEnum fallback = default) where TEnum : struct, Enum =>
		System.Enum.TryParse<TEnum>(String(name), ignoreCase: true, out var value) && System.Enum.IsDefined(value) ? value : fallback;

	/// <summary>A markup layer, from the character it was written on.</summary>
	public IMarkup? Markup(string name) =>
		Text(name) is { Runs.IsDefaultOrEmpty: false } text && text.Runs[0].Markups.Count > 0 ? text.Runs[0].Markups[0] : null;

	/// <summary>A gradient.</summary>
	public ColorGradient? Gradient(string name)
	{
		if (!TryGet(name, JsonValueKind.Object, out var value)) return null;
		var inner = Inner(value);
		var stops = inner.Texts("st")
			.SelectMany(text => text.Runs.SelectMany(run => run.Markups).OfType<IColorMarkup>().Take(1))
			.ToImmutableArray();
		return new ColorGradient(stops, inner.Enum("sp", GradientSpace.Oklch))
		{
			Mirror = inner.Bool("mi"),
			Repeat = inner.Int("re", 1, 1, 1000),
		};
	}

	/// <summary>A border.</summary>
	public BorderStyle? Border(string name)
	{
		if (!TryGet(name, JsonValueKind.Object, out var value)) return null;
		var inner = Inner(value);
		var style = BorderStyle.Preset(inner.String("n") ?? string.Empty) ?? BorderStyle.None;
		MarkupText Piece(string key, MarkupText fallback) => inner.Text(key) ?? fallback;
		return style with
		{
			TopLeft = Piece("tl", style.TopLeft),
			Top = Piece("t", style.Top),
			TopRight = Piece("tr", style.TopRight),
			Left = Piece("l", style.Left),
			Right = Piece("r", style.Right),
			BottomLeft = Piece("bl", style.BottomLeft),
			Bottom = Piece("b", style.Bottom),
			BottomRight = Piece("br", style.BottomRight),
			TeeLeft = Piece("el", style.TeeLeft),
			TeeRight = Piece("er", style.TeeRight),
			TitleOpen = Piece("o", style.TitleOpen),
			TitleClose = Piece("c", style.TitleClose),
		};
	}

	/// <summary>A tree guide.</summary>
	public TreeGuide? Guide(string name)
	{
		if (!TryGet(name, JsonValueKind.Object, out var value)) return null;
		var inner = Inner(value);
		var style = TreeGuide.Preset(inner.String("n") ?? string.Empty) ?? TreeGuide.None;
		MarkupText Piece(string key, MarkupText fallback) => inner.Text(key) ?? fallback;
		return style with
		{
			Name = inner.String("n") ?? style.Name,
			Branch = Piece("b", style.Branch),
			Last = Piece("l", style.Last),
			Pipe = Piece("p", style.Pipe),
			Blank = Piece("e", style.Blank),
		};
	}

	/// <summary>A theme; one that sets nothing when there is none.</summary>
	public LayoutTheme Theme(string name)
	{
		if (!TryGet(name, JsonValueKind.Object, out var value)) return LayoutTheme.Default;
		var inner = Inner(value);
		return new LayoutTheme
		{
			Border = inner.Border("bs"),
			Guide = inner.Guide("gd"),
			GaugeFilled = inner.Text("gf"),
			GaugeEmpty = inner.Text("ge"),
			GaugeOpen = inner.Text("go"),
			GaugeClose = inner.Text("gc"),
			Bullet = inner.Text("bu"),
			FieldSeparator = inner.Text("fs"),
			HeaderRule = inner.Text("hr"),
			BorderColor = inner.Markup("cb"),
			TitleColor = inner.Markup("ct"),
			HeadingColor = inner.Markup("ch"),
			LabelColor = inner.Markup("cl"),
			SeparatorColor = inner.Markup("cs"),
			BulletColor = inner.Markup("cu"),
			GuideColor = inner.Markup("cg"),
			HeaderRuleColor = inner.Markup("cr"),
			GaugeFilledColor = inner.Markup("cf"),
			GaugeEmptyColor = inner.Markup("ce"),
			StripeColor = inner.Markup("cz"),
		};
	}

	/// <summary>The hex number written under <paramref name="name"/>, or zero.</summary>
	internal ulong Hex(string name) =>
		String(name) is { } hex && ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) ? value : 0UL;
}
