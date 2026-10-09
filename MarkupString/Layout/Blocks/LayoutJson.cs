using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace MarkupString.Layout;

/// <summary>
/// The JSON form of a <see cref="LayoutMarkup"/>. Core's own, like the shared vocabulary, because the
/// text inside the tree carries markup of every kind and has to be written with the registry the
/// whole text is. Each block is written by its <see cref="BlockCodec"/>.
/// </summary>
internal static class LayoutJson
{
	public const string Kind = "layout";

	public static void Write(Utf8JsonWriter writer, LayoutMarkup layout, MarkupRegistry? registry)
	{
		writer.WriteNumber("w", layout.Width);
		if (layout.Fluid) writer.WriteBoolean("fl", true);
		writer.WriteNumber("l", layout.Length);
		writer.WriteString("h", layout.Hash.ToString("x16", CultureInfo.InvariantCulture));
		writer.WriteString("id", layout.Id.ToString("x16", CultureInfo.InvariantCulture));
		writer.WritePropertyName("n");
		new BlockWriter(writer, registry, layout.Width).Object(layout.Root);
	}

	public static IMarkup Read(JsonElement element, MarkupRegistry? registry)
	{
		var state = new BlockReader.State(registry);
		var layout = new BlockReader(element, state, 0);
		var root = layout.Block("n") ?? new Stack([]);
		// A tree that could not all be read must not be drawn in place of its text, or laid out again:
		// a length no text has keeps the block from ever counting as intact.
		var length = state.Incomplete ? -1 : layout.Int("l") ?? -1;
		return new LayoutMarkup(root, layout.Int("w") ?? 0, layout.Bool("fl"), length, layout.Hex("h"), layout.Hex("id"));
	}

	public static BlockCodec? CodecFor(Type type, MarkupRegistry? registry) =>
		ByType.TryGetValue(type, out var codec) ? codec : registry?.FindBlockCodec(type);

	public static BlockCodec? CodecFor(string kind, MarkupRegistry? registry) =>
		ByKind.TryGetValue(kind, out var codec) ? codec : registry?.FindBlockCodec(kind);

	/// <summary>Whether <paramref name="kind"/> names a built-in block, which a registered codec may not claim.</summary>
	public static bool IsBuiltIn(string kind) => ByKind.ContainsKey(kind);

	private static readonly BlockCodec[] BuiltIns =
	[
		BlockCodec.Create<TextBlock>("text",
			(b, w) =>
			{
				w.Text("c", b.Content);
				if (b.Alignment is { } alignment) w.String("a", Name(alignment));
			},
			r => new TextBlock(r.Text("c") ?? MarkupText.Empty) { Alignment = r.String("a") is null ? null : r.Enum("a", Alignment.Left) }),
		BlockCodec.Create<Stack>("stack", (b, w) => w.Blocks("ch", b.Children.IsDefault ? [] : b.Children), r => new Stack(r.Blocks("ch"))),
		BlockCodec.Create<Rule>("rule",
			(b, w) =>
			{
				w.Text("ti", b.Title);
				w.Border("bs", b.Border);
				w.Enum("ta", b.TitleAlignment, Alignment.Center);
				WriteTitles(w, "tt", b.Titles);
			},
			r => new Rule(r.Text("ti")) { Border = r.Border("bs"), TitleAlignment = r.Enum("ta", Alignment.Center), Titles = ReadTitles(r, "tt") }),
		BlockCodec.Create<Frame>("frame",
			(b, w) =>
			{
				w.Block("b", b.Body);
				w.Border("bs", b.Border);
				w.Text("ti", b.Title);
				w.Enum("ta", b.TitleAlignment, Alignment.Center);
				w.Int("p", b.Padding, 1);
				WriteTitles(w, "tt", b.Titles);
				WriteTitles(w, "bt", b.BottomTitles);
			},
			r => new Frame(r.Block("b") ?? new Stack([]))
			{
				Border = r.Border("bs"),
				Title = r.Text("ti"),
				TitleAlignment = r.Enum("ta", Alignment.Center),
				Padding = r.Int("p", 1, 0, 64),
				Titles = ReadTitles(r, "tt"),
				BottomTitles = ReadTitles(r, "bt"),
			}),
		BlockCodec.Create<Flex>("flex",
			(b, w) =>
			{
				w.Blocks("it", b.Items.IsDefault ? [] : b.Items);
				w.Int("g", b.Gap, 2);
				w.Text("s", b.Separator);
				w.Enum("j", b.Justify);
				w.Enum("a", b.Align);
				w.Bool("v", b.Vertical);
			},
			r => new Flex(r.Blocks("it"))
			{
				Gap = r.Int("g", 2, 0, 64),
				Separator = r.Text("s"),
				Justify = r.Enum<FlexJustify>("j"),
				Align = r.Enum<FlexAlign>("a"),
				Vertical = r.Bool("v"),
			}),
		BlockCodec.Create<Sized>("sized",
			(b, w) =>
			{
				w.Block("c", b.Content);
				if (b.Basis.Kind != BlockSizeKind.Auto) w.String("b", b.Basis.ToString());
				w.Int("m", b.Min, 1);
				w.Int("g", b.Grow, 1);
			},
			r => new Sized(r.Block("c") ?? new Stack([]))
			{
				Basis = BlockSize.TryParse(r.String("b"), out var basis) ? basis : BlockSize.Auto,
				Min = r.Int("m", 1, 0, 4096),
				Grow = r.Int("g", 1, 0, 1000),
			}),
		BlockCodec.Create<Figure>("figure",
			(b, w) =>
			{
				w.String("s", b.Image.Source);
				w.String("d", b.Image.Description);
				if (b.Image.Width is { } width) w.Int("w", width);
				if (b.Image.Height is { } height) w.Int("h", height);
				if (b.Art.Length > 0) w.Text("art", b.Art);
				w.Enum("f", b.Float);
				w.Block("bd", b.Beside);
				w.Int("g", b.Gap, 2);
			},
			r => new Figure(new ImageMarkup(r.String("s") ?? string.Empty, r.String("d"), r.Int("w"), r.Int("h")), r.Text("art") ?? MarkupText.Empty)
			{
				Float = r.Enum<FigureFloat>("f"),
				Beside = r.Block("bd"),
				Gap = r.Int("g", 2, 0, 64),
			}),
		BlockCodec.Create<Fields>("fields",
			(b, w) =>
			{
				w.Array("f", b.Items.IsDefault ? [] : b.Items, (field, fw) =>
				{
					fw.Text("k", field.Label);
					fw.Block("v", field.Value);
				});
				w.Enum("a", b.LabelAlignment, Alignment.Left);
				w.Text("s", b.Separator);
				w.Text("ld", b.Leader);
				w.Int("c", b.Columns, 1);
				w.Int("g", b.Gap, 3);
				w.Bool("sp", b.Striped);
			},
			r => new Fields(r.Array("f", fr => new Field(fr.Text("k") ?? MarkupText.Empty, fr.Block("v") ?? new Stack([]))))
			{
				LabelAlignment = r.Enum("a", Alignment.Left),
				Separator = r.Text("s"),
				Leader = r.Text("ld"),
				Columns = r.Int("c", 1, 1, 64),
				Gap = r.Int("g", 3, 0, 64),
				Striped = r.Bool("sp"),
			}),
		BlockCodec.Create<Tree>("tree",
			(b, w) =>
			{
				WriteTreeItems(w, b.Items);
				w.Guide("gd", b.Guide);
			},
			r => new Tree(ReadTreeItems(r)) { Guide = r.Guide("gd") }),
		BlockCodec.Create<Gauge>("gauge",
			(b, w) =>
			{
				w.Number("v", b.Value);
				w.Number("m", b.Maximum);
				w.Text("k", b.Label);
				w.Text("f", b.Filled);
				w.Text("e", b.Empty);
				w.Text("o", b.Open);
				w.Text("c", b.Close);
				w.Enum("sh", b.Show);
				w.Int("bw", b.BarWidth);
				w.Gradient("gr", b.Gradient);
				w.Enum("sd", b.Shade);
			},
			r => new Gauge(r.Number("v") ?? 0, r.Number("m") ?? 0)
			{
				Label = r.Text("k"),
				Filled = r.Text("f"),
				Empty = r.Text("e"),
				Open = r.Text("o"),
				Close = r.Text("c"),
				Show = r.Enum<GaugeShow>("sh"),
				BarWidth = r.Int("bw", 0, 0, 4096),
				Gradient = r.Gradient("gr"),
				Shade = r.Enum<GaugeShade>("sd"),
			}),
		BlockCodec.Create<Bullets>("bullets",
			(b, w) =>
			{
				w.Blocks("it", b.Items.IsDefault ? [] : b.Items);
				w.Enum("st", b.Style);
				w.Text("mk", b.Marker);
				w.Int("s", b.Start, 1);
			},
			r => new Bullets(r.Blocks("it")) { Style = r.Enum<BulletStyle>("st"), Marker = r.Text("mk"), Start = r.Int("s", 1, -100000, 100000) }),
		BlockCodec.Create<Grid>("grid",
			(b, w) =>
			{
				w.Texts("it", b.Items.IsDefault ? [] : b.Items);
				w.Int("g", b.Gap, 2);
				w.Bool("ac", b.Across);
			},
			r => new Grid(r.Texts("it")) { Gap = r.Int("g", 2, 0, 64), Across = r.Bool("ac") }),
		BlockCodec.Create<Table>("table",
			(b, w) =>
			{
				w.Array("cols", b.Columns.IsDefault ? [] : b.Columns, (column, cw) =>
				{
					cw.Text("h", column.Header);
					cw.Enum("a", column.Alignment, Alignment.Left);
					cw.Int("mn", column.Min, 1);
					cw.Int("mx", column.Max);
					cw.Int("p", column.Priority, 1);
					cw.Bool("nw", !column.Wrap);
					cw.Int("gr", column.Grow);
				});
				w.Array("rows", b.Rows.IsDefault ? [] : b.Rows, (row, rw) => rw.Blocks("c", row.IsDefault ? [] : row));
				w.Int("g", b.Gap, 2);
				w.Text("s", b.Separator);
				w.Text("hr", b.HeaderRule);
				w.Bool("sp", b.Striped);
			},
			r => new Table(
				r.Array("cols", cr => new TableColumn(cr.Text("h") ?? MarkupText.Empty)
				{
					Alignment = cr.Enum("a", Alignment.Left),
					Min = cr.Int("mn", 1, 0, 4096),
					Max = cr.Int("mx", 0, 0, 4096),
					Priority = cr.Int("p", 1, 1, 1000),
					Wrap = !cr.Bool("nw"),
					Grow = cr.Int("gr", 0, 0, 1000),
				}),
				r.Array("rows", rr => rr.Blocks("c")))
			{
				Gap = r.Int("g", 2, 0, 64),
				Separator = r.Text("s"),
				HeaderRule = r.Text("hr"),
				Striped = r.Bool("sp"),
			}),
		BlockCodec.Create<Aligned>("aligned",
			(b, w) =>
			{
				w.Block("c", b.Content);
				w.String("a", Name(b.Alignment));
			},
			r => new Aligned(r.Block("c") ?? new Stack([]), r.Enum("a", Alignment.Left))),
		BlockCodec.Create<Shaded>("shaded",
			(b, w) =>
			{
				w.Block("c", b.Content);
				w.Gradient("gr", b.Gradient);
				w.Enum("fl", b.Flow, GradientFlow.Across);
			},
			r => new Shaded(r.Block("c") ?? new Stack([]), r.Gradient("gr") ?? new ColorGradient([])) { Flow = r.Enum("fl", GradientFlow.Across) }),
		BlockCodec.Create<Colored>("colored",
			(b, w) =>
			{
				w.Block("c", b.Content);
				w.Markup("m", b.Markup);
			},
			r => r.Markup("m") is { } markup ? new Colored(r.Block("c") ?? new Stack([]), markup) : new Colored(r.Block("c") ?? new Stack([]), NeutralMarkup.Instance)),
		BlockCodec.Create<Themed>("themed",
			(b, w) =>
			{
				w.Block("c", b.Content);
				w.Theme("th", b.Theme);
				w.Bool("fb", b.Fallback);
			},
			r => new Themed(r.Block("c") ?? new Stack([]), r.Theme("th")) { Fallback = r.Bool("fb") }),
	];

	private static readonly FrozenDictionary<string, BlockCodec> ByKind = BuiltIns.ToFrozenDictionary(codec => codec.Kind, StringComparer.Ordinal);

	private static readonly FrozenDictionary<Type, BlockCodec> ByType = BuiltIns.ToFrozenDictionary(codec => codec.BlockType);

	private static void WriteTreeItems(BlockWriter writer, System.Collections.Immutable.ImmutableArray<TreeItem> items) =>
		writer.Array("it", items.IsDefault ? [] : items, (item, iw) =>
		{
			iw.Block("c", item.Content);
			if (!item.Children.IsDefaultOrEmpty) WriteTreeItems(iw, item.Children);
		});

	private static System.Collections.Immutable.ImmutableArray<TreeItem> ReadTreeItems(BlockReader reader) =>
		reader.Array("it", item => new TreeItem(item.Block("c") ?? new Stack([]), ReadTreeItems(item)));

	/// <summary>Titles beside a rule's or a frame's own, each with its side; nothing when there are none.</summary>
	private static void WriteTitles(BlockWriter writer, string name, System.Collections.Immutable.ImmutableArray<EdgeTitle> titles)
	{
		if (titles.IsDefaultOrEmpty) return;
		writer.Array(name, titles, (title, tw) =>
		{
			tw.Text("t", title.Text);
			tw.Enum("s", title.Side, Alignment.Center);
		});
	}

	private static System.Collections.Immutable.ImmutableArray<EdgeTitle> ReadTitles(BlockReader reader, string name) =>
		reader.Array(name, title => new EdgeTitle(title.Text("t") ?? MarkupText.Empty, title.Enum("s", Alignment.Center)));

	private static string Name(Alignment alignment) => alignment.ToString().ToLowerInvariant();
}
