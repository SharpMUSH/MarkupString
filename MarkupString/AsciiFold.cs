using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using MarkupString.Layout;

namespace MarkupString;

/// <summary>
/// Text for a reader whose client shows only ASCII, or only Latin-1: each character it cannot show
/// becomes the nearest one it can. A middle dot <c>·</c> becomes <c>*</c>, a dash <c>-</c>, a curly quote
/// a straight one, <c>é</c> an <c>e</c>, box drawing its nearest ASCII; anything else <c>?</c>, once for
/// each cell it took.
/// </summary>
/// <remarks>
/// <para>
/// Every built-in stand-in is as wide as the character it replaces, so text padded into columns keeps
/// them. <see cref="Translations"/> set by a game come first and may be longer or shorter; a layout
/// block folded through <see cref="LayoutContext.Fold"/> is measured after the fold, so it stays aligned
/// whatever they are.
/// </para>
/// <para>
/// Markup is kept: a replacement carries the markup of the character it replaces. A point
/// (<see cref="IPointMarkup"/>) keeps its carrier.
/// </para>
/// </remarks>
public sealed class AsciiFold
{
	/// <summary>The built-in stand-ins, for an ASCII-only reader.</summary>
	public static AsciiFold Default { get; } = new();

	/// <summary>Creates a fold.</summary>
	/// <param name="translations">
	/// Stand-ins that come before the built-in ones: each key one character (one grapheme cluster) outside
	/// ASCII, each value printable ASCII, empty to leave the character out.
	/// </param>
	/// <param name="latin1">Whether the reader shows Latin-1 (ISO-8859-1), which keeps <c>·</c> and <c>é</c>.</param>
	/// <exception cref="ArgumentException">A key is not one character outside ASCII, or a value is not printable ASCII.</exception>
	public AsciiFold(IEnumerable<KeyValuePair<string, string>>? translations = null, bool latin1 = false)
	{
		var checkedTranslations = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (key, value) in translations ?? [])
		{
			if (!IsOneCluster(key) || IsAscii(key))
				throw new ArgumentException($"'{key}' is not one character outside ASCII.", nameof(translations));
			if (value is null || !IsPrintableAscii(value))
				throw new ArgumentException($"The stand-in for '{key}' is not printable ASCII.", nameof(translations));
			checkedTranslations[key] = value;
		}
		Translations = checkedTranslations.ToFrozenDictionary(StringComparer.Ordinal);
		Latin1 = latin1;
	}

	/// <summary>The stand-ins that come before the built-in ones.</summary>
	public IReadOnlyDictionary<string, string> Translations { get; }

	/// <summary>Whether the reader shows Latin-1, so only what Latin-1 lacks is replaced.</summary>
	public bool Latin1 { get; }

	/// <summary><paramref name="text"/> in the characters the reader shows.</summary>
	public string Fold(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (IsAscii(text)) return text;

		var builder = new StringBuilder(text.Length);
		var span = text.AsSpan();
		foreach (var range in Graphemes.Enumerate(span))
		{
			var cluster = span[range];
			if (Translate(cluster) is { } replacement) builder.Append(replacement);
			else builder.Append(cluster);
		}
		return builder.ToString();
	}

	/// <summary><paramref name="text"/> in the characters the reader shows, its markup kept.</summary>
	public MarkupText Fold(MarkupText text)
	{
		ArgumentNullException.ThrowIfNull(text);
		var source = text.Text;
		if (IsAscii(source)) return text;

		var points = PointRanges(text.Runs);
		var span = source.AsSpan();
		var builder = new StringBuilder(source.Length);
		// Where each index of the old text lands in the new one, so the runs can follow.
		var map = new int[source.Length + 1];
		var changed = false;
		foreach (var range in Graphemes.Enumerate(span))
		{
			var (start, end) = (range.Start.Value, range.End.Value);
			var cluster = span[start..end];
			var replacement = InPoint(points, start) ? null : Translate(cluster);
			for (var i = start; i < end; i++) map[i] = builder.Length;
			if (replacement is null) builder.Append(cluster);
			else
			{
				builder.Append(replacement);
				changed = true;
			}
		}
		map[source.Length] = builder.Length;
		if (!changed) return text;

		var runs = ImmutableArray.CreateBuilder<Run>(text.Runs.Length);
		foreach (var run in text.Runs)
		{
			var start = map[run.Start];
			var end = map[run.End];
			if (end > start) runs.Add(new Run(start, end - start, run.Markups));
		}
		return new MarkupText(builder.ToString(), runs.ToImmutable());
	}

	/// <summary>What <paramref name="cluster"/> becomes, or null to keep it.</summary>
	private string? Translate(ReadOnlySpan<char> cluster)
	{
		if (IsAscii(cluster)) return null;

		var key = cluster.ToString();
		if (Translations.TryGetValue(key, out var translated)) return translated;
		if (Latin1)
		{
			if (IsLatin1(cluster)) return null;
			// A letter written as a base and an accent may have one Latin-1 character for both.
			if (Composed(cluster) is { } composed) return composed;
		}

		if (BuiltIn(key) is { } builtIn) return builtIn;

		var width = DisplayWidth.Of(cluster);
		// A letter with accents as the letter, a fullwidth one as the letter and a space, to keep its two cells.
		if (Letters(cluster) is { Length: > 0 } letters) return letters.Length < width ? letters.PadRight(width) : letters;
		return new string('?', width);
	}

	/// <summary>
	/// The ASCII letters <paramref name="cluster"/> is written with, its accents left out; null when it
	/// holds anything else. Read from tables of its own rather than Unicode normalisation, which a
	/// runtime without ICU does not do.
	/// </summary>
	private static string? Letters(ReadOnlySpan<char> cluster)
	{
		var builder = new StringBuilder(cluster.Length);
		foreach (var c in cluster)
		{
			if (c is >= ' ' and <= '~') builder.Append(c);
			else if (c is >= '\u00C0' and <= '\u024F' && LatinLetters[c - '\u00C0'] is not '\0' and var letter) builder.Append(letter);
			else if (c is >= '\uFF01' and <= '\uFF5E') builder.Append((char)(c - 0xFEE0));
			else if (CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)) return null;
		}
		return builder.ToString();
	}

	/// <summary>The one Latin-1 character a letter and a single accent make, or null.</summary>
	private static string? Composed(ReadOnlySpan<char> cluster)
	{
		if (cluster.Length != 2) return null;
		for (var i = 0; i < Latin1Composed.Length; i += 3)
			if (Latin1Composed[i] == cluster[0] && Latin1Composed[i + 1] == cluster[1]) return Latin1Composed[i + 2].ToString();
		return null;
	}

	/// <summary>The built-in stand-in for <paramref name="cluster"/>, or null.</summary>
	private static string? BuiltIn(string cluster)
	{
		if (Table.TryGetValue(cluster, out var value)) return value;
		// A character followed by a variation selector (☆︎, ✔️) stands in as the character does.
		var first = Rune.GetRuneAt(cluster, 0);
		var rest = cluster.AsSpan(first.Utf16SequenceLength);
		if (rest.Length > 0 && !IsVariationSelectors(rest)) return null;

		var c = first.Value;
		if (c is >= 0x2500 and <= 0x257F && BorderStyle.AsciiFor((char)c) is { } line) return line.ToString();
		if (c is >= 0x2580 and <= 0x259F) return "#";
		return rest.Length > 0 && Table.TryGetValue(first.ToString(), out var bare) ? bare : null;
	}

	private static readonly FrozenDictionary<string, string> Table = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		// Dots and bullets.
		["·"] = "*", ["•"] = "*", ["∙"] = "*", ["⋅"] = "*", ["‧"] = "*", ["●"] = "*", ["◦"] = "o", ["○"] = "o",
		["▪"] = "*", ["■"] = "#", ["□"] = "#", ["◆"] = "*", ["◇"] = "*", ["★"] = "*", ["☆"] = "*", ["‣"] = ">",
		["…"] = ".",
		// Dashes and minus.
		["‐"] = "-", ["‑"] = "-", ["‒"] = "-", ["–"] = "-", ["—"] = "-", ["―"] = "-", ["−"] = "-", ["⁃"] = "-",
		// Quotes and primes.
		["‘"] = "'", ["’"] = "'", ["‚"] = "'", ["‛"] = "'", ["′"] = "'", ["‹"] = "<", ["›"] = ">",
		["“"] = "\"", ["”"] = "\"", ["„"] = "\"", ["‟"] = "\"", ["″"] = "\"", ["«"] = "\"", ["»"] = "\"",
		// Spaces.
		[" "] = " ", [" "] = " ", [" "] = " ", [" "] = " ", [" "] = " ", [" "] = " ",
		[" "] = " ", [" "] = " ", [" "] = " ",
		// Arrows.
		["←"] = "<", ["→"] = ">", ["↑"] = "^", ["↓"] = "v", ["⇐"] = "<", ["⇒"] = ">", ["⇑"] = "^", ["⇓"] = "v",
		["◀"] = "<", ["▶"] = ">", ["▲"] = "^", ["▼"] = "v", ["◂"] = "<", ["▸"] = ">", ["▴"] = "^", ["▾"] = "v",
		// Signs.
		["×"] = "x", ["÷"] = "/", ["≤"] = "<", ["≥"] = ">", ["≈"] = "~", ["✓"] = "+", ["✔"] = "+", ["✗"] = "x",
		["✘"] = "x", ["¦"] = "|", ["¡"] = "!", ["¿"] = "?", ["°"] = "o",
		// Letters with no decomposition.
		["ß"] = "s", ["æ"] = "a", ["Æ"] = "A", ["œ"] = "o", ["Œ"] = "O", ["ø"] = "o", ["Ø"] = "O", ["ł"] = "l",
		["Ł"] = "L", ["đ"] = "d", ["Đ"] = "D", ["ð"] = "d", ["Ð"] = "D", ["þ"] = "p", ["Þ"] = "P", ["ı"] = "i",
	}.ToFrozenDictionary(StringComparer.Ordinal);

	/// <summary>The letter each of U+00C0 to U+024F is written on, or a null character for none.</summary>
	private const string LatinLetters =
		"AAAAAA\0CEEEEIIII\0NOOOOO\0\0UUUUY\0\0aaaaaa\0ceeeeiiii\0nooooo\0\0uuuuy\0y" +
		"AaAaAaCcCcCcCcDd\0\0EeEeEeEeEeGgGgGgGgHh\0\0IiIiIiIiI\0\0\0JjKk\0LlLlLl\0" +
		"\0\0\0NnNnNn\0\0\0OoOoOo\0\0RrRrRrSsSsSsSsTtTt\0\0UuUuUuUuUuUuWwYyYZzZzZz\0" +
		"\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0Oo\0\0\0\0\0\0\0\0\0\0\0\0\0Uu\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0" +
		"\0\0\0\0\0\0\0\0\0\0\0\0\0AaIiOoUuUuUuUuUu\0AaAa\0\0\0\0GgKkOoOo\0\0j\0\0\0Gg\0\0NnAa\0\0\0\0" +
		"AaAaEeEeIiIiOoOoRrRrUuUuSsTt\0\0Hh\0\0\0\0\0\0AaEeOoOoOoOoYy\0\0\0\0\0\0\0\0\0\0\0\0" +
		"\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0";

	/// <summary>Each Latin-1 letter with an accent, as the letter, the accent, and the character the two make.</summary>
	private const string Latin1Composed =
		"A\u0300\u00C0A\u0301\u00C1A\u0302\u00C2A\u0303\u00C3A\u0308\u00C4A\u030A\u00C5C\u0327\u00C7E\u0300\u00C8E\u0301\u00C9E\u0302\u00CAE\u0308\u00CBI\u0300\u00CCI\u0301\u00CDI\u0302\u00CEI\u0308\u00CFN\u0303\u00D1" +
		"O\u0300\u00D2O\u0301\u00D3O\u0302\u00D4O\u0303\u00D5O\u0308\u00D6U\u0300\u00D9U\u0301\u00DAU\u0302\u00DBU\u0308\u00DCY\u0301\u00DDa\u0300\u00E0a\u0301\u00E1a\u0302\u00E2a\u0303\u00E3a\u0308\u00E4a\u030A\u00E5" +
		"c\u0327\u00E7e\u0300\u00E8e\u0301\u00E9e\u0302\u00EAe\u0308\u00EBi\u0300\u00ECi\u0301\u00EDi\u0302\u00EEi\u0308\u00EFn\u0303\u00F1o\u0300\u00F2o\u0301\u00F3o\u0302\u00F4o\u0303\u00F5o\u0308\u00F6u\u0300\u00F9" +
		"u\u0301\u00FAu\u0302\u00FBu\u0308\u00FCy\u0301\u00FDy\u0308\u00FF";

	/// <summary>The stretches of <paramref name="runs"/> that carry a point, which keep their carriers.</summary>
	private static List<(int Start, int End)>? PointRanges(ImmutableArray<Run> runs)
	{
		List<(int Start, int End)>? ranges = null;
		foreach (var run in runs)
			foreach (var markup in run.Markups)
				if (markup is IPointMarkup)
				{
					(ranges ??= []).Add((run.Start, run.End));
					break;
				}
		return ranges;
	}

	private static bool InPoint(List<(int Start, int End)>? points, int index)
	{
		if (points is null) return false;
		foreach (var (start, end) in points)
			if (index >= start && index < end) return true;
		return false;
	}

	private static bool IsVariationSelectors(ReadOnlySpan<char> text)
	{
		foreach (var c in text)
			if (c is not (>= '︀' and <= '️')) return false;
		return true;
	}

	private static bool IsOneCluster(string? text)
	{
		if (string.IsNullOrEmpty(text)) return false;
		var count = 0;
		foreach (var _ in Graphemes.Enumerate(text)) count++;
		return count == 1;
	}

	private static bool IsAscii(ReadOnlySpan<char> text) => Ascii.IsValid(text);

	private static bool IsLatin1(ReadOnlySpan<char> text)
	{
		foreach (var c in text)
			if (c > 'ÿ') return false;
		return true;
	}

	private static bool IsPrintableAscii(ReadOnlySpan<char> text)
	{
		foreach (var c in text)
			if (c is < ' ' or > '~') return false;
		return true;
	}
}
