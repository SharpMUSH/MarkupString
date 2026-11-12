using System.Text.Json;

/// <summary>
/// How MarkupText stores and copies its text: deferred concatenation, one-copy splicing, exactly
/// sized runs on extraction, and splitting with options. Each result has to match what the plain,
/// eager construction gives.
/// </summary>
public class RepresentationTests
{
	private sealed record Tag(string Name) : IMarkup;

	private sealed class TagCodec : IMarkupCodec
	{
		public string Kind => "tag";
		public Type MarkupType => typeof(Tag);
		public void Write(Utf8JsonWriter writer, IMarkup markup) => writer.WriteString("n", ((Tag)markup).Name);
		public IMarkup Read(JsonElement element) => new Tag(element.GetProperty("n").GetString() ?? string.Empty);
	}

	private static readonly Tag Red = new("red");
	private static readonly Tag Blue = new("blue");
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.With(new TagCodec());

	private static string Word(int i) => "w" + (i * 7919 % 100_000);

	/// <summary>A piece of <paramref name="length"/> letters, red, blue or unstyled by <paramref name="i"/>.</summary>
	private static MarkupText Piece(int i, int length)
	{
		var text = new string((char)('a' + i % 26), length);
		return (i % 3) switch
		{
			0 => MarkupText.Plain(text),
			1 => MarkupText.Wrap(Red, text),
			_ => MarkupText.Wrap(Blue, text),
		};
	}

	/// <summary>The runs of <paramref name="pieces"/> laid end to end, coalesced as the constructor does.</summary>
	private static List<Run> ExpectedRuns(IEnumerable<MarkupText> pieces)
	{
		var runs = new List<Run>();
		var offset = 0;
		foreach (var piece in pieces)
		{
			foreach (var run in piece.Runs)
			{
				var shifted = run with { Start = run.Start + offset };
				if (runs.Count > 0 && runs[^1].End == shifted.Start && runs[^1].Markups.Equals(shifted.Markups))
					runs[^1] = runs[^1] with { Length = runs[^1].Length + shifted.Length };
				else
					runs.Add(shifted);
			}
			offset += piece.Length;
		}
		return runs;
	}

	// ── Deferred concatenation ───────────────────────────────────────────────────

	[Test]
	public async Task Concat_BelowThreshold_JoinsAtOnce()
	{
		var text = MarkupText.Concat(Piece(1, 10), Piece(2, 10));
		await Assert.That(text.IsDeferred).IsFalse();
	}

	[Test]
	public async Task Concat_AtThreshold_DefersAndKnowsItsLength()
	{
		var a = Piece(1, MarkupText.DeferThreshold / 2);
		var b = Piece(2, MarkupText.DeferThreshold / 2);
		var text = MarkupText.Concat(a, b);

		await Assert.That(text.IsDeferred).IsTrue();
		await Assert.That(text.Length).IsEqualTo(MarkupText.DeferThreshold);
		await Assert.That(text.IsDeferred).IsTrue();
		await Assert.That(text.Text).IsEqualTo(a.Text + b.Text);
		await Assert.That(text.IsDeferred).IsFalse();
		await Assert.That(text.Runs.SequenceEqual(ExpectedRuns([a, b]))).IsTrue();
	}

	/// <summary>
	/// The softcode accumulator, <c>setq(0,%q0&lt;piece&gt;)</c> in a loop: thousands of appends to a
	/// chain that is never read until the end. The join walks the chain without recursing.
	/// </summary>
	[Test]
	public async Task Accumulator_OfManyAppends_JoinsToTheEagerResult()
	{
		var pieces = Enumerable.Range(0, 20_000).Select(i => Piece(i / 2, 7)).ToList();
		var text = MarkupText.Empty;
		foreach (var piece in pieces) text = MarkupText.Concat(text, piece);

		await Assert.That(text.IsDeferred).IsTrue();
		await Assert.That(text.Length).IsEqualTo(20_000 * 7);
		await Assert.That(text.Text).IsEqualTo(string.Concat(pieces.Select(p => p.Text)));
		await Assert.That(text.Runs.SequenceEqual(ExpectedRuns(pieces))).IsTrue();
	}

	[Test]
	public async Task ConcatOfSpan_OverThreshold_SkipsEmptyPartsAndMatchesEager()
	{
		MarkupText[] pieces = [Piece(1, 600), MarkupText.Empty, Piece(1, 300), Piece(2, 400)];
		var text = MarkupText.Concat(pieces);

		await Assert.That(text.IsDeferred).IsTrue();
		await Assert.That(text.Length).IsEqualTo(1_300);
		await Assert.That(text.Text).IsEqualTo(string.Concat(pieces.Select(p => p.Text)));
		// The two red pieces meet across the empty part and coalesce into one run.
		await Assert.That(text.Runs.SequenceEqual(ExpectedRuns(pieces))).IsTrue();
		await Assert.That(text.Runs.Length).IsEqualTo(2);
	}

	/// <summary>
	/// SharpMUSH trims the end of each function argument it evaluates. Trimming an accumulator that
	/// ends in no space must not join it, or every pass of the loop pays for a full copy again.
	/// </summary>
	[Test]
	public async Task Trim_OnAChainWithNothingToTrim_LeavesItDeferred()
	{
		var text = MarkupText.Concat(Piece(1, 800), Piece(2, 800));
		var trimmed = text.Trim(TrimType.TrimBoth);

		await Assert.That(ReferenceEquals(trimmed, text)).IsTrue();
		await Assert.That(text.IsDeferred).IsTrue();
	}

	[Test]
	public async Task Trim_OnAChainEndingInSpaces_TrimsAcrossParts()
	{
		var text = MarkupText.Concat([MarkupText.Plain("  "), Piece(1, 1_100), MarkupText.Plain("   ")]);
		await Assert.That(text.IsDeferred).IsTrue();
		var trimmed = text.Trim(TrimType.TrimBoth);

		await Assert.That(trimmed.Text).IsEqualTo(new string('b', 1_100));
		await Assert.That(trimmed.Runs.SequenceEqual([new Run(0, 1_100, MarkupSet.Of(Red))])).IsTrue();
	}

	[Test]
	public async Task OperationsOnAChain_MatchTheJoinedText()
	{
		var pieces = Enumerable.Range(0, 300).Select(i => Piece(i, 5)).ToList();
		var chain = MarkupText.Empty;
		foreach (var piece in pieces) chain = MarkupText.Concat(chain, piece);
		var eager = new MarkupText(string.Concat(pieces.Select(p => p.Text)), [.. ExpectedRuns(pieces)]);

		await Assert.That(chain.IsDeferred).IsTrue();
		await Assert.That(MarkupTextSerializer.Serialize(chain, Registry)).IsEqualTo(MarkupTextSerializer.Serialize(eager, Registry));
		await Assert.That(chain.Substring(700, 50).Runs.SequenceEqual(eager.Substring(700, 50).Runs)).IsTrue();
		await Assert.That(chain.Equals(eager)).IsTrue();
	}

	[Test]
	public async Task Serializer_RoundTripsADeferredText()
	{
		var text = MarkupText.Concat([Piece(1, 700), Piece(0, 10), Piece(2, 700)]);
		await Assert.That(text.IsDeferred).IsTrue();

		var json = MarkupTextSerializer.Serialize(text, Registry);
		var back = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(back.Text).IsEqualTo(text.Text);
		await Assert.That(back.Runs.SequenceEqual(text.Runs)).IsTrue();
	}

	/// <summary>
	/// A MarkupText is shared between threads. Readers racing to join one chain all see the same text
	/// and runs, and Length never throws or reads a half-published state.
	/// </summary>
	[Test]
	public async Task ConcurrentReaders_OfOneChain_AllSeeTheSameResult()
	{
		var pieces = Enumerable.Range(0, 400).Select(i => Piece(i, 9)).ToList();
		var expectedText = string.Concat(pieces.Select(p => p.Text));
		var expectedRuns = ExpectedRuns(pieces);
		var failures = 0;

		for (var round = 0; round < 50; round++)
		{
			var chain = MarkupText.Empty;
			foreach (var piece in pieces) chain = MarkupText.Concat(chain, piece);
			using var start = new ManualResetEventSlim();
			var readers = Enumerable.Range(0, 8).Select(r => Task.Run(() =>
			{
				start.Wait();
				for (var i = 0; i < 200; i++)
					if (chain.Length != expectedText.Length) Interlocked.Increment(ref failures);
				if (r % 2 == 0 && !chain.Runs.SequenceEqual(expectedRuns)) Interlocked.Increment(ref failures);
				if (chain.Text != expectedText) Interlocked.Increment(ref failures);
				if (!chain.Runs.SequenceEqual(expectedRuns)) Interlocked.Increment(ref failures);
			})).ToArray();
			start.Set();
			await Task.WhenAll(readers);
		}

		await Assert.That(failures).IsEqualTo(0);
	}

	// ── Splice ───────────────────────────────────────────────────────────────────

	[Test]
	public async Task Splice_StyledEdits_KeepTheRunsBetweenThem()
	{
		var text = MarkupText.Concat([MarkupText.Wrap(Red, "aaaa"), MarkupText.Plain("bbbb"), MarkupText.Wrap(Blue, "cccc")]);
		var result = text.Splice([new Edit(2, 4, MarkupText.Wrap(Blue, "X")), new Edit(9, 0, MarkupText.Plain("YY"))]);

		await Assert.That(result.Text).IsEqualTo("aaXbbcYYccc");
		await Assert.That(result.Runs.SequenceEqual([
			new Run(0, 2, MarkupSet.Of(Red)),
			new Run(2, 1, MarkupSet.Of(Blue)),
			new Run(5, 1, MarkupSet.Of(Blue)),
			new Run(8, 3, MarkupSet.Of(Blue)),
		])).IsTrue();
	}

	/// <summary>More than 64 edits take their ranges from a rented buffer instead of the stack.</summary>
	[Test]
	public async Task ReplaceAll_WithManyHits_MatchesStringReplace()
	{
		var source = string.Join(";", Enumerable.Range(0, 500).Select(Word));
		var text = MarkupText.Concat(source.Split(';').Select((w, i) => i % 2 == 0 ? MarkupText.Wrap(Red, w + ";") : MarkupText.Plain(w + ";")).ToArray());
		var result = text.ReplaceAll(";", MarkupText.Wrap(Blue, "<>"));

		await Assert.That(result.Text).IsEqualTo((source + ";").Replace(";", "<>"));
		await Assert.That(result.Runs.Count(r => r.Markups.Equals(MarkupSet.Of(Blue)))).IsEqualTo(500);
		await Assert.That(result.Runs.Count(r => r.Markups.Equals(MarkupSet.Of(Red)))).IsEqualTo(250);
	}

	[Test]
	public async Task Splice_PlainTextAndPlainReplacements_HasNoRuns()
	{
		var result = MarkupText.Plain("hello world").Splice([new Edit(0, 5, MarkupText.Plain("HELLO")), new Edit(6, 5, MarkupText.Empty)]);
		await Assert.That(result.Text).IsEqualTo("HELLO ");
		await Assert.That(result.Runs.IsEmpty).IsTrue();
	}

	// ── Extraction ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Substring copies the runs overlapping its range into an array of exactly their number. Checked
	/// against clipping every run by hand, over random styled text with combining marks, so ranges
	/// that snap back from a mark are covered too.
	/// </summary>
	[Test]
	public async Task Substring_ClipsRunsLikeAReferenceClip()
	{
		var random = new Random(42);
		Tag[] tags = [Red, Blue, new("green"), new("bold")];
		var mismatches = 0;
		for (var trial = 0; trial < 500; trial++)
		{
			var parts = new List<MarkupText>();
			for (var i = random.Next(1, 30); i > 0; i--)
			{
				var s = new string((char)('a' + random.Next(26)), random.Next(1, 6)) + (random.Next(8) == 0 ? "é" : "");
				parts.Add(random.Next(3) == 0 ? MarkupText.Plain(s) : MarkupText.Wrap(tags[random.Next(tags.Length)], s));
			}
			var text = MarkupText.Concat(parts);
			for (var k = 0; k < 40; k++)
			{
				var start = random.Next(-2, text.Length + 2);
				var sub = text.Substring(start, random.Next(0, text.Length + 2));
				if (sub.Length == 0)
				{
					if (!sub.Runs.IsEmpty) mismatches++;
					continue;
				}
				// The text has no cluster longer than "e" + U+0301, so a start snaps back by at most one.
				var from = Math.Max(0, start);
				if (from < text.Length && text.Text[from] == '́') from--;
				var to = from + sub.Length;
				var expected = text.Runs
					.Where(r => r.Start < to && r.End > from)
					.Select(r => new Run(Math.Max(r.Start, from) - from, Math.Min(r.End, to) - Math.Max(r.Start, from), r.Markups));
				if (text.Text[from..to] != sub.Text || !expected.SequenceEqual(sub.Runs)) mismatches++;
			}
		}
		await Assert.That(mismatches).IsEqualTo(0);
	}

	// ── Split with options ───────────────────────────────────────────────────────

	[Test]
	public async Task Split_RemoveEmptyEntries_DropsEmptyWords()
	{
		var parts = MarkupText.Plain("  a  b c ").Split(" ", StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(parts.Select(p => p.Text)).IsEquivalentTo(["a", "b", "c"]);
	}

	[Test]
	public async Task Split_TrimEntries_TrimsEachSegmentButKeepsEmptyOnes()
	{
		var parts = MarkupText.Plain(" a ,, b\t,").Split(",", StringSplitOptions.TrimEntries);
		await Assert.That(parts.Select(p => p.Text).ToArray()).IsEquivalentTo(["a", "", "b", ""]);
	}

	[Test]
	public async Task Split_TrimAndRemoveEmpty_DropsSegmentsThatTrimToNothing()
	{
		var parts = MarkupText.Plain(" a , ,b ").Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(parts.Select(p => p.Text).ToArray()).IsEquivalentTo(["a", "b"]);
	}

	[Test]
	public async Task Split_TrimEntries_WithNoDelimiterFound_TrimsTheWholeText()
	{
		var parts = MarkupText.Plain("  word ").Split(",", StringSplitOptions.TrimEntries);
		await Assert.That(parts.Select(p => p.Text).ToArray()).IsEquivalentTo(["word"]);
	}

	[Test]
	public async Task Split_UndefinedOption_Throws()
	{
		await Assert.That(() => MarkupText.Plain("a b").Split(" ", (StringSplitOptions)4)).Throws<ArgumentException>();
	}

	[Test]
	public async Task Split_MarkupDelimiter_UsesItsPlainText()
	{
		var parts = MarkupText.Plain("a--b----c").Split(MarkupText.Wrap(Red, "--"), StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(parts.Select(p => p.Text).ToArray()).IsEquivalentTo(["a", "b", "c"]);
	}

	/// <summary>
	/// The options must give exactly what splitting and then filtering gives, styled runs included,
	/// on random lists of words, spaces and tabs.
	/// </summary>
	[Test]
	public async Task Split_WithOptions_MatchesSplitThenFilter()
	{
		var random = new Random(7);
		var mismatches = 0;
		for (var trial = 0; trial < 500; trial++)
		{
			var parts = new List<MarkupText>();
			for (var i = random.Next(0, 20); i > 0; i--)
			{
				var piece = random.Next(4) switch
				{
					0 => " ",
					1 => "\t",
					2 => "  ",
					_ => Word(random.Next(1000)),
				};
				parts.Add(random.Next(2) == 0 ? MarkupText.Plain(piece) : MarkupText.Wrap(random.Next(2) == 0 ? Red : Blue, piece));
			}
			var text = MarkupText.Concat(parts);

			foreach (var options in new[] { StringSplitOptions.None, StringSplitOptions.RemoveEmptyEntries, StringSplitOptions.TrimEntries, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries })
			{
				IEnumerable<MarkupText> expected = text.Split(" ");
				if (options.HasFlag(StringSplitOptions.TrimEntries))
					expected = expected.Select(s => s.Trim(TrimType.TrimBoth, " \t"));
				if (options.HasFlag(StringSplitOptions.RemoveEmptyEntries))
					expected = expected.Where(s => s.Length > 0);
				var actual = text.Split(" ", options);
				if (!expected.Select(s => (s.Text, Runs: string.Join(",", s.Runs))).SequenceEqual(actual.Select(s => (s.Text, Runs: string.Join(",", s.Runs)))))
					mismatches++;
			}
		}
		await Assert.That(mismatches).IsEqualTo(0);
	}

	/// <summary>
	/// A coloured list split into its words: each word carries one run, so its runs are one exact
	/// array, not a default builder grown for eight and copied out.
	/// </summary>
	[Test]
	public async Task SplittingColouredList_AllocatesNoRunBuilders()
	{
		const int iterations = 2_000;
		var list = MarkupText.Concat(Enumerable.Range(0, 100)
			.SelectMany(i => new[] { MarkupText.Wrap(i % 2 == 0 ? Red : Blue, Word(i)), MarkupText.Space })
			.ToArray());
		_ = list.Text;
		for (var i = 0; i < 100; i++) GC.KeepAlive(list.Split(" ", StringSplitOptions.RemoveEmptyEntries));

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < iterations; i++) GC.KeepAlive(list.Split(" ", StringSplitOptions.RemoveEmptyEntries));
		var perSplit = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;

		// 100 words: the array, and a string, a MarkupText and a one-run array each (about 13 KB). A
		// builder per word came to about 31 KB.
		await Assert.That(perSplit).IsBetween(6_000, 18_000);
	}
}
