using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>A title set into a line or a frame's edge: against its left end, its right end, or in its middle.</summary>
/// <param name="Text">The title.</param>
/// <param name="Side"><see cref="Alignment.Left"/>, <see cref="Alignment.Right"/>, or the middle for any other value.</param>
public sealed record EdgeTitle(MarkupText Text, Alignment Side = Alignment.Center)
{
	/// <summary>
	/// How important it is, as a <see cref="TableColumn.Priority"/> is: when the titles do not fit, the highest number is
	/// left out first. Unset, it follows the side: 1 on the left, 2 on the right, 3 in the middle.
	/// </summary>
	public int? Priority { get; init; }
}

/// <summary>A line across the width, with titles set into it.</summary>
/// <param name="Title">The title, or none.</param>
/// <remarks>
/// <see cref="Titles"/> add more, each at its own side: one left, one in the middle and one right, or two on the right.
/// Titles on one side sit in the order given, a cell of the line apart. When they do not all fit, the middle ones go
/// first, then the right, then the left, and on a side the one farthest from its end goes first.
/// </remarks>
public sealed record Rule(MarkupText? Title = null) : Block
{
	/// <inheritdoc/>
	public override Block MapText(Func<MarkupText, MarkupText> map) =>
		this with { Title = Title is null ? null : map(Title), Titles = BlockText.Map(Titles, map) };

	/// <summary>Whose horizontal edge and title brackets the line is drawn with; unset, the frame's around it or the theme's.</summary>
	public BorderStyle? Border { get; init; }

	/// <summary>Where the title sits along the line.</summary>
	public Alignment TitleAlignment { get; init; } = Alignment.Center;

	/// <summary>More titles, after <see cref="Title"/>, each at its own side.</summary>
	public ImmutableArray<EdgeTitle> Titles { get; init; } = [];

	/// <summary><see cref="Title"/> at <see cref="TitleAlignment"/>, then <see cref="Titles"/>; the empty ones left out.</summary>
	internal IReadOnlyList<EdgeTitle> EdgeTitles => BlockText.Titles(Title, TitleAlignment, Titles);

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		var style = context.Paint(context.Border(Border));
		lines.Add(BlockText.Edge(MarkupText.Empty, style.Top, MarkupText.Empty, BlockText.Painted(context, EdgeTitles), style, width));
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (BlockText.Read(EdgeTitles) is { } titles) lines.Add(titles);
	}
}

/// <summary>A frame around <paramref name="Body"/>, its title set into the top edge.</summary>
/// <param name="Body">What is framed. A <see cref="Stack"/>'s <see cref="Rule"/> children become dividers that meet the sides.</param>
/// <remarks>Usually made with <see cref="BlockExtensions.Bordered"/>.</remarks>
public sealed record Frame(Block Body) : Block
{
	/// <inheritdoc/>
	public override Block MapText(Func<MarkupText, MarkupText> map) =>
		this with
		{
			Body = Body.MapText(map),
			Title = Title is null ? null : map(Title),
			Titles = BlockText.Map(Titles, map),
			BottomTitles = BlockText.Map(BottomTitles, map),
		};

	/// <summary>The characters the frame is drawn with; unset, the theme's.</summary>
	public BorderStyle? Border { get; init; }

	/// <summary>The title in the top edge, or none.</summary>
	public MarkupText? Title { get; init; }

	/// <summary>Where the title sits along the edge.</summary>
	public Alignment TitleAlignment { get; init; } = Alignment.Center;

	/// <summary>More titles in the top edge, after <see cref="Title"/>, each at its own side, as a <see cref="Rule"/>'s.</summary>
	public ImmutableArray<EdgeTitle> Titles { get; init; } = [];

	/// <summary>Titles in the bottom edge, each at its own side, as a <see cref="Rule"/>'s: a page count, a footnote.</summary>
	public ImmutableArray<EdgeTitle> BottomTitles { get; init; } = [];

	/// <summary>Spaces between each side and the body.</summary>
	public int Padding { get; init; } = 1;

	private Block[] Parts => Body is Stack { Children.IsDefault: false } stack ? [.. stack.Children] : [Body];

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		var style = context.Paint(context.Border(Border));
		var none = style.Name == BorderStyle.None.Name;
		var padding = BlockText.Blank(Padding);
		var inner = Math.Max(1, width - style.Left.DisplayWidth - style.Right.DisplayWidth - padding.DisplayWidth * 2);

		var top = BlockText.Titles(Title, TitleAlignment, Titles);
		var bottom = BlockText.Titles(null, Alignment.Center, BottomTitles);
		if (!none || top.Count > 0)
			lines.Add(BlockText.Edge(style.TopLeft, style.Top, style.TopRight, BlockText.Painted(context, top), style, width));

		var body = new List<MarkupText>();
		foreach (var child in Parts)
		{
			if (child is Rule rule)
			{
				var titles = rule.EdgeTitles;
				if (none && titles.Count == 0) continue;
				var ruleStyle = context.Paint(context.Border(rule.Border ?? Border));
				lines.Add(BlockText.Edge(style.TeeLeft, ruleStyle.Top, style.TeeRight, BlockText.Painted(context, titles), ruleStyle, width));
				continue;
			}

			body.Clear();
			context.Draw(child, inner, body);
			foreach (var line in body)
				lines.Add(MarkupText.Concat([style.Left, padding, BlockText.Fit(line, inner), padding, style.Right]));
		}

		if (!none || bottom.Count > 0)
			lines.Add(BlockText.Edge(style.BottomLeft, style.Bottom, style.BottomRight, BlockText.Painted(context, bottom), style, width));
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (BlockText.Read(BlockText.Titles(Title, TitleAlignment, Titles)) is { } top) lines.Add(top);
		foreach (var child in Parts) context.Draw(child, width, lines);
		if (BlockText.Read(BlockText.Titles(null, Alignment.Center, BottomTitles)) is { } bottom) lines.Add(bottom);
	}
}
