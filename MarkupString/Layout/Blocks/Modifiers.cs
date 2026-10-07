using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>Text inside <paramref name="Content"/> that sets no alignment of its own sits as <paramref name="Alignment"/> says.</summary>
/// <param name="Content">What is aligned.</param>
/// <param name="Alignment">Where its text sits.</param>
public sealed record Aligned(Block Content, Alignment Alignment) : Block
{
	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines) =>
		(context with { TextAlignment = Alignment }).Draw(Content, width, lines);
}

/// <summary>
/// <paramref name="Content"/> drawn in the colours of <paramref name="Gradient"/>: its borders, rules
/// and text alike. Spaces are left alone, and colour the content sets itself is kept.
/// </summary>
/// <param name="Content">What is shaded.</param>
/// <param name="Gradient">The colours.</param>
public sealed record Shaded(Block Content, ColorGradient Gradient) : Block
{
	/// <summary>Which way the colours run.</summary>
	public GradientFlow Flow { get; init; } = GradientFlow.Across;

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines) =>
		lines.AddRange(Gradient.ShadeLines(context.Lines(Content, width), Flow, width));
}

/// <summary><paramref name="Content"/> drawn under <paramref name="Markup"/> — a colour, say — which colour it sets itself overrides.</summary>
/// <param name="Content">What is coloured.</param>
/// <param name="Markup">The layer laid over each of its lines.</param>
public sealed record Colored(Block Content, IMarkup Markup) : Block
{
	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		foreach (var line in context.Lines(Content, width)) lines.Add(MarkupText.Wrap(Markup, line));
	}
}

/// <summary><paramref name="Content"/> drawn in <paramref name="Theme"/>, over the theme around it.</summary>
/// <param name="Content">What is themed.</param>
/// <param name="Theme">What it sets; what it leaves unset comes from around it.</param>
public sealed record Themed(Block Content, LayoutTheme Theme) : Block
{
	/// <summary>
	/// Whether <see cref="Theme"/> only fills in what the theme around it leaves unset, rather than
	/// overriding it: a game's default look, under which the theme a reader draws it in still shows.
	/// </summary>
	public bool Fallback { get; init; }

	/// <summary>The theme the content is drawn in, given the one <paramref name="around"/> it.</summary>
	public LayoutTheme Within(LayoutTheme around)
	{
		ArgumentNullException.ThrowIfNull(around);
		return Fallback ? around.Over(Theme) : Theme.Over(around);
	}

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines) =>
		(context with { Theme = Within(context.Theme) }).Draw(Content, width, lines);
}

/// <summary>Builds blocks up: a frame round one, a size in a row, a colour, a look.</summary>
public static class BlockExtensions
{
	/// <summary>A <see cref="Frame"/> round <paramref name="body"/>.</summary>
	/// <param name="body">What is framed.</param>
	/// <param name="title">The title in the top edge, or none.</param>
	/// <param name="border">The frame's characters; unset, the theme's.</param>
	public static Frame Bordered(this Block body, MarkupText? title = null, BorderStyle? border = null) =>
		new(body) { Title = title, Border = border };

	/// <summary><paramref name="content"/>, asking a <see cref="Flex"/> for a width.</summary>
	/// <param name="content">What is sized.</param>
	/// <param name="basis">The width it asks for; <see cref="BlockSize.Auto"/> shares what the others leave.</param>
	/// <param name="min">The fewest cells it can be drawn in; below this the row stacks.</param>
	/// <param name="grow">Its share of the spare width.</param>
	public static Sized Sized(this Block content, BlockSize basis = default, int min = 1, int grow = 1) =>
		new(content) { Basis = basis, Min = min, Grow = grow };

	/// <summary><paramref name="content"/> with its text, where it sets none of its own, sitting as <paramref name="alignment"/> says.</summary>
	public static Aligned Aligned(this Block content, Alignment alignment) => new(content, alignment);

	/// <summary><paramref name="content"/> in the colours of <paramref name="gradient"/>, running <paramref name="flow"/>.</summary>
	public static Shaded Shaded(this Block content, ColorGradient gradient, GradientFlow flow = GradientFlow.Across) =>
		new(content, gradient) { Flow = flow };

	/// <summary><paramref name="content"/> under <paramref name="markup"/>.</summary>
	public static Colored Colored(this Block content, IMarkup markup) => new(content, markup);

	/// <summary><paramref name="content"/> in <paramref name="theme"/>, over the theme around it.</summary>
	public static Themed Themed(this Block content, LayoutTheme theme) => new(content, theme);

	/// <summary><paramref name="content"/> in <paramref name="theme"/> wherever the theme around it sets nothing (<see cref="Layout.Themed.Fallback"/>).</summary>
	public static Themed ThemedUnder(this Block content, LayoutTheme theme) => new(content, theme) { Fallback = true };

	/// <summary><paramref name="text"/> as a block, to build on.</summary>
	public static TextBlock ToBlock(this MarkupText text) => new(text);

	/// <summary>Blocks one under the other.</summary>
	public static Stack Stacked(this IEnumerable<Block> blocks) => new([.. blocks]);

	/// <summary>Blocks side by side.</summary>
	public static Flex InRow(this IEnumerable<Block> blocks) => new([.. blocks]);
}
