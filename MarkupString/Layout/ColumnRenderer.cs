using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>
/// Draws shaped lines into a column of fixed width by laying down the fill as a background and
/// stamping the text onto it.
/// </summary>
/// <remarks>
/// One model serves every alignment. Left, right and centre stamp a single segment; full
/// justification stamps one per word at distributed offsets, and the fill shows through the
/// gaps between them because every slice is sampled at its own position in the column.
/// </remarks>
internal static class ColumnRenderer
{
	internal static MarkupText[] Render(ImmutableArray<TextLine> lines, ColumnFormat format)
	{
		var rendered = new MarkupText[lines.Length];
		for (var i = 0; i < lines.Length; i++) rendered[i] = RenderLine(lines[i], format);
		return rendered;
	}

	/// <summary>
	/// The column drawn with nothing in it, for a row where it has run out of lines. Penn's
	/// filler fills an exhausted column, so this is the same drawing a blank line gets.
	/// </summary>
	internal static MarkupText Blank(ColumnFormat format)
	{
		var body = Blank(new TextLine(MarkupText.Empty, 0, Math.Max(0, format.Width), true), format);
		return format.Markup is null ? body : MarkupText.Wrap(format.Markup, body);
	}

	private static MarkupText RenderLine(TextLine line, ColumnFormat format)
	{
		var available = Math.Max(0, line.Width - line.Start);
		var text = format.Truncation == TruncationType.Truncate && line.Text.DisplayWidth > available
			? line.Text.TruncateToWidth(available, format.CutFrom)
			: line.Text;

		var body = text.Length == 0
			? Blank(line, format)
			: Stamp(line, format, text);

		return format.Markup is null ? body : MarkupText.Wrap(format.Markup, body);
	}

	/// <summary>A line with nothing on it: the column's fill, or spaces when asked for spaces.</summary>
	private static MarkupText Blank(TextLine line, ColumnFormat format)
	{
		if (format.NoFill) return MarkupText.Empty;
		return format.BlankLineFill == BlankLineFill.Spaces
			? MarkupText.Space.Repeat(line.Width)
			: FillPattern.Slice(format.Fill, 0, line.Width, format.FillPhase);
	}

	private static MarkupText Stamp(TextLine line, ColumnFormat format, MarkupText text)
	{
		var cells = text.DisplayWidth;
		var alignment = Resolve(format.Alignment, line, text);
		if (alignment is Alignment.Full) return Justified.Stamp(line, format, text);

		// Text wider than its column can only happen under Overflow, and then it starts where
		// the indent puts it and there is nothing left to fill.
		var start = cells >= line.Width - line.Start
			? line.Start
			: alignment switch
			{
				Alignment.Right => line.Width - cells,
				Alignment.Center => line.Start + (line.Width - line.Start - cells) / 2,
				_ => line.Start,
			};

		var lead = FillPattern.Slice(format.Fill, 0, start, format.FillPhase);
		var tailCells = line.Width - start - cells;
		if (format.NoFill || tailCells <= 0) return MarkupText.Concat(lead, text);

		var tail = FillPattern.Slice(format.FillRight ?? format.Fill, start + cells, tailCells, format.FillPhase);
		return MarkupText.Concat([lead, text, tail]);
	}

	/// <summary>
	/// The alignment actually used for this line: paragraph justification left-aligns the line
	/// that ends a paragraph, and neither it nor full justification has anywhere to put the
	/// cells when the line holds a single word.
	/// </summary>
	private static Alignment Resolve(Alignment alignment, TextLine line, MarkupText text)
	{
		if (alignment == Alignment.Paragraph) alignment = line.EndsParagraph ? Alignment.Left : Alignment.Full;
		if (alignment != Alignment.Full) return alignment;
		return text.Text.Contains(' ') && text.DisplayWidth < line.Width - line.Start
			? Alignment.Full
			: Alignment.Left;
	}
}
