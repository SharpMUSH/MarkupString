using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>
/// Lays a <see cref="Block"/> tree out as lines of text at a given width, and wraps the result in the
/// <see cref="LayoutMarkup"/> a richer format draws instead.
/// </summary>
public static class BlockLayout
{
	/// <summary>
	/// Lays <paramref name="root"/> out at <paramref name="width"/> and returns the lines, joined by
	/// newlines, under a <see cref="LayoutMarkup"/> that carries the tree.
	/// </summary>
	/// <param name="root">The tree.</param>
	/// <param name="width">The width in display cells.</param>
	/// <param name="fluid">Whether a reader may lay it out again at a width of its own (<see cref="Relayout"/>).</param>
	/// <param name="context">How to draw it; <see cref="LayoutContext.Default"/> when null.</param>
	public static MarkupText Build(Block root, int width, bool fluid = false, LayoutContext? context = null)
	{
		ArgumentNullException.ThrowIfNull(root);
		// The tree stays as it was given; only the lines are folded, by Lines.
		var text = MarkupText.Join(MarkupText.NewLine, Lines(root, width, context));
		return text.Length == 0 ? text : MarkupText.Wrap(new LayoutMarkup(root, width, fluid, text.Text), text);
	}

	/// <summary>
	/// Lays <paramref name="block"/> out at <paramref name="width"/> display cells, one entry per line, its
	/// text folded first when the context has a <see cref="LayoutContext.Fold"/>.
	/// </summary>
	public static IReadOnlyList<MarkupText> Lines(Block block, int width, LayoutContext? context = null)
	{
		ArgumentNullException.ThrowIfNull(block);
		context ??= LayoutContext.Default;
		return context.Lines(context.Fold is { } fold ? block.MapText(fold.Fold) : block, width);
	}

	/// <summary>
	/// The tree <paramref name="content"/> was laid out from when the whole of it is one intact block,
	/// so a layout built from it nests that block rather than quoting its text; otherwise a
	/// <see cref="TextBlock"/> holding it.
	/// </summary>
	public static Block AsBlock(MarkupText content)
	{
		ArgumentNullException.ThrowIfNull(content);
		foreach (var region in BlockRegions.Find(content))
			if (region.Start == 0 && region.End == content.Length && region.Markup is LayoutMarkup layout && region.Intact)
				return layout.Root;
		return new TextBlock(content);
	}

	/// <summary>
	/// <paramref name="content"/> as a sequence of blocks: each intact block standing on lines of its own
	/// as the tree it was laid out from, and the text between blocks as <see cref="TextBlock"/>s, the line
	/// ending that separates text from a block dropped. How a builder that takes a body nests the
	/// blocks inside it — a box whose body holds columns, then a rule, then text.
	/// </summary>
	/// <param name="content">The text.</param>
	/// <param name="alignment">The alignment the text between blocks is given, or none of its own.</param>
	public static IReadOnlyList<Block> Blocks(MarkupText content, Alignment? alignment = null)
	{
		ArgumentNullException.ThrowIfNull(content);
		var blocks = new List<Block>();
		var position = 0;
		foreach (var region in BlockRegions.Find(content))
		{
			if (region.Markup is not LayoutMarkup layout || !region.Intact || !region.Standalone) continue;
			AddText(content, position, region.Start, alignment, blocks);
			blocks.Add(layout.Root);
			position = region.End;
		}
		AddText(content, position, content.Length, alignment, blocks);
		return blocks;
	}

	private static void AddText(MarkupText content, int start, int end, Alignment? alignment, List<Block> blocks)
	{
		var text = content.Text;
		if (start > 0 && start < end && text[start] == '\r') start++;
		if (start > 0 && start < end && text[start] == '\n') start++;
		if (end < text.Length && end > start && text[end - 1] == '\n') end--;
		if (end < text.Length && end > start && text[end - 1] == '\r') end--;
		if (end <= start) return;
		blocks.Add(new TextBlock(content.Substring(start, end - start)) { Alignment = alignment });
	}

	/// <summary>
	/// <paramref name="text"/> with every intact block laid out again under <paramref name="context"/>:
	/// a <see cref="LayoutMarkup.Fluid"/> block at <paramref name="width"/>, any other at its own width.
	/// A block that was cut, edited or shares a line with other text is left as it is.
	/// </summary>
	/// <param name="text">The text.</param>
	/// <param name="width">The reader's width, or zero or less to keep every block's own.</param>
	/// <param name="context">The reader: ASCII only, a screen reader, a look.</param>
	public static MarkupText Relayout(MarkupText text, int width, LayoutContext context)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(context);

		var edits = new List<(int Start, int Length, MarkupText Replacement)>();
		foreach (var region in BlockRegions.Find(text))
		{
			if (region.Markup is not LayoutMarkup layout || !region.Intact || !region.Standalone) continue;
			var target = layout.Fluid && width > 0 ? width : layout.Width;
			if (target == layout.Width && context == LayoutContext.Default) continue;
			edits.Add((region.Start, region.End - region.Start, Build(layout.Root, target, layout.Fluid, context)));
		}

		for (var i = edits.Count - 1; i >= 0; i--)
			text = text.Replace(edits[i].Start, edits[i].Length, edits[i].Replacement);
		return text;
	}
}

/// <summary>Finds the stretches of a text each block layer covers.</summary>
internal static class BlockRegions
{
	internal readonly record struct Region(int Start, int End, IBlockMarkup Markup, bool Intact, bool Standalone);

	/// <summary>
	/// Every block region in <paramref name="text"/>, outermost layers first where two start together,
	/// in order. A region is the run of adjacent runs that carry an equal layer.
	/// </summary>
	internal static IEnumerable<Region> Find(MarkupText text)
	{
		var runs = text.Runs;
		for (var i = 0; i < runs.Length; i++)
		{
			if (Outermost(runs[i].Markups) is not { } block) continue;
			if (i > 0 && runs[i - 1].End == runs[i].Start && Contains(runs[i - 1].Markups, block)) continue;
			var last = Extent(runs, i, block);
			yield return Describe(text, runs[i].Start, runs[last].End, block);
			i = last;
		}
	}

	/// <summary>The index of the last run of the region <paramref name="block"/> starts at run <paramref name="first"/>.</summary>
	internal static int Extent(ImmutableArray<Run> runs, int first, IMarkup block)
	{
		var last = first;
		while (last + 1 < runs.Length && runs[last + 1].Start == runs[last].End && Contains(runs[last + 1].Markups, block)) last++;
		return last;
	}

	internal static Region Describe(MarkupText text, int start, int end, IBlockMarkup block)
	{
		var content = text.Text.AsSpan();
		var standalone = (start == 0 || content[start - 1] == '\n')
			&& (end == content.Length || content[end] is '\n' or '\r');
		return new Region(start, end, block, block.Covers(content[start..end]), standalone);
	}

	internal static bool Contains(MarkupSet markups, IMarkup markup)
	{
		foreach (var item in markups)
			if (Equals(item, markup)) return true;
		return false;
	}

	private static IBlockMarkup? Outermost(MarkupSet markups)
	{
		for (var i = markups.Count - 1; i >= 0; i--)
			if (markups[i] is IBlockMarkup block) return block;
		return null;
	}
}
