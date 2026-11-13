using System.Globalization;
using System.Text;

/// <summary>
/// The measures count a stretch of printable ASCII with a vectorised scan and step through everything else
/// a character at a time. These check the two paths against the plain per-character walk they replaced,
/// over text that switches between them at every kind of boundary.
/// </summary>
public class DisplayWidthScanTests
{
	/// <summary>Pieces chosen to sit on either side of a switch: controls, DEL, marks, a joiner, wide and paired characters.</summary>
	private static readonly string[] Pieces =
	[
		"a", "Z", " ", "~", "hello world", "\u007F", "\t", "\n", "\r\n", "\u0007", "\u009B",
		"́", "é", "‍", "日", "本語", "Ａ", "ｱ", "가",
		"\U0001F468‍\U0001F469", "\U0001F600", "\uD83D", "\uDE00", "é", "ำ", " ",
	];

	private static IEnumerable<string> Samples()
	{
		var random = new Random(20261112);
		for (var i = 0; i < 2_000; i++)
		{
			var builder = new StringBuilder();
			var count = random.Next(0, 12);
			for (var j = 0; j < count; j++) builder.Append(Pieces[random.Next(Pieces.Length)]);
			yield return builder.ToString();
		}
	}

	/// <summary>What <see cref="DisplayWidth.Of(ReadOnlySpan{char}, ControlCharacterWidth)"/> computed before the scan.</summary>
	private static int ReferenceOf(string text, ControlCharacterWidth controls)
	{
		var width = 0;
		var afterJoiner = false;
		foreach (var rune in text.EnumerateRunes())
		{
			var cells = DisplayWidth.OfRune(rune, controls);
			if (afterJoiner && cells == 2) cells = 0;
			afterJoiner = rune.Value == 0x200D;
			width += cells;
		}
		return width;
	}

	/// <summary>What <see cref="DisplayWidth.IndexAtWidth(ReadOnlySpan{char}, int, ControlCharacterWidth)"/> computed before the scan.</summary>
	private static int ReferenceIndexAtWidth(string text, int cells, ControlCharacterWidth controls)
	{
		if (cells <= 0) return 0;
		var used = 0;
		var position = 0;
		while (position < text.Length)
		{
			int length, width;
			if (text[position] is >= ' ' and < '\u007F'
				&& (position + 1 >= text.Length || text[position + 1] is >= ' ' and < '\u007F'))
			{
				length = 1;
				width = 1;
			}
			else
			{
				length = StringInfo.GetNextTextElementLength(text.AsSpan(position));
				if (length <= 0) length = 1;
				width = ReferenceOf(text.Substring(position, length), controls);
			}
			if (used + width > cells) return position;
			used += width;
			position += length;
		}
		return text.Length;
	}

	/// <summary>
	/// The kana, ideograph and Hangul ranges answered before the category and wide-range lookups get what the lookups
	/// give: two cells, as letters.
	/// </summary>
	[Test]
	public async Task OfRune_IdeographAndHangulShortcut_AgreesWithTheTables()
	{
		var disagreeing = new List<int>();
		foreach (var (start, end) in new[] { (0x3041, 0x3096), (0x30A1, 0x30FA), (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xAC00, 0xD7A3) })
		{
			for (var value = start; value <= end; value++)
			{
				var rune = new Rune(value);
				var wide = DisplayWidthTable.WideRanges.Any(r => r.Start <= value && value <= r.End);
				if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.OtherLetter || !wide || DisplayWidth.OfRune(rune) != 2)
					disagreeing.Add(value);
			}
		}
		await Assert.That(disagreeing).IsEmpty();
	}

	[Test]
	public async Task Of_MatchesThePerCharacterWalk()
	{
		foreach (var text in Samples())
		{
			foreach (var controls in new[] { ControlCharacterWidth.Zero, ControlCharacterWidth.One })
				await Assert.That(DisplayWidth.Of(text, controls)).IsEqualTo(ReferenceOf(text, controls));
		}
	}

	/// <summary>
	/// The bounded measure the line breaker asks: exact at or under the limit, and over it whenever the
	/// whole text is, so "does the rest fit" gets the answer a full count gives.
	/// </summary>
	[Test]
	public async Task Of_WithALimit_AgreesWithTheFullCountAboutFitting()
	{
		foreach (var text in Samples())
		{
			var full = ReferenceOf(text, ControlCharacterWidth.Zero);
			for (var limit = 0; limit <= full + 1; limit++)
			{
				var bounded = DisplayWidth.Of(text, ControlCharacterWidth.Zero, limit);
				await Assert.That(bounded <= limit).IsEqualTo(full <= limit);
				if (full <= limit) await Assert.That(bounded).IsEqualTo(full);
			}
		}
	}

	[Test]
	public async Task IndexAtWidth_MatchesThePerClusterWalk()
	{
		foreach (var text in Samples())
		{
			var full = ReferenceOf(text, ControlCharacterWidth.One);
			for (var cells = 0; cells <= full + 1; cells++)
			{
				foreach (var controls in new[] { ControlCharacterWidth.Zero, ControlCharacterWidth.One })
				{
					await Assert.That(DisplayWidth.IndexAtWidth(text, cells, controls))
						.IsEqualTo(ReferenceIndexAtWidth(text, cells, controls));
				}
			}
		}
	}

	/// <summary>
	/// Wrapping measured the rest of the paragraph for every line it cut, which made a long paragraph cost
	/// the square of its length. Each line now measures no further than the line.
	/// </summary>
	[Test]
	public async Task WrapLines_OnALongParagraph_BreaksAtTheLastSpaceThatFits()
	{
		var words = string.Join(" ", Enumerable.Range(0, 2_000).Select(i => new string((char)('a' + i % 26), 1 + i % 7)));
		var lines = MarkupText.Plain(words).WrapLines(78);

		await Assert.That(string.Join(" ", lines.Select(l => l.ToPlainText()))).IsEqualTo(words);
		foreach (var line in lines)
		{
			await Assert.That(line.DisplayWidth).IsLessThanOrEqualTo(78);
			await Assert.That(line.ToPlainText()).DoesNotStartWith(" ");
		}
		// Every line but the last was cut at a space: the next word would not have fitted.
		for (var i = 0; i < lines.Length - 1; i++)
		{
			var next = lines[i + 1].ToPlainText().Split(' ')[0];
			await Assert.That(lines[i].DisplayWidth + 1 + next.Length).IsGreaterThan(78);
		}
	}
}
