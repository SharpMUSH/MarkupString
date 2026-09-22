using System.Collections.Immutable;
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

	public string Text { get; }
	public ImmutableArray<Run> Runs { get; }
	public int Length => Text.Length;

	/// <summary>
	/// Constructs a <see cref="MarkupText"/> from raw text and candidate runs. Normalises the
	/// runs: drops empty/out-of-range runs, sorts by <see cref="Run.Start"/>, and coalesces
	/// adjacent runs carrying an equal <see cref="MarkupSet"/>.
	/// </summary>
	internal MarkupText(string text, ImmutableArray<Run> runs)
	{
		Text = text;
		Runs = Normalise(runs, text.Length);
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

	public static MarkupText Wrap(MarkupSet markups, string text) =>
		text.Length == 0 ? Empty : new MarkupText(text, [new Run(0, text.Length, markups)]);

	public static MarkupText Wrap(IMarkup markup, MarkupText inner)
	{
		if (inner.Length == 0) return Empty;
		var outer = MarkupSet.Of(markup);
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
		var runs = ImmutableArray.CreateBuilder<Run>(a.Runs.Length + b.Runs.Length);
		runs.AddRange(a.Runs);
		foreach (var run in b.Runs) runs.Add(run with { Start = run.Start + a.Length });
		return new MarkupText(a.Text + b.Text, runs.ToImmutable());
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
			totalLength += p.Length;
			totalRuns += p.Runs.Length;
			nonEmpty++;
			last = p;
		}
		if (nonEmpty == 0) return Empty;
		if (nonEmpty == 1) return last!;
		var runs = ImmutableArray.CreateBuilder<Run>(totalRuns);
		var text = string.Create(totalLength, (parts: parts.ToArray(), runs), static (span, state) =>
		{
			var offset = 0;
			foreach (var p in state.parts)
			{
				if (p.Length == 0) continue;
				p.Text.AsSpan().CopyTo(span[offset..]);
				foreach (var run in p.Runs) state.runs.Add(run with { Start = run.Start + offset });
				offset += p.Length;
			}
		});
		return new MarkupText(text, runs.ToImmutable());
	}

	public static MarkupText Concat(IEnumerable<MarkupText> parts) =>
		parts is MarkupText[] array ? Concat(array.AsSpan()) : Concat(parts.ToArray().AsSpan());

	public static MarkupText Join(MarkupText separator, IEnumerable<MarkupText> parts) =>
		Join(_ => separator, parts);

	public static MarkupText Join(Func<int, MarkupText> separator, IEnumerable<MarkupText> parts)
	{
		var list = new List<MarkupText>();
		var i = 0;
		foreach (var part in parts)
		{
			if (i > 0) list.Add(separator(i));
			list.Add(part);
			i++;
		}
		return Concat(list.ToArray().AsSpan());
	}

	/// <summary>
	/// The text as a reader sees it: <see cref="Text"/> without the carriers points ride on
	/// (<see cref="IPointMarkup"/>), which are positions rather than characters.
	/// </summary>
	public string ToPlainText() => _plainText ??= WithoutPoints();

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
		foreach (var markup in run.Markups)
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
	private static ImmutableArray<Run> Normalise(ImmutableArray<Run> runs, int length)
	{
		if (runs.IsDefaultOrEmpty) return ImmutableArray<Run>.Empty;
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
			var run = new Run(start, end - start, raw.Markups);
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
