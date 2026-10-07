# MarkupString.Ansi

Terminal styling for [`MarkupString`](https://www.nuget.org/packages/MarkupString): colours (the 16
standard ones, xterm-256 and truecolor), bold/faint/italic/underline/overline/strike/blink/invert,
and links — either a URL or a command for the client to send.

One `AnsiMarkup` layer per span; a run's stack of layers folds into a single SGR sequence for the
terminal, one `<span>` for HTML, or the nearest equivalent in the other formats. Transitions are
written as diffs, so nested spans do not restate what is already in effect.

```sh
dotnet add package MarkupString.Ansi
```

## Usage

```csharp
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();

var text = MarkupText.Concat(
  MarkupText.Plain("Hello, "),
  MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "world"));

text.Render(MarkupFormat.Ansi);   // Hello, \e[1;31mworld\e[0m
text.Render(MarkupFormat.Html);   // Hello, <span style="color: #ff5555">world</span>

var json = MarkupTextSerializer.Serialize(text);
var back = MarkupTextSerializer.Deserialize(json);
```

`AnsiCodeParser.Parse` takes MUSH `ansi()` syntax — `hr`, `/R`, `+xterm200`, `#ff5555`,
`<255 0 0>` — and `AnsiMarkup.Create(...)` builds the same thing from named arguments.
`AnsiCodeWriter.Write(style)` goes the other way, from a style to the codes that produce it.

## Formats

`WithAnsi()` registers emitters for `Ansi`, `Html`, `Pueblo`, `Mxp` and `BBCode`. `Plain` needs
none: the body passes through.

A command link (`linkKind: LinkKind.Command`) is written in each client's own dialect: `<A XCH_CMD>`
for Pueblo, `<SEND HREF>` for MXP, an `ms-cmd-link` anchor carrying `xch_cmd` for HTML, and the bare
text for a terminal. Pueblo and MXP are different dialects, and each client prints the other's
tags as text.

An MXP client reads tags only on a line opened in secure mode. The core package's
`MarkupRegistry.WithMxpSecureLines()` opens every line of `Mxp` output with `ESC[1z`. Use it on the
registry that renders for an MXP connection, and leave it off the one used for tests, logs and
previews.

A client that cannot show every colour gets its own registry:
`MarkupRegistry.Default.WithAnsiOutput(AnsiColorDepth.Xterm256)` writes each colour as the nearest one the
256-colour palette has, `Standard` as the nearest of the sixteen, `Attributes` with no colour at all, and
`None` with no SGR. It covers `Ansi`, `Pueblo` and `Mxp`. Pass `hyperlinks: false` for a terminal that
prints OSC 8 instead of reading it.

### Terminal features: links and pictures

`WithAnsiOutput(new AnsiOutputOptions(depth, features) { Pictures = source })` describes one client
fully. `TerminalFeatures` says what its terminal reads beyond colour:

| Feature | Written as |
|---|---|
| `Hyperlinks` | a URL link as OSC 8 (`ESC ] 8 ; ; url ST`) |
| `CommandLinks` | a command link as MSLP (`ESC ] 68 ; 1 ; SEND ; command BEL`, then the underlined text) |
| `KittyGraphics` | a picture through the Kitty graphics protocol, with Unicode placeholders |
| `InlineImages` | a picture as an iTerm2 inline image (`ESC ] 1337 ; File=…`) |
| `Sixel` | a picture as sixel graphics |
| `BlockArt` | a picture as coloured `▀`/`▄` half blocks, plain text any UTF-8 colour terminal shows |

Pictures are drawn into the cells a `Figure` reserves when it is laid out for such a reader
(`LayoutContext.Pictures`, which answers the cells a picture takes). Each row is marked with
`PictureCellsMarkup` over the figure's text art, so a box, a flex row or a table around it lines up
whichever way it ends up drawn. The pixels come from the host's `ITerminalPictureSource`: this package
neither fetches nor decodes a file. Without the pixels, or without the feature, the row is its art.

- **Kitty** sends the picture once per connection (`a=T,U=1`, zlib-compressed RGBA in 4096-byte
  chunks, `q=2` so nothing comes back as input) and writes each cell as `U+10EEEE` with row and column
  diacritics, the image id as a truecolor foreground. Placeholders are text: they wrap, scroll and are
  erased like it. `MarkTransmitted` is how the source says whether the terminal already has it.
- **iTerm2 and sixel** are pixels over the screen. On the picture's first row the cursor makes room
  below (`ESC D` per row), comes back up, and draws the picture between `ESC 7` and `ESC 8`; every row
  then steps over its cells with `CSI n C`, so no text is written over it. Sixel is the cells' size in
  pixels (`CellWidth` × `CellHeight`, 10 × 20 unless the host knows better).
- **Half blocks** letterbox the picture into two pixels a cell, at the client's colour depth.

None of these is sent on a guess: a MUD client that is not a terminal emulator may print them.

## Styling the HTML output

The HTML-family emitters write `ms-*` classes for the attributes with a fixed rendering (bold,
italic, underline, strike, overline, faint, blink, invert, and command links) and inline `style`
for colours, which are open-ended. `AnsiCss.Fixed` is the stylesheet for those classes — include
it once per page, or copy its rules into your own sheet.

## Extension points

- `IAnsiStyleSource` — implement it on your own `IMarkup` and this package's fold picks the style
  up, so a layer of yours can contribute bold or a colour without its own emitter per format.
- `IMarkupEmitter` / `IMarkupSetEmitter` / `IMarkupCodec` — replace any of this package's
  registrations by adding yours to the registry afterwards.

## AOT and trimming

`IsAotCompatible`; no reflection, no dynamic code. Registration is an explicit `WithAnsi()` call,
so nothing depends on a type surviving the trimmer by name.

## Licence

Apache-2.0. Source, guides and issues: [SharpMUSH/MarkupString](https://github.com/SharpMUSH/MarkupString).
