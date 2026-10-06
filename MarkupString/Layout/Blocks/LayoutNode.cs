using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>
/// One piece of a block layout: a box, a rule, columns, a picture, or text. A tree of these is laid
/// out as lines of text by <see cref="BlockLayout"/> and drawn as page structure by a format that
/// can (HTML), from the same description.
/// </summary>
/// <remarks>
/// A node has no width of its own. Widths come from the caller of <see cref="BlockLayout.Lines"/>
/// and are shared out by <see cref="FlexNode"/>, so the same tree fits a 78-column terminal, a
/// 40-column phone client and a browser window.
/// </remarks>
public abstract record LayoutNode
{
	private protected LayoutNode()
	{
	}
}

/// <summary>Text, word-wrapped to the width it is given. Line endings in it are kept.</summary>
/// <param name="Content">The text, with its own markup.</param>
/// <param name="Alignment">Where each line sits in the width.</param>
public sealed record TextNode(MarkupText Content, Alignment Alignment = Alignment.Left) : LayoutNode;

/// <summary>Its children, one under the other.</summary>
/// <remarks>Inside a <see cref="BoxNode"/>, a <see cref="RuleNode"/> child becomes a divider that meets the box's sides.</remarks>
public sealed record StackNode(ImmutableArray<LayoutNode> Children) : LayoutNode;

/// <summary>A frame around <paramref name="Body"/>, its title set into the top edge.</summary>
/// <param name="Body">What is framed.</param>
/// <param name="Border">The characters the frame is drawn with.</param>
/// <param name="Title">The title in the top edge, or none.</param>
/// <param name="TitleAlignment">Where the title sits along the edge.</param>
/// <param name="Padding">Spaces between each side and the body.</param>
public sealed record BoxNode(
	LayoutNode Body,
	BorderStyle Border,
	MarkupText? Title = null,
	Alignment TitleAlignment = Alignment.Center,
	int Padding = 1) : LayoutNode;

/// <summary>A line across the width, with an optional title set into it.</summary>
/// <param name="Title">The title, or none.</param>
/// <param name="Border">Whose horizontal edge and title brackets the line is drawn with.</param>
/// <param name="TitleAlignment">Where the title sits along the line.</param>
public sealed record RuleNode(
	MarkupText? Title,
	BorderStyle Border,
	Alignment TitleAlignment = Alignment.Center) : LayoutNode;

/// <summary>Items side by side, sharing the width, stacked instead when they do not fit.</summary>
public sealed record FlexNode(ImmutableArray<FlexItem> Items, FlexOptions Options) : LayoutNode;

/// <summary>
/// A picture, the text art a terminal shows instead of it, and the content that flows round it.
/// </summary>
/// <param name="Image">The picture: its address and description.</param>
/// <param name="Art">Text art standing in for the picture where pictures are not shown; its lines keep their own spacing. Empty for none.</param>
/// <param name="Float">Which side the picture sits on, with <paramref name="Beside"/> flowing round it.</param>
/// <param name="Beside">The content that flows round the picture, or none.</param>
/// <param name="Gap">Cells between the art and the content beside it.</param>
public sealed record FigureNode(
	ImageMarkup Image,
	MarkupText Art,
	FigureFloat Float = FigureFloat.None,
	LayoutNode? Beside = null,
	int Gap = 2) : LayoutNode;

/// <summary>Which side a figure sits on.</summary>
public enum FigureFloat
{
	/// <summary>On a line of its own, with what is beside it underneath.</summary>
	None,

	/// <summary>On the left, with what is beside it flowing down its right.</summary>
	Left,

	/// <summary>On the right, with what is beside it flowing down its left.</summary>
	Right,
}

/// <summary>One item of a <see cref="FlexNode"/>.</summary>
/// <param name="Content">What the item holds.</param>
/// <param name="Basis">The width it asks for; <see cref="BlockSize.Auto"/> shares out what the others leave.</param>
/// <param name="Min">The fewest cells it can be drawn in; below this the items stack.</param>
/// <param name="Grow">Its share of the spare width, against the other items'. Zero takes none.</param>
public sealed record FlexItem(LayoutNode Content, BlockSize Basis = default, int Min = 1, int Grow = 1);

/// <summary>How a <see cref="FlexNode"/> places its items.</summary>
public sealed record FlexOptions
{
	/// <summary>The default: side by side, a gap of two cells, stacking when they do not fit.</summary>
	public static FlexOptions Default { get; } = new();

	/// <summary>Cells between two items, when there is no <see cref="Separator"/>.</summary>
	public int Gap { get; init; } = 2;

	/// <summary>Drawn on every line between two items, in place of the gap: <c>" | "</c>, say.</summary>
	public MarkupText? Separator { get; init; }

	/// <summary>Where the spare width goes when no item grows into it.</summary>
	public FlexJustify Justify { get; init; }

	/// <summary>Where a shorter item sits against the tallest.</summary>
	public FlexAlign Align { get; init; }

	/// <summary>Always one under the other.</summary>
	public bool Vertical { get; init; }
}

/// <summary>Where the spare width of a <see cref="FlexNode"/> goes.</summary>
public enum FlexJustify
{
	/// <summary>After the last item.</summary>
	Start,

	/// <summary>Before the first item.</summary>
	End,

	/// <summary>Half before, half after.</summary>
	Center,

	/// <summary>Between the items.</summary>
	Between,
}

/// <summary>Where a shorter item sits against the tallest in a <see cref="FlexNode"/>.</summary>
public enum FlexAlign
{
	/// <summary>At the top.</summary>
	Start,

	/// <summary>In the middle.</summary>
	Center,

	/// <summary>At the bottom.</summary>
	End,
}

/// <summary>How wide something asks to be: shared out, a number of cells, or a share of the width.</summary>
public readonly record struct BlockSize
{
	private BlockSize(BlockSizeKind kind, int value)
	{
		Kind = kind;
		Value = value;
	}

	/// <summary>Whatever is left.</summary>
	public static BlockSize Auto => default;

	/// <summary>Exactly <paramref name="cells"/> display cells.</summary>
	public static BlockSize Cells(int cells) => new(BlockSizeKind.Cells, Math.Max(0, cells));

	/// <summary><paramref name="percent"/> per cent of the width.</summary>
	public static BlockSize Percent(int percent) => new(BlockSizeKind.Percent, Math.Clamp(percent, 0, 100));

	/// <summary>What kind of size this is.</summary>
	public BlockSizeKind Kind { get; }

	/// <summary>The cells or the percentage; zero for <see cref="BlockSizeKind.Auto"/>.</summary>
	public int Value { get; }

	/// <summary>The cells this asks for out of <paramref name="available"/>, or null for auto.</summary>
	public int? Resolve(int available) => Kind switch
	{
		BlockSizeKind.Cells => Value,
		BlockSizeKind.Percent => available * Value / 100,
		_ => null,
	};

	/// <summary>
	/// Reads <c>auto</c>, a number of cells (<c>35</c>) or a percentage (<c>40%</c>).
	/// </summary>
	public static bool TryParse(ReadOnlySpan<char> text, out BlockSize size)
	{
		text = text.Trim();
		size = Auto;
		if (text.IsEmpty || text.Equals("auto", StringComparison.OrdinalIgnoreCase)) return true;
		if (text[^1] == '%')
		{
			if (!int.TryParse(text[..^1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var percent) || percent > 100) return false;
			size = Percent(percent);
			return true;
		}
		if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var cells)) return false;
		size = Cells(cells);
		return true;
	}

	/// <inheritdoc/>
	public override string ToString() => Kind switch
	{
		BlockSizeKind.Cells => Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
		BlockSizeKind.Percent => Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%",
		_ => "auto",
	};
}

/// <summary>The kinds of <see cref="BlockSize"/>.</summary>
public enum BlockSizeKind
{
	/// <summary>Whatever is left.</summary>
	Auto,

	/// <summary>A number of display cells.</summary>
	Cells,

	/// <summary>A percentage of the width.</summary>
	Percent,
}

/// <summary>
/// Labelled values — <c>Sex: Male</c>, <c>Species: Human</c> — each label beside its value and the
/// values lined up in one column, a value that wraps keeping to that column.
/// </summary>
/// <param name="Fields">The labels and values, in order.</param>
/// <param name="Options">How they are laid out.</param>
/// <remarks>
/// Too narrow for a label beside its value, each label goes on a line of its own with its value
/// indented under it. With <see cref="FieldsOptions.Columns"/> above one the fields are dealt into
/// columns side by side, down each column first, and the columns stack when they do not fit.
/// </remarks>
public sealed record FieldsNode(ImmutableArray<Field> Fields, FieldsOptions Options) : LayoutNode;

/// <summary>One label and its value in a <see cref="FieldsNode"/>.</summary>
/// <param name="Label">The label; empty to carry on the value above.</param>
/// <param name="Value">The value.</param>
public sealed record Field(MarkupText Label, LayoutNode Value);

/// <summary>How a <see cref="FieldsNode"/> lays out its fields.</summary>
public sealed record FieldsOptions
{
	/// <summary>The default: labels on the left, <c>": "</c> after each, one column.</summary>
	public static FieldsOptions Default { get; } = new();

	/// <summary>Where a label sits in the label column: <see cref="Alignment.Left"/> or <see cref="Alignment.Right"/>.</summary>
	public Alignment LabelAlignment { get; init; } = Alignment.Left;

	/// <summary>Drawn after each label.</summary>
	public MarkupText Separator { get; init; } = MarkupText.Plain(": ");

	/// <summary>A pattern filling the label column after a short label — <c>.</c> draws <c>Name......: </c> — or none.</summary>
	public MarkupText? Leader { get; init; }

	/// <summary>How many columns the fields are dealt into.</summary>
	public int Columns { get; init; } = 1;

	/// <summary>Cells between two columns.</summary>
	public int Gap { get; init; } = 3;
}

/// <summary>
/// Items and the items under them, each level drawn under its parent with guide lines —
/// <c>├─ </c>, <c>└─ </c> — joining them. The top level sits at the left edge with no guide.
/// </summary>
/// <param name="Items">The top-level items.</param>
/// <param name="Guide">The characters the guide lines are drawn with.</param>
public sealed record TreeNode(ImmutableArray<TreeItem> Items, TreeGuide Guide) : LayoutNode;

/// <summary>One item of a <see cref="TreeNode"/> and the items under it.</summary>
/// <param name="Content">What the item shows.</param>
/// <param name="Children">The items under it, or none.</param>
public sealed record TreeItem(LayoutNode Content, ImmutableArray<TreeItem> Children = default);

/// <summary>
/// A bar showing <paramref name="Value"/> against <paramref name="Maximum"/> — hit points, a quest's
/// progress — filling the width it is given, with its label before and the figures after.
/// </summary>
/// <param name="Value">How much there is.</param>
/// <param name="Maximum">How much there could be; the bar is full at this.</param>
/// <param name="Label">What it measures, or none.</param>
/// <param name="Options">How it is drawn.</param>
public sealed record GaugeNode(double Value, double Maximum, MarkupText? Label, GaugeOptions Options) : LayoutNode;

/// <summary>How a <see cref="GaugeNode"/> is drawn.</summary>
public sealed record GaugeOptions
{
	/// <summary>The default: <c>[█████░░░░░] 50%</c>.</summary>
	public static GaugeOptions Default { get; } = new();

	/// <summary>The filled part, repeated.</summary>
	public MarkupText Filled { get; init; } = MarkupText.Plain("█");

	/// <summary>The empty part, repeated.</summary>
	public MarkupText Empty { get; init; } = MarkupText.Plain("░");

	/// <summary>Before the bar.</summary>
	public MarkupText Open { get; init; } = MarkupText.Plain("[");

	/// <summary>After the bar.</summary>
	public MarkupText Close { get; init; } = MarkupText.Plain("]");

	/// <summary>The figures written after the bar.</summary>
	public GaugeShow Show { get; init; } = GaugeShow.Percent;

	/// <summary>The bar's own width in cells, or zero to fill what the label and figures leave.</summary>
	public int BarWidth { get; init; }
}

/// <summary>The figures a <see cref="GaugeNode"/> writes after its bar.</summary>
public enum GaugeShow
{
	/// <summary><c>50%</c>.</summary>
	Percent,

	/// <summary><c>6/12</c>.</summary>
	Value,

	/// <summary>Nothing.</summary>
	None,
}

/// <summary>
/// A list, each item after its marker — a bullet, a dash, a number — and wrapping under itself, not
/// under the marker.
/// </summary>
/// <param name="Items">The items.</param>
/// <param name="Options">The markers.</param>
public sealed record BulletsNode(ImmutableArray<LayoutNode> Items, BulletOptions Options) : LayoutNode;

/// <summary>How a <see cref="BulletsNode"/> marks its items.</summary>
public sealed record BulletOptions
{
	/// <summary>The default: <c>•</c>.</summary>
	public static BulletOptions Default { get; } = new();

	/// <summary>What kind of marker.</summary>
	public BulletStyle Style { get; init; } = BulletStyle.Bullet;

	/// <summary>The marker for <see cref="BulletStyle.Custom"/>.</summary>
	public MarkupText? Marker { get; init; }

	/// <summary>The first number of a numbered list.</summary>
	public int Start { get; init; } = 1;
}

/// <summary>The markers a <see cref="BulletsNode"/> can use.</summary>
public enum BulletStyle
{
	/// <summary><c>•</c>, <c>*</c> in ASCII.</summary>
	Bullet,

	/// <summary><c>-</c>.</summary>
	Dash,

	/// <summary><c>*</c>.</summary>
	Star,

	/// <summary><c>1.</c>, <c>2.</c>, ...</summary>
	Number,

	/// <summary><c>a.</c>, <c>b.</c>, ...</summary>
	Alpha,

	/// <summary><c>i.</c>, <c>ii.</c>, ...</summary>
	Roman,

	/// <summary>No marker; the items indented.</summary>
	None,

	/// <summary><see cref="BulletOptions.Marker"/>.</summary>
	Custom,
}

/// <summary>
/// Short items in as many columns as fit, every column as wide as the widest item, filled down each
/// column first — the way <c>ls</c> lists files, and a name-only who list wants.
/// </summary>
/// <param name="Items">The items.</param>
/// <param name="Gap">Cells between two columns.</param>
/// <param name="Across">Fill along each row first instead.</param>
public sealed record GridNode(ImmutableArray<MarkupText> Items, int Gap = 2, bool Across = false) : LayoutNode;

/// <summary>
/// Rows under column headings. Columns grow to fit their widest cell; when the table is too wide, the
/// columns that may wrap give way first, then the least important columns are left out, and when
/// even one column will not fit each row becomes a card of labelled values.
/// </summary>
/// <param name="Columns">The columns, in order.</param>
/// <param name="Rows">The rows, a cell per column; a short row is padded with empty cells.</param>
/// <param name="Options">How it is drawn.</param>
public sealed record TableNode(ImmutableArray<TableColumn> Columns, ImmutableArray<ImmutableArray<LayoutNode>> Rows, TableOptions Options) : LayoutNode;

/// <summary>One column of a <see cref="TableNode"/>.</summary>
/// <param name="Header">The heading.</param>
/// <param name="Alignment">Where each cell's text sits.</param>
/// <param name="Min">The fewest cells the column is drawn in before it is left out.</param>
/// <param name="Max">The most cells it grows to, or zero for no limit.</param>
/// <param name="Priority">How important it is: when the table is too wide the highest number is left out first. One is never left out while another column could be.</param>
/// <param name="Wrap">Whether its cells may wrap onto more lines; a column that may not is cut instead.</param>
public sealed record TableColumn(MarkupText Header, Alignment Alignment = Alignment.Left, int Min = 1, int Max = 0, int Priority = 1, bool Wrap = true);

/// <summary>How a <see cref="TableNode"/> is drawn.</summary>
public sealed record TableOptions
{
	/// <summary>The default: two cells between columns, the headings underlined.</summary>
	public static TableOptions Default { get; } = new();

	/// <summary>Cells between two columns, when there is no <see cref="Separator"/>.</summary>
	public int Gap { get; init; } = 2;

	/// <summary>Drawn between two columns on every line instead of the gap.</summary>
	public MarkupText? Separator { get; init; }

	/// <summary>The line under the headings, repeated; empty for none.</summary>
	public MarkupText HeaderRule { get; init; } = MarkupText.Plain("-");
}
