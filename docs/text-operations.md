# Text operations

Every operation on `MarkupText` returns a new value and carries the markup with it. Two ideas run
through all of them:

- **Indices and lengths are UTF-16 code units** — the same unit as `string.Length` — so a
  `MarkupText` can stand in for a `string` in code that already indexes one.
- **Widths are display cells.** Padding, centring and truncation measure what a terminal draws:
  East Asian wide and fullwidth characters count two, combining marks and controls count zero.

```csharp
var cjk = MarkupText.Plain("日本語abc");
cjk.Length;         // 6 — code units
cjk.DisplayWidth;   // 9 — cells
```

## Grapheme safety

No operation ever leaves half a grapheme cluster behind. Which way an index moves depends on what
the operation is doing:

| | Direction | Operations |
|---|---|---|
| **Extraction** | inward | `Substring`, `Split`, `Trim`, truncation inside `Pad`/`Center` |
| **Edit** | outward | `Splice`, `Insert`, `Remove`, `Replace`, `ReplaceAll` |

An extraction that would split a cluster gives you less rather than a broken cluster; an edit that
lands inside one takes the whole cluster in, so the result is never mojibake.

```csharp
var text = MarkupText.Plain("a😀b");   // Length 4: 'a', two surrogates, 'b'

text.Substring(0, 2).Text;             // "a"     — 2 would cut the emoji, so it snaps back
text.Substring(2).Text;                // "😀b"   — 2 snaps back to the emoji's start
text.Insert(2, MarkupText.Plain("X")); // "aX😀b" — the edit point snaps out to the boundary
```

One exception, and it is deliberate: a range that reaches the end of the text keeps the whole tail.
`x.Substring(n, x.Length - n)` — "take the rest" — returns the rest even when `n` snapped backwards,
which is what every caller of that idiom means.

The primitives are public if you need them yourself: `Graphemes.IsBoundary`,
`Graphemes.SnapStart`, `Graphemes.SnapEnd`, and `DisplayWidth.Of` / `OfRune` / `IndexAtWidth`.

## Wrapping and layout

`Pad`, `Center` and the rest measure a single value. Breaking text into lines, drawing it as a
column and assembling columns into rows live in `MarkupString.Layout` and have their own guide:
[Layout](layout.md).

```csharp
text.WrapLines(40);                 // break at the last space that fits
text.WrapLines(40, WrapMode.Cell);  // break at the width, mid-word
text.ExpandTabs(4);
text.TruncateToWidth(20, CutFrom.End);
```

## Slicing and searching

```csharp
text.Substring(start);
text.Substring(start, length);          // clamped to the text; never throws on overshoot
text.Split(",");                        // ordinal, non-overlapping
text.Split(delimiterMarkupText);        // splits on its plain text; its markup is ignored
text.IndexOf("needle");
text.LastIndexOf("needle");
text.IndexesOf("needle");               // every occurrence, lazily
text.Trim(TrimType.TrimBoth);           // spaces
text.Trim(TrimType.TrimStart, "-_");    // any of these characters
```

`Split` on empty text yields no segments; an empty delimiter yields the text unsplit.

## Building

```csharp
MarkupText.Plain("text");
MarkupText.Wrap(markup, "text");             // one layer over a whole span
MarkupText.Wrap(markup, existingMarkupText); // layered outside what is already there
MarkupText.Concat(a, b);
MarkupText.Concat([a, b, c]);                // ReadOnlySpan or IEnumerable
MarkupText.Join(separator, parts);
MarkupText.Join(i => separatorForIndex(i), parts);
text.Repeat(3);
text.AttachTail(tail);
```

`MarkupText.Empty`, `MarkupText.Space` and `MarkupText.NewLine` are the constants you would
otherwise re-allocate.

## Editing

`Splice` is the primitive: a batch of non-overlapping replacements applied in one pass, so N edits
cost one rebuild rather than N.

```csharp
MarkupText.Plain("abcdef").Splice([
  new Edit(1, 2, MarkupText.Plain("XY")),   // replace "bc"
  new Edit(4, 1, MarkupText.Empty),         // delete "e"
]);                                          // "aXYdf"
```

`Insert`, `Remove`, `Replace` and `ReplaceAll` are one-edit shorthands for it.

## Padding and alignment

```csharp
text.Pad(fill, width, PadType.Right, TruncationType.Truncate);
text.Center(fillLeft, fillRight, width, TruncationType.Truncate);
```

`PadType` is `Left`, `Right`, `Center` or `Full` (distribute the extra cells into the word gaps).
`TruncationType.Truncate` cuts text that is already too wide, on a cluster boundary;
`Overflow` leaves it alone.

The result is exactly `width` display cells, with two exceptions: `Overflow` keeps text that was
already wider, and `Full` has nowhere to put the cells when the text has no word gap. A cell that
the fill cannot express — the odd cell left by a two-cell fill — is taken by a space, so the width
always holds.

```csharp
var cjk = MarkupText.Plain("日本語abc");                                    // 9 cells
cjk.Pad(MarkupText.Plain("."), 12, PadType.Right, TruncationType.Truncate); // "日本語abc..."
cjk.Pad(MarkupText.Plain("."), 5,  PadType.Right, TruncationType.Truncate); // "日本." — 4 cells cut, 1 filled

MarkupText.Plain("hi").Center(MarkupText.Plain("<"), MarkupText.Plain(">"), 9, TruncationType.Truncate);
// "<<<hi>>>>" — the odd cell goes right
```

## Transforming

```csharp
text.Apply(s => s.ToUpperInvariant());          // whole plain text at once
text.Map(part => part.Trim(TrimType.TrimBoth)); // each run and each plain gap on its own
```

`Apply` keeps the runs when the transform keeps the length; a transform that changes the length
returns plain text, because the run positions no longer mean anything. `Map` hands you each styled
run and each unmarked gap as its own `MarkupText` and concatenates what you return.

## Comparing

```csharp
a == b;                                  // plain text only — markup is ignored
a.TextEquals("literal");
a.Equals(b, MarkupFormat.Ansi);          // renders identically as ANSI
a.Equals(b, MarkupFormat.Html, registry);
```

Text-only equality is the default because finding and comparing text is the common case, and
because "are these the same" has no answer independent of a format: two values can be identical on
a terminal and different in HTML. Say which format you mean and the second overload answers it.

## Inspecting the runs

```csharp
foreach (var (start, length, markups) in text.Runs)
{
  // markups is a MarkupSet: innermost-first, value-equal, interned
}
```

Runs are always sorted, non-overlapping, and coalesced — identically marked neighbours are one run,
and text with no markup at all appears as a gap rather than a run. The constructor rejects
overlapping runs, so anything you get back holds that invariant.
