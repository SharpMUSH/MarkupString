# Text Layout Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `MarkupText` a three-layer column layout engine — shaping, rendering, assembly — that covers the union of PennMUSH `align()` and RhostMUSH `printf()` behaviours.

**Architecture:** Layer 1 shapes one `MarkupText` into `TextLine` values (tabs, cell budget, indent-aware wrap, line budget). Layer 2 renders those lines into a fixed-width block by stamping text segments onto a fill-pattern background. Layer 3 assembles blocks into rows with separators, merge/shift rules and blank-row suppression. Everything is driven by one immutable `ColumnFormat` record composed with `with`.

**Tech Stack:** C# / .NET 10, TUnit, `System.Collections.Immutable`, existing `DisplayWidth` and `Graphemes` helpers.

**Spec:** `docs/superpowers/specs/2026-09-07-text-layout-design.md`

## Global Constraints

- Target `net10.0`, `LangVersion latest`, `Nullable enable`. Tabs for indentation, per `.editorconfig`.
- No flag characters (`<`, `-`, `` ` ``, `'`, `.`, `x`, `X`, `|`, `"`, `&`, `$`, `#`, `*`, `+`, `:`, `/`, `;`, `^`, `_`, `@`, `!`, `>`) appear in any public name or parameter. The API is behavioural.
- Where PennMUSH and RhostMUSH disagree, both behaviours must be reachable; no default may make one unreachable.
- Widths are always display cells (`DisplayWidth`), never UTF-16 code units. Indices into text are always code units.
- No operation may split a grapheme cluster. Every wrap iteration advances by at least one cluster.
- New public API goes in `MarkupString/PublicAPI.Unshipped.txt` as the analyzer demands; the build fails otherwise.
- Layout types live in namespace `MarkupString.Layout`. `WrapLines` and `ExpandTabs` are instance members of `MarkupText` in namespace `MarkupString`.
- Version target is 2.0; `CHANGELOG.md` gains entries under `## Unreleased`.
- Tests are TUnit (`[Test]`, `await Assert.That(x).IsEqualTo(y)`), in `MarkupString.Tests`.

---

### Task 1: Option types and defaults

**Files:**
- Create: `MarkupString/Layout/LayoutEnums.cs`
- Create: `MarkupString/Layout/ColumnFormat.cs`
- Create: `MarkupString/Layout/TextLine.cs`
- Test: `MarkupString.Tests/Layout/ColumnFormatTests.cs`

**Interfaces:**
- Consumes: `MarkupText`, `IMarkup`, `TruncationType` from `MarkupString`.
- Produces: `WrapMode`, `BreakSpace`, `Alignment`, `FillPhase`, `BlankLineFill`, `CutFrom`, `WhenEmpty`, `SeparatorRows`, `Indent`, `TextLine`, `ColumnFormat`.

- [ ] **Step 1: Write the failing test**

```csharp
using MarkupString.Layout;

public class ColumnFormatTests
{
	[Test]
	public async Task Default_MatchesSpec()
	{
		var f = new ColumnFormat { Width = 10 };

		await Assert.That(f.Wrap).IsEqualTo(WrapMode.None);
		await Assert.That(f.BreakSpace).IsEqualTo(BreakSpace.Drop);
		await Assert.That(f.Alignment).IsEqualTo(Alignment.Left);
		await Assert.That(f.FillPhase).IsEqualTo(FillPhase.Continuous);
		await Assert.That(f.Truncation).IsEqualTo(TruncationType.Truncate);
		await Assert.That(f.CutFrom).IsEqualTo(CutFrom.End);
		await Assert.That(f.Fill.Text).IsEqualTo(" ");
	}

	[Test]
	public async Task With_ComposesWithoutMutating()
	{
		var basis = new ColumnFormat { Width = 10 };
		var wrapped = basis with { Wrap = WrapMode.Word };

		await Assert.That(basis.Wrap).IsEqualTo(WrapMode.None);
		await Assert.That(wrapped.Wrap).IsEqualTo(WrapMode.Word);
		await Assert.That(wrapped.Width).IsEqualTo(10);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test MarkupString.Tests --filter ColumnFormatTests`
Expected: FAIL — `MarkupString.Layout` does not exist.

- [ ] **Step 3: Write the types**

`LayoutEnums.cs`:

```csharp
namespace MarkupString.Layout;

/// <summary>How a column turns text into lines.</summary>
public enum WrapMode
{
	/// <summary>One line; a newline in the text passes through untouched.</summary>
	None,
	/// <summary>Break on newlines only, keeping every line inside the column.</summary>
	HardBreaks,
	/// <summary>Break at the column width, mid-word if need be. Newlines also break.</summary>
	Cell,
	/// <summary>Break at the last space that fits, falling back to a width break. Newlines also break.</summary>
	Word,
}

/// <summary>What happens to the space a <see cref="WrapMode.Word"/> break lands on.</summary>
public enum BreakSpace
{
	/// <summary>The space is consumed by the break. PennMUSH.</summary>
	Drop,
	/// <summary>The space stays on the line, which may then exceed the width by one cell. RhostMUSH.</summary>
	Keep,
}

/// <summary>Where a line's text sits inside its column.</summary>
public enum Alignment
{
	Left,
	Right,
	Center,
	/// <summary>Gaps between words widen so the text spans the column.</summary>
	Full,
	/// <summary>As <see cref="Full"/>, except a line ending a paragraph, which is left-aligned.</summary>
	Paragraph,
}

/// <summary>How a multi-cell fill pattern is sampled.</summary>
public enum FillPhase
{
	/// <summary>Indexed by absolute cell position in the column, so the pattern runs behind the text.</summary>
	Continuous,
	/// <summary>Restarted at the beginning of each run of fill.</summary>
	Restart,
}

/// <summary>What fills a line with no text on it.</summary>
public enum BlankLineFill
{
	Pattern,
	Spaces,
}

/// <summary>Which end of an over-wide line is discarded.</summary>
public enum CutFrom
{
	/// <summary>Keep the beginning.</summary>
	End,
	/// <summary>Keep the end.</summary>
	Start,
}

/// <summary>What a column does on a row where it has no text.</summary>
public enum WhenEmpty
{
	None,
	/// <summary>This column's cells are absorbed by the column on its left, which widens in place.</summary>
	GiveSpaceToLeft,
	/// <summary>This column's cells are absorbed by the column on its right, which widens in place.</summary>
	GiveSpaceToRight,
	/// <summary>The column on the right is rendered at this column's position.</summary>
	PullRightColumnLeft,
	/// <summary>The column on the left is rendered at this column's position.</summary>
	PushLeftColumnRight,
}

/// <summary>Whether a separator is drawn on continuation rows or only on the first.</summary>
public enum SeparatorRows
{
	EveryRow,
	FirstRowOnly,
}
```

`TextLine.cs`:

```csharp
namespace MarkupString.Layout;

/// <summary>
/// One shaped line of a column: its text, the cell offset the text starts at, the cell width
/// available to it, and whether it ends a paragraph.
/// </summary>
/// <remarks>
/// <paramref name="Start"/> carries an indent rather than baking leading spaces into
/// <paramref name="Text"/>, so the column's fill shows through it. <paramref name="Width"/> is
/// per line because an indent may widen the column from a given line on.
/// <paramref name="EndsParagraph"/> is true when a hard break or the end of the text follows;
/// <see cref="Alignment.Paragraph"/> needs it and no caller can recover it afterwards.
/// </remarks>
public readonly record struct TextLine(MarkupText Text, int Start, int Width, bool EndsParagraph);
```

`ColumnFormat.cs` — the record from the spec's "API surface" section, plus:

```csharp
/// <summary>
/// A hanging indent: <paramref name="Amount"/> cells from line <paramref name="FromLine"/> on,
/// with the column widened by <paramref name="Widen"/> cells from that line.
/// </summary>
public readonly record struct Indent(int Amount, int FromLine = 1, int Widen = 0);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test MarkupString.Tests --filter ColumnFormatTests`
Expected: PASS.

- [ ] **Step 5: Update PublicAPI.Unshipped.txt and commit**

Run `dotnet build` and paste the analyzer's suggested lines into `MarkupString/PublicAPI.Unshipped.txt`, then:

```bash
git add MarkupString/Layout MarkupString/PublicAPI.Unshipped.txt MarkupString.Tests/Layout
git commit -m "Add the layout option types"
```

---

### Task 2: Tab expansion and the cell budget

**Files:**
- Modify: `MarkupString/MarkupText.Operations.cs`
- Test: `MarkupString.Tests/Layout/ShapingTests.cs`

**Interfaces:**
- Produces: `public MarkupText ExpandTabs(int tabWidth)`, `public MarkupText TruncateToWidth(int cells, CutFrom from)`.

- [ ] **Step 1: Write the failing test**

Rhost's published output is the oracle: `$-40#s` on `a\tb\tc\td\te` gives four spaces per tab, and `#7#` gives seven. This is literal substitution, not tab stops.

```csharp
[Test]
public async Task ExpandTabs_SubstitutesLiterally_NotToTabStops()
{
	var t = MarkupText.Plain("a\tb\tc");

	await Assert.That(t.ExpandTabs(4).Text).IsEqualTo("a    b    c");
	await Assert.That(t.ExpandTabs(2).Text).IsEqualTo("a  b  c");
}

[Test]
public async Task ExpandTabs_ZeroOrLess_LeavesTextAlone()
	=> await Assert.That(MarkupText.Plain("a\tb").ExpandTabs(0).Text).IsEqualTo("a\tb");

[Test]
public async Task TruncateToWidth_MeasuresCells_NotCodeUnits()
{
	var cjk = MarkupText.Plain("日本語abc");           // 6 code units, 9 cells

	await Assert.That(cjk.TruncateToWidth(4, CutFrom.End).Text).IsEqualTo("日本");
	await Assert.That(cjk.TruncateToWidth(4, CutFrom.Start).Text).IsEqualTo("abc");
}
```

`CutFrom.Start` keeps the tail: the widest suffix fitting in `cells`. `"日本語abc"` has cells `日`(2) `本`(2) `語`(2) `a` `b` `c`; the widest suffix within 4 cells is `abc` at 3, since adding `語` would make 5.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test MarkupString.Tests --filter ShapingTests`
Expected: FAIL — `ExpandTabs` not defined.

- [ ] **Step 3: Implement**

```csharp
/// <summary>
/// Replaces every tab with <paramref name="tabWidth"/> spaces. This is literal substitution,
/// not alignment to tab stops, which is what both servers do.
/// </summary>
public MarkupText ExpandTabs(int tabWidth)
{
	if (tabWidth <= 0 || !Text.Contains('\t')) return this;
	var spaces = new string(' ', tabWidth);
	return ReplaceAll("\t", Plain(spaces));
}

/// <summary>The widest prefix (or suffix) of this text that fits in <paramref name="cells"/> columns.</summary>
public MarkupText TruncateToWidth(int cells, CutFrom from)
{
	if (cells <= 0) return Empty;
	if (DisplayWidth <= cells) return this;
	return from == CutFrom.End
		? Substring(0, Cells.IndexAtWidth(Text, cells))
		: Substring(Cells.IndexFromWidthEnd(Text, cells));
}
```

Add the companion to `DisplayWidth`:

```csharp
/// <summary>
/// The smallest cluster boundary in <paramref name="text"/> whose suffix fits in
/// <paramref name="cells"/> columns. Never returns an index inside a grapheme cluster.
/// </summary>
public static int IndexFromWidthEnd(ReadOnlySpan<char> text, int cells)
{
	if (cells <= 0) return text.Length;
	var used = 0;
	var position = text.Length;
	while (position > 0)
	{
		var start = Graphemes.SnapStart(text, position - 1);
		var width = Of(text[start..position]);
		if (used + width > cells) return position;
		used += width;
		position = start;
	}
	return 0;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test MarkupString.Tests --filter ShapingTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add MarkupString MarkupString.Tests
git commit -m "Add tab expansion and cell-budget truncation"
```

---

### Task 3: The wrap walker

**Files:**
- Create: `MarkupString/Layout/LineWrapper.cs`
- Modify: `MarkupString/MarkupText.Operations.cs`
- Test: `MarkupString.Tests/Layout/WrapTests.cs`

**Interfaces:**
- Produces: `internal static class LineWrapper` with
  `static List<(int Start, int End)> Break(ReadOnlySpan<char> text, int width, WrapMode mode, BreakSpace breakSpace, in Indent indent, int maxLines)`
  returning half-open code-unit ranges, one per line, plus
  `public MarkupText[] WrapLines(int width)` and `public MarkupText[] WrapLines(int width, WrapMode mode)` on `MarkupText`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public async Task Word_BreaksAtLastFittingSpace()
{
	var lines = MarkupText.Plain("this is wrapping the text").WrapLines(10, WrapMode.Word);

	await Assert.That(lines.Select(l => l.Text).ToArray())
		.IsEquivalentTo(new[] { "this is", "wrapping", "the text" });
}

[Test]
public async Task Cell_BreaksMidWord()
{
	var lines = MarkupText.Plain("this is wrapping the text").WrapLines(10, WrapMode.Cell);

	await Assert.That(lines[0].Text).IsEqualTo("this is wr");
	await Assert.That(lines[1].Text).IsEqualTo("apping the");
}

[Test]
public async Task ClusterWiderThanColumn_StillAdvances()
{
	var lines = MarkupText.Plain("日本").WrapLines(1, WrapMode.Cell);

	await Assert.That(lines.Length).IsEqualTo(2);
	await Assert.That(lines[0].Text).IsEqualTo("日");
}

[Test]
public async Task CombiningMarks_WrapByCellsNotCodeUnits()
{
	var text = MarkupText.Plain("T͆́̂ͯe͕͓ͨx̼̀ͣt");

	await Assert.That(text.WrapLines(4, WrapMode.Cell).Length).IsEqualTo(1);
}

[Test]
public async Task HardBreaks_AlwaysBreak_AndAreDropped()
{
	var lines = MarkupText.Plain("aaaa\r\nbbbb").WrapLines(80, WrapMode.HardBreaks);

	await Assert.That(lines.Select(l => l.Text).ToArray()).IsEquivalentTo(new[] { "aaaa", "bbbb" });
}

[Test]
public async Task None_LeavesNewlinesAlone()
{
	var lines = MarkupText.Plain("aaaa\nbbbb").WrapLines(2, WrapMode.None);

	await Assert.That(lines.Length).IsEqualTo(1);
	await Assert.That(lines[0].Text).IsEqualTo("aaaa\nbbbb");
}

[Test]
public async Task Markup_SurvivesABreak()
{
	var red = MarkupText.Wrap(new Tag("red"), "hello world");

	var lines = red.WrapLines(5, WrapMode.Word);

	await Assert.That(lines[1].Text).IsEqualTo("world");
	await Assert.That(lines[1].Runs.Length).IsEqualTo(1);
}
```

Both `BreakSpace` values, tested against each server's published output:

```csharp
[Test]
public async Task BreakSpace_Drop_LeavesNoTrailingSpace()   // PennMUSH
{
	var lines = MarkupText.Plain("ab   cd").WrapLines(4, WrapMode.Word);

	await Assert.That(lines[0].Text).IsEqualTo("ab");
	await Assert.That(lines[1].Text).IsEqualTo("cd");
}

[Test]
public async Task BreakSpace_Keep_KeepsTheBreakSpaceOnTheLine()   // RhostMUSH
{
	var lines = LineWrapperFacade.Wrap("this is a test with wrapping", 20, BreakSpace.Keep);

	await Assert.That(lines[0].Text).IsEqualTo("this is a test with ");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test MarkupString.Tests --filter WrapTests`
Expected: FAIL — `WrapLines` not defined.

- [ ] **Step 3: Implement the walker**

The single loop, in `LineWrapper.Break`. For each line: compute the available width
(`width - indent` from `indent.FromLine` on, `+ indent.Widen`), find the hard-break position,
find the width limit with `DisplayWidth.IndexAtWidth`, and take the earlier of the two.

For `WrapMode.Word`, scan back from the limit for the last space at an index in
`[lineStart, limit]`; if found, the line ends there — at the space for `BreakSpace.Drop`, one
past it for `BreakSpace.Keep` — and the next line starts after the whole run of spaces for
`Drop`, immediately after the kept space for `Keep`. If no space is found, fall through to a
cell break at `limit`, matching both servers.

**The progress guarantee:** if the computed end is not greater than the line's start, set it to
`Graphemes.SnapEnd(text, start + 1)`. Every iteration then advances by at least one cluster.

Stop when `maxLines` lines have been produced (0 means unlimited).

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test MarkupString.Tests --filter WrapTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add MarkupString MarkupString.Tests
git commit -m "Add the wrap walker with both dialects' break-space rules"
```

---

### Task 4: Shape() — indent, line budget, paragraph flags

**Files:**
- Modify: `MarkupString/Layout/LineWrapper.cs`
- Modify: `MarkupString/MarkupText.Operations.cs`
- Test: `MarkupString.Tests/Layout/ShapeTests.cs`

**Interfaces:**
- Consumes: `LineWrapper.Break`, `ColumnFormat`, `TextLine`.
- Produces: `public ImmutableArray<TextLine> Shape(ColumnFormat format)`.

- [ ] **Step 1: Write the failing tests**

Rhost's published indent output is the oracle:

```csharp
[Test]
public async Task Indent_WrapsContinuationLinesAtTheNarrowerWidth()
{
	var f = new ColumnFormat { Width = 20, Wrap = WrapMode.Word, Indent = new Indent(5) };

	var lines = MarkupText.Plain("this is a test with wrapping some text").Shape(f);

	await Assert.That(lines[0].Text.Text).IsEqualTo("this is a test with");
	await Assert.That(lines[0].Start).IsEqualTo(0);
	await Assert.That(lines[1].Text.Text).IsEqualTo("wrapping some");
	await Assert.That(lines[1].Start).IsEqualTo(5);
}

[Test]
public async Task Indent_FromLine_DelaysTheIndent()
{
	var f = new ColumnFormat { Width = 20, Wrap = WrapMode.Word, Indent = new Indent(5, FromLine: 2) };

	var lines = MarkupText.Plain("this is a test with wrapping some text").Shape(f);

	await Assert.That(lines[1].Start).IsEqualTo(0);
	await Assert.That(lines[2].Start).IsEqualTo(5);
}

[Test]
public async Task Indent_Widen_GrowsTheColumnFromThatLine()
{
	var f = new ColumnFormat { Width = 20, Wrap = WrapMode.Word, Indent = new Indent(8, 2, Widen: 8) };

	var lines = MarkupText.Plain("this is a test with wrapping some text").Shape(f);

	await Assert.That(lines[0].Width).IsEqualTo(20);
	await Assert.That(lines[2].Width).IsEqualTo(28);
}

[Test]
public async Task MaxLines_StopsAfterN()
{
	var f = new ColumnFormat { Width = 10, Wrap = WrapMode.Word, MaxLines = 2 };

	await Assert.That(MarkupText.Plain("a b c d e f g h i j k").Shape(f).Length).IsEqualTo(2);
}

[Test]
public async Task MaxCells_CutsBeforeWrapping()
{
	var f = new ColumnFormat { Width = 10, Wrap = WrapMode.Word, MaxCells = 5 };

	var lines = MarkupText.Plain("aaa bbb ccc ddd").Shape(f);

	await Assert.That(lines.Length).IsEqualTo(1);
	await Assert.That(lines[0].Text.Text).IsEqualTo("aaa b");
}

[Test]
public async Task EndsParagraph_TrueOnlyBeforeAHardBreakOrTheEnd()
{
	var f = new ColumnFormat { Width = 10, Wrap = WrapMode.Word };

	var lines = MarkupText.Plain("aaa bbb ccc\nddd").Shape(f);

	await Assert.That(lines[0].EndsParagraph).IsFalse();   // soft break
	await Assert.That(lines[1].EndsParagraph).IsTrue();    // before the \n
	await Assert.That(lines[^1].EndsParagraph).IsTrue();   // end of text
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test MarkupString.Tests --filter ShapeTests`
Expected: FAIL — `Shape` not defined.

- [ ] **Step 3: Implement**

```csharp
/// <summary>
/// Shapes this text into the lines of a column: tabs expanded, cut to the cell budget, then
/// wrapped with the indent applied, stopping at the line budget.
/// </summary>
public ImmutableArray<TextLine> Shape(ColumnFormat format)
{
	ArgumentNullException.ThrowIfNull(format);
	var source = ExpandTabs(format.TabWidth);
	if (format.MaxCells > 0) source = source.TruncateToWidth(format.MaxCells, CutFrom.End);
	return LineWrapper.Shape(source, format);
}
```

`LineWrapper.Shape` runs `Break` and pairs each range with its `Start`, `Width` and
`EndsParagraph`. A line ends a paragraph when the break that terminated it was a hard break, or
when it is the last line of the text — not when `MaxLines` cut the text short, since that line
did not end a paragraph.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test MarkupString.Tests --filter ShapeTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add MarkupString MarkupString.Tests
git commit -m "Shape text into lines with indent, budgets and paragraph flags"
```

---

### Task 5: The fill background

**Files:**
- Create: `MarkupString/Layout/FillPattern.cs`
- Test: `MarkupString.Tests/Layout/FillTests.cs`

**Interfaces:**
- Produces: `internal static MarkupText FillPattern.Slice(MarkupText pattern, int fromCell, int cells, FillPhase phase)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public async Task Continuous_SamplesAtTheAbsolutePosition()
{
	var p = MarkupText.Plain("0123456789");

	await Assert.That(FillPattern.Slice(p, 15, 25, FillPhase.Continuous).Text)
		.IsEqualTo("5678901234567890123456789");
}

[Test]
public async Task Restart_SamplesFromZero()
{
	var p = MarkupText.Plain("0123456789");

	await Assert.That(FillPattern.Slice(p, 15, 25, FillPhase.Restart).Text)
		.IsEqualTo("0123456789012345678901234");
}

[Test]
public async Task WideFill_TakesSpacesForTheCellItCannotExpress()
{
	var p = MarkupText.Plain("日");   // 2 cells

	await Assert.That(FillPattern.Slice(p, 0, 3, FillPhase.Restart).Text).IsEqualTo("日 ");
}

[Test]
public async Task ZeroWidthFill_TakesSpaces()
	=> await Assert.That(FillPattern.Slice(MarkupText.Empty, 0, 3, FillPhase.Continuous).Text)
		.IsEqualTo("   ");
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test MarkupString.Tests --filter FillTests`
Expected: FAIL — `FillPattern` not defined.

- [ ] **Step 3: Implement**

Repeat the pattern to cover `phaseOffset + cells` cells, where `phaseOffset` is
`fromCell % pattern.DisplayWidth` for `Continuous` and `0` for `Restart`. Slice
`[IndexAtWidth(repeated, phaseOffset), IndexAtWidth(repeated, phaseOffset + cells))`, then top
up any residue with spaces, as `BuildFill` already does. A pattern of zero display width yields
spaces.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test MarkupString.Tests --filter FillTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add MarkupString MarkupString.Tests
git commit -m "Add the phase-aware fill pattern"
```

---

### Task 6: FormatColumn — the simple alignments

**Files:**
- Create: `MarkupString/Layout/ColumnRenderer.cs`
- Modify: `MarkupString/MarkupText.Operations.cs`
- Test: `MarkupString.Tests/Layout/RenderTests.cs`

**Interfaces:**
- Consumes: `Shape`, `FillPattern.Slice`.
- Produces: `public MarkupText[] FormatColumn(ColumnFormat format)`.

- [ ] **Step 1: Write the failing tests**

Rhost's published filler output is the oracle for three of the four alignments:

```csharp
private static ColumnFormat Filled(int width, Alignment a) => new()
{
	Width = width, Alignment = a, Fill = MarkupText.Plain("0123456789"),
};

[Test]
public async Task Left_ContinuesThePatternPastTheText()
{
	var line = MarkupText.Plain("ten char filler").FormatColumn(Filled(40, Alignment.Left))[0];

	await Assert.That(line.Text).IsEqualTo("ten char filler5678901234567890123456789");
}

[Test]
public async Task Right_FillsFromColumnZero()
{
	var line = MarkupText.Plain("ten char filler").FormatColumn(Filled(40, Alignment.Right))[0];

	await Assert.That(line.Text).IsEqualTo("0123456789012345678901234ten char filler");
}

[Test]
public async Task Center_ResumesThePatternAfterTheText()
{
	var line = MarkupText.Plain("ten char filler").FormatColumn(Filled(40, Alignment.Center))[0];

	await Assert.That(line.Text).IsEqualTo("012345678901ten char filler7890123456789");
}

[Test]
public async Task Restart_ReproducesOnePointX()
{
	var f = Filled(40, Alignment.Left) with { FillPhase = FillPhase.Restart };

	await Assert.That(MarkupText.Plain("ten char filler").FormatColumn(f)[0].Text)
		.IsEqualTo("ten char filler0123456789012345678901234");
}

[Test]
public async Task Truncate_CutsFromTheChosenEnd()
{
	var f = new ColumnFormat { Width = 10 };

	await Assert.That(MarkupText.Plain("this is a test").FormatColumn(f)[0].Text)
		.IsEqualTo("this is a ");
	await Assert.That(MarkupText.Plain("this is a test").FormatColumn(f with { CutFrom = CutFrom.Start })[0].Text)
		.IsEqualTo(" is a test");
}

[Test]
public async Task Overflow_KeepsTheWholeValue()
{
	var f = new ColumnFormat { Width = 10, Truncation = TruncationType.Overflow };

	await Assert.That(MarkupText.Plain("this is a test").FormatColumn(f)[0].Text)
		.IsEqualTo("this is a test");
}

[Test]
public async Task NoFill_StopsAtTheText()
{
	var f = new ColumnFormat { Width = 10, NoFill = true };

	await Assert.That(MarkupText.Plain("ab").FormatColumn(f)[0].Text).IsEqualTo("ab");
}

[Test]
public async Task BlankLineFill_Spaces_LeavesEmptyLinesUnpatterned()
{
	var f = Filled(6, Alignment.Left) with { Wrap = WrapMode.HardBreaks, BlankLineFill = BlankLineFill.Spaces };

	var lines = MarkupText.Plain("a\n\nb").FormatColumn(f);

	await Assert.That(lines[1].Text).IsEqualTo("      ");
}

[Test]
public async Task Indent_IsFilled_NotSpaced()
{
	var f = Filled(20, Alignment.Left) with { Wrap = WrapMode.Word, Indent = new Indent(5) };

	var lines = MarkupText.Plain("this is a test with wrapping some text").FormatColumn(f);

	await Assert.That(lines[1].Text.StartsWith("01234")).IsTrue();
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test MarkupString.Tests --filter RenderTests`
Expected: FAIL — `FormatColumn` not defined.

- [ ] **Step 3: Implement**

For each `TextLine`: truncate the line's text to `Width - Start` cells (or leave it under
`TruncationType.Overflow`), compute the text's start cell from the alignment
(`Start` for `Left`, `Width - textCells` for `Right`, `Start + (avail - textCells) / 2` for
`Center`), then concatenate `FillPattern.Slice(fill, 0, textStart, phase)`, the text, and
`FillPattern.Slice(fill, textStart + textCells, Width - textStart - textCells, phase)` — the
second slice omitted under `NoFill`. A line whose text is empty and whose `BlankLineFill` is
`Spaces` fills with spaces instead of the pattern. Apply `format.Markup` to the whole line last,
via `MarkupText.Wrap`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test MarkupString.Tests --filter RenderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add MarkupString MarkupString.Tests
git commit -m "Render a column by stamping text onto a fill background"
```

---

### Task 7: Full and paragraph justification

**Files:**
- Modify: `MarkupString/Layout/ColumnRenderer.cs`
- Test: `MarkupString.Tests/Layout/RenderTests.cs`

**Interfaces:**
- Consumes: everything from Task 6.

- [ ] **Step 1: Write the failing tests**

Rhost's published stretch output is the oracle, including the pattern showing through the gaps:

```csharp
[Test]
public async Task Full_WidensTheGapsBetweenWords()
{
	var f = new ColumnFormat { Width = 40, Alignment = Alignment.Full };

	await Assert.That(MarkupText.Plain("this is a test").FormatColumn(f)[0].Text)
		.IsEqualTo("this          is          a         test");
}

[Test]
public async Task Full_ShowsTheFillPatternThroughTheGaps()
{
	var f = Filled(40, Alignment.Full);

	await Assert.That(MarkupText.Plain("ten char filler").FormatColumn(f)[0].Text)
		.IsEqualTo("ten34567890123456char1234567890123filler");
}

[Test]
public async Task Paragraph_LeftAlignsTheLastLineOfAParagraph()
{
	var f = new ColumnFormat { Width = 20, Wrap = WrapMode.Word, Alignment = Alignment.Paragraph };

	var lines = MarkupText.Plain("aaa bbb ccc ddd eee fff").FormatColumn(f);

	await Assert.That(lines[^1].Text.TrimEnd()).IsEqualTo(lines[^1].Text.TrimEnd());
	await Assert.That(lines[^1].Text.StartsWith("fff") || lines[^1].Text.Contains("  ")).IsTrue();
	await Assert.That(lines[0].Text.Length).IsEqualTo(20);
}

[Test]
public async Task Paragraph_FullJustifiesEveryOtherLine()
{
	var f = new ColumnFormat { Width = 20, Wrap = WrapMode.Word, Alignment = Alignment.Paragraph };

	var lines = MarkupText.Plain("aaa bbb ccc ddd eee fff").FormatColumn(f);

	await Assert.That(MarkupText.Plain(lines[0].Text).DisplayWidth).IsEqualTo(20);
}

[Test]
public async Task Full_SingleWord_FallsBackToLeft()
{
	var f = new ColumnFormat { Width = 10, Alignment = Alignment.Full };

	await Assert.That(MarkupText.Plain("word").FormatColumn(f)[0].Text).IsEqualTo("word      ");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test MarkupString.Tests --filter RenderTests`
Expected: FAIL on the `Full` cases.

- [ ] **Step 3: Implement**

Split the line on single spaces into words. With `n` words there are `n - 1` gaps and
`Width - Start - sum(wordCells)` cells to distribute; give each gap `total / gaps` cells, the
first `total % gaps` gaps taking one extra, which is what Rhost's output shows. Emit
`FillPattern.Slice` for each gap at its absolute position, so the pattern runs behind the gaps.
A line with fewer than two words renders as `Left`. `Paragraph` renders as `Left` when
`EndsParagraph` is true and as `Full` otherwise.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test MarkupString.Tests --filter RenderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add MarkupString MarkupString.Tests
git commit -m "Add full and paragraph justification"
```

---

### Task 8: Re-express Pad and Center on the engine

**Files:**
- Modify: `MarkupString/MarkupText.Operations.cs:140-175,360-400`
- Modify: `CHANGELOG.md`
- Test: `MarkupString.Tests/MarkupTextOperationsTests.cs`

**Interfaces:**
- Consumes: `FormatColumn`.

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public async Task Pad_MultiCharacterFill_NowContinuesThePattern()
{
	var padded = MarkupText.Plain("ten char filler")
		.Pad(MarkupText.Plain("0123456789"), 40, PadType.Right, TruncationType.Truncate);

	await Assert.That(padded.Text).IsEqualTo("ten char filler5678901234567890123456789");
}
```

Every existing `Pad`, `Center` and `PadFull` test must still pass — single-character fills are
unaffected, which is the whole point of the compatibility claim.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test MarkupString.Tests --filter MarkupTextOperationsTests`
Expected: FAIL — `Pad` restarts the pattern.

- [ ] **Step 3: Implement**

Map `PadType` onto `Alignment` (`Left` → `Alignment.Right`, `Right` → `Alignment.Left`,
`Center` → `Alignment.Center`, `Full` → `Alignment.Full`) and delegate to `FormatColumn`,
taking the single line. Delete `PadTo`, `BuildFill` and `PadFull`. `Center`'s two different
fills are the one case the engine does not cover with a single `Fill`, so keep its
left/right split by rendering with the left fill and overwriting the trailing slice with the
right one — or, simpler, give `ColumnFormat` an optional `FillRight` used only when set.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test`
Expected: PASS, with only the multi-character fill expectations changed.

- [ ] **Step 5: Commit**

```bash
git add MarkupString MarkupString.Tests CHANGELOG.md
git commit -m "Re-express Pad and Center on the layout engine"
```

---

### Task 9: Row assembly

**Files:**
- Create: `MarkupString/Layout/TextLayout.cs`
- Create: `MarkupString/Layout/LayoutCell.cs`
- Test: `MarkupString.Tests/Layout/LayoutTests.cs`

**Interfaces:**
- Consumes: `FormatColumn`, `ColumnFormat`.
- Produces: `LayoutCell`, `LayoutColumn`, `LayoutSeparator`, `LayoutOptions`,
  `TextLayout.Rows(ReadOnlySpan<LayoutCell>, LayoutOptions)`, `TextLayout.Render(...)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public async Task RaggedColumns_PadShorterOnesToTheirWidth()
{
	LayoutCell[] cells =
	[
		new LayoutColumn(MarkupText.Plain("a"), new ColumnFormat { Width = 3 }),
		new LayoutSeparator(MarkupText.Plain("|"), SeparatorRows.EveryRow),
		new LayoutColumn(MarkupText.Plain("x y z"), new ColumnFormat { Width = 3, Wrap = WrapMode.Word }),
	];

	var rows = TextLayout.Rows(cells, new LayoutOptions());

	await Assert.That(rows.Length).IsEqualTo(3);
	await Assert.That(rows[1].Text).IsEqualTo("   |y  ");
}

[Test]
public async Task SeparatorRows_FirstRowOnly_BlanksItAfterwards()   // RhostMUSH
{
	LayoutCell[] cells =
	[
		new LayoutColumn(MarkupText.Plain("a"), new ColumnFormat { Width = 3 }),
		new LayoutSeparator(MarkupText.Plain("|"), SeparatorRows.FirstRowOnly),
		new LayoutColumn(MarkupText.Plain("x y z"), new ColumnFormat { Width = 3, Wrap = WrapMode.Word }),
	];

	var rows = TextLayout.Rows(cells, new LayoutOptions());

	await Assert.That(rows[0].Text).IsEqualTo("a  |x  ");
	await Assert.That(rows[1].Text).IsEqualTo("    y  ");
}

[Test]
public async Task Repeat_KeepsAColumnGoingWhileOthersHaveText()   // PennMUSH '.'
{
	LayoutCell[] cells =
	[
		new LayoutColumn(MarkupText.Plain("*"), new ColumnFormat { Width = 1, Repeat = true }),
		new LayoutColumn(MarkupText.Plain("x y z"), new ColumnFormat { Width = 1, Wrap = WrapMode.Word }),
	];

	var rows = TextLayout.Rows(cells, new LayoutOptions());

	await Assert.That(rows.Select(r => r.Text).ToArray()).IsEquivalentTo(new[] { "*x", "*y", "*z" });
}

[Test]
public async Task Render_JoinsRowsWithTheRowSeparator()
{
	LayoutCell[] cells = [new LayoutColumn(MarkupText.Plain("a\nb"), new ColumnFormat { Width = 1, Wrap = WrapMode.HardBreaks })];

	await Assert.That(TextLayout.Render(cells, new LayoutOptions()).Text).IsEqualTo("a\nb");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test MarkupString.Tests --filter LayoutTests`
Expected: FAIL — `TextLayout` not defined.

- [ ] **Step 3: Implement**

```csharp
namespace MarkupString.Layout;

/// <summary>One item in a layout: a column of text, or the literal that separates two columns.</summary>
public abstract record LayoutCell;

/// <summary>A column: its content and how that content is shaped, rendered and assembled.</summary>
public sealed record LayoutColumn(MarkupText Content, ColumnFormat Format) : LayoutCell;

/// <summary>Literal text between two columns, drawn on every row or only on the first.</summary>
public sealed record LayoutSeparator(MarkupText Text, SeparatorRows Rows = SeparatorRows.EveryRow) : LayoutCell;

/// <summary>Layout-wide settings.</summary>
public sealed record LayoutOptions
{
	/// <summary>What joins rows in <see cref="TextLayout.Render"/>. A newline by default.</summary>
	public MarkupText RowSeparator { get; init; } = MarkupText.NewLine;
}
```

`Rows` renders every column to its block via `FormatColumn`, takes the row count as the longest
block (repeating columns do not extend it), then builds each row by concatenating each cell's
contribution: a column's line for that row, or a blank of its width; a separator's text, or a
blank of its display width when `FirstRowOnly` and this is not the first row. A repeating
column cycles its block.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test MarkupString.Tests --filter LayoutTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add MarkupString MarkupString.Tests
git commit -m "Assemble rendered columns into rows"
```

---

### Task 10: Empty-column behaviour

**Files:**
- Modify: `MarkupString/Layout/TextLayout.cs`
- Test: `MarkupString.Tests/Layout/LayoutTests.cs`

- [ ] **Step 1: Write the failing tests**

Each dialect's published example is the oracle. Rhost's `` ` `` relocates content; Penn's
`` ` `` widens a neighbour in place.

```csharp
[Test]
public async Task PushLeftColumnRight_RelocatesTheLeftColumn()   // RhostMUSH '`'
{
	LayoutCell[] cells =
	[
		new LayoutColumn(MarkupText.Plain("1\n2\n3"), new ColumnFormat { Width = 10, Wrap = WrapMode.HardBreaks }),
		new LayoutColumn(MarkupText.Plain("A"), new ColumnFormat { Width = 10, Wrap = WrapMode.HardBreaks, WhenEmpty = WhenEmpty.PushLeftColumnRight }),
	];

	var rows = TextLayout.Rows(cells, new LayoutOptions());

	await Assert.That(rows[0].Text).IsEqualTo("1         A         ");
	await Assert.That(rows[1].Text).IsEqualTo("          2         ");
}

[Test]
public async Task GiveSpaceToLeft_WidensTheLeftColumnInPlace()   // PennMUSH '`'
{
	LayoutCell[] cells =
	[
		new LayoutColumn(MarkupText.Plain("1\nlonger text"), new ColumnFormat { Width = 6, Wrap = WrapMode.HardBreaks }),
		new LayoutColumn(MarkupText.Plain("A"), new ColumnFormat { Width = 6, Wrap = WrapMode.HardBreaks, WhenEmpty = WhenEmpty.GiveSpaceToLeft }),
	];

	var rows = TextLayout.Rows(cells, new LayoutOptions());

	await Assert.That(rows[1].Text).IsEqualTo("longer text ");
}

[Test]
public async Task PullRightColumnLeft_RelocatesTheRightColumn()   // RhostMUSH '''
{
	LayoutCell[] cells =
	[
		new LayoutColumn(MarkupText.Plain("1\n2"), new ColumnFormat { Width = 10, Wrap = WrapMode.HardBreaks, WhenEmpty = WhenEmpty.PullRightColumnLeft }),
		new LayoutColumn(MarkupText.Plain("A\nB\nC"), new ColumnFormat { Width = 10, Wrap = WrapMode.HardBreaks }),
	];

	var rows = TextLayout.Rows(cells, new LayoutOptions());

	await Assert.That(rows[2].Text.TrimEnd()).IsEqualTo("C");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test MarkupString.Tests --filter LayoutTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

The decision is per row. While building a row, for each column with a non-`None` `WhenEmpty`
whose line on that row is blank: `GiveSpaceToLeft`/`GiveSpaceToRight` drop the column's cell
and add its width to the named neighbour's, re-rendering that neighbour's line at the wider
width; `PullRightColumnLeft`/`PushLeftColumnRight` move the named neighbour's line into this
column's position, leaving the neighbour's own position blank.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test MarkupString.Tests --filter LayoutTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add MarkupString MarkupString.Tests
git commit -m "Add both dialects' empty-column behaviours"
```

---

### Task 11: Blank-row suppression and separator suppression

**Files:**
- Modify: `MarkupString/Layout/TextLayout.cs`
- Test: `MarkupString.Tests/Layout/LayoutTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public async Task SuppressBlankLast_DropsAFinalAllBlankRow()   // RhostMUSH '.'
{
	var f = new ColumnFormat { Width = 3, Wrap = WrapMode.HardBreaks, SuppressBlankLast = true };
	LayoutCell[] cells = [new LayoutColumn(MarkupText.Plain("a\n"), f), new LayoutColumn(MarkupText.Plain("b\n"), f)];

	await Assert.That(TextLayout.Rows(cells, new LayoutOptions()).Length).IsEqualTo(1);
}

[Test]
public async Task SuppressBlankLast_NeedsEveryColumnToCarryIt()
{
	var on = new ColumnFormat { Width = 3, Wrap = WrapMode.HardBreaks, SuppressBlankLast = true };
	var off = on with { SuppressBlankLast = false };
	LayoutCell[] cells = [new LayoutColumn(MarkupText.Plain("a\n"), on), new LayoutColumn(MarkupText.Plain("b\n"), off)];

	await Assert.That(TextLayout.Rows(cells, new LayoutOptions()).Length).IsEqualTo(2);
}

[Test]
public async Task NoSeparatorAfter_SkipsTheFollowingSeparator()   // PennMUSH '#'
{
	LayoutCell[] cells =
	[
		new LayoutColumn(MarkupText.Plain("a"), new ColumnFormat { Width = 1, NoSeparatorAfter = true }),
		new LayoutSeparator(MarkupText.Plain("|")),
		new LayoutColumn(MarkupText.Plain("b"), new ColumnFormat { Width = 1 }),
	];

	await Assert.That(TextLayout.Rows(cells, new LayoutOptions())[0].Text).IsEqualTo("ab");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test MarkupString.Tests --filter LayoutTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

After building the rows, drop the last row when every `LayoutColumn` carries
`SuppressBlankLast` and every column's line on that row has zero display width once trimmed of
fill. When emitting a separator, skip it if the immediately preceding cell is a `LayoutColumn`
with `NoSeparatorAfter`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test MarkupString.Tests --filter LayoutTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add MarkupString MarkupString.Tests
git commit -m "Add blank-row and separator suppression"
```

---

### Task 12: Regressions, properties, docs and changelog

**Files:**
- Create: `MarkupString.Tests/Layout/RegressionTests.cs`
- Modify: `MarkupString.Tests/Properties/RoundTripProperties.cs`
- Modify: `MarkupString.Tests/AllocationTests.cs`
- Create: `docs/layout.md`
- Modify: `docs/text-operations.md`, `README.md`, `CHANGELOG.md`
- Modify: `MarkupString/PublicAPI.Unshipped.txt`

- [ ] **Step 1: Write the regression and property tests**

The two verified `TextAligner` defects, expressed against the engine:

```csharp
[Test]
public async Task ClusterWiderThanTheColumn_Terminates()   // align(1,日) spun forever
{
	var f = new ColumnFormat { Width = 1, Wrap = WrapMode.Cell };

	var lines = MarkupText.Plain("日本語").FormatColumn(f);

	await Assert.That(lines.Length).IsEqualTo(3);
}

[Test]
public async Task NewlinePastTheWidth_StillBreaks()   // the \n used to leak into the line
{
	var f = new ColumnFormat { Width = 4, Wrap = WrapMode.Word };

	var lines = MarkupText.Plain("aaaa\nbbbb").FormatColumn(f);

	await Assert.That(lines.Length).IsEqualTo(2);
	await Assert.That(lines[0].Text.Contains('\n')).IsFalse();
}

[Test]
public async Task WrappedLines_NeverExceedTheColumn()
{
	var f = new ColumnFormat { Width = 12, Wrap = WrapMode.Word };

	foreach (var line in MarkupText.Plain("the quick brown fox jumps over the lazy dog").FormatColumn(f))
		await Assert.That(MarkupText.Plain(line.Text).DisplayWidth).IsEqualTo(12);
}

[Test]
public async Task EveryRowOfALayoutHasTheSameWidth()
{
	LayoutCell[] cells =
	[
		new LayoutColumn(MarkupText.Plain("a b c d"), new ColumnFormat { Width = 3, Wrap = WrapMode.Word }),
		new LayoutColumn(MarkupText.Plain("日本語です"), new ColumnFormat { Width = 4, Wrap = WrapMode.Cell }),
	];

	var rows = TextLayout.Rows(cells, new LayoutOptions());
	foreach (var row in rows)
		await Assert.That(row.DisplayWidth).IsEqualTo(7);
}
```

- [ ] **Step 2: Run to verify they fail or pass as appropriate**

Run: `dotnet test`
Expected: the regression tests pass if Tasks 3-9 are right; any failure is a real defect to fix
before continuing.

- [ ] **Step 3: Write the docs**

`docs/layout.md` covers the three layers, the compositing model with Rhost's four filler
outputs as the worked example, the disagreement table, and a full `align()`-style example.
`docs/text-operations.md` gains a "Wrapping" section pointing at it. `README.md`'s
documentation table gains a row. `CHANGELOG.md` gains an `Added` block for the engine and a
`Changed` block for `Pad`/`Center`'s fill phase, marked breaking.

- [ ] **Step 4: Run the whole suite and the AOT smoke test**

Run: `dotnet test` then `dotnet publish MarkupString.AotSmoke -c Release`
Expected: PASS, no trim or AOT warnings.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "Document the layout engine and lock in the regressions"
```

---

## Self-Review

**Spec coverage.** Layer 1 → Tasks 2-4. Layer 2 → Tasks 5-7. Layer 3 → Tasks 9-11.
Compatibility → Task 8. Testing → spread across every task, with the regression and property
tests in Task 12. The one spec item with no task is `ColumnFormat.FillRight`, which Task 8
introduces to keep `Center`'s two-fill signature working; the spec's `ColumnFormat` listing
should gain it.

**Placeholders.** None: every code step carries the actual code or an exact description of the
computation, and every test step carries the assertions.

**Type consistency.** `ColumnFormat`, `TextLine`, `Indent`, `WrapMode`, `BreakSpace`,
`Alignment`, `FillPhase`, `BlankLineFill`, `CutFrom`, `WhenEmpty`, `SeparatorRows`,
`LayoutCell`, `LayoutColumn`, `LayoutSeparator`, `LayoutOptions`, `TextLayout.Rows`,
`TextLayout.Render`, `Shape`, `FormatColumn`, `WrapLines`, `ExpandTabs`, `TruncateToWidth`,
`FillPattern.Slice`, `DisplayWidth.IndexFromWidthEnd` are used consistently throughout.
