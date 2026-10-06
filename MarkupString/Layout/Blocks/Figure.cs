namespace MarkupString.Layout;

/// <summary>
/// A picture, the text art a terminal shows instead of it, and the content that flows round it.
/// </summary>
/// <param name="Image">The picture: its address and description.</param>
/// <param name="Art">Text art standing in for the picture where pictures are not shown; its lines keep their own spacing. Empty for none.</param>
public sealed record Figure(ImageMarkup Image, MarkupText Art) : Block
{
	/// <summary>Which side the picture sits on, with <see cref="Beside"/> flowing round it.</summary>
	public FigureFloat Float { get; init; }

	/// <summary>The content that flows round the picture, or none. Text flows round the art and widens once past it.</summary>
	public Block? Beside { get; init; }

	/// <summary>Cells between the art and the content beside it.</summary>
	public int Gap { get; init; } = 2;

	private string Description => Image.Description is { Length: > 0 } description ? description : Image.Source;

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Art.Length == 0)
		{
			lines.AddRange(MarkupText.Plain($"[{Description}]").FormatColumn(BlockText.Column(width, Alignment.Left)));
			if (Beside is { } after) context.Draw(after, width, lines);
			return;
		}

		var art = Art.Split("\n").Select(line => line.Text.EndsWith('\r') ? line.Substring(0, line.Length - 1) : line).ToArray();
		var artWidth = Math.Min(width, art.Max(line => line.DisplayWidth));
		var narrow = width - artWidth - Math.Max(0, Gap);

		if (Float == FigureFloat.None || Beside is null || narrow < 8)
		{
			foreach (var line in art) lines.Add(BlockText.Fit(line, width));
			if (Beside is { } below) context.Draw(below, width, lines);
			return;
		}

		var gap = BlockText.Blank(Gap);
		var blankArt = BlockText.Blank(artWidth);
		List<MarkupText> beside;
		if (Beside is TextBlock text)
		{
			// The text wraps at the narrow width beside the art and widens once it is past it.
			var flow = BlockText.Column(narrow, text.Alignment ?? context.TextAlignment) with { Indent = new Indent(0, art.Length, artWidth + gap.DisplayWidth) };
			beside = [.. text.Content.FormatColumn(flow)];
		}
		else
		{
			beside = context.Lines(Beside, narrow);
		}

		var rows = Math.Max(art.Length, beside.Count);
		for (var row = 0; row < rows; row++)
		{
			if (row >= art.Length && Beside is TextBlock)
			{
				lines.Add(BlockText.Fit(beside[row], width));
				continue;
			}
			var picture = row < art.Length ? BlockText.Fit(art[row], artWidth) : blankArt;
			var words = row < beside.Count ? BlockText.Fit(beside[row], narrow) : BlockText.Blank(narrow);
			lines.Add(Float == FigureFloat.Left
				? MarkupText.Concat([picture, gap, words])
				: MarkupText.Concat([words, gap, picture]));
		}
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		lines.Add(MarkupText.Plain($"Image: {Description}"));
		if (Beside is { } after) context.Draw(after, width, lines);
	}
}

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
