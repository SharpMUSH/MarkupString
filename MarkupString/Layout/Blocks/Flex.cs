using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>Items side by side, sharing the width, stacked instead when they do not fit.</summary>
/// <param name="Items">The items; size one with <see cref="BlockExtensions.Sized"/>, or it shares out what the sized ones leave.</param>
public sealed record Flex(ImmutableArray<Block> Items) : Block
{
	/// <inheritdoc/>
	public override Block MapText(Func<MarkupText, MarkupText> map) =>
		this with { Items = BlockText.Map(Items, map) };

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

	/// <summary>The cells between two items.</summary>
	internal int Spacing => Separator?.DisplayWidth ?? Math.Max(0, Gap);

	/// <summary>How an item asks to be sized: as its <see cref="Sized"/> says, or sharing what is left.</summary>
	internal static Sized SizeOf(Block item) => item as Sized ?? new Sized(item);

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Items.IsDefaultOrEmpty) return;
		var widths = Vertical ? null : Share(width);
		if (widths is null)
		{
			DrawLinear(context, width, lines);
			return;
		}

		var columns = new List<MarkupText>[Items.Length];
		var height = 0;
		for (var i = 0; i < Items.Length; i++)
		{
			columns[i] = context.Lines(Items[i], widths[i]);
			height = Math.Max(height, columns[i].Count);
		}

		var spare = width - widths.Sum() - Spacing * (Items.Length - 1);
		var (lead, between) = Justify switch
		{
			FlexJustify.End => (spare, 0),
			FlexJustify.Center => (spare / 2, 0),
			FlexJustify.Between when Items.Length > 1 => (0, spare / (Items.Length - 1)),
			_ => (0, 0),
		};

		var divider = Separator is { } drawn ? context.AsciiOnly ? BorderStyle.AsciiText(drawn) ?? drawn : drawn : BlockText.Blank(Spacing);
		var parts = new List<MarkupText>(Items.Length * 2 + 2);
		for (var row = 0; row < height; row++)
		{
			parts.Clear();
			if (lead > 0) parts.Add(BlockText.Blank(lead));
			for (var i = 0; i < Items.Length; i++)
			{
				if (i > 0)
				{
					if (between > 0) parts.Add(BlockText.Blank(between));
					parts.Add(divider);
				}
				var offset = Align switch
				{
					FlexAlign.Center => (height - columns[i].Count) / 2,
					FlexAlign.End => height - columns[i].Count,
					_ => 0,
				};
				var index = row - offset;
				parts.Add(index >= 0 && index < columns[i].Count ? BlockText.Fit(columns[i][index], widths[i]) : BlockText.Blank(widths[i]));
			}
			lines.Add(BlockText.Fit(MarkupText.Concat(parts.ToArray().AsSpan()), width));
		}
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Items.IsDefaultOrEmpty) return;
		foreach (var item in Items) context.Draw(item, width, lines);
	}

	/// <summary>The width of each item, or null when they cannot sit side by side and must stack.</summary>
	private int[]? Share(int width)
	{
		var sizes = Items.Select(SizeOf).ToArray();
		var available = width - Spacing * (sizes.Length - 1);
		if (available < sizes.Length) return null;

		var widths = new int[sizes.Length];
		var weights = new int[sizes.Length];
		var fixedSum = 0;
		for (var i = 0; i < sizes.Length; i++)
		{
			if (sizes[i].Basis.Resolve(available) is { } cells)
			{
				widths[i] = cells;
				fixedSum += cells;
				weights[i] = Math.Max(0, sizes[i].Grow);
			}
			else
			{
				weights[i] = Math.Max(1, sizes[i].Grow);
			}
		}

		var remaining = available - fixedSum;
		if (remaining < 0) return null;

		var autos = Enumerable.Range(0, sizes.Length).Where(i => sizes[i].Basis.Kind == BlockSizeKind.Auto).ToArray();
		var growers = autos.Length > 0 ? autos : Enumerable.Range(0, sizes.Length).Where(i => weights[i] > 0).ToArray();
		var total = growers.Sum(i => weights[i]);
		if (total > 0 && remaining > 0)
		{
			var given = 0;
			foreach (var i in growers)
			{
				var share = remaining * weights[i] / total;
				widths[i] += share;
				given += share;
			}
			for (var k = 0; given < remaining; k = (k + 1) % growers.Length, given++) widths[growers[k]]++;
		}

		for (var i = 0; i < sizes.Length; i++)
			if (widths[i] < Math.Max(1, sizes[i].Min)) return null;
		return widths;
	}
}

/// <summary>
/// How wide <paramref name="Content"/> asks to be in a <see cref="Flex"/>. Anywhere else it is drawn
/// as its content.
/// </summary>
/// <param name="Content">What is sized.</param>
public sealed record Sized(Block Content) : Block
{
	/// <inheritdoc/>
	public override Block MapText(Func<MarkupText, MarkupText> map) =>
		this with { Content = Content.MapText(map) };

	/// <summary>The width it asks for; <see cref="BlockSize.Auto"/> shares out what the others leave.</summary>
	public BlockSize Basis { get; init; }

	/// <summary>The fewest cells it can be drawn in; below this the items stack.</summary>
	public int Min { get; init; } = 1;

	/// <summary>Its share of the spare width, against the other items'. Zero takes none.</summary>
	public int Grow { get; init; } = 1;

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines) => context.Draw(Content, width, lines);
}

/// <summary>Where the spare width of a <see cref="Flex"/> goes.</summary>
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

/// <summary>Where a shorter item sits against the tallest in a <see cref="Flex"/>.</summary>
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
