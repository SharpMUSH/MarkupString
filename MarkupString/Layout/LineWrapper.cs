using Cells = MarkupString.DisplayWidth;

namespace MarkupString.Layout;

/// <summary>
/// The line-breaking walk behind <see cref="MarkupText.Shape"/>: one pass over the text yielding
/// each line's code-unit range together with the geometry the renderer needs.
/// </summary>
/// <remarks>
/// The indent is applied inside this walk rather than after it, because a continuation line
/// wraps at <c>width - indent</c> — laying the indent on afterwards would produce lines that no
/// longer fit. Every iteration advances by at least one grapheme cluster, so a column narrower
/// than a single cluster overflows rather than looping.
/// </remarks>
internal static class LineWrapper
{
	/// <summary>One line's span and geometry, before its text is sliced out.</summary>
	internal readonly record struct LineSpan(int Start, int End, int Indent, int Width, bool EndsParagraph);

	internal static List<LineSpan> Break(ReadOnlySpan<char> text, ColumnFormat format)
	{
		var lines = new List<LineSpan>();
		var baseWidth = Math.Max(1, format.Width);

		if (format.Wrap == WrapMode.None)
		{
			lines.Add(new LineSpan(0, text.Length, 0, baseWidth, true));
			return lines;
		}

		var position = 0;
		while (true)
		{
			var (indent, width) = Geometry(format, baseWidth, lines.Count);
			var available = Math.Max(1, width - indent);

			var found = text[position..].IndexOf('\n');
			var hardBreak = found < 0 ? -1 : position + found;
			// A carriage return belongs to the break, not to the line it terminates.
			var contentEnd = hardBreak < 0 ? text.Length
				: hardBreak > position && text[hardBreak - 1] == '\r' ? hardBreak - 1
				: hardBreak;

			int end, next;
			bool endsParagraph;
			if (format.Wrap == WrapMode.HardBreaks || Cells.Of(text[position..contentEnd]) <= available)
			{
				end = contentEnd;
				next = hardBreak < 0 ? -1 : hardBreak + 1;
				endsParagraph = true;
			}
			else
			{
				var limit = position + Cells.IndexAtWidth(text[position..contentEnd], available);
				var space = format.Wrap == WrapMode.Word ? LastSpace(text, position, limit) : -1;
				// BreakSpace decides only where the line ends: PennMUSH cuts at the space,
				// RhostMUSH one past it, so a Rhost line may run a cell wide.
				end = space < 0 ? limit : format.BreakSpace == BreakSpace.Keep ? space + 1 : space;
				next = space < 0 ? limit : space + 1;
				// A soft break that consumed the tail ends the walk; only a hard break at the very
				// end of the text is allowed to leave a trailing empty line.
				if (next >= text.Length) next = -1;
				endsParagraph = false;
			}

			// A cluster wider than the column takes no cells, so the walk would sit still. It
			// overflows its line instead, which is what makes the walk terminate. An empty line
			// between two hard breaks is not this case: there the break itself moves us on.
			if (next >= 0 && next <= position && position < text.Length)
			{
				end = Graphemes.SnapEnd(text, position + 1);
				next = end >= text.Length ? -1 : end;
				endsParagraph = end >= text.Length;
			}

			lines.Add(new LineSpan(position, end, indent, width, endsParagraph));

			if (format.MaxLines > 0 && lines.Count >= format.MaxLines) break;
			if (next < 0) break;
			position = next;
		}

		return lines;
	}

	/// <summary>The indent and total width of line <paramref name="index"/>, zero-based.</summary>
	private static (int Indent, int Width) Geometry(ColumnFormat format, int baseWidth, int index)
	{
		var indent = format.Indent;
		if (indent.Amount <= 0 || index < indent.FromLine) return (0, baseWidth);
		// An indent that leaves no room for text would stall the walk.
		return (Math.Min(indent.Amount, baseWidth - 1), baseWidth + Math.Max(0, indent.Widen));
	}

	/// <summary>
	/// The last space at an index in <c>[from, limit]</c>, or -1. The line's own first character
	/// is never a break point, which would give an empty line and drop the space for nothing.
	/// </summary>
	private static int LastSpace(ReadOnlySpan<char> text, int from, int limit)
	{
		for (var k = Math.Min(limit, text.Length - 1); k > from; k--)
			if (text[k] == ' ') return k;
		return -1;
	}
}
