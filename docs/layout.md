# Layout

Wrapping, justification, filling and column assembly. Everything here measures in **display
cells**, so wide characters take the two columns they occupy and combining marks take none.

```csharp
using MarkupString;
using MarkupString.Layout;

var column = new ColumnFormat { Width = 20, Wrap = WrapMode.Word, Alignment = Alignment.Center };

foreach (var line in text.FormatColumn(column)) Console.WriteLine(line.Render(MarkupFormat.Ansi));
```

If all you want is lines, there is a one-liner and you can stop reading here:

```csharp
text.WrapLines(40);                  // break at the last space that fits
text.WrapLines(40, WrapMode.Cell);   // break at the width, mid-word
```

## Recipes

Every output below is what the code actually prints.

**Wrap a paragraph.**

```csharp
MarkupText.Plain("The quick brown fox jumps over the lazy dog").WrapLines(20);
```

```
The quick brown fox
jumps over the lazy
dog
```

**A centred heading in a rule.** The fill is any `MarkupText`, so it can carry its own colour.

```csharp
var heading = new ColumnFormat { Width = 34, Alignment = Alignment.Center, Fill = MarkupText.Plain("-") };

MarkupText.Plain(" Inventory ").FormatColumn(heading);
```

```
----------- Inventory ------------
```

**Leader dots between a label and a value** — two columns, the first filled with dots, the second
right-aligned.

```csharp
var name  = new ColumnFormat { Width = 24, Fill = MarkupText.Plain(".") };
var value = new ColumnFormat { Width = 10, Alignment = Alignment.Right };

TextLayout.Rows(
[
    new LayoutColumn(MarkupText.Plain("Brass lantern"), name),
    new LayoutColumn(MarkupText.Plain("1"), value),
], new LayoutOptions());
```

```
Brass lantern...........         1
```

**A two-column page.** `Alignment.Paragraph` justifies every line except the one that ends a
paragraph, so the last line of each column keeps its natural spacing.

```csharp
var body = new ColumnFormat { Width = 24, Wrap = WrapMode.Word, Alignment = Alignment.Paragraph };

TextLayout.Rows(
[
    new LayoutColumn(left, body),
    new LayoutSeparator(MarkupText.Plain("  |  ")),
    new LayoutColumn(right, body),
], new LayoutOptions());
```

```
The  hall  is  long  and  |  A  fire burns at the far
low, its ceiling lost in  |  end.
smoke.                    |
```

**A hanging indent**, for a command list or a glossary.

```csharp
var hanging = new ColumnFormat { Width = 34, Wrap = WrapMode.Word, Indent = new Indent(4) };

MarkupText.Plain("look <thing> -- examine something in the room more closely").FormatColumn(hanging);
```

```
look <thing> -- examine something
    in the room more closely
```

Note what none of these had to say: nothing measures `string.Length`, and nothing special-cases
a wide character or a combining mark. Swap any of the text above for CJK or emoji and the columns
still line up, because every width here is a display cell.

## Three layers

A column is shaped, then drawn, then assembled with its neighbours. One record,
`ColumnFormat`, carries all three, composed with `with`:

| | What it decides | Properties |
|---|---|---|
| **Shape** | text → lines | `Width`, `Wrap`, `BreakSpace`, `TabWidth`, `MaxCells`, `MaxLines`, `Indent` |
| **Draw** | lines → a block of fixed width | `Alignment`, `Fill`, `FillRight`, `FillPhase`, `BlankLineFill`, `Truncation`, `CutFrom`, `NoFill`, `Markup` |
| **Assemble** | blocks → rows | `WhenEmpty`, `Repeat`, `NoSeparatorAfter`, `SuppressBlankLast` |

`Shape` gives you the intermediate if you want it — a `TextLine` per line, carrying the indent
offset, that line's width, and whether it ends a paragraph.

## Shaping

```
expand tabs → cut to MaxCells → wrap, stopping at MaxLines
```

The order is fixed because getting it wrong is silent. In particular the indent is applied
*inside* the wrap: a continuation line wraps at `Width - Indent.Amount`, so laying the indent on
afterwards would produce lines that no longer fit.

```csharp
new ColumnFormat { Width = 20, Wrap = WrapMode.Word, Indent = new Indent(5) }
```

```
this is a test with        ← 19 cells
    wrapping some          ← indented 4, so this line wrapped at 15
    text
```

`Indent(amount, fromLine, widen)` also delays the indent and widens the column from that line,
which is how a merged column grows.

| `WrapMode` | Behaviour |
|---|---|
| `None` | One line; a newline passes through untouched |
| `HardBreaks` | Break on newlines only, every line kept in the column |
| `Cell` | Break at the width, mid-word; newlines also break |
| `Word` | Break at the last space that fits; newlines also break |

`Word` falls back to a `Cell` break for a word longer than the column. Every line advances by at
least one grapheme cluster, so a column narrower than a single cluster terminates rather than
looping — it simply cannot draw that cluster, and truncation blanks it rather than breaking the
row's width.

## Drawing

A drawn line is a **background of the fill pattern with the text stamped onto it**. The pattern
is indexed by absolute cell position in the column, so it reads as one unbroken run:

```csharp
new ColumnFormat { Width = 40, Fill = MarkupText.Plain("0123456789") }
```

```
Left       ten char filler5678901234567890123456789
Right      0123456789012345678901234ten char filler
Center     012345678901ten char filler7890123456789
Full       ten34567890123456char1234567890123filler
```

The left-aligned case resumes at `5` because the text consumed cells 0 to 14, and the fully
justified case shows the pattern through every gap it opened. Set `FillPhase = FillPhase.Restart`
to begin the pattern afresh at each run of fill instead. A single-character fill — nearly every
use — is identical either way.

The fill is a `MarkupText`, so it carries its own markup; `Markup` on the format applies to the
whole line, fill included. `Alignment.Paragraph` justifies fully except on a line that ends a
paragraph, which is left-aligned.

## Assembling

A layout is a sequence of cells, each a column or the literal between two columns:

```csharp
var rows = TextLayout.Rows(
[
    new LayoutColumn(left, new ColumnFormat { Width = 12, Wrap = WrapMode.Word }),
    new LayoutSeparator(MarkupText.Plain(" | ")),
    new LayoutColumn(right, new ColumnFormat { Width = 30, Wrap = WrapMode.Word }),
], new LayoutOptions());
```

The row count comes from the tallest column that does not repeat. A column that has run out of
lines contributes its fill, so everything below stays aligned. `TextLayout.Render` joins the rows
with `LayoutOptions.RowSeparator`.

## Emulating a server

The engine speaks behaviour, never flag characters — which is deliberate, because the two
servers this covers use the same characters for different things:

| Character | PennMUSH | RhostMUSH |
|---|---|---|
| `-` | centre-justify | left-justify |
| `.` | repeat this column | suppress an all-blank last row |
| `` ` `` | merge with the column to the **left** | shift the **left** column to the right |
| `'` | merge with the column to the **right** | shift the **right** column to the left |

Map each server's characters onto the properties yourself. Where the two genuinely disagree,
both behaviours are here and neither is the library's opinion:

| Behaviour | PennMUSH | RhostMUSH | Property |
|---|---|---|---|
| Space at a word break | dropped | kept on the line, which may run a cell wide | `BreakSpace` |
| Separator on continuation rows | drawn | blanked | `LayoutSeparator.Rows` |
| An exhausted column | merges: a neighbour widens in place | shifts: a neighbour's line moves into its position | `WhenEmpty` |

`WhenEmpty` names four distinct things rather than two flags:

- `GiveSpaceToLeft` / `GiveSpaceToRight` — PennMUSH's merge. The neighbour absorbs this column's
  cells and goes on wrapping into the extra room.
- `PullRightColumnLeft` / `PushLeftColumnRight` — RhostMUSH's shift. The neighbour's line is drawn
  at this column's position and its own position is left blank. The text moves; nothing widens.
  The two slots trade widths along with the line, so a shift between columns of different widths
  still leaves the row the width it was.

A merge is abandoned rather than half-applied in two cases, both because applying half of one
would drop cells and leave the row short:

- the target already carries an `Indent` — one `Indent` cannot describe one width from one line
  and another from another;
- the giving column is itself a merge target — from the merge row on it is wider than its own
  `Width` says, so it cannot correctly pass "its" cells on. The merge *into* it wins.

Give the merge target no indent of its own if you need both, and keep merges to one link.

The rest maps straight across. PennMUSH's `x` is `Wrap = HardBreaks`; its `X` is that plus
`MaxLines = 1`; its `$` is `NoFill`, its `#` is `NoSeparatorAfter`, its `.` is `Repeat`, and its
`(ansi)` is `Markup`. RhostMUSH's `&` is `HardBreaks`, `|` is `Cell`, `|"` is `Word`, `+` is
`Truncation = Overflow`, `*` is `CutFrom = Start`, `/N/` is `MaxCells`, `/wN/` is `MaxLines`,
`#N#` is `TabWidth`, `;N.n+w;` is `Indent`, `:pattern:` is `Fill`, `:!pattern:` is
`BlankLineFill = Spaces`, and its `.` is `SuppressBlankLast`.

Their defaults differ too, and the library takes neither side: PennMUSH columns default to
`Wrap = Word`, RhostMUSH fields to `Wrap = None` and `Alignment = Right`. Set them explicitly.

## What is not here

Format-string parsing in any dialect, and anything that depends on values rather than text —
PennMUSH's argument counting, RhostMUSH's `!` `@` `<` `>` null-elision codes, which read
neighbouring *arguments* rather than the text in front of them. Resolve those first, then hand
the engine the columns you are left with.

## See also

- [Text operations](text-operations.md) — slicing, padding, trimming, and the grapheme rules
  every operation obeys.
