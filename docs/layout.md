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
var finger = new Stack(
[
  new Flex(
  [
    MarkupText.Plain("Sex: Male\nSpecies: Human").ToBlock().Sized(BlockSize.Cells(35)),
    MarkupText.Plain("Job: Dark Warrior\nOnline: 1h").ToBlock().Sized(BlockSize.Cells(36)),
  ]) { Separator = MarkupText.Plain(" | ") },
  new Rule(MarkupText.Plain("Quote")),
  MarkupText.Plain("Hooooo?"),
]).Bordered(MarkupText.Plain("Mannaz Byron"), BorderStyle.Mush with { TitleOpen = MarkupText.Plain("<< "), TitleClose = MarkupText.Plain(" >>") });

var text = BlockLayout.Build(finger, 78);
```

```
+=============================<< Mannaz Byron >>=============================+
| Sex: Male                           | Job: Dark Warrior                    |
| Species: Human                      | Online: 1h                           |
+=================================<< Quote >>================================+
| Hooooo?                                                                    |
+============================================================================+
```

That is `text.ToPlainText()` and what every terminal format writes. `Render(MarkupFormat.Html)`
draws the same tree as a `<fieldset>` with a legend, a divider, and a flex row whose items ask for
35 and 36 `ch` and wrap onto rows of their own on a narrower page. Include `LayoutCss.Fixed`, or
your own copy of its rules, on the page.

### How it fits together

- **A block draws itself.** Every block derives from `Block` and draws its own lines at the width it
  is given (`Draw`), and its content in reading order for a screen reader (`DrawLinear`). It measures
  itself (`Measure`) when a table or a row needs to know how wide it wants to be. Text converts to a
  block wherever one is wanted.
- **Optional means "inherit unless set".** Optional properties are `init` properties. A look the block
  leaves unset — a border, a tree guide, a gauge's pieces, the bullet, the separator after a label, the
  line under table headings — comes from the `LayoutTheme` of the `LayoutContext` it is drawn in, and
  in the end from `LayoutTheme.Defaults`. A rule inside a frame with no border of its own takes the
  frame's.
- **Modifiers wrap any block.** `Bordered`, `Sized`, `Aligned`, `Shaded`, `Colored` and `Themed` are
  extension methods that return a wrapping block, so they chain.
- **Formats plug in per block.** The terminal text comes from the block. HTML comes from a renderer
  registered for its type; a block with none is shown as its lines in a `<pre>`. JSON comes from a
  `BlockCodec`.

| Block | Terminal | HTML |
|---|---|---|
| `Frame` (`.Bordered(title, border)`) | the frame, its title set into the top edge | `<fieldset>` and `<legend>` |
| `Rule` | a line of the border's top edge with the title in it; inside a frame, a divider meeting the sides | a line drawn in CSS |
| `Flex` | items side by side at widths shared from their `Sized` bases, stacked when one would fall under its `Min` | a wrapping flex row |
| `Figure` | the text art, with `Beside` flowing round it; MXP and Pueblo write the picture on its first row and keep its cells blank | an `<img>` floated beside it |
| `Fields` | labels in one column, values lined up in the next, a long value wrapping under itself | a `<dl>` laid out as a two-column grid |
| `Tree` | items under their parents, joined by guide lines | nested `<ul>` with the guides drawn in CSS |
| `Gauge` | a bar filled to its share of the width, with its figures | a `<meter>` |
| `Bullets` | items with a bullet or number, wrapped lines hanging under the text | `<ul>` or `<ol>` |
| `Grid` | short items in as many columns as fit, down each column or across each row | a CSS multi-column or grid list |
| `Table` | columns sized to their widest cell, wrapping and then leaving out columns when narrow | a `<table>` that hides low-priority columns on a narrow page |
| `TextBlock`, `Stack` | wrapped text; children in order | the same, as blocks |

| Modifier | What it does |
|---|---|
| `.Bordered(title, border)` | a `Frame` round the block |
| `.Sized(basis, min, grow)` | how wide it asks to be in a `Flex` |
| `.Aligned(alignment)` | where text inside it sits, unless the text says otherwise; a table column does this for its cells |
| `.Shaded(gradient, flow)` | its borders and text in the colours of a gradient |
| `.Colored(markup)` | a colour (or any layer) under the colour it sets itself |
| `.Themed(theme)` | a different look for everything inside it that sets none of its own |
| `.ThemedUnder(theme)` | a look that fills in only what the theme around it leaves unset, as a game's default does under a reader's own |

```csharp
// One sheet, heavy frames and arrow bullets throughout, the title row shaded.
var sheet = new Stack([header.Shaded(gradient), stats, notes])
  .Themed(new LayoutTheme { Border = BorderStyle.Heavy, Bullet = MarkupText.Plain("→") });
```

**Labelled values.** `Fields` is the `Sex: Male` / `Species: Human` part of a sheet: the label
column is as wide as the longest label (at most half the width) and every value starts in the same
column. It sets the separator (the theme's `": "` unless given), right-aligned labels, a leader that
fills from the label to the separator, and how many columns the fields are dealt into, down each
column first.

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

**Trees.** `Tree` draws each `TreeItem` with its children under it. The top level sits at the left
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

**Gauges.** `new Gauge(value, maximum) { Label = ... }` draws a bar. With no `BarWidth` the bar takes
what the label and figures leave of the width. The filled and empty pieces (`█`, `░`) and the ends come
from the theme unless set, and `Show` makes the figures read `50%`, `6/12` or nothing.

```
HP [██████░░░░░] 50%         at 20
HP [######-----] 50%         AsciiOnly
HP: 6 of 12 (50%)            Linear
```

**Gradients.** A `ColorGradient` is colour stops (any `IColorMarkup`, such as an `AnsiMarkup` with a
foreground) blended in a `GradientSpace`. `Oklch`, the default, keeps the middle as bright and vivid
as the ends, so red to green passes through yellow rather than sRGB's dark olive; `Oklab` blends
straight across with no hue swing; `Hsl` gives the brighter, uneven rainbow sweep. `Mirror` runs the
colours there and back; `Repeat` runs them more than once over the length.

`gradient.Shade(text, flow)` colours any text, and `.Shaded(gradient, flow)` any block. The
`GradientFlow` says which way the colours run:

| Flow | Each character's colour comes from |
|---|---|
| `Characters` | its place among the characters that show, in reading order, on through every line |
| `Words` | its word's place among the words |
| `Across` | its column, so the colours line up down the text (the default for a block) |
| `Down` | its line |
| `Diagonal` | its column and line together, from the top-left corner to the bottom-right |

Spaces take no colour, and colour the text sets itself is kept. A gauge's `Gradient` shades its
filled part: with `GaugeShade.Cells` each cell takes the colour at its place along the whole bar, with
`GaugeShade.Value` the filled part is one colour, the one at the value's place. In HTML a shaded
block's text is clipped to a CSS `linear-gradient` in the same space (after a fallback through colours
worked out here) and its borders are drawn in it; a browser cannot run colour along characters or
words, so those run across.

Each shaded character carries the layer of the stop it lies nearest. A blend needs 256 colours or
more: a terminal limited to the sixteen standard colours (`AnsiColorDepth.Standard`) is sent that
stop's own colour instead (`AnsiStyle.StandardForeground`), so red to blue shows as a red half and a
blue half rather than the jumpy nearest-colour mix of every blended shade.

**Lists.** `Bullets` marks each item with the theme's bullet, a dash, a number, a letter or a roman
numeral (`BulletStyle`), or a marker of your own, starting from `Start`. Numbers line up on their
right, and a wrapped line hangs under the item's text.

```
• Be kind to          9. Nine
  other players      10. Ten
• No spam
```

**Columns of names.** `Grid` is the `ls` layout for short items such as a `who` list: as many
columns as the widest item allows, filled down each column, or across each row with `Across`.

```
Mannaz    Ilse      Bram
Raya      Quill
Tomas     Ottoline
```

**Tables.** `Table` takes `TableColumn`s (header, alignment, least and most width, priority, whether
it wraps) and rows of cells; a cell's text takes its column's alignment. Each column asks for its
widest cell. When the table is too wide, the columns that wrap give way, widest first, down to their
least width; then the column with the highest `Priority` number is left out, and so on. A column that
does not wrap is shown whole or not at all. When not even the most important column fits, each row
becomes a card of labelled values. In HTML, a column of priority 2 carries `ms-p2` and one of 3 or
more `ms-p3`, which `LayoutCss.Fixed` hides on narrow pages.

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

**Laying out again.** `BlockLayout.Relayout(text, width, context)` replaces each intact block with
a fresh layout: a block built with `fluid: true` at the reader's width, and any block with ASCII
borders (`AsciiOnly`) or as its content in reading order (`Linear`, for a screen reader).

`AsciiOnly` translates each box-drawing character to its nearest ASCII one: a light line `-`, a double
or heavy one `=`, an upright `|`, a corner or tee `+`. A double frame stays recognisably double, and
colour on a piece is kept. A piece holding anything else (an emoji, a title bracket like `┤ `) takes
the `ascii` preset's piece, as does any tree guide piece, so the last branch stays `` `- ``. A flex
separator is translated the same way, and so are gauge, bullet and table pieces (a `•` becomes `*`,
a `█` `#`). Text inside a block is never changed. `Linear` drops borders
and guides, reads fields as `Label: value` lines and indents tree levels with spaces.

**Nesting.** `BlockLayout.AsBlock(content)` returns the tree of a text that is one whole block, and
a `TextBlock` otherwise, so a builder that takes text as an argument nests a block it is given.
`BlockLayout.Blocks(content)` splits a text into the blocks standing on lines of their own and the
text between them.

### Themes and palettes

A `LayoutTheme` sets the colour of each part as well as its characters: `BorderColor`, `TitleColor`,
`HeadingColor`, `LabelColor`, `SeparatorColor`, `BulletColor`, `GuideColor`, `HeaderRuleColor`,
`GaugeFilledColor`, `GaugeEmptyColor` and `StripeColor`. Each is a markup layer, so a title can be bold as well as
coloured, and colour a piece sets itself still wins. Nothing is coloured by default.

A `ThemePalette` names eleven colours by what they are for (`ThemeRole`: background, surface,
foreground, primary, secondary, tertiary, muted, success, warning, error, info), and `ToTheme` maps
them onto the parts: borders and gauge bars primary, titles secondary and bold, labels secondary,
bullets tertiary, headings primary and bold, guides and separators muted, stripes on the surface. The
ANSI package does the painting:

```csharp
var sheet = character.Bordered(MarkupText.Plain("Ann")).Themed(ThemePalette.Preset("nord")!.ToLayoutTheme());
```

Each palette colour is a `ThemeColor`: an exact colour, the standard colour (0-15) a sixteen-colour
client is sent instead, or both. The standard colour is picked by kind (`ColorMath.StandardSlot`), so a
pastel blue is blue on a sixteen-colour client rather than the grey nearest it by RGB. A palette of
standard colours alone, `ThemePalette.Terminal`, shows each reader the game in their own client's
colours.

Four ways to make one:

- **Presets**: `terminal`; one for each MSSP genre, `fantasy`, `historical`, `horror`, `modern`,
  `mystery`, `romance` (MSSP's Adult as well), `science-fiction` and `spiritual`
  (`ThemePalette.Genres`), each with a look of its own (below); and `catppuccin-mocha`, `catppuccin-latte`, `dracula`, `gruvbox-dark`, `nord`,
  `solarized-dark`, `solarized-light`, `tokyo-night` (`ThemePalette.Preset(name)`).
- **base16**: `ThemePalette.FromBase16(name, colors)` takes any of the hundreds of base16 schemes,
  mapped by base16's own guide (`base0D` primary, `base03` muted, `base08` error, ...).
- **From one colour**: `ThemePalette.Generate(seed, harmony, mode, contrast)`. The accents' hues come
  from the seed's by `ThemeHarmony` (monochrome, analogous, complementary, split, triadic, tetradic),
  and each is made lighter or darker, keeping its hue, until its WCAG contrast with the background
  reaches 3:1 for lines and 4.5:1 for text; `contrast` from 0 to 1 raises both toward 7:1. Success,
  warning, error and info stay green, amber, red and blue, turned a little toward the seed.
- **JSON**: `ThemePalette.TryParse` reads a preset's name, or an object with one of `preset`,
  `base16` or `seed` (with `harmony`, `contrast`), and `mode`, `name` and `colors` to set roles:
  `{"preset":"nord","colors":{"primary":"#bf616a","muted":8}}`. `ToJson` writes one back.

A theme is more than its colours. `ThemePalette.Look`, a `ThemeLook`, sets the shapes too: a border
preset, its corners, edges and sides, the ornaments round a title (`"╡ ❖ "`, `" ❖ ╞"`), the tree
guide, the bullet, a gauge's pieces, the field separator and the rule under table headings. In JSON
it is `look`:
`{"preset":"nord","look":{"border":"double","corners":["❖","❖","❖","❖"],"edge":"═","title":["╡ ","  ╞"],"bullet":"❧","gauge":["[","█","░","]"]}}`.
Corners and the side are one column wide, the edge is a pattern repeated along the top, the bottom
and a rule, and no piece may hold a control character; a look that breaks one of these is refused
with the reason, never drawn.
A look given with a preset changes only what it names; `"look":null` drops the preset's. A reader
whose client has only ASCII gets the ASCII form of each piece.

`palette.Check()` lists the roles whose contrast with the background is under what they need, and
`ColorMath` has the pieces: `Contrast`, `WithContrast`, `ToOklch`/`FromOklch`, `Rotate`.

In HTML a themed block writes its colours as custom properties (`--ms-border`, `--ms-title`,
`--ms-label`, ...), which `LayoutCss` reads, so a page that sets them themes every layout on it. A
fallback theme (`ThemedUnder`) writes the `-default` form, under what the page sets.

### Striped rows

A wide table is easier to read across when every second row has a background of its own. Set
`Striped` on a `Table` or `Fields`, and every second row, all of its lines and the whole width, is laid
on the theme's `StripeColor`. A cell's own background still wins. With no stripe colour nothing is
coloured; in HTML the table or list gets `ms-striped` and `LayoutCss` uses `--ms-stripe`, a faint grey
when unset. A table drawn as cards, and a reading-order layout, are not striped.

```csharp
var roster = new Table(columns, rows) { Striped = true }.Themed(ThemePalette.Preset("nord")!.ToLayoutTheme());
```

### A block of your own

Derive from `Block` and draw. The context says whether the reader wants ASCII (`context.Glyph`
translates a piece) or reading order, and `context.Draw` draws a child.

```csharp
public sealed record Dice(ImmutableArray<int> Faces) : Block
{
  public override void Draw(LayoutContext context, int width, IList<MarkupText> lines) =>
    lines.Add(MarkupText.Plain(string.Join(" ", Faces.Select(f => context.AsciiOnly ? $"[{f}]" : ((char)('⚀' + f - 1)).ToString()))));

  public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines) =>
    lines.Add(MarkupText.Plain("Rolled " + string.Join(", ", Faces)));
}

var registry = MarkupRegistry.Empty.WithAnsi().WithHtml()
  .With(BlockCodec.Create<Dice>("dice",
    (dice, w) => w.String("f", string.Join(",", dice.Faces)),
    r => new Dice([.. (r.String("f") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)])))
  .WithBlockHtml<Dice>((dice, html) => html.Write($"<span class=\"dice\">{string.Join(" ", dice.Faces)}</span>"));
```

Without the codec, a dice block is written to JSON as the text it draws, so it still shows. A
reader that meets a kind it has no codec for leaves the whole layout as its text and never lays it
out again. Without the HTML renderer, it shows in a page as its lines in a `<pre>`.
