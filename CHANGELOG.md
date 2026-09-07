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
