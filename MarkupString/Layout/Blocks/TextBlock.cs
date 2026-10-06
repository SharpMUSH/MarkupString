using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>Text, word-wrapped to the width it is given. Line endings in it are kept.</summary>
/// <param name="Content">The text, with its own markup.</param>
public sealed record TextBlock(MarkupText Content) : Block
{
	/// <summary>Where each line sits in the width; unset, where the context puts text (<see cref="LayoutContext.TextAlignment"/>).</summary>
	public Alignment? Alignment { get; init; }

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines) =>
		lines.AddRange(Content.FormatColumn(BlockText.Column(width, Alignment ?? context.TextAlignment)));

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines) =>
		lines.AddRange(Content.WrapLines(width));

	/// <summary>Its longest line once wrapped at <paramref name="width"/>, wherever it is aligned.</summary>
	public override BlockMeasure Measure(LayoutContext context, int width)
	{
		var natural = 0;
		foreach (var line in Content.FormatColumn(BlockText.Column(Math.Max(1, width), Layout.Alignment.Left)))
			natural = Math.Max(natural, line.Trim(TrimType.TrimEnd).DisplayWidth);
		return new BlockMeasure(1, natural);
	}
}

/// <summary>Its children, one under the other.</summary>
/// <remarks>As a <see cref="Frame"/>'s body, a <see cref="Rule"/> child becomes a divider that meets the frame's sides.</remarks>
public sealed record Stack(ImmutableArray<Block> Children) : Block
{
	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Children.IsDefault) return;
		foreach (var child in Children) context.Draw(child, width, lines);
	}
}
