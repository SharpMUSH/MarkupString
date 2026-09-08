using System.Globalization;

namespace MarkupString;

public sealed partial class MarkupText
{
	private int _graphemeCount = -1;

	/// <summary>Number of extended grapheme clusters, independent of markup and display width.</summary>
	public int GraphemeCount => _graphemeCount >= 0 ? _graphemeCount : _graphemeCount = Graphemes.Count(Text);

	/// <summary>Lazily extracts whole clusters, retaining every markup layer without rendering.</summary>
	/// <remarks>Uses constant traversal storage; each yielded result allocates its own text and clipped runs.</remarks>
	public IEnumerable<MarkupText> EnumerateGraphemes()
	{
		var position = 0;
		while (position < Length)
		{
			var end = position + StringInfo.GetNextTextElementLength(Text.AsSpan(position));
			yield return ExtractRange(position, end);
			position = end;
		}
	}

	/// <summary>Extracts the tail starting at a zero-based grapheme index.</summary>
	public MarkupText SubstringGraphemes(int start) => SubstringGraphemes(start, int.MaxValue);

	/// <summary>Extracts at most <paramref name="count"/> clusters from a zero-based grapheme index.</summary>
	/// <remarks>Negative starts clamp to zero; nonpositive counts and starts beyond the text return empty.</remarks>
	public MarkupText SubstringGraphemes(int start, int count)
	{
		if (count <= 0 || Length == 0) return Empty;
		start = Math.Max(0, start);
		foreach (var range in Graphemes.Enumerate(Text))
		{
			if (start > 0) { start--; continue; }
			var position = range.Start.Value;
			var end = range.End.Value;
			while (--count > 0 && end < Length)
				end += StringInfo.GetNextTextElementLength(Text.AsSpan(end));
			return ExtractRange(position, end);
		}
		return Empty;
	}
}
