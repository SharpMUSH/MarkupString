using System.Globalization;
using System.Text;
namespace MarkupString;

/// <summary>
/// Grapheme cluster boundaries over UTF-16 text. Every index this returns is a cluster
/// boundary, so cutting there never leaves a lone surrogate, a stranded combining mark, or
/// half of an emoji sequence.
/// </summary>
/// <remarks>
/// The expensive path (<see cref="StringInfo.GetNextTextElementLength(ReadOnlySpan{char})"/>)
/// runs only when the characters around the index could belong to one cluster; plain text
/// pays two comparisons.
/// </remarks>
public static class Graphemes
{
	private const char ZeroWidthJoiner = '\u200D';

	/// <summary>Counts extended grapheme clusters using the runtime Unicode segmentation rules.</summary>
	public static int Count(ReadOnlySpan<char> text)
	{
		var count = 0;
		foreach (var range in Enumerate(text)) count++;
		return count;
	}

	/// <summary>Enumerates cluster ranges in UTF-16 code units without allocating a boundary array.</summary>
	public static Enumerator Enumerate(ReadOnlySpan<char> text) => new(text);

	/// <summary>A forward-only, allocation-free enumerator of UTF-16 cluster ranges.</summary>
	public ref struct Enumerator
	{
		private readonly ReadOnlySpan<char> _text;
		private int _position;
		internal Enumerator(ReadOnlySpan<char> text) { _text = text; _position = 0; Current = default; }
		public Range Current { get; private set; }
		public readonly Enumerator GetEnumerator() => this;
		public bool MoveNext()
		{
			if (_position >= _text.Length) return false;
			var start = _position;
			_position += StringInfo.GetNextTextElementLength(_text[_position..]);
			Current = start.._position;
			return true;
		}
	}

	/// <summary>Returns the largest cluster boundary at or before <paramref name="index"/>.</summary>
	public static int SnapStart(ReadOnlySpan<char> text, int index)
	{
		if (index <= 0) return 0;
		if (index >= text.Length) return text.Length;
		return MayBeInsideCluster(text, index) ? BoundaryAtOrBefore(text, index) : index;
	}

	/// <summary>Returns the smallest cluster boundary at or after <paramref name="index"/>.</summary>
	public static int SnapEnd(ReadOnlySpan<char> text, int index)
	{
		if (index <= 0) return 0;
		if (index >= text.Length) return text.Length;
		if (!MayBeInsideCluster(text, index)) return index;
		var start = BoundaryAtOrBefore(text, index);
		if (start == index) return index;
		var length = StringInfo.GetNextTextElementLength(text[start..]);
		return length <= 0 ? index : Math.Min(text.Length, start + length);
	}

	/// <summary>True when <paramref name="index"/> is a cluster boundary (or an edge of the text).</summary>
	public static bool IsBoundary(ReadOnlySpan<char> text, int index)
	{
		if (index <= 0 || index >= text.Length) return true;
		return !MayBeInsideCluster(text, index) || BoundaryAtOrBefore(text, index) == index;
	}

	private static int BoundaryAtOrBefore(ReadOnlySpan<char> text, int index)
	{
		// Walk back to a proven boundary, however long the cluster or RI sequence is.
		var scan = index;
		while (scan > 0 && MayBeInsideCluster(text, scan)) scan--;

		var position = scan;
		while (position < index)
		{
			var length = StringInfo.GetNextTextElementLength(text[position..]);
			if (length <= 0) break;
			if (position + length > index) return position;
			position += length;
		}
		return position;
	}

	/// <summary>
	/// Cheap over-approximation: false guarantees <paramref name="index"/> is a boundary, true
	/// only means the cluster walk has to decide. Never under-reports, because every character
	/// that can take part in a UAX #29 no-break rule is caught here: nothing below U+0300 joins
	/// anything except CR LF (GB3); surrogates carry every non-BMP participant (emoji modifiers,
	/// regional indicators, the non-BMP prepend characters), with a safe exception for adjacent
	/// complete non-RI symbols; ZWJ covers GB11; the Hangul ranges
	/// cover GB6-GB8; and marks, format characters and the handful of
	/// <see cref="IsExtendingChar"/> exceptions cover GB9, GB9a, GB9b and GB9c, on either side of
	/// the index.
	/// </summary>
	private static bool MayBeInsideCluster(ReadOnlySpan<char> text, int index)
	{
		var current = text[index];
		var previous = text[index - 1];
		if (current < '\u0300' && previous < '\u0300') return previous == '\r' && current == '\n';
		if (char.IsSurrogate(current) || char.IsSurrogate(previous))
		{
			// Two complete supplementary symbols (for example adjacent emoji) have a break,
			// except regional indicators whose pairing depends on the preceding RI count.
			// Keep every other category conservative: modifiers, marks and prepend scalars
			// must still reach the contextual runtime segmenter.
			if (index >= 2 && index + 1 < text.Length
				&& Rune.TryCreate(text[index - 2], previous, out var left)
				&& Rune.TryCreate(current, text[index + 1], out var right)
				&& IsIndependentSymbol(left) && IsIndependentSymbol(right)) return false;
			return true;
		}
		if (current == ZeroWidthJoiner || previous == ZeroWidthJoiner) return true;
		if (IsHangul(current) || IsHangul(previous)) return true;
		return IsExtendingChar(current) || IsExtendingChar(previous);
	}

	private static bool IsIndependentSymbol(Rune rune) =>
		Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherSymbol
		&& rune.Value is not (>= 0x1F1E6 and <= 0x1F1FF);

	/// <summary>
	/// Hangul jamo (conjoining and extended) and precomposed Hangul syllables: the characters
	/// GB6, GB7 and GB8 join into a single syllable cluster.
	/// </summary>
	private static bool IsHangul(char c) => c
		is >= '\u1100' and <= '\u11FF'
		or >= '\uA960' and <= '\uA97C'
		or >= '\uAC00' and <= '\uD7A3'
		or >= '\uD7B0' and <= '\uD7FB';

	/// <summary>
	/// A character that can bind to its neighbour without being a mark or a format character:
	/// the halfwidth katakana sound marks (Other_Grapheme_Extend), the Thai and Lao sara am
	/// (SpacingMark) and MALAYALAM LETTER DOT REPH (Prepend).
	/// </summary>
	private static bool IsExtendingChar(char c) =>
		c is '\u0E33' or '\u0EB3' or '\u0D4E' or '\uFF9E' or '\uFF9F'
		|| IsExtending(CharUnicodeInfo.GetUnicodeCategory(c));

	private static bool IsExtending(UnicodeCategory category) => category
		is UnicodeCategory.NonSpacingMark
		or UnicodeCategory.SpacingCombiningMark
		or UnicodeCategory.EnclosingMark
		or UnicodeCategory.Format;
}
