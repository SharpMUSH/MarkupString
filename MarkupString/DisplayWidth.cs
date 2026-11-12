using System.Buffers;
using System.Globalization;
using System.Text;
namespace MarkupString;

/// <summary>How <see cref="DisplayWidth"/> counts a C0 or C1 control character.</summary>
public enum ControlCharacterWidth
{
	/// <summary>0 cells, as a terminal draws it. The default.</summary>
	Zero,

	/// <summary>
	/// 1 cell per control code point, for callers that count characters the way a MUSH's
	/// <c>strlen</c> does: a tab or a newline is one character there, though it draws nothing.
	/// </summary>
	One,
}

/// <summary>
/// Terminal column width of literal, unencoded text: 0 cells for controls, combining marks and format characters,
/// 2 for East Asian wide and fullwidth code points, 1 for everything else. Each measure has an
/// overload taking a <see cref="ControlCharacterWidth"/> that counts controls as 1 instead.
/// Use <see cref="OfAnsi"/> or <see cref="OfTagged"/> for text that already contains formatting syntax.
/// </summary>
public static partial class DisplayWidth
{
	private const int ZeroWidthJoiner = 0x200D;
	private const int HangulJungseongStart = 0x1160;
	private const int HangulJongseongEnd = 0x11FF;

	/// <summary>
	/// Space to tilde: one cell and one cluster a character. A <see cref="SearchValues{T}"/> rather than
	/// <c>IndexOfAnyExceptInRange</c>, whose generic code allocates on every call until the JIT has optimised it.
	/// </summary>
	private static readonly SearchValues<char> PrintableAscii =
		SearchValues.Create(string.Create(0x7F - ' ', 0, static (span, _) =>
		{
			for (var i = 0; i < span.Length; i++) span[i] = (char)(' ' + i);
		}));

	/// <summary>Cells occupied by <paramref name="rune"/> on its own: 0, 1 or 2.</summary>
	public static int OfRune(Rune rune) => OfRune(rune, ControlCharacterWidth.Zero);

	/// <summary>
	/// Cells occupied by <paramref name="rune"/> on its own, with a C0 or C1 control counted as
	/// <paramref name="controls"/> says.
	/// </summary>
	public static int OfRune(Rune rune, ControlCharacterWidth controls)
	{
		var value = rune.Value;
		if (value < 0x20 || value is >= 0x7F and <= 0x9F) return controls == ControlCharacterWidth.One ? 1 : 0;
		if (value < 0x300) return 1;
		if (value is >= HangulJungseongStart and <= HangulJongseongEnd) return 0;
		if (IsZeroWidthCategory(Rune.GetUnicodeCategory(rune))) return 0;
		return IsWide(value) ? 2 : 1;
	}

	/// <summary>
	/// Cells occupied by <paramref name="text"/>. A ZERO WIDTH JOINER collapses the emoji that
	/// follows it into the sequence it joins, so a joined family counts as one emoji.
	/// </summary>
	public static int Of(ReadOnlySpan<char> text) => Of(text, ControlCharacterWidth.Zero);

	/// <summary>
	/// Cells occupied by <paramref name="text"/>, with each C0 or C1 control code point counted as
	/// <paramref name="controls"/> says — so under <see cref="ControlCharacterWidth.One"/> a CRLF
	/// is 2.
	/// </summary>
	public static int Of(ReadOnlySpan<char> text, ControlCharacterWidth controls) => Of(text, controls, int.MaxValue);

	/// <summary>
	/// <see cref="Of(ReadOnlySpan{char}, ControlCharacterWidth)"/>, stopping as soon as the count passes
	/// <paramref name="limit"/>: a result above it says only that the text is wider. A line breaker asking
	/// whether the rest of a paragraph fits needs no more than that.
	/// </summary>
	internal static int Of(ReadOnlySpan<char> text, ControlCharacterWidth controls, int limit)
	{
		var width = 0;
		while (true)
		{
			// Printable ASCII is one cell a character and ends any joiner sequence, so a stretch of it
			// is counted by a vectorised scan for where it stops.
			var ascii = text.IndexOfAnyExcept(PrintableAscii);
			if (ascii < 0) return width + text.Length;
			width += ascii;
			if (width > limit) return width;
			text = text[ascii..];

			var afterJoiner = false;
			while (!text.IsEmpty && text[0] is not (>= ' ' and <= '~'))
			{
				Rune.DecodeFromUtf16(text, out var rune, out var consumed);
				var cells = OfRune(rune, controls);
				if (afterJoiner && cells == 2) cells = 0;
				afterJoiner = rune.Value == ZeroWidthJoiner;
				width += cells;
				text = text[consumed..];
			}
			if (text.IsEmpty || width > limit) return width;
		}
	}

	/// <summary>
	/// The largest cluster boundary in <paramref name="text"/> whose prefix fits in
	/// <paramref name="cells"/> columns. Never returns an index inside a grapheme cluster.
	/// </summary>
	public static int IndexAtWidth(ReadOnlySpan<char> text, int cells)
		=> IndexAtWidth(text, cells, ControlCharacterWidth.Zero);

	/// <summary>
	/// <see cref="IndexAtWidth(ReadOnlySpan{char}, int)"/>, with each C0 or C1 control counted as
	/// <paramref name="controls"/> says.
	/// </summary>
	public static int IndexAtWidth(ReadOnlySpan<char> text, int cells, ControlCharacterWidth controls)
	{
		if (cells <= 0) return 0;
		var used = 0;
		var position = 0;
		while (position < text.Length)
		{
			// A stretch of printable ASCII is one cell and one cluster a character, except its last
			// character when something follows that could extend it: take as much of it as fits at once.
			var ascii = text[position..].IndexOfAnyExcept(PrintableAscii);
			var simple = ascii < 0 ? text.Length - position : ascii - 1;
			if (simple > 0)
			{
				var take = Math.Min(simple, cells - used);
				used += take;
				position += take;
				if (take < simple) return position;
				if (position >= text.Length) break;
			}

			int length, width;
			if (IsSimple(text, position))
			{
				length = 1;
				width = 1;
			}
			else
			{
				length = StringInfo.GetNextTextElementLength(text[position..]);
				if (length <= 0) length = 1;
				width = Of(text.Slice(position, length), controls);
			}
			if (used + width > cells) return position;
			used += width;
			position += length;
		}
		return text.Length;
	}

	/// <summary>
	/// The smallest cluster boundary in <paramref name="text"/> whose suffix fits in
	/// <paramref name="cells"/> columns. The mirror of <see cref="IndexAtWidth(ReadOnlySpan{char}, int)"/>: never returns
	/// an index inside a grapheme cluster.
	/// </summary>
	public static int IndexFromWidthEnd(ReadOnlySpan<char> text, int cells)
		=> IndexFromWidthEnd(text, cells, ControlCharacterWidth.Zero);

	/// <summary>
	/// <see cref="IndexFromWidthEnd(ReadOnlySpan{char}, int)"/>, with each C0 or C1 control counted
	/// as <paramref name="controls"/> says.
	/// </summary>
	public static int IndexFromWidthEnd(ReadOnlySpan<char> text, int cells, ControlCharacterWidth controls)
	{
		if (cells <= 0) return text.Length;
		var used = 0;
		var position = text.Length;
		while (position > 0)
		{
			var start = Graphemes.SnapStart(text, position - 1);
			var width = Of(text[start..position], controls);
			if (used + width > cells) return position;
			used += width;
			position = start;
		}
		return 0;
	}

	/// <summary>A printable ASCII character not followed by anything that could extend it.</summary>
	private static bool IsSimple(ReadOnlySpan<char> text, int position) =>
		text[position] is >= ' ' and < '\u007F'
		&& (position + 1 >= text.Length || text[position + 1] is >= ' ' and < '\u007F');

	private static bool IsZeroWidthCategory(UnicodeCategory category) => category
		is UnicodeCategory.NonSpacingMark
		or UnicodeCategory.EnclosingMark
		or UnicodeCategory.Format;

	private static bool IsWide(int value)
	{
		var ranges = DisplayWidthTable.WideRanges;
		var low = 0;
		var high = ranges.Length - 1;
		while (low <= high)
		{
			var mid = (low + high) >> 1;
			var (start, end) = ranges[mid];
			if (value < start) high = mid - 1;
			else if (value > end) low = mid + 1;
			else return true;
		}
		return false;
	}
}
