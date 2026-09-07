# Changelog

All notable changes to `MarkupString`, `MarkupString.Ansi` and `MarkupString.Html`. The three
packages share one version and are released together.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

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
- All three packages are `IsAotCompatible`, with a native-AOT publish in CI that fails on any
  trim or AOT warning.
