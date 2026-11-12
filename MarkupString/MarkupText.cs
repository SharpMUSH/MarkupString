using System.Collections.Immutable;
using System.Runtime.InteropServices;
namespace MarkupString;

/// <summary>
/// Immutable text with zero or more styled-span <see cref="Run"/>s layered over it. Gaps
/// between runs are plain text. Equality (<see cref="Equals(MarkupText?)"/>, <c>==</c>,
/// <see cref="GetHashCode"/>) is ordinal comparison of <see cref="ToPlainText"/> only — markup never
/// participates, and neither do the carriers points ride on.
/// </summary>
public sealed partial class MarkupText : IEquatable<MarkupText>
{
	public static readonly MarkupText Empty = new(string.Empty, ImmutableArray<Run>.Empty);
	public static readonly MarkupText Space = new(" ", ImmutableArray<Run>.Empty);
	public static readonly MarkupText NewLine = new("\n", ImmutableArray<Run>.Empty);

	/// <summary>The characters. On text built by a deferred concatenation, the first read joins the parts.</summary>
	public string Text => Volatile.Read(ref _payload) as string ?? Flatten();

	/// <summary>The styled spans over <see cref="Text"/>, sorted and coalesced.</summary>
	public ImmutableArray<Run> Runs
	{
		get
		{
			if (Volatile.Read(ref _payload) is not string) Flatten();
			return _runs;
		}
	}

	public int Length => Volatile.Read(ref _payload) switch
	{
		// One read: another thread may join the parts between two reads of the field.
		string text => text.Length,
		var deferred => ((Deferred)deferred).Length,
	};

	/// <summary>
	/// The text as a <see cref="string"/>, or, until the first read joins them, a <see cref="Deferred"/>
	/// concatenation. One field for both keeps every MarkupText the size it was.
	/// </summary>
	private object _payload;

	/// <summary>The parts of a concatenation not yet joined, and their total length.</summary>
	private sealed class Deferred(MarkupText[] parts, int length)
	{
		public readonly MarkupText[] Parts = parts;
		public readonly int Length = length;
	}
	private ImmutableArray<Run> _runs;

	/// <summary>Whether this text is a concatenation that has not been joined yet.</summary>
	internal bool IsDeferred => Volatile.Read(ref _payload) is Deferred;

	/// <summary>
	/// The length from which <see cref="Concat(MarkupText, MarkupText)"/> records its parts instead of
	/// copying them. Below it a copy is cheap and the string is likely to be read soon anyway.
	/// </summary>
	internal const int DeferThreshold = 1024;

	private MarkupText(MarkupText[] parts, int length)
	{
		_payload = new Deferred(parts, length);
	}

	/// <summary>
	/// The character at <paramref name="index"/>, found without joining a deferred concatenation. The
	/// last character of an accumulator is one step away; the first is as deep as the chain.
	/// </summary>
	private char CharAt(int index)
	{
		var node = this;
		while (true)
		{
			var payload = Volatile.Read(ref node._payload);
			if (payload is string text) return text[index];
			foreach (var part in ((Deferred)payload).Parts)
			{
				if (index < part.Length)
				{
					node = part;
					break;
				}
				index -= part.Length;
			}
		}
	}

	/// <summary>
	/// Joins a deferred concatenation. The walk is iterative, because a softcode loop can chain
	/// thousands of appends. Runs are written before the text is published; two threads racing here
	/// both build the same text, and the one that loses throws its copy away.
	/// </summary>
	private string Flatten()
	{
		if (Volatile.Read(ref _payload) is not Deferred deferred) return (string)_payload;
		var parts = deferred.Parts;

		var leaves = new List<MarkupText>();
		var stack = new Stack<MarkupText>();
		for (var i = parts.Length - 1; i >= 0; i--) stack.Push(parts[i]);
		var totalRuns = 0;
		while (stack.Count > 0)
		{
			var node = stack.Pop();
			if (Volatile.Read(ref node._payload) is Deferred { Parts: var inner })
			{
				for (var i = inner.Length - 1; i >= 0; i--) stack.Push(inner[i]);
				continue;
			}
			// Either flat from the start, or joined by another reader since: its text and runs are set.
			leaves.Add(node);
			totalRuns += node._runs.IsDefault ? 0 : node._runs.Length;
		}

		var text = string.Create(deferred.Length, leaves, static (span, leaves) =>
		{
			var offset = 0;
			foreach (var leaf in leaves)
			{
				var t = (string)leaf._payload;
				t.AsSpan().CopyTo(span[offset..]);
				offset += t.Length;
			}
		});

		var runs = ImmutableArray<Run>.Empty;
		if (totalRuns > 0)
		{
			var builder = ImmutableArray.CreateBuilder<Run>(totalRuns);
			var start = 0;
			foreach (var leaf in leaves)
			{
				foreach (var run in leaf._runs) builder.Add(run with { Start = run.Start + start });
				start += leaf.Length;
			}
			runs = Normalise(builder.MoveToImmutable(), text, text.Length);
		}

		_runs = runs;
		return Interlocked.CompareExchange(ref _payload, text, deferred) as string ?? text;
	}

	/// <summary>
	/// Constructs a <see cref="MarkupText"/> from raw text and candidate runs. Normalises the
	/// runs: drops empty/out-of-range runs and points that do not match the text they cover, sorts by
	/// <see cref="Run.Start"/>, and coalesces adjacent runs carrying an equal <see cref="MarkupSet"/>.
	/// </summary>
	internal MarkupText(string text, ImmutableArray<Run> runs)
	{
		_payload = text;
		_runs = Normalise(runs, text, text.Length);
	}

	public static MarkupText Plain(string text) => text.Length switch
	{
		0 => Empty,
		1 when text[0] == ' ' => Space,
		1 when text[0] == '\n' => NewLine,
		_ => new MarkupText(text, ImmutableArray<Run>.Empty),
	};

	/// <summary>
	/// A bell: a point in the text asking the client to get someone's attention, measuring zero display
	/// cells. See <see cref="BellMarkup"/>.
	/// </summary>
	public static MarkupText Bell() => Wrap(BellMarkup.Instance, BellMarkup.Character);

	public static MarkupText Wrap(IMarkup markup, string text) => Wrap(MarkupSet.Of(markup), text);

	/// <exception cref="ArgumentException">
	/// A point in <paramref name="markups"/> stands over something other than its own carrier.
	/// </exception>
	public static MarkupText Wrap(MarkupSet markups, string text)
	{
		if (text.Length == 0) return Empty;
		CheckPoints(markups, text);
		return new MarkupText(text, [new Run(0, text.Length, markups)]);
	}

	/// <exception cref="ArgumentException">
	/// <paramref name="markup"/> is a point and <paramref name="inner"/> is something other than its own
	/// carrier, or already carries markup of its own.
	/// </exception>
	public static MarkupText Wrap(IMarkup markup, MarkupText inner)
	{
		if (inner.Length == 0) return Empty;
		if (markup is IPointMarkup point)
		{
			CheckPoint(point, inner.Text);
			if (!inner.Runs.IsDefaultOrEmpty)
			{
				throw new ArgumentException(
					$"{markup.GetType().Name} is a point, and a point marks its carrier alone, which carries no markup of its own.",
					nameof(inner));
			}
		}

		var outer = MarkupSet.Of(markup);
		// Plain text, the usual case for ansi() and its kin: one run, nothing to merge.
		if (inner.Runs.IsDefaultOrEmpty) return new MarkupText(inner.Text, [new Run(0, inner.Length, outer)]);

		var builder = ImmutableArray.CreateBuilder<Run>(inner.Runs.Length * 2 + 1);
		var position = 0;
		foreach (var run in inner.Runs)
		{
			if (run.Start > position) builder.Add(new Run(position, run.Start - position, outer));
			builder.Add(new Run(run.Start, run.Length, run.Markups.Append(markup)));
			position = run.End;
		}
		if (position < inner.Length) builder.Add(new Run(position, inner.Length - position, outer));
		return new MarkupText(inner.Text, builder.ToImmutable());
	}

	public static MarkupText Concat(MarkupText a, MarkupText b)
	{
		if (a.Length == 0) return b;
		if (b.Length == 0) return a;
		if (a.Length + b.Length >= DeferThreshold) return new MarkupText([a, b], checked(a.Length + b.Length));
		var runs = ImmutableArray.CreateBuilder<Run>(a.Runs.Length + b.Runs.Length);
		runs.AddRange(a.Runs);
		foreach (var run in b.Runs) runs.Add(run with { Start = run.Start + a.Length });
		return new MarkupText(a.Text + b.Text, runs.MoveToImmutable());
	}

	public static MarkupText Concat(ReadOnlySpan<MarkupText> parts)
	{
		var totalLength = 0;
		var totalRuns = 0;
		var nonEmpty = 0;
		MarkupText? last = null;
		foreach (var p in parts)
		{
			if (p.Length == 0) continue;
			totalLength = checked(totalLength + p.Length);
			nonEmpty++;
			last = p;
		}
		if (nonEmpty == 0) return Empty;
		if (nonEmpty == 1) return last!;
		if (totalLength >= DeferThreshold)
		{
			var kept = new MarkupText[nonEmpty];
			var k = 0;
			foreach (var p in parts) if (p.Length > 0) kept[k++] = p;
			return new MarkupText(kept, totalLength);
		}
		foreach (var p in parts) totalRuns += p.Runs.Length;
		var text = string.Create(totalLength, parts, static (span, parts) =>
		{
			var offset = 0;
			foreach (var p in parts)
			{
				p.Text.AsSpan().CopyTo(span[offset..]);
				offset += p.Length;
			}
		});
		if (totalRuns == 0) return new MarkupText(text, ImmutableArray<Run>.Empty);

		var runs = ImmutableArray.CreateBuilder<Run>(totalRuns);
		var start = 0;
		foreach (var p in parts)
		{
			foreach (var run in p.Runs) runs.Add(run with { Start = run.Start + start });
			start += p.Length;
		}
		return new MarkupText(text, runs.MoveToImmutable());
	}

	public static MarkupText Concat(IEnumerable<MarkupText> parts) => Concat(AsSpan(parts));

	public static MarkupText Join(MarkupText separator, IEnumerable<MarkupText> parts) =>
		Join(separator, AsSpan(parts));

	public static MarkupText Join(Func<int, MarkupText> separator, IEnumerable<MarkupText> parts)
	{
		var list = parts.TryGetNonEnumeratedCount(out var count)
			? new List<MarkupText>(Math.Max(0, count * 2 - 1))
			: [];
		var i = 0;
		foreach (var part in parts)
		{
			if (i > 0) list.Add(separator(i));
			list.Add(part);
			i++;
		}
		return Concat(CollectionsMarshal.AsSpan(list));
	}

	/// <summary>
	/// <paramref name="parts"/> with <paramref name="separator"/> between each two, written straight into
	/// the result: what <see cref="Concat(ReadOnlySpan{MarkupText})"/> of the interleaved parts makes,
	/// without building them.
	/// </summary>
	private static MarkupText Join(MarkupText separator, ReadOnlySpan<MarkupText> parts)
	{
		if (parts.Length == 0) return Empty;
		if (separator.Length == 0) return Concat(parts);

		var separators = parts.Length - 1;
		var totalLength = separator.Length * separators;
		var totalRuns = separator.Runs.Length * separators;
		var nonEmpty = 0;
		MarkupText? last = null;
		foreach (var p in parts)
		{
			if (p.Length == 0) continue;
			totalLength += p.Length;
			totalRuns += p.Runs.Length;
			nonEmpty++;
			last = p;
		}
		// The one non-empty piece is returned as it is, as Concat returns it.
		if (nonEmpty + separators == 1) return last ?? separator;
		if (nonEmpty + separators == 0) return Empty;

		var text = string.Create(totalLength, new JoinState(parts, separator.Text), static (span, state) =>
		{
			var offset = 0;
			for (var i = 0; i < state.Parts.Length; i++)
			{
				if (i > 0)
				{
					state.Separator.AsSpan().CopyTo(span[offset..]);
					offset += state.Separator.Length;
				}
				var part = state.Parts[i].Text;
				part.AsSpan().CopyTo(span[offset..]);
				offset += part.Length;
			}
		});
		if (totalRuns == 0) return new MarkupText(text, ImmutableArray<Run>.Empty);

		var runs = ImmutableArray.CreateBuilder<Run>(totalRuns);
		var start = 0;
		for (var i = 0; i < parts.Length; i++)
		{
			if (i > 0)
			{
				foreach (var run in separator.Runs) runs.Add(run with { Start = run.Start + start });
				start += separator.Length;
			}
			foreach (var run in parts[i].Runs) runs.Add(run with { Start = run.Start + start });
			start += parts[i].Length;
		}
		return new MarkupText(text, runs.MoveToImmutable());
	}

	private readonly ref struct JoinState(ReadOnlySpan<MarkupText> parts, string separator)
	{
		public ReadOnlySpan<MarkupText> Parts { get; } = parts;
		public string Separator { get; } = separator;
	}

	/// <summary>The parts as a span: in place for an array or a list, copied once otherwise.</summary>
	private static ReadOnlySpan<MarkupText> AsSpan(IEnumerable<MarkupText> parts) => parts switch
	{
		MarkupText[] array => array,
		List<MarkupText> list => CollectionsMarshal.AsSpan(list),
		_ => parts.ToArray(),
	};

	/// <summary>
	/// The text as a reader sees it: <see cref="Text"/> without the carriers points ride on
	/// (<see cref="IPointMarkup"/>), which are positions rather than characters.
	/// </summary>
	public string ToPlainText() => _plainText ??= WithoutPoints();

	/// <summary>
	/// The text with every visual layer taken off and its data layers (<see cref="IDataMarkup"/>) kept: what
	/// <see cref="ToPlainText"/> gives, plus the data. Points go with their carriers, as they do there.
	/// </summary>
	public MarkupText WithoutVisualMarkup()
	{
		if (Runs.IsDefaultOrEmpty) return this;

		var hasData = false;
		foreach (var run in Runs)
		{
			foreach (var markup in run.Markups.Items)
			{
				if (markup is not IDataMarkup) continue;
				hasData = true;
				break;
			}
			if (hasData) break;
		}
		if (!hasData) return Plain(ToPlainText());

		var parts = new List<MarkupText>(Runs.Length * 2 + 1);
		var position = 0;
		foreach (var run in Runs)
		{
			if (run.Start > position) parts.Add(Plain(Text[position..run.Start]));
			position = run.End;
			if (IsPoint(run)) continue;

			var data = new List<IMarkup>();
			foreach (var markup in run.Markups.Items)
				if (markup is IDataMarkup) data.Add(markup);
			var body = Text.Substring(run.Start, run.Length);
			parts.Add(data.Count == 0 ? Plain(body) : Wrap(MarkupSet.Of(data), body));
		}
		if (position < Text.Length) parts.Add(Plain(Text[position..]));
		return Concat(parts);
	}

	/// <summary>
	/// The text with every data layer (<see cref="IDataMarkup"/>) taken off and everything else kept: the
	/// counterpart of <see cref="WithoutVisualMarkup"/>, for text whose data must not be trusted.
	/// </summary>
	public MarkupText WithoutDataMarkup() => WithoutMarkup(static markup => markup is IDataMarkup);

	/// <summary>The text with every layer <paramref name="drop"/> says yes to taken off and everything else kept.</summary>
	internal MarkupText WithoutMarkup(Func<IMarkup, bool> drop)
	{
		if (Runs.IsDefaultOrEmpty) return this;

		var dropping = false;
		foreach (var run in Runs)
		{
			foreach (var markup in run.Markups.Items)
			{
				if (!drop(markup)) continue;
				dropping = true;
				break;
			}
			if (dropping) break;
		}
		if (!dropping) return this;

		var builder = ImmutableArray.CreateBuilder<Run>(Runs.Length);
		foreach (var run in Runs)
		{
			var kept = new List<IMarkup>(run.Markups.Count);
			foreach (var markup in run.Markups.Items)
				if (!drop(markup)) kept.Add(markup);
			if (kept.Count == run.Markups.Count) builder.Add(run);
			else if (kept.Count > 0) builder.Add(run with { Markups = MarkupSet.Of(kept) });
		}
		return new MarkupText(Text, builder.ToImmutable());
	}

	/// <summary>The same as <see cref="ToPlainText"/>.</summary>
	public override string ToString() => ToPlainText();
	public bool Equals(MarkupText? other) => other is not null && string.Equals(ToPlainText(), other.ToPlainText(), StringComparison.Ordinal);
	public bool TextEquals(string? text) => string.Equals(ToPlainText(), text, StringComparison.Ordinal);
	public override bool Equals(object? obj) => obj is MarkupText other && Equals(other);
	public override int GetHashCode() => string.GetHashCode(ToPlainText(), StringComparison.Ordinal);

	private string? _plainText;

	private string WithoutPoints()
	{
		var hasPoint = false;
		foreach (var run in Runs)
		{
			if (IsPoint(run))
			{
				hasPoint = true;
				break;
			}
		}
		if (!hasPoint) return Text;

		var plain = new System.Text.StringBuilder(Text.Length);
		var position = 0;
		foreach (var run in Runs)
		{
			if (!IsPoint(run)) continue;
			plain.Append(Text, position, run.Start - position);
			position = run.End;
		}
		return plain.Append(Text, position, Text.Length - position).ToString();
	}

	private static bool IsPoint(Run run)
	{
		foreach (var markup in run.Markups.Items)
			if (markup is IPointMarkup) return true;
		return false;
	}
	public static bool operator ==(MarkupText? a, MarkupText? b) => a is null ? b is null : a.Equals(b);
	public static bool operator !=(MarkupText? a, MarkupText? b) => !(a == b);

	/// <summary>
	/// Drops empty or out-of-range runs, sorts by start, clips to the text length, and merges
	/// adjacent runs with an equal <see cref="MarkupSet"/>. Returns the rebuilt array whenever
	/// any run was altered (clipped, dropped, merged, or sorted) — never the original array in
	/// that case, even when the run count happens to match.
	/// </summary>
	/// <summary>Every point in the set marks only its own carrier.</summary>
	private static void CheckPoints(MarkupSet markups, string text)
	{
		foreach (var markup in markups.Items)
			if (markup is IPointMarkup point)
				CheckPoint(point, text);
	}

	private static void CheckPoint(IPointMarkup point, string text)
	{
		if (IsCarriedBy(point, text.AsSpan())) return;

		throw new ArgumentException(
			$"{point.GetType().Name} is a point: it marks its own carrier, not text. Build one with MarkupText.Point.",
			nameof(text));
	}

	/// <summary>Whether <paramref name="text"/> is that point's carrier, one character per occurrence.</summary>
	private static bool IsCarriedBy(IPointMarkup point, ReadOnlySpan<char> text)
	{
		var carrier = point.Carrier.AsSpan();
		if (carrier.Length == 0 || text.Length == 0 || text.Length % carrier.Length != 0) return false;

		for (var i = 0; i < text.Length; i += carrier.Length)
			if (!text.Slice(i, carrier.Length).SequenceEqual(carrier))
				return false;

		return true;
	}

	/// <summary>
	/// The set with any point that does not match the text it covers dropped. Construction refuses that
	/// shape, so this is what stands between a hand-written or corrupted serialised cover and a renderer
	/// that would write the point and swallow the text under it.
	/// </summary>
	private static MarkupSet WithoutMisplacedPoints(MarkupSet markups, ReadOnlySpan<char> text)
	{
		var keep = true;
		foreach (var markup in markups.Items)
		{
			if (markup is IPointMarkup point && !IsCarriedBy(point, text))
			{
				keep = false;
				break;
			}
		}

		if (keep) return markups;

		var kept = new List<IMarkup>(markups.Count);
		foreach (var markup in markups.Items)
			if (markup is not IPointMarkup point || IsCarriedBy(point, text))
				kept.Add(markup);

		return kept.Count == 0 ? MarkupSet.Of(NeutralMarkup.Instance) : MarkupSet.Of(kept);
	}

	/// <summary>
	/// Whether <paramref name="runs"/> are already what <see cref="Normalise"/> would make of them: in
	/// order, in range, none empty, no two adjacent ones carrying an equal set, and every point on its
	/// own carrier. Nearly every caller hands over runs in that form, so checking first spares them a
	/// copy.
	/// </summary>
	private static bool IsNormal(ImmutableArray<Run> runs, string text, int length)
	{
		var previousEnd = 0;
		MarkupSet? previous = null;
		foreach (var run in runs)
		{
			if (run.Length <= 0 || run.Start < previousEnd || run.End > length) return false;
			if (previous is not null && run.Start == previousEnd && previous.Equals(run.Markups)) return false;
			if (!ReferenceEquals(WithoutMisplacedPoints(run.Markups, text.AsSpan(run.Start, run.Length)), run.Markups))
				return false;

			previousEnd = run.End;
			previous = run.Markups;
		}

		return true;
	}

	private static ImmutableArray<Run> Normalise(ImmutableArray<Run> runs, string text, int length)
	{
		if (runs.IsDefaultOrEmpty) return ImmutableArray<Run>.Empty;
		if (IsNormal(runs, text, length)) return runs;
		var sorted = false;
		for (var i = 1; i < runs.Length; i++) if (runs[i].Start < runs[i - 1].Start) { sorted = true; break; }
		var source = sorted ? runs.Sort((a, b) => a.Start.CompareTo(b.Start)) : runs;
		var changed = sorted;
		var builder = ImmutableArray.CreateBuilder<Run>(source.Length);
		foreach (var raw in source)
		{
			var start = Math.Max(0, raw.Start);
			var end = Math.Min(length, raw.End);
			if (end <= start)
			{
				changed = true;
				continue;
			}
			var markups = WithoutMisplacedPoints(raw.Markups, text.AsSpan(start, end - start));
			if (!ReferenceEquals(markups, raw.Markups)) changed = true;
			var run = new Run(start, end - start, markups);
			if (run.Start != raw.Start || run.Length != raw.Length) changed = true;
			if (builder.Count > 0)
			{
				var prev = builder[^1];
				if (run.Start < prev.End) throw new ArgumentException("Runs must not overlap.");
				if (prev.End == run.Start && prev.Markups.Equals(run.Markups))
				{
					builder[^1] = prev with { Length = prev.Length + run.Length };
					changed = true;
					continue;
				}
			}
			builder.Add(run);
		}
		return changed ? builder.ToImmutable() : runs;
	}
}
