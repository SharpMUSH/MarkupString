# MarkupString

[![CI](https://github.com/SharpMUSH/MarkupString/actions/workflows/ci.yml/badge.svg)](https://github.com/SharpMUSH/MarkupString/actions/workflows/ci.yml)
[![MarkupString](https://img.shields.io/nuget/v/MarkupString?label=MarkupString)](https://www.nuget.org/packages/MarkupString)
[![MarkupString.Ansi](https://img.shields.io/nuget/v/MarkupString.Ansi?label=MarkupString.Ansi)](https://www.nuget.org/packages/MarkupString.Ansi)
[![MarkupString.Html](https://img.shields.io/nuget/v/MarkupString.Html?label=MarkupString.Html)](https://www.nuget.org/packages/MarkupString.Html)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

**Immutable styled text for terminals and the web.** One value — a plain string plus layered
markup runs over it — renders to ANSI, HTML, Pueblo, MXP, BBCode or plain text, and round-trips
through JSON without losing a layer the reader does not understand.

```csharp
var text = MarkupText.Concat(
  MarkupText.Plain("Hello, "),
  MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "world"));

text.Render(MarkupFormat.Ansi);   // Hello, \e[1;31mworld\e[0m
text.Render(MarkupFormat.Html);   // Hello, <span style="color: #ff5555">world</span>
text.Render(MarkupFormat.Plain);  // Hello, world
```

Slicing, padding, wrapping, trimming and the rest of the string operations carry the markup with
them, and measure in **display cells** — wide CJK, combining marks and emoji sequences count
correctly, and no operation ever cuts a grapheme cluster in half. On top of them sits a column
layout engine: wrap, justify, fill, and assemble columns into aligned rows.

## Install

```sh
dotnet add package MarkupString
dotnet add package MarkupString.Ansi
dotnet add package MarkupString.Html
```

| Package | What it gives you |
|---|---|
| [`MarkupString`](https://www.nuget.org/packages/MarkupString) | The `MarkupText` type, runs, formats, the registry, the emitter/codec contracts, the JSON serializer, grapheme and display-width helpers. No rendering opinions. |
| [`MarkupString.Ansi`](https://www.nuget.org/packages/MarkupString.Ansi) | Terminal styling: colours (16 / xterm-256 / truecolor), attributes, links; an `ansi()` code parser and an escape-sequence parser; emitters for ANSI, HTML, Pueblo, MXP and BBCode. |
| [`MarkupString.Html`](https://www.nuget.org/packages/MarkupString.Html) | Raw HTML/MXP tag markup — an MXP `<send>`, an anchor, a `<span class>` — plus the stylesheet for the classes the emitters write. |

The core package renders nothing on its own: emitters live in the kind packages, so a consumer
that only needs one of them pays for one of them, and a kind of your own is a first-class peer
rather than a fork.

## Getting started

```csharp
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

// Once, at startup. Set-once: a second, different registry throws.
MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();

var prompt = MarkupText.Wrap(
  HtmlMarkup.Create("send", "href=\"north\""),
  MarkupText.Wrap(AnsiCodeParser.Parse("hc"), "Go north"));

Console.WriteLine(prompt.Render(MarkupFormat.Ansi));
```

Columns, wrapping and justification come from the same value:

```csharp
var body = new ColumnFormat { Width = 24, Wrap = WrapMode.Word, Alignment = Alignment.Paragraph };

TextLayout.Rows(
[
  new LayoutColumn(left, body),
  new LayoutSeparator(MarkupText.Plain("  |  ")),
  new LayoutColumn(right, body),
], new LayoutOptions());
```

`ToString()` is always the plain text — it is never format-specific. Rendering is explicit:
`Render(MarkupFormat.Ansi)`, `RenderTo(format, bufferWriter)` when you have somewhere to write.

## Documentation

| Guide | |
|---|---|
| [Getting started](docs/getting-started.md) | Install, wire up the registry, build and render your first styled text. |
| [Text operations](docs/text-operations.md) | Slicing, padding, alignment, splitting, splicing — and the grapheme and display-width rules they obey. |
| [Layout](docs/layout.md) | Wrapping, justification, fills as patterns, and assembling columns into rows. |
| [Formats and rendering](docs/formats.md) | The six built-in formats, what each emits, framers, custom formats. |
| [Custom markup kinds](docs/custom-markup.md) | Write your own `IMarkup`, emitters and codec; compose with the kinds already registered. |
| [Serialization](docs/serialization.md) | The JSON wire format, forward compatibility, `UnknownMarkup`. |
| [Releasing](docs/releasing.md) | How a version is cut and published (maintainers). |

## Design

- **Runs, not a tree.** Text is a `string`; markup is an `ImmutableArray<Run>` of coalesced,
  non-overlapping ranges, each holding a stack of layers. Identically marked neighbours merge,
  overlapping runs are rejected at construction, and a slice is a clip of that array. This is the
  model behind `NSAttributedString`, Swift's `AttributedString` and VS Code's line tokens.
- **An explicit registry, not reflection.** Emitters are keyed on `(markup type, format)` in a
  `FrozenDictionary` built by `WithAnsi()`/`WithHtml()`/`With(...)`. Nothing is discovered at
  runtime, so nothing breaks under trimming, and adding a kind is a call, not a convention.
- **Diffed output.** ANSI transitions are written as the difference between the previous run's
  style and this one's, so nested styling does not restate what is already in effect and no run
  pays for a reset it does not need.
- **Unicode-correct by construction.** Extractions snap inward to cluster boundaries, edits snap
  outward; padding and alignment measure in cells, not code units.
- **AOT and trimming clean.** All three packages are `IsAotCompatible` with no reflection and no
  dynamic code, and CI publishes a native binary with every assembly rooted, failing on any
  `IL2xxx`/`IL3xxx` warning.

## Requirements

.NET 10 or later.

## Versioning

Semantic versioning, driven by [MinVer](https://github.com/adamralph/minver): the tag `v1.2.3`
builds `1.2.3`, and any other commit builds the next patch as a `-preview.0.N` prerelease. The
three packages share one version and are released together. Public API changes are tracked in
`PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` and enforced at build time.

## Contributing

```sh
dotnet build MarkupString.slnx
dotnet run --project MarkupString.Tests
```

The build fails with `FORMAT001` if C# no longer matches `.editorconfig`; the error text carries
the `dotnet format whitespace --folder <dir>` command that fixes it (run it until it reports no
changes — the formatter needs two passes to converge).

### Text measurement units

`MarkupText.Length`, run offsets, `Substring`, and search results use UTF-16 code units.
A Unicode scalar is one code point (one or two UTF-16 units); use .NET `EnumerateRunes()`
on `Text` when scalar iteration is intended. `GraphemeCount` counts extended grapheme
clusters, while `DisplayWidth` measures terminal columns. For `界e\u0301😀` these values
are respectively 5 UTF-16 units, 4 scalars, 3 clusters, and 5 columns.

```csharp
var clusters = text.EnumerateGraphemes(); // lazy IEnumerable<MarkupText>
var second = text.SubstringGraphemes(1, 1);
var tail = text.SubstringGraphemes(1);
foreach (var range in Graphemes.Enumerate(text.Text))
{
    // range contains UTF-16 offsets, suitable for indexing the original text
}
```

Grapheme indexes are zero-based. Negative starts clamp to zero, nonpositive counts
return empty, and ranges past the end clamp to the available clusters. Extraction
preserves every ANSI, HTML, and custom markup layer, even when a run boundary occurs
inside a cluster. It never renders into a particular format. Enumeration keeps constant
traversal storage, a compiler-generated iterator object, and the yielded cluster text
and clipped runs; the core
`Graphemes.Enumerate` range enumerator and `Graphemes.Count` allocate no boundary array.

Segmentation follows the running .NET `StringInfo` Unicode rules. Text is never Unicode
normalized: composed and decomposed spellings retain their original UTF-16 content.
Malformed UTF-16 is retained unchanged; segmentation follows `StringInfo` behavior for
unpaired surrogates. Scalar enumeration through .NET `EnumerateRunes()` instead reports
replacement runes for malformed sequences. MarkupString does not repair or reject them.
Existing UTF-16 slicing and display-width policies remain unchanged. Snapping walks back
to a proven boundary with no maximum cluster length, including long combining, ZWJ,
and regional-indicator sequences. Adjacent supplementary symbols use a constant-time
boundary check. Regional-indicator pairing requires preceding context, so repeated random
UTF-16 snapping within an uninterrupted flag sequence can rescan that sequence; use forward
grapheme enumeration for linear segmentation when traversing all clusters.

## Licence

Apache-2.0. Extracted from and used by [SharpMUSH](https://github.com/SharpMUSH/SharpMUSH).

