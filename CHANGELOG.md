# Changelog

All notable changes to `MarkupString`, `MarkupString.Ansi`, `MarkupString.Html`, `MarkupString.Mxp`
and `MarkupString.Pueblo`. The packages share one version and are released together.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Changed

- **A Pueblo line ending is `<BR>` and a newline.** A Pueblo client renders the stream as HTML, where a
  newline is whitespace, so every line ran into the one after it. `MarkupFormat.Pueblo` now encodes with
  the new `TextEncoding.HtmlLineBreaks`, which is `Html` plus that substitution — what PennMUSH's
  `queue_eol` writes in HTML mode — and the `\r` of a `\r\n` goes with it. A blank line is a break like
  any other, and text that does not end in a newline gets none.
  - `MarkupFormat.Html` and `MarkupFormat.Mxp` are unchanged, deliberately: a page decides its own line
    handling in its stylesheet, and an MXP client reads a newline as a break already.

## 2.3.0 — 2026-09-22

### Added

- **A shared vocabulary.** A game puts a thing in its text once and every format writes it in its own
  dialect, or stands something else in for it. The types are core's and say what a thing is; each
  format's package says how it is written:

  | Factory | MXP | Pueblo | HTML | ANSI / Plain / BBCode |
  |---|---|---|---|---|
  | `MarkupText.Sound`, `Music` | `<SOUND>`, `<MUSIC>` | `<img xch_sound="play"\|"loop">` | `<audio>` | nothing |
  | `MarkupText.StopSound` | `<SOUND Off>`, `<MUSIC Off>` | `<img xch_sound="stop">` | `ms-sound-stop` | nothing |
  | `MarkupText.Image` | `<IMAGE>` | `<img>` | `<img>` | its description, or its address; BBCode `[img]` |
  | `MarkupText.Pane` | `<FRAME>` + `<DEST>` | `<xch_pane action="redirect">` and back | `ms-pane` | its text |
  | `MarkupText.ClearScreen` | nothing | `<xch_page clear="text">` | `ms-clear` | ANSI `ESC[H ESC[2J` |
  | `MarkupText.Prefetch` | nothing | `<xch_prefetch>` | `<link rel="prefetch">` | nothing |
  | `MarkupText.ExpireLinks` | `<EXPIRE>` | nothing | `ms-expire` | nothing |
  | `MarkupText.Variable`, `Gauge`, `Status` | `<VAR>`, `<GAUGE>`, `<STAT>` | their text | `ms-variable`, `ms-gauge`, `ms-status` | their text |
  | `MarkupText.Relocate`, `LoginPrompt` | `<RELOCATE>`, `<USER>`, `<PASSWORD>` | nothing | nothing | nothing |

  A link is already this kind of thing (`AnsiMarkup` with a `LinkKind`), so a picture inside a link is
  a picture that is a link in every dialect. The vocabulary serialises as built-in kinds, so storing it
  needs no package registered.
- **Points.** `IPointMarkup` is a markup that stands at a point rather than marking text — a sound, a
  bell, a clear. It rides on its own `IPointMarkup.Carrier` (a zero-width space, `MarkupText.PointCarrier`;
  the bell keeps its U+0007), and `MarkupText.Point(markup)` builds one of your own. A format with no
  emitter for a point writes nothing at all — not the carrier, and not the layers around it. A point
  marks its carrier and nothing else: `MarkupText.Wrap` refuses one over other text, and a cover read
  back with a point out of place drops the point and keeps the text.
- **`MarkupString.Mxp`**, a new package. `WithMxp()` writes the vocabulary as MXP's secure elements.
  `WithMxp(supports)` holds each element to what the client answered to `<SUPPORT>`: one it refused is
  written as a format without MXP writes it — nothing for a sound, the description for a picture, the
  text in the main window for a pane. `MxpRegistration.Elements` lists the names to ask about.
- **`MarkupString.Pueblo`**, a new package. `WithPueblo()` writes the vocabulary in the Pueblo
  client's own extensions, with the names and attributes its source reads.
- **A bell.** `MarkupText.Bell()` marks a point in the text where the client is asked to get someone's
  attention: U+0007 for a terminal, Pueblo or MXP client, and an empty `<span class="ms-bell"
  role="alert">` for HTML, where the page decides what a bell means. Plain and BBCode leave nothing
  behind. It is a point riding on the U+0007 it marks, which measures zero display cells, so it survives
  slicing, concatenation and padding without moving anything laid out around it — and it is the only
  way to get a control character into rendered output, since the encodings drop them from ordinary
  text.

### Changed

- **`ToPlainText()`, `ToString()` and equality leave out point carriers.** A sound or a bell is not
  text a reader or a pattern sees: `Concat(Sound(...), Plain("hi"))` equals `Plain("hi")`. `Text`
  still holds the carriers, so positions, slicing and padding are unchanged.
- **The kinds core serialises itself are reserved.** `MarkupRegistry.With(IMarkupCodec)` refuses a
  codec claiming `"neutral"` or one of the shared vocabulary's kinds, which are written and read
  without consulting a registry; a codec registered under one would write text that read back as
  something else.
- **A layer from another package nested inside ANSI styling now renders inside it.** The Ansi
  package's set emitters used to wrap every layer they do not own around their own output, whatever
  the nesting: `Wrap(red, Wrap(HtmlMarkup "b", "x"))` gave `<b><span style="color: …">x</span></b>`.
  It now gives `<span style="color: …"><b>x</b></span>`, and a picture inside a command link stays
  inside the link rather than replacing it. A layer *between* two styled layers keeps its place too:
  `[bold, tag, red]` is a bold inside a tag inside a red, rather than one folded bold-red inside a tag.
  A terminal is unchanged — a style there is state rather than nesting, so the sequence is still
  written once around the run. A stretch that clears (`AnsiStyle.Clear`) stays cleared across a
  delegated layer, so the styling outside it is still discarded.

## 2.2.0 — 2026-09-19

### Added

- **Checked `HtmlMarkup` construction.** `HtmlMarkup.Tag(name, params attributes)` validates the
  tag and attribute names and writes each value encoded, so nothing in a value can end the attribute
  or the tag. `HtmlMarkup.IsValidTagName`, `IsValidAttributeName` and `TryParseAttributes` (which
  reads a raw attribute string with its values decoded, as a client reads them) are public.
- **`HtmlTagPolicy`**, for tags from somewhere untrusted: which tags and attributes are allowed,
  which attributes are checked as addresses (against `UrlSafety`), and whether one bad attribute
  drops just itself or all of them. `TryCreate` builds a checked tag from a name and a raw attribute
  string; `Apply` holds an existing one to the policy. It is machinery, not a posture: `WellFormed`
  allows any well-formed tag, address checking is off until you name the attributes (with your own
  list or the published `AddressAttributes`), and a policy is a record, so `with` narrows one to
  whatever your application considers safe.
- **`WithHtml(HtmlTagPolicy)`** and `HtmlTagEmitter(format, policy)`: every tag rendered in `Html` is
  held to the policy as it is written, including markup that arrived deserialised or was built with
  the unchecked `HtmlMarkup.Create`. A refused tag leaves its body in place. Pueblo and MXP output is
  unchanged unless you register a policy for them too.
- **`ILineFramer`**, registered with `MarkupRegistry.With(ILineFramer)` and found with
  `FindLineFramer`: a prefix written at the start of every line that has content, in a slot of its
  own beside `IFormatFramer`.
- **`MxpSecureLineFramer`** and `MarkupRegistry.WithMxpSecureLines()`: open every line of
  `Mxp` output in secure mode (`ESC[1z`), which an MXP client needs before it reads the tags on a
  line. Opt-in, for the registry that renders for a connection.

### Documentation

- The guides no longer show `HtmlMarkup.Create("send", ...)` as a portable link. `<send>` is MXP's
  command link; a Pueblo client prints it as text. The formats guide now has a Pueblo-and-MXP table,
  and every example builds a link with `AnsiMarkup`'s `LinkKind.Command`, which each format writes
  in its own dialect.
- The formats guide said `TextEncoding.Html` escapes `"`; it has not since 2.1.0.

## 2.1.0 — 2026-09-08

### Added

- `MarkupText.GraphemeCount`, lazy `EnumerateGraphemes()`, and `SubstringGraphemes(start[, count])`
  preserve all markup layers while counting and extracting whole extended grapheme clusters.
- Allocation-free `Graphemes.Count` and `Graphemes.Enumerate` expose cluster counts and UTF-16 ranges.
- Explicit UTF-16, scalar, grapheme, column, normalization, and malformed-input contracts,
  with long-cluster, mixed-markup reconstruction, allocation, and native-AOT coverage.

### Changed

- Snapping uses proven boundaries without a fixed lookback and avoids prefix rescans between
  adjacent complete supplementary symbols. Context-sensitive flag pairing remains correct.

## 2.0.0 — 2026-09-07

### Changed

- **`TextEncoding.Html` no longer encodes `"` and `'`.** It now writes entities for `<`, `>` and
  `&` only — the characters that are actually markup in HTML *text*. A quote and an apostrophe are
  markup only inside an attribute value, and this encoding is never applied to one: `HtmlTagEmitter`
  writes the body between `>` and `</`, and the layers that emit attributes (`AnsiHtmlEmitter`'s
  `xch_cmd`/`href`/`title`, and whatever a caller passes as `HtmlMarkup.Attributes`) encode their
  own and are untouched by this. So the extra two entities protected nothing, and cost something:
  over a MUD socket they inflate every apostrophe in every line of dialogue fivefold, and they leave
  Pueblo and MXP output depending on the two entities a client is least likely to have implemented.

  This changes rendered output for `Html`, `Pueblo` and `Mxp` — a consumer with snapshot tests over
  text containing quotes or apostrophes will see them move. It is not a change in what is safe to
  render.

### Added

- A column layout engine in `MarkupString.Layout`. `ColumnFormat` describes a column — how its
  text is shaped into lines, how those lines are drawn, and how it behaves among its neighbours
  — and is composed with `with`. `MarkupText.FormatColumn` draws one; `TextLayout.Rows` and
  `TextLayout.Render` assemble several into aligned rows.
- `MarkupText.WrapLines`, for callers who only want lines, plus `MarkupText.Shape`,
  `MarkupText.ExpandTabs`, `MarkupText.TruncateToWidth` and `DisplayWidth.IndexFromWidthEnd`
  underneath it.
- Between them these cover the union of PennMUSH `align()` and RhostMUSH `printf()`. Where the
  two servers disagree — the space a word break lands on, whether a separator survives onto
  continuation rows, whether an exhausted column merges or shifts — both behaviours are
  reachable and neither is a default. See [the layout guide](docs/layout.md).

### Changed

- **Breaking:** a multi-character fill given to `Pad` or `Center` is now a pattern indexed by
  position in the result, rather than one restarted where the text stops. Left-padding
  `ten char filler` to 40 with `0123456789` now reads `ten char filler5678901234...` — the
  pattern continues behind the text instead of beginning again — which is what the servers this
  mirrors emit, and what makes a filler read as one unbroken run. Single-character fills, which
  is nearly every use, are unaffected. `ColumnFormat.FillPhase` set to `Restart` reproduces the
  old behaviour exactly.
- **Breaking:** `Pad` with `PadType.Full` on text with no word gap to widen now fills out to the
  requested width instead of returning the text untouched. A padding operation that hands back
  something narrower than the width it was given is a trap for a caller laying out columns.

## 1.1.0 — 2026-09-07

### Added

- `AnsiCss.Fixed` in `MarkupString.Ansi` — the stylesheet for the `ms-*` classes. Every one of
  those classes is written by this package's HTML emitter, so a consumer rendering HTML with
  `MarkupString` + `MarkupString.Ansi` alone can now reach the rules; in 1.0.0 they existed only in
  `MarkupString.Html`, a package such a consumer has no reason to take.

### Removed

- **Breaking:** `HtmlCss` in `MarkupString.Html`. Use `AnsiCss.Fixed`; the rules are identical.
  Nothing else in that package writes an `ms-*` class, so the type had no business being there.
  A removal in a minor is a semver break; 1.0.0 is unlisted, having been published the same day
  and consumed by nothing, so there is no one to break.

## 1.0.0 — 2026-09-07

### Added

- `MarkupText` — immutable text plus coalesced, non-overlapping runs of layered markup, with
  slicing, searching, splitting, trimming, splicing, padding, centring and joining that carry the
  markup with them.
- Grapheme-cluster safety on every operation: extractions snap inward, edits snap outward, and a
  range reaching the end keeps the tail. `Graphemes` exposes the primitives.
- Display-width measurement from the Unicode 16.0 East Asian Width table, used by padding,
  centring and truncation. `DisplayWidth` exposes the primitives.
- `MarkupRegistry` — an immutable, reflection-free table of emitters, framers and codecs, built
  with `With(...)` and optionally installed set-once as `MarkupRegistry.Default`.
- Six built-in formats — `Plain`, `Ansi`, `Html`, `Pueblo`, `Mxp`, `BBCode` — plus
  `MarkupFormat.Custom(name, encoding)`.
- `MarkupTextSerializer` — a compact JSON shape with a palette of distinct markup sets and a flat
  run cover. Markup whose kind has no codec survives as `UnknownMarkup` and is written back
  verbatim.
- `MarkupString.Ansi` — `AnsiMarkup`, a semantic `AnsiColor` union (default / standard / xterm-256
  / truecolor) with redmean downgrades, `AnsiCodeParser` for MUSH `ansi()` codes, `AnsiEscapeParser`
  for existing SGR text, a state-diffing `SgrWriter`, and emitters for all six formats.
- `MarkupString.Html` — `HtmlMarkup` for raw HTML/MXP tags, folding `b`/`i`/`u`/`s` into terminal
  styling through `IAnsiStyleSource`, and `HtmlCss.Fixed` for the `ms-*` classes the emitters write.
- Every package is `IsAotCompatible`, with a native-AOT publish in CI that fails on any
  trim or AOT warning.
