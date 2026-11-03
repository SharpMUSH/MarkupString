using System.Collections.Immutable;
using CsCheck;
namespace MarkupString.Tests.Properties;

/// <summary>
/// <see cref="MarkupText.Split(string)"/>, <see cref="MarkupText.Join(MarkupText, IEnumerable{MarkupText})"/>
/// and <see cref="MarkupText.Concat(ReadOnlySpan{MarkupText})"/> take shorter ways for text with no runs.
/// These check them against the straightforward forms they replaced, over plain and marked texts, empty
/// items, delimiters at either end, multi-character delimiters and delimiters inside a cluster.
/// </summary>
public class SplitJoinProperties
{
	private const string Seed = "000000000000";

	private sealed record Tag(string Name) : IMarkup;

	private static readonly MarkupSet Red = MarkupSet.Of(new Tag("red"));
	private static readonly MarkupSet Blue = MarkupSet.Of(new Tag("blue"));

	/// <summary>
	/// Pieces chosen to put delimiters next to each other, at the ends, and inside clusters: a
	/// combining mark after a delimiter, <c>\r\n</c> (one cluster) against a <c>\n</c> delimiter.
	/// </summary>
	private static readonly string[] Alphabet =
	[
		"a", "bc", ",", ", ", " ", "\n", "\r\n", "\r", "日", "́", "\U0001F600", ",,",
	];

	private static readonly Gen<MarkupText> GenPiece = Gen.OneOf(
		Gen.Int[0, Alphabet.Length - 1].Select(i => MarkupText.Plain(Alphabet[i])),
		Gen.Int[0, Alphabet.Length - 1].Select(i => MarkupText.Plain(Alphabet[i])),
		Gen.Int[0, Alphabet.Length - 1].Select(i => MarkupText.Wrap(Red, Alphabet[i])),
		Gen.Int[0, Alphabet.Length - 1].Select(i => MarkupText.Wrap(Blue, Alphabet[i])),
		Gen.Const(MarkupText.Bell()));

	private static readonly Gen<MarkupText> GenText =
		GenPiece.Array[0, 10].Select(parts => ReferenceConcat(parts));

	private static readonly Gen<string> GenDelimiter =
		Gen.OneOf(Gen.Const(""), Gen.Const(","), Gen.Const(", "), Gen.Const(" "), Gen.Const("\n"),
			Gen.Const("\r\n"), Gen.Const("́"), Gen.Const("a"), Gen.Const(",,"), Gen.Const("bc,"));

	[Test]
	public void Split_MatchesTheReference_OnPlainAndMarkedText()
	{
		Action<(MarkupText Text, string Delimiter)> property = t =>
		{
			AssertSame(ReferenceSplit(t.Text, t.Delimiter), t.Text.Split(t.Delimiter), t.Text, t.Delimiter);

			// The same characters with no runs split at the same places, and every segment is plain.
			var plain = MarkupText.Plain(t.Text.Text);
			var plainSegments = plain.Split(t.Delimiter);
			AssertSame(ReferenceSplit(plain, t.Delimiter), plainSegments, plain, t.Delimiter);
			var markedSegments = t.Text.Split(t.Delimiter);
			if (plainSegments.Length != markedSegments.Length)
				throw new MarkupPropertyException($"plain split gave {plainSegments.Length} segments, marked {markedSegments.Length}");
			for (var i = 0; i < plainSegments.Length; i++)
			{
				if (plainSegments[i].Text != markedSegments[i].Text || !plainSegments[i].Runs.IsEmpty)
					throw new MarkupPropertyException($"segment {i} of a plain split differs from the marked one");
			}
		};

		Check.Sample(Gen.Select(GenText, GenDelimiter), property, seed: Seed, iter: 5000);
	}

	[Test]
	public void Join_MatchesTheReference()
	{
		Action<(MarkupText[] Parts, MarkupText Separator)> property = t =>
		{
			var expected = ReferenceJoin(t.Separator, t.Parts);
			var context = $"join of {t.Parts.Length} parts";
			AssertSame(expected, MarkupText.Join(t.Separator, t.Parts), context);
			AssertSame(expected, MarkupText.Join(t.Separator, t.Parts.ToList()), context + " (list)");
			AssertSame(expected, MarkupText.Join(t.Separator, t.Parts.Select(p => p)), context + " (lazy)");
			AssertSame(expected, MarkupText.Join(_ => t.Separator, t.Parts), context + " (separator function)");
			AssertSame(expected, MarkupText.Join(_ => t.Separator, t.Parts.Where(_ => true)), context + " (lazy, separator function)");
		};

		Check.Sample(Gen.Select(GenText.Array[0, 8], GenText), property, seed: Seed, iter: 5000);
	}

	[Test]
	public void Concat_MatchesTheReference()
	{
		Action<MarkupText[]> property = parts =>
		{
			var expected = ReferenceConcat(parts);
			AssertSame(expected, MarkupText.Concat(parts.AsSpan()), "concat");
			AssertSame(expected, MarkupText.Concat(parts.Select(p => p)), "concat (lazy)");
			AssertSame(expected, MarkupText.Concat(parts.ToList()), "concat (list)");
		};

		Check.Sample(GenText.Array[0, 8], property, seed: Seed, iter: 5000);
	}

	[Test]
	public void Join_CallsTheSeparatorFunctionOncePerGapInOrder()
	{
		var calls = new List<int>();
		var joined = MarkupText.Join(i =>
		{
			calls.Add(i);
			return MarkupText.Plain($"<{i}>");
		}, [MarkupText.Plain("a"), MarkupText.Plain("b"), MarkupText.Plain("c")]);

		if (joined.Text != "a<1>b<2>c" || !calls.SequenceEqual([1, 2]))
			throw new MarkupPropertyException($"{joined.Text}, calls {string.Join(",", calls)}");
	}

	// ── References: the forms the shorter ways replaced ─────────────────────────

	private static MarkupText[] ReferenceSplit(MarkupText text, string delimiter)
	{
		if (text.Length == 0) return [];
		if (delimiter.Length == 0) return [text];

		List<int>? positions = null;
		var position = 0;
		while (position <= text.Length - delimiter.Length)
		{
			var found = text.Text.IndexOf(delimiter, position, StringComparison.Ordinal);
			if (found < 0) break;
			(positions ??= []).Add(found);
			position = found + delimiter.Length;
		}
		if (positions is null) return [text];

		var segments = new MarkupText[positions.Count + 1];
		var cursor = 0;
		for (var i = 0; i < positions.Count; i++)
		{
			segments[i] = ReferenceSubstring(text, cursor, positions[i] - cursor);
			cursor = positions[i] + delimiter.Length;
		}
		segments[^1] = ReferenceSubstring(text, cursor, text.Length);
		return segments;
	}

	/// <summary><see cref="MarkupText.Substring(int, int)"/> as it was, clipping runs one by one.</summary>
	private static MarkupText ReferenceSubstring(MarkupText text, int start, int length)
	{
		if (length <= 0 || start >= text.Length) return MarkupText.Empty;
		var clampedStart = Math.Max(0, start);
		var from = Graphemes.SnapStart(text.Text, clampedStart);
		var to = length >= text.Length - clampedStart ? text.Length : Graphemes.SnapStart(text.Text, from + length);
		if (to <= from) return MarkupText.Empty;
		if (from == 0 && to == text.Length) return text;

		var runs = ImmutableArray.CreateBuilder<Run>();
		foreach (var run in text.Runs)
		{
			var s = Math.Max(run.Start, from);
			var e = Math.Min(run.End, to);
			if (e > s) runs.Add(new Run(s - from, e - s, run.Markups));
		}
		return new MarkupText(text.Text[from..to], runs.ToImmutable());
	}

	private static MarkupText ReferenceJoin(MarkupText separator, MarkupText[] parts)
	{
		var list = new List<MarkupText>();
		for (var i = 0; i < parts.Length; i++)
		{
			if (i > 0) list.Add(separator);
			list.Add(parts[i]);
		}
		return ReferenceConcat([.. list]);
	}

	private static MarkupText ReferenceConcat(MarkupText[] parts)
	{
		var nonEmpty = parts.Where(p => p.Length > 0).ToArray();
		if (nonEmpty.Length == 0) return MarkupText.Empty;
		if (nonEmpty.Length == 1) return nonEmpty[0];
		var runs = ImmutableArray.CreateBuilder<Run>();
		var offset = 0;
		foreach (var p in nonEmpty)
		{
			foreach (var run in p.Runs) runs.Add(run with { Start = run.Start + offset });
			offset += p.Length;
		}
		return new MarkupText(string.Concat(nonEmpty.Select(p => p.Text)), runs.ToImmutable());
	}

	// ── Comparison ───────────────────────────────────────────────────────────────

	private static void AssertSame(MarkupText[] expected, MarkupText[] actual, MarkupText text, string delimiter)
	{
		var context = $"split of {Escape(text.Text)} ({text.Runs.Length} runs) on {Escape(delimiter)}";
		if (expected.Length != actual.Length)
			throw new MarkupPropertyException($"{context}: {actual.Length} segments, expected {expected.Length}");
		for (var i = 0; i < expected.Length; i++) AssertSame(expected[i], actual[i], $"{context}, segment {i}");
	}

	private static void AssertSame(MarkupText expected, MarkupText actual, string context)
	{
		if (!string.Equals(expected.Text, actual.Text, StringComparison.Ordinal))
			throw new MarkupPropertyException($"{context}: text {Escape(actual.Text)}, expected {Escape(expected.Text)}");
		if (!expected.Runs.SequenceEqual(actual.Runs))
		{
			throw new MarkupPropertyException(
				$"{context}: runs [{string.Join(", ", actual.Runs)}], expected [{string.Join(", ", expected.Runs)}]");
		}
	}

	private static string Escape(string text) =>
		"\"" + string.Concat(text.Select(c => c is >= ' ' and < '\u007f' ? c.ToString() : $"\\u{(int)c:x4}")) + "\"";
}
