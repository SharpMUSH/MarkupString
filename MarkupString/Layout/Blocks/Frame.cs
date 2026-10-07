namespace MarkupString.Layout;

/// <summary>A line across the width, with an optional title set into it.</summary>
/// <param name="Title">The title, or none.</param>
public sealed record Rule(MarkupText? Title = null) : Block
{
	/// <summary>Whose horizontal edge and title brackets the line is drawn with; unset, the frame's around it or the theme's.</summary>
	public BorderStyle? Border { get; init; }

	/// <summary>Where the title sits along the line.</summary>
	public Alignment TitleAlignment { get; init; } = Alignment.Center;

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		var style = context.Paint(context.Border(Border));
		var title = Title is null ? null : context.Paint(theme => theme.TitleColor, Title);
		lines.Add(BlockText.Edge(MarkupText.Empty, style.Top, MarkupText.Empty, title, TitleAlignment, style, width));
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Title is { Length: > 0 } title) lines.Add(title);
	}
}

/// <summary>A frame around <paramref name="Body"/>, its title set into the top edge.</summary>
/// <param name="Body">What is framed. A <see cref="Stack"/>'s <see cref="Rule"/> children become dividers that meet the sides.</param>
/// <remarks>Usually made with <see cref="BlockExtensions.Bordered"/>.</remarks>
public sealed record Frame(Block Body) : Block
{
	/// <summary>The characters the frame is drawn with; unset, the theme's.</summary>
	public BorderStyle? Border { get; init; }

	/// <summary>The title in the top edge, or none.</summary>
	public MarkupText? Title { get; init; }

	/// <summary>Where the title sits along the edge.</summary>
	public Alignment TitleAlignment { get; init; } = Alignment.Center;

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

		if (!none || Title is { Length: > 0 })
			lines.Add(BlockText.Edge(style.TopLeft, style.Top, style.TopRight, Title is null ? null : context.Paint(theme => theme.TitleColor, Title), TitleAlignment, style, width));

		var body = new List<MarkupText>();
		foreach (var child in Parts)
		{
			if (child is Rule rule)
			{
				if (none && rule.Title is not { Length: > 0 }) continue;
				var ruleStyle = context.Paint(context.Border(rule.Border ?? Border));
				var ruleTitle = rule.Title is null ? null : context.Paint(theme => theme.TitleColor, rule.Title);
				lines.Add(BlockText.Edge(style.TeeLeft, ruleStyle.Top, style.TeeRight, ruleTitle, rule.TitleAlignment, ruleStyle, width));
				continue;
			}

			body.Clear();
			context.Draw(child, inner, body);
			foreach (var line in body)
				lines.Add(MarkupText.Concat([style.Left, padding, BlockText.Fit(line, inner), padding, style.Right]));
		}

		if (!none) lines.Add(BlockText.Edge(style.BottomLeft, style.Bottom, style.BottomRight, null, Alignment.Center, style, width));
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Title is { Length: > 0 } title) lines.Add(title);
		foreach (var child in Parts) context.Draw(child, width, lines);
	}
}
