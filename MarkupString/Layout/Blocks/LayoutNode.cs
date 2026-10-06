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
