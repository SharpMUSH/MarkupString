# Text layout engine — design

Status: approved 2026-09-07. Target: MarkupString 2.0.

## Problem

`MarkupText` can measure display cells and pad a single value, but it cannot lay text out.
There is no wrapping operation at all, and no way to assemble several wrapped values into
aligned rows. Consumers therefore hand-roll it: SharpMUSH's `TextAligner` reimplements
wrapping for `align()`, with verified defects — a column narrower than one grapheme cluster
never advances, so `align(1,日)` loops forever, and a `\n` at or past the column width leaks
into the emitted line because a code-unit index is compared against a cell count.

The target capability is the union of PennMUSH `align()` and RhostMUSH `printf()`. Both are
column layout engines over styled text; between them they cover wrapping, justification,
filling, truncation, indentation, tab expansion, line and cell budgets, column merging and
row assembly.

## Principles

**The engine's vocabulary is behavioural, never syntactic.** The two dialects' flag
characters collide outright:

| Character | PennMUSH | RhostMUSH |
|---|---|---|
| `-` | centre-justify | left-justify |
| `.` | repeat this column | suppress an all-blank last row |
| `` ` `` | merge with the column to the **left** | shift the **left** column to the right |
| `'` | merge with the column to the **right** | shift the **right** column to the left |

No flag character appears anywhere in the API. Each server's parser maps its own characters
onto named options.

**Where the servers disagree, both behaviours are reachable.** The library is not a dialect
and never picks a winner; the caller selects. Where only one server has a capability, that is
absence rather than conflict, and the knob is available to every caller regardless of which
server they are emulating.

**Defaults favour neither dialect.** Where Penn and Rhost differ only in default (wrap mode,
for instance), the library's default is chosen for a caller who is emulating nothing, and both
servers set the property explicitly.

## Layers

1. **Shaping** — one `MarkupText` becomes lines: tab expansion, cell budget, wrapping,
   line budget, indentation.
2. **Rendering** — those lines become a fixed-width block: justification, filling,
   truncation, per-column markup.
3. **Assembly** — blocks become rows: separators, merge and shift rules, blank-row
   suppression, row separators.

Layer 4, the format-string surface (`$…s`, `%r`, and the null-elision codes `!` `@` `<` `>`
whose meaning depends on neighbouring *arguments* rather than on text), stays in SharpMUSH.

## Layer 1 — shaping

Fixed pipeline, because the ordering constraints are real and silent when left to callers:

```
expand tabs → cut to MaxCells → indent-aware wrap, stopping at MaxLines
```

Indent is a parameter of wrapping, not a step after it. Rhost's `$-20"|;5;s` wraps
continuation lines at `width - indent`:

```
this is a test with        ← 19 cells, full width
    wrapping some          ← indent 4, wrapped at 15
    text
```

`;N.n+w;` compounds this: from line `n` on, indent by `N` *and* widen the column by `w`, so
the text width holds constant while the block grows.

Tab expansion is literal substitution, not tab stops: Rhost's `$-40#s` on `a%tb%tc%td%te`
gives `a    b    c    d    e`, four spaces per tab rather than alignment to column 4.

### Wrap modes

Rhost's `&` and `|` are not independent — `|` is a superset of `&` — so hard-break handling is
a value of the wrap mode rather than a separate flag.

| `WrapMode` | Rhost | Penn | Behaviour |
|---|---|---|---|
| `None` | *default* | — | One line; `\n` passes through untouched |
| `HardBreaks` | `&` | `x` | Break on `\n`/`\r\n` only, every line kept in the column |
| `Cell` | `\|` | — | Break at the width, mid-word; hard breaks honoured |
| `Word` | `\|"` | *default* | Break at the last space that fits; hard breaks honoured |

Penn's `x` is `HardBreaks` plus layer 2's `TruncationType.Truncate`; `X` is that plus
`MaxLines = 1`. Neither needs a mode of its own.

### Termination guarantee

Every line advances by at least one grapheme cluster. A cluster wider than the column
overflows its line rather than producing an empty one. This is what makes `align(1,日)`
terminate, and it gets a dedicated test.

### Disagreements encoded

| Behaviour | Penn | Rhost | Property |
|---|---|---|---|
| Default wrap mode | word | none | `Wrap` |
| Space at a soft break | dropped | kept on the previous line, which may then exceed the width by one cell | `BreakSpace` |
| Over-long word | cell break | cell break | *agreed — no property* |

The break-space difference is in Rhost's source: `t = s_padstring + i_lastspace; if (*t) t++;`
breaks *after* the space rather than at it
([functions.c:11203](https://github.com/RhostMUSH/trunk/blob/master/Server/src/functions.c#L11203)).
It is visible in Rhost's own help output, where `$-20"|;5;s` emits `this is a test with ` —
twenty cells including the trailing space.

### Output

```csharp
public readonly record struct TextLine(MarkupText Text, int Start, int Width, bool EndsParagraph);
```

`Start` is the indent offset, carried rather than baked in as leading spaces, so layer 2 fills
`[0, Start)` with the column's filler — with a `:.:` fill that is what the servers emit, and it
keeps right-justification of an indented line sane. `Width` is per-line because of `;N.n+w;`.
`EndsParagraph` is true when the line is followed by a hard break or ends the text; Penn's `=`
needs it and no caller can re-derive it after the fact.

## Layer 2 — rendering

### Compositing model

A rendered line is a `Width`-cell **background of the fill pattern** with text segments
**stamped onto it** at computed offsets. This is one model for every alignment, and it is what
Rhost actually does:

```
$-40:0123456789:s  "ten char filler"  →  ten char filler5678901234567890123456789
$40:0123456789:s   "ten char filler"  →  0123456789012345678901234ten char filler
$^40:0123456789:s  "ten char filler"  →  012345678901ten char filler7890123456789
$_40:0123456789:s  "ten char filler"  →  ten34567890123456char1234567890123filler
```

The left-justified case resumes the pattern at `5` because the text consumed cells 0-14; the
stretch case shows the pattern through every widened gap. Today's `Pad` restarts the pattern
at the text's edge and emits `ten char filler0123456789…`.

The pattern is indexed by **absolute cell position within the column**. `FillPhase.Continuous`
samples it at the true position; `FillPhase.Restart` samples from 0 at the start of each fill
segment, which is 1.x `Pad` behaviour. Both are reachable — 2.0 changes a default, it does not
remove a behaviour.

Alignment decides only where the segments go: one segment for `Left`/`Right`/`Center`, one per
word at distributed offsets for `Full`/`Paragraph`. `Paragraph` (Penn `=`) renders as `Full`
except on a line whose `EndsParagraph` is true, which renders as `Left`.

Cells a pattern cannot express — the odd cell left by a two-cell fill — take spaces, as
`BuildFill` already does.

### Properties

| Behaviour | Penn | Rhost | Property |
|---|---|---|---|
| Justification | `<` `-` `>` `_` `=` | `-` `^` `_`, right default | `Alignment { Left, Right, Center, Full, Paragraph }` |
| Filler | single character | `:pattern:`, markup-bearing | `Fill` (a `MarkupText`) |
| Fill phase | n/a (single char) | continuous | `FillPhase { Continuous, Restart }` |
| Blank-line filler | — | `:!pattern:` → spaces | `BlankLineFill { Pattern, Spaces }` |
| Overflow | always cut | `+` keeps | `Truncation` (existing `TruncationType`) |
| Cut side | keeps the left | `*` keeps the right | `CutFrom { End, Start }` |
| No filler after text | `$` | — | `NoFill` |
| Column markup | `(ansi)` | on the filler only | `Markup`, plus `Fill` carrying its own |

Rhost's markup-bearing filler needs no property: `Fill` is a `MarkupText`.

## Layer 3 — assembly

Input is a sequence of cells, each a column or a separator. That single shape covers both
dialects: Penn passes the same `<colsep>` between every column, Rhost passes the literal text
that appeared between its fields.

```csharp
public abstract record LayoutCell;
public sealed record LayoutColumn(MarkupText Content, ColumnFormat Format) : LayoutCell;
public sealed record LayoutSeparator(MarkupText Text, SeparatorRows Rows) : LayoutCell;
```

### Disagreements encoded

**Separator repetition.** Penn inserts `<colsep>` "between every column, on every row". Rhost
does not: its continuation loop emits `\r\n` and then, for each field, `i_breakarray[i]`
*spaces* — the accumulated width of the literal that preceded it, not the literal itself
([functions.c:13280](https://github.com/RhostMUSH/trunk/blob/master/Server/src/functions.c#L13280)).
Encoded as `SeparatorRows { EveryRow, FirstRowOnly }`.

**Empty-column behaviour.** Penn *merges* — an exhausted column yields its cells to a
neighbour, which widens in place. Rhost *shifts* — a neighbour's content relocates into the
empty column's position. Rhost's own example makes the difference plain: with `` ` `` on
column 2, rows where column 2 is empty render column 1's content at column 2's offset, not at
column 1's. Five named values, no dialect implied:

```csharp
public enum WhenEmpty { None, GiveSpaceToLeft, GiveSpaceToRight, PullRightColumnLeft, PushLeftColumnRight }
```

Penn `` ` `` is `GiveSpaceToLeft`, Penn `'` is `GiveSpaceToRight`, Rhost `'` is
`PullRightColumnLeft`, Rhost `` ` `` is `PushLeftColumnRight`. The decision is per row, not per
column, in both dialects.

**Remaining behaviours.** `Repeat` (Penn `.`) repeats a column's content while another column
still has text. `NoSeparatorAfter` (Penn `#`) suppresses the following separator. Rhost's `.`
becomes `SuppressBlankLast`, a per-column flag; a final row is dropped when every column
carries the flag and every cell is blank, matching Rhost's "if on every field".

## API surface

One record carries a column, because a column is one thing to the caller and `with`
expressions make it composable without a builder:

```csharp
public sealed record ColumnFormat
{
    // Layer 1 — shaping
    public int Width { get; init; }
    public WrapMode Wrap { get; init; } = WrapMode.None;
    public BreakSpace BreakSpace { get; init; } = BreakSpace.Drop;
    public int TabWidth { get; init; }
    public int MaxCells { get; init; }
    public int MaxLines { get; init; }
    public Indent Indent { get; init; }

    // Layer 2 — rendering
    public Alignment Alignment { get; init; } = Alignment.Left;
    public MarkupText Fill { get; init; } = MarkupText.Space;
    public MarkupText? FillRight { get; init; }   // Center's second fill; Fill is used when null
    public FillPhase FillPhase { get; init; } = FillPhase.Continuous;
    public BlankLineFill BlankLineFill { get; init; } = BlankLineFill.Pattern;
    public TruncationType Truncation { get; init; } = TruncationType.Truncate;
    public CutFrom CutFrom { get; init; } = CutFrom.End;
    public bool NoFill { get; init; }
    public IMarkup? Markup { get; init; }

    // Layer 3 — assembly
    public WhenEmpty WhenEmpty { get; init; }
    public bool Repeat { get; init; }
    public bool NoSeparatorAfter { get; init; }
    public bool SuppressBlankLast { get; init; }
}
```

Entry points, with the primitives public underneath so a single-concern caller gets a
one-liner:

```csharp
// Layer 1
public MarkupText[] WrapLines(int width);
public MarkupText[] WrapLines(int width, WrapMode mode);
public ImmutableArray<TextLine> Shape(ColumnFormat format);
public MarkupText ExpandTabs(int tabWidth);

// Layer 2
public MarkupText[] FormatColumn(ColumnFormat format);

// Layer 3
public static MarkupText[] TextLayout.Rows(ReadOnlySpan<LayoutCell> cells, LayoutOptions options);
public static MarkupText TextLayout.Render(ReadOnlySpan<LayoutCell> cells, LayoutOptions options);
```

`LayoutOptions` carries the row separator (default `\n`) and nothing else at present.

Layout types live in a `MarkupString.Layout` namespace; `WrapLines`, `ExpandTabs` and the
existing operations stay in `MarkupString`.

## Compatibility

MarkupString 2.0. `Pad` and `Center` keep their signatures and become thin calls into the
engine, so there is exactly one filling implementation. Their behaviour changes only for
multi-character fills that do not begin at column 0, because the engine defaults to
`FillPhase.Continuous`; `FillPhase.Restart` reproduces 1.x exactly. `PadType`,
`TruncationType` and `TrimType` are unchanged.

## Testing

TUnit, alongside the existing operation tests.

- **Layer 1**: each wrap mode; both `BreakSpace` values against Rhost's and Penn's published
  output; hard breaks including `\r\n`; `MaxCells` and `MaxLines`; indent with `fromLine` and
  `widen`; tab expansion; markup surviving a break; the 66-code-unit / 11-cell combining-mark
  string wrapping at 11.
- **Termination**: a cluster wider than the column advances — the `align(1,日)` regression.
- **Layer 2**: every alignment against Rhost's four published filler outputs; both fill
  phases; `Paragraph` versus `Full` on a paragraph's last line; both cut sides; overflow;
  `NoFill`; blank-line filler.
- **Layer 3**: both separator modes; each `WhenEmpty` value against its dialect's published
  example; repeat; blank-row suppression; ragged column heights.
- **Regression**: the two verified `TextAligner` failures, expressed against the new engine.
- **Properties**: no rendered line exceeds its column width except where `TruncationType.Overflow`,
  `BreakSpace.Keep` or an over-wide cluster permits it; every row of a layout has the same
  display width.
- **Allocation**: the existing `AllocationTests` gain coverage for a plain wrap, since the
  repo already guards allocation on the hot paths.

## Out of scope

Format-string parsing in any dialect; `%r` and other MUSH substitutions; the null-elision
codes `!` `@` `<` `>`, which depend on neighbouring arguments rather than on text; and
Rhost's `0`-prefix zero-padding, which is a numeric-formatting concern rather than a layout
one.
