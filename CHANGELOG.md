# Changelog

All notable changes to `MarkupString`, `MarkupString.Ansi` and `MarkupString.Html`. The three
packages share one version and are released together.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

Nothing published yet. The first release will be `v1.0.0`; everything below describes what it will
contain.

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
