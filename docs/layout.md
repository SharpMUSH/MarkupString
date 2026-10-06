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

## Blocks: boxes, rules, columns and pictures

`TextLayout` assembles columns into rows. One level up, `BlockLayout` lays out a tree of blocks —
the boxes, titled rules, side-by-side columns and pictures a game draws its `+finger` and `+sheet`
screens with — and keeps the tree with the text, so a format that can draw structure does.

```csharp
var finger = new BoxNode(
  new StackNode(
  [
    new FlexNode(
    [
      new FlexItem(new TextNode(MarkupText.Plain("Sex: Male\nSpecies: Human")), BlockSize.Cells(35)),
      new FlexItem(new TextNode(MarkupText.Plain("Job: Dark Warrior\nOnline: 1h")), BlockSize.Cells(36)),
    ], new FlexOptions { Separator = MarkupText.Plain(" | ") }),
    new RuleNode(MarkupText.Plain("Quote"), BorderStyle.Mush),
    new TextNode(MarkupText.Plain("Hooooo?")),
  ]),
  BorderStyle.Mush with { TitleOpen = MarkupText.Plain("<< "), TitleClose = MarkupText.Plain(" >>") },
  MarkupText.Plain("Mannaz Byron"));

var text = BlockLayout.Build(finger, 78);
```

```
+=============================<< Mannaz Byron >>=============================+
| Sex: Male                           | Job: Dark Warrior                    |
| Species: Human                      | Online: 1h                           |
+==================================< Quote >=================================+
| Hooooo?                                                                    |
+============================================================================+
```

That is `text.ToPlainText()` and what every terminal format writes. `Render(MarkupFormat.Html)`
draws the same tree as a `<fieldset>` with a legend, a divider, and a flex row whose items ask for
35 and 36 `ch` and wrap onto rows of their own on a narrower page. Include `LayoutCss.Fixed`, or
your own copy of its rules, on the page.

| Node | Terminal | HTML |
|---|---|---|
| `BoxNode` | the frame, its title set into the top edge | `<fieldset>` and `<legend>` |
| `RuleNode` | a line of the border's top edge with the title in it; inside a box, a divider meeting the sides | a line drawn in CSS |
| `FlexNode` | items side by side at widths shared from `BlockSize` bases, stacked when one would fall under its `Min` | a wrapping flex row |
| `FigureNode` | the text art, with `Beside` flowing round it | an `<img>` floated beside it |
| `FieldsNode` | labels in one column, values lined up in the next, a long value wrapping under itself | a `<dl>` laid out as a two-column grid |
| `TreeNode` | items under their parents, joined by guide lines | nested `<ul>` with the guides drawn in CSS |
| `GaugeNode` | a bar filled to its share of the width, with its figures | a `<meter>` |
| `BulletsNode` | items with a bullet or number, wrapped lines hanging under the text | `<ul>` or `<ol>` |
| `GridNode` | short items in as many columns as fit, down each column or across each row | a CSS multi-column or grid list |
| `TableNode` | columns sized to their widest cell, wrapping and then leaving out columns when narrow | a `<table>` that hides low-priority columns on a narrow page |
| `TextNode`, `StackNode` | wrapped text; children in order | the same, as blocks |

**Labelled values.** `FieldsNode` is the `Sex: Male` / `Species: Human` part of a sheet: the label
column is as wide as the longest label (at most half the width) and every value starts in the same
column. `FieldsOptions` sets the separator (`": "`), right-aligned labels, a leader that fills from
the label to the separator, and how many columns the fields are dealt into, down each column first.

```
Columns = 2, at 60:
Sex:     Male                   Job:    Dark Warrior
Species: Human                  Origin: Super Robot Wars AG

LabelAlignment = Alignment.Right:
    Sex: Male
Species: Human

Leader = MarkupText.Plain("."):
Sex....: Male
Species: Human
```

When the value column would be narrower than ten cells, each label goes on a line of its own with
its value indented under it, and columns that do not fit stack.

**Trees.** `TreeNode` draws each `TreeItem` with its children under it. The top level sits at the left
edge; each level below gets a guide. `TreeGuide` has six presets (`line`, `rounded`, `heavy`,
`double`, `ascii`, `none`), and its four pieces (branch, last branch, the pipe that carries a level on,
and the blank where it has ended) can be replaced.

```
Channels                  Channels
├─ Public                 |- Public
│  ├─ +chat               |  |- +chat
│  └─ +ooc                |  `- +ooc
└─ Staff                  `- Staff
   └─ +admin                 `- +admin
```

**Gauges.** `GaugeNode(value, maximum, label)` draws a bar. With no `BarWidth` the bar takes what the
label and figures leave of the width. `GaugeOptions` sets the filled and empty pieces (`█`, `░`), the
ends, and whether the figures read `50%`, `6/12` or nothing.

```
HP [██████░░░░░] 50%         at 20
HP [######-----] 50%         AsciiOnly
HP: 6 of 12 (50%)            Linear
```

**Gradients.** `GaugeOptions.Gradient` shades the filled part with a `ColorGradient`: colour stops
(any `IColorMarkup`, such as an `AnsiMarkup` with a foreground) blended in a `GradientSpace`.
`Oklch`, the default, keeps the middle as bright and vivid as the ends, so red to green passes
through yellow rather than sRGB's dark olive; `Oklab` blends straight across with no hue swing;
`Hsl` gives the brighter, uneven rainbow sweep. With `GaugeShade.Cells` each cell takes the colour
at its place along the whole bar; with `GaugeShade.Value` the filled part is one colour, the one at
the value's place. In HTML the bar becomes a `div` whose fill is a CSS `linear-gradient` in the same
space, after a fallback through nine colours worked out here. `ColorGradient.At`, `Paint` and
`ToCss` are there for anything else that wants a gradient.

**Lists.** `BulletsNode` marks each item with a bullet, a dash, a number, a letter or a roman numeral
(`BulletStyle`), or a marker of your own, starting from `Start`. Numbers line up on their right, and a
wrapped line hangs under the item's text.

```
• Be kind to          9. Nine
  other players      10. Ten
• No spam
```

**Columns of names.** `GridNode` is the `ls` layout for short items such as a `who` list: as many
columns as the widest item allows, filled down each column, or across each row with `Across`.

```
Mannaz    Ilse      Bram
Raya      Quill
Tomas     Ottoline
```

**Tables.** `TableNode` takes `TableColumn`s (header, alignment, least and most width, priority,
whether it wraps) and rows of cells. Each column asks for its widest cell. When the table is too
wide, the columns that wrap give way, widest first, down to their least width; then the column with
the highest `Priority` number is left out, and so on. A column that does not wrap is shown whole or
not at all. When not even the most important column fits, each row becomes a card of labelled
values. In HTML, a column of priority 2 carries `ms-p2` and one of 3 or more `ms-p3`, which `LayoutCss.Fixed`
hides on narrow pages.

```
At 30:                            At 18:
Name    Idle  Doing               Name    Idle
------------------------------    ------------
Mannaz    0s  Hooooo?             Mannaz    0s
Raya      5m  Writing a scene     Raya      5m
              in the garden
```

**Borders.** `BorderStyle` has seven presets, found by name with `BorderStyle.Preset`. Every piece
is a `MarkupText` — a corner, an edge, a side, a tee where a divider meets a side, the brackets
round a title — so any of them can be replaced or coloured, and an edge is a fill pattern.

**The text is the value.** A block is the text it was laid out as, with a `LayoutMarkup` over it.
Slicing, editing and searching work on the text. The renderer draws the tree only when the stretch
the layer covers is unchanged and on lines of its own; a cut or edited block renders as text.

**Laying out again.** `BlockLayout.Relayout(text, width, options)` replaces each intact block with
a fresh layout: a block built with `fluid: true` at the reader's width, and any block with ASCII
borders (`AsciiOnly`) or as its content in reading order (`Linear`, for a screen reader).

`AsciiOnly` translates each box-drawing character to its nearest ASCII one: a light line `-`, a double
or heavy one `=`, an upright `|`, a corner or tee `+`. A double frame stays recognisably double, and
colour on a piece is kept. A piece holding anything else (an emoji, a title bracket like `┤ `) takes
the `ascii` preset's piece, as does any tree guide piece, so the last branch stays `` `- ``. A flex
separator is translated the same way, and so are gauge, bullet and table pieces (a `•` becomes `*`,
a `█` `#`). Text inside a block is never changed. `Linear` drops borders
and guides, reads fields as `Label: value` lines and indents tree levels with spaces.

**Nesting.** `BlockLayout.AsNode(content)` returns the tree of a text that is one whole block, and
a `TextNode` otherwise, so a builder that takes text as an argument nests a block it is given.
