namespace MarkupString.Layout;

/// <summary>
/// Full justification: the words of a line pushed apart until they span the column, with the
/// fill showing through every gap it opens.
/// </summary>
internal static class Justified
{
	internal static MarkupText Stamp(TextLine line, ColumnFormat format, MarkupText text)
	{
		var words = Words(text);
		var gaps = words.Count - 1;

		var wordCells = 0;
		foreach (var word in words) wordCells += word.DisplayWidth;
		var free = line.Width - line.Start - wordCells;

		var parts = new List<MarkupText>(words.Count * 2 + 1);
		if (line.Start > 0) parts.Add(FillPattern.Slice(format.Fill, 0, line.Start, format.FillPhase));

		var cell = line.Start;
		for (var i = 0; i < words.Count; i++)
		{
			parts.Add(words[i]);
			cell += words[i].DisplayWidth;
			if (i == gaps) break;

			// The remainder goes to the earliest gaps, which is where both servers put it.
			var width = free / gaps + (i < free % gaps ? 1 : 0);
			parts.Add(FillPattern.Slice(format.Fill, cell, width, format.FillPhase));
			cell += width;
		}

		return MarkupText.Concat(parts.ToArray().AsSpan());
	}

	/// <summary>The maximal runs of non-space in <paramref name="text"/>.</summary>
	private static List<MarkupText> Words(MarkupText text)
	{
		var words = new List<MarkupText>();
		var position = 0;
		while (position < text.Length)
		{
			while (position < text.Length && text.Text[position] == ' ') position++;
			var start = position;
			while (position < text.Length && text.Text[position] != ' ') position++;
			if (position > start) words.Add(text.Substring(start, position - start));
		}
		return words;
	}
}
