using System.Globalization;
using MarkupString.Ansi;
using MarkupString.Html;

public class GraphemeContractTests
{
	private sealed record Custom(string Name) : IMarkup;

	private static IEnumerable<string> Samples()
	{
		yield return "";
		yield return "a界e\u0301😀👍🏽👩‍👩‍👧‍👦🇺🇸\r\n";
		yield return "x" + "e" + new string('\u0301', 1024) + "tail";
		yield return "x" + string.Concat(Enumerable.Repeat("👩‍", 256)) + "👧tail";
		yield return string.Concat(Enumerable.Repeat("🇺", 129)) + "tail";
		yield return "\ud800a\udc00e\u0301";
		var random = new Random(1979);
		string[] atoms = ["a", "界", "\u0301", "😀", "🏽", "\u200d", "🇺", "🇸", "\r", "\n", "\u0600", "\u1100", "\u1161"];
		for (var i = 0; i < 50; i++)
			yield return string.Concat(Enumerable.Range(0, 30).Select(_ => atoms[random.Next(atoms.Length)]));
	}

	[Test]
	public async Task RangeEnumerationUsesNoManagedAllocation()
	{
		var text = string.Concat(Enumerable.Repeat("界e\u0301😀", 1000));
		for (var i = 0; i < 10; i++) _ = Graphemes.Count(text);
		var before = GC.GetAllocatedBytesForCurrentThread();
		var count = Graphemes.Count(text);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		await Assert.That(count).IsEqualTo(3000);
		await Assert.That(allocated).IsEqualTo(0L);
	}

	[Test]
	public async Task SegmentationAndSnappingMatchRuntimeAtEveryCodeUnit()
	{
		foreach (var text in Samples())
		{
			var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
			await Assert.That(Graphemes.Count(text)).IsEqualTo(boundaries.Length - 1);
			var actual = new List<int>();
			foreach (var range in Graphemes.Enumerate(text)) actual.Add(range.Start.Value);
			await Assert.That(actual.SequenceEqual(boundaries[..^1])).IsTrue();
			for (var i = 0; i <= text.Length; i++)
			{
				await Assert.That(Graphemes.SnapStart(text, i)).IsEqualTo(boundaries.Last(b => b <= i));
				await Assert.That(Graphemes.SnapEnd(text, i)).IsEqualTo(boundaries.First(b => b >= i));
				await Assert.That(Graphemes.IsBoundary(text, i)).IsEqualTo(boundaries.Contains(i));
				await Assert.That(MarkupText.Plain(text).Substring(i, text.Length - i).Text)
					.IsEqualTo(i == text.Length ? "" : text[boundaries.Last(b => b <= i)..]);
			}
		}
	}

	[Test]
	public async Task ExtractedClustersReconstructMixedMarkupIncludingInteriorRunBoundaries()
	{
		foreach (var text in Samples())
		{
			var parts = text.Select((c, i) => (i % 3) switch
			{
				0 => MarkupText.Wrap(new Custom("custom"), c.ToString()),
				1 => MarkupText.Wrap(AnsiMarkup.Create(underlined: true), c.ToString()),
				_ => MarkupText.Wrap(HtmlMarkup.Create("b"), c.ToString())
			}).ToArray();
			var source = MarkupText.Concat(parts.AsSpan());
			var clusters = source.EnumerateGraphemes().ToArray();
			var joined = MarkupText.Concat(clusters.AsSpan());
			await Assert.That(joined.Text).IsEqualTo(source.Text);
			await Assert.That(joined.Runs.SequenceEqual(source.Runs)).IsTrue();
			await Assert.That(clusters.Length).IsEqualTo(source.GraphemeCount);
			for (var i = 0; i < clusters.Length; i++)
			{
				var extracted = source.SubstringGraphemes(i, 1);
				await Assert.That(extracted.Text).IsEqualTo(clusters[i].Text);
				await Assert.That(extracted.Runs.SequenceEqual(clusters[i].Runs)).IsTrue();
			}
		}
	}

	[Test]
	public async Task ExplicitUnitsAndClamping()
	{
		var text = MarkupText.Plain("界e\u0301😀");
		await Assert.That(text.Length).IsEqualTo(5);
		await Assert.That(text.GraphemeCount).IsEqualTo(3);
		await Assert.That(text.DisplayWidth).IsEqualTo(5);
		await Assert.That(text.SubstringGraphemes(1).Text).IsEqualTo("e\u0301😀");
		await Assert.That(text.SubstringGraphemes(-1, int.MaxValue).Text).IsEqualTo(text.Text);
		await Assert.That(text.SubstringGraphemes(int.MaxValue).Text).IsEqualTo("");
		await Assert.That(text.SubstringGraphemes(0, -1).Text).IsEqualTo("");
	}
}
