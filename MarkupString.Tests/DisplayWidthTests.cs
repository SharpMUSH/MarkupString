using System.Text;

public class DisplayWidthTests
{
	[Test]
	public async Task Of_Ascii_IsOneCellPerCharacter()
		=> await Assert.That(DisplayWidth.Of("abc")).IsEqualTo(3);

	[Test]
	public async Task Of_EastAsianWide_IsTwoCellsPerCharacter()
		=> await Assert.That(DisplayWidth.Of("\u65E5\u672C")).IsEqualTo(4);

	[Test]
	public async Task Of_CombiningMark_CountsAsZero()
	{
		await Assert.That(DisplayWidth.Of("\u00E9")).IsEqualTo(1);
		await Assert.That(DisplayWidth.Of("e\u0301")).IsEqualTo(1);
	}

	[Test]
	public async Task Of_ZwjEmojiSequence_CountsOnce()
		=> await Assert.That(DisplayWidth.Of("\U0001F468\u200D\U0001F469\u200D\U0001F467")).IsEqualTo(2);

	[Test]
	public async Task Of_Empty_IsZero()
		=> await Assert.That(DisplayWidth.Of("")).IsEqualTo(0);

	[Test]
	public async Task Of_HalfwidthKatakana_IsOneCell()
		=> await Assert.That(DisplayWidth.Of("\uFF71")).IsEqualTo(1);

	[Test]
	public async Task Of_FullwidthLatin_IsTwoCells()
		=> await Assert.That(DisplayWidth.Of("\uFF21")).IsEqualTo(2);

	[Test]
	public async Task OfRune_ClassifiesControlsMarksAndJamoAsZero()
	{
		await Assert.That(DisplayWidth.OfRune(new Rune('\u0007'))).IsEqualTo(0);
		await Assert.That(DisplayWidth.OfRune(new Rune('\u009B'))).IsEqualTo(0);
		await Assert.That(DisplayWidth.OfRune(new Rune('\u0301'))).IsEqualTo(0);
		await Assert.That(DisplayWidth.OfRune(new Rune('\u200D'))).IsEqualTo(0);
		await Assert.That(DisplayWidth.OfRune(new Rune('\u1160'))).IsEqualTo(0);
		await Assert.That(DisplayWidth.OfRune(new Rune('a'))).IsEqualTo(1);
		await Assert.That(DisplayWidth.OfRune(new Rune('\u65E5'))).IsEqualTo(2);
	}

	[Test]
	public async Task IndexAtWidth_StopsBeforeSplittingAWideCharacter()
	{
		await Assert.That(DisplayWidth.IndexAtWidth("\u65E5\u672C\u8A9E", 5)).IsEqualTo(2);
		await Assert.That(DisplayWidth.IndexAtWidth("\u65E5\u672C\u8A9E", 6)).IsEqualTo(3);
		await Assert.That(DisplayWidth.IndexAtWidth("\u65E5\u672C\u8A9E", 0)).IsEqualTo(0);
		await Assert.That(DisplayWidth.IndexAtWidth("abcde", 3)).IsEqualTo(3);
		await Assert.That(DisplayWidth.IndexAtWidth("abc", 99)).IsEqualTo(3);
	}

	[Test]
	public async Task IndexAtWidth_KeepsCombiningMarkWithItsBase()
		=> await Assert.That(DisplayWidth.IndexAtWidth("e\u0301x", 1)).IsEqualTo(2);

	[Test]
	public async Task Of_ControlCharacters_AreZeroByDefault()
	{
		await Assert.That(DisplayWidth.Of("a\tb")).IsEqualTo(2);
		await Assert.That(DisplayWidth.Of("a\tb", ControlCharacterWidth.Zero)).IsEqualTo(2);
		await Assert.That(MarkupText.Plain("a\tb").DisplayWidth).IsEqualTo(2);
		await Assert.That(MarkupText.Plain("a\tb").GetDisplayWidth(ControlCharacterWidth.Zero)).IsEqualTo(2);
	}

	[Test]
	[Arguments('\u0000')]
	[Arguments('\u0007')]
	[Arguments('\t')]
	[Arguments('\n')]
	[Arguments('\r')]
	[Arguments('\u001B')]
	[Arguments('\u001F')]
	[Arguments('\u007F')]
	[Arguments('\u0080')]
	[Arguments('\u009B')]
	[Arguments('\u009F')]
	public async Task OfRune_ControlCharacterWidthOne_CountsEveryC0AndC1ControlAsOne(char control)
	{
		await Assert.That(DisplayWidth.OfRune(new Rune(control))).IsEqualTo(0);
		await Assert.That(DisplayWidth.OfRune(new Rune(control), ControlCharacterWidth.One)).IsEqualTo(1);
	}

	[Test]
	public async Task OfRune_ControlCharacterWidthOne_LeavesEverythingElseAlone()
	{
		// Only C0 and C1 change: the neighbours of both ranges, combining marks, joiners, jamo and
		// wide characters measure as they do by default.
		foreach (var value in new[] { 0x20, 0x7E, 0xA0, 0x0301, 0x200D, 0x1160, 0x65E5 })
		{
			var rune = new Rune(value);
			await Assert.That(DisplayWidth.OfRune(rune, ControlCharacterWidth.One)).IsEqualTo(DisplayWidth.OfRune(rune));
		}
	}

	[Test]
	public async Task Of_ControlCharacterWidthOne_CountsEachControlCodePoint()
	{
		await Assert.That(DisplayWidth.Of("a\tb", ControlCharacterWidth.One)).IsEqualTo(3);
		await Assert.That(DisplayWidth.Of("\t\t", ControlCharacterWidth.One)).IsEqualTo(2);
		await Assert.That(DisplayWidth.Of("a\r\nb", ControlCharacterWidth.One)).IsEqualTo(4);
		await Assert.That(DisplayWidth.Of("\u65E5\te\u0301", ControlCharacterWidth.One)).IsEqualTo(4);
	}

	[Test]
	public async Task GetDisplayWidth_ControlCharacterWidthOne_CountsControlsWithoutChangingDisplayWidth()
	{
		var text = MarkupText.Plain("a\tb\n");

		await Assert.That(text.GetDisplayWidth(ControlCharacterWidth.One)).IsEqualTo(4);
		await Assert.That(text.DisplayWidth).IsEqualTo(2);
	}

	[Test]
	public async Task IndexAtWidth_ControlCharacterWidthOne_GivesAControlACell()
	{
		await Assert.That(DisplayWidth.IndexAtWidth("a\tb", 1)).IsEqualTo(2);
		await Assert.That(DisplayWidth.IndexAtWidth("a\tb", 1, ControlCharacterWidth.One)).IsEqualTo(1);
		await Assert.That(DisplayWidth.IndexAtWidth("a\tb", 2, ControlCharacterWidth.One)).IsEqualTo(2);
	}

	[Test]
	public async Task IndexFromWidthEnd_ControlCharacterWidthOne_GivesAControlACell()
	{
		await Assert.That(DisplayWidth.IndexFromWidthEnd("a\tb", 1)).IsEqualTo(1);
		await Assert.That(DisplayWidth.IndexFromWidthEnd("a\tb", 1, ControlCharacterWidth.One)).IsEqualTo(2);
		await Assert.That(DisplayWidth.IndexFromWidthEnd("a\tb", 2, ControlCharacterWidth.One)).IsEqualTo(1);
	}
}
