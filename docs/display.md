# Display

A decoded line is a `MarkupText` (see [Decoding](decoding.md)). To show it, fold each run's markup
into a `DisplayStyle` (RGB colours, attributes, what a click does) degraded to what the output can
show. `SpanRenderer` gives runs of text for a web page; `CellRenderer` gives a grid of cells for a
window that draws its own.

```csharp
using System.Net;
using MarkupString;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Display;

var decoders = DecoderRegistry.Empty.WithVt();
var display = DisplayRegistry.Core.WithAnsi();
var options = new DisplayOptions { Capabilities = OutputCapabilities.Browser };

var line = Decoder.DecodeAll("\e[1;31mDanger\e[0m: see \e]8;;https://example.com/help\e\\help\e]8;;\e\\", decoders)[0];

var html = new System.Text.StringBuilder();
foreach (var span in SpanRenderer.Render(line.Content, display, options))
{
    if (span.Point is not null) continue;
    var css = SpanRenderer.Css(span.Style);
    var classes = SpanRenderer.CssClasses(span.Style);
    var encoded = WebUtility.HtmlEncode(span.Text);
    if (span.Style.Interaction is { Kind: InteractionKind.Url } link)
        html.Append($"<a href=\"{WebUtility.HtmlEncode(link.Target)}\" class=\"{classes}\">{encoded}</a>");
    else if (css.Length == 0 && classes.Length == 0)
        html.Append(encoded);
    else
        html.Append($"<span style=\"{css}\" class=\"{classes}\">{encoded}</span>");
}
```

```html
<span style="color:#aa0000;font-weight:bold" class="">Danger</span>: see <a href="https://example.com/help" class="ms-link">help</a>
```

`Decoder` clashes with `System.Text.Decoder`, so a file that imports `System.Text` has to qualify one
of them.

## What an output can show

`OutputCapabilities` describes one output:

| Property | Type | Default |
|---|---|---|
| `Colors` | `ColorLevel`: `None`, `Attributes`, `Standard`, `Xterm256`, `TrueColor` | `TrueColor` |
| `Attributes` | `TextAttributes` flags: bold, faint, italic, underline, blink, inverse, conceal, strikethrough, overline | `All` |
| `Interactions` | `InteractionKinds` flags: `Url`, `Command`, `Prompt`, `Menu` | `All` |
| `Pictures` | `PictureMethods` flags: `Bitmap`, `KittyPlaceholders`, `InlineImages`, `Sixel`, `HalfBlocks` | `None` |
| `MovingPictures`, `Sound`, `Panes` | `bool` | `false` |
| `Unicode` | `UnicodeLevel`: `Full`, `Narrow`, `Ascii` | `Full` |

`ColorLevel.None` means no attributes either; `Attributes` means attributes without colour.

Three presets:

| Preset | Differs from the defaults |
|---|---|
| `PlainText` | No colours, no attributes, no interactions |
| `Browser` | `Bitmap` pictures, moving pictures, sound, panes |
| `CellGrid` | `Bitmap` and `HalfBlocks` pictures, moving pictures, sound, panes |

Change one with `with`:

```csharp
var capabilities = OutputCapabilities.Browser with { Colors = ColorLevel.Xterm256, Attributes = TextAttributes.All & ~TextAttributes.Blink };
```

A terminal's capabilities come from its `TerminalProfile` and the colour depth you settled on:

```csharp
var terminal = TerminalProfile.Kitty.ToOutputCapabilities(AnsiColorDepth.Xterm256);
// Colors = Xterm256, Interactions = Url, Pictures = KittyPlaceholders, HalfBlocks, MovingPictures = True
```

`ToOutputCapabilities` sets `Url` from `Hyperlinks`, `Command` from `CommandLinks`, and pictures from
`KittyGraphics`, `InlineImages`, `Sixel` and `BlockArt`. It never sets sound or panes.

`Intersect` gives what both outputs can show: the lesser colour and Unicode level, and the shared
flags. `Union` gives what either can, for a client that shows one session in two places and tells
the server once.

```csharp
var both = OutputCapabilities.Browser.Intersect(terminal);    // Xterm256, Url, no pictures, no sound
var either = OutputCapabilities.Browser.Union(terminal);      // TrueColor, All, Bitmap | KittyPlaceholders | HalfBlocks, sound
```

`BestPictureMethod` is the first of `Bitmap`, `KittyPlaceholders`, `InlineImages`, `Sixel`,
`HalfBlocks` that the output has.

## The display registry

A `DisplayRegistry` is the list of `IStyleContributor`s that fold markup into a style. Build it once
and share it.

| Registry | Knows |
|---|---|
| `DisplayRegistry.Empty` | Nothing: every run is plain |
| `DisplayRegistry.Core` | The shared vocabulary (`VocabularyStyles`): `ImageMarkup`, `PaneMarkup`, `CommandMenuMarkup`, `HighlightMarkup` |
| `Core.WithAnsi()` | Also `AnsiStyles`: every layer that is an `IAnsiStyleSource` (SGR, an MXP colour, an HTML `<b>`) |
| `AnsiDisplay.Default` | The same as `Core.WithAnsi()` |

`WithAnsi(boldIsBright: true)` draws bold text in a standard colour in its bright version, as many
MUD clients do: `\e[1;31m` becomes `#ff5555` instead of `#aa0000`.

Contributors run in the order they were added, each writing into one `StyleBuilder`. Within a
contributor the innermost layer has the last word. `AnsiStyles` sets the foreground and attributes,
but leaves a background or interaction an earlier contributor set, so a `HighlightMarkup` over SGR
text keeps its background and a command menu keeps its menu.

A contributor of your own reads the layers it knows and leaves the rest:

```csharp
sealed class ChatColor : IStyleContributor
{
    public void Contribute(MarkupSet markups, StyleBuilder style, DisplayOptions options)
    {
        foreach (var markup in markups)
        {
            if (markup is PaneMarkup { Name: "chat" }) style.Foreground ??= new RgbColor(150, 200, 255);
        }
    }
}

var display = DisplayRegistry.Core.WithAnsi().With(new ChatColor());
```

Added last with `??=`, it colours chat text only where the server gave no colour.

`display.Resolve(markups, options)` folds one `MarkupSet` and degrades it. The renderers call it once
per markup set in a text.

## Options

`DisplayOptions`:

| Property | Default | |
|---|---|---|
| `Capabilities` | `Browser` | What the output can show |
| `Colors` | null | An `IColorResolver`; null uses the xterm palette |
| `DefaultForeground` | `(229, 229, 229)` | Used when inverse text has no colour of its own |
| `DefaultBackground` | `(0, 0, 0)` | Used when inverse text has no background of its own |
| `Links` | `UrlPolicy.Safe` | Which link addresses are drawn as clickable; any other stays text |
| `TabWidth` | 8 | Cells between tab stops, in `CellRenderer` |

`SpanRenderer` and `CellRenderer` take an `IStyleResolver`, which a `DisplayRegistry` is, so a host can
give them its own.

An `IColorResolver` decides what "red" is. Subclass `AnsiColors` to change part of the palette:

```csharp
sealed class Theme : AnsiColors
{
    public override RgbColor Standard(int index, bool bright) =>
        index == 1 && !bright ? new RgbColor(220, 50, 47) : base.Standard(index, bright);

    // Keep text readable when the server paints it on its own colour.
    public override RgbColor Legible(RgbColor foreground, RgbColor background) =>
        foreground == background ? new RgbColor(255, 255, 255) : foreground;
}

var themed = new DisplayOptions { Colors = new Theme() };
var text = Decoder.DecodeAll("\e[31mred\e[0m \e[34;44msame\e[0m", decoders)[0].Content;
```

```
"red"  color:#dc322f
" "
"same" color:#ffffff;background-color:#0000aa
```

`Legible` is called only when `Colors` is set, with the final foreground and the background
(`DefaultBackground` when there is none).

## Degrading

`Resolve` drops what the output cannot show, in this order:

- **Colours.** At `ColorLevel.Attributes` or `None` both colours are dropped. At `Standard` each colour
  becomes the nearest of the sixteen, and at `Xterm256` the nearest of the 256 (after the inverse swap
  below), by redmean distance, as `Colors` draws them (the xterm palette when it is null). The colour
  `Legible` returns is reduced the same way. At `TrueColor` RGB passes through.
- **Attributes.** Those the output lacks are dropped. At `ColorLevel.None` every attribute is.
- **Prompts.** A `CommandMenuMarkup` with `Prompt` set is a `Prompt` interaction.
- **Inverse.** With colour, foreground and background are swapped, filling in `DefaultForeground` and
  `DefaultBackground`; `Inverse` stays set for information. At `Attributes` the flag is kept for the
  output to draw when it has `TextAttributes.Inverse`, and dropped when it does not.
- **Link groups.** A `LinkGroupMarkup` (as an OSC 8 `id` makes) sets the `Group` of the interaction
  it is over, the innermost winning, whichever contributor made the link.
- **Interactions.** Menu choices go when the output has no `Menu`. A prompt link becomes a command
  link on an output with `Command` but not `Prompt`. Any other interaction the output lacks is dropped,
  and the text stays.
- **Pictures** are dropped when `Pictures` is `None`, leaving the picture's text.
- **Panes** are dropped when `Panes` is false, so the text shows in the main output.

The renderers also fold text with `AsciiFold` at `UnicodeLevel.Ascii`, draw a wide character as `?`
in cells at `Narrow`, and drop sound points (`SoundMarkup`, `SoundStopMarkup`) when `Sound` is false.
Other points, such as a bell, are kept.

The same line for three outputs:

```csharp
var sample = Decoder.DecodeAll("\e[1;31mred\e[0m \e[7minverse\e[0m", decoders)[0].Content;
SpanRenderer.Render(sample, display, new DisplayOptions { Capabilities = OutputCapabilities.Browser });
```

| Capabilities | Spans |
|---|---|
| `Browser` | `"red"` #aa0000, bold · `" "` · `"inverse"` #000000 on #e5e5e5, inverse |
| `Browser with { Colors = ColorLevel.Attributes }` | `"red"` bold · `" "` · `"inverse"` inverse, no colours |
| `PlainText` | `"red inverse"` |

A command menu on a browser, and on an output with commands but no menus:

```csharp
var menu = MarkupText.Wrap(new CommandMenuMarkup([new("Look", "look sword"), new("Take", "take sword")], "A sword"), "sword");

display.Resolve(menu.Runs[0].Markups, options).Interaction;
// Kind = Command, Target = look sword, Choices: 2

display.Resolve(menu.Runs[0].Markups, new DisplayOptions
{
    Capabilities = OutputCapabilities.Browser with { Interactions = InteractionKinds.Command },
}).Interaction;
// Kind = Command, Target = look sword, Choices: null
```

## Spans

`SpanRenderer.Render(text, registry, options)` returns `DisplaySpan`s in order. Adjacent text with
the same style is one span. A point (a bell, a sound, a clear) is a span of its own with empty `Text`
and `Point` set. Text no markup covers is `DisplayStyle.Plain`.

`SpanRenderer.Css(style)` is inline CSS: `color`, `background-color`, `font-weight:bold`,
`opacity:0.6` for faint, `font-style:italic`, `text-decoration` for underline, overline and
line-through, and `color:transparent` for conceal, in place of the text's colour. `CssClasses(style)` gives the classes a page
styles itself:

| Class | When |
|---|---|
| `ms-blink` | Blink, which CSS has no property for |
| `ms-conceal` | Conceal |
| `ms-link`, `ms-command`, `ms-prompt` | The interaction's kind |
| `ms-menu` | An interaction with more than one choice |
| `ms-picture` | A picture |
| `ms-invert` | Inverse with no colours to swap, at `ColorLevel.Attributes`; `AnsiCss.Fixed` styles it |

`SpanRenderer.Hex(color)` writes `#rrggbb`.

## Cells

`CellRenderer.Layout(text, columns, registry, options, wordWrap)` lays text on a grid for an output
that draws cells itself.

```csharp
var grid = new DisplayOptions { Capabilities = OutputCapabilities.CellGrid };
var text = Decoder.DecodeAll("Go \e]8;;https://example.com\e\\north\e]8;;\e\\ to the 漢字 gate", decoders)[0].Content;

var layout = CellRenderer.Layout(text, 12, display, grid, wordWrap: true);
foreach (var row in layout.Rows)
{
    var shown = string.Concat(row.Cells.Select(cell => cell.Text));
    Console.WriteLine($"|{shown}| {row.Cells.Count} cells{(row.Wrapped ? ", wrapped" : "")}");
}
```

```
|Go north to | 12 cells, wrapped
|the 漢字 | 9 cells, wrapped
|gate| 4 cells
```

- `columns` is the width to wrap at; 0 does not wrap. A line feed in the text starts a new row.
- Without `wordWrap` a row breaks at the last cell. With it, a row breaks after the last space that
  fits, and the space stays at the end of the row. A word with no space before it on the row breaks
  at the last cell.
- A row is as long as its text and is never padded. `Wrapped` says the next row continues it.
- A tab advances to the next multiple of `TabWidth`, never past `columns`, and its cells take the
  style of its run.
- A wide character takes two cells: the second is a continuation, with empty `Text` and `Width` 0
  (`IsContinuation`). A wide character that does not fit in the last column moves to the next row.
- A combining mark joins the cell before it.

`layout.At(row, column)` is the cell there, or null past the text. `InteractionAt(row, column)` is
what clicking it does; the right half of a wide character answers for the whole.

```csharp
layout.InteractionAt(0, 4)?.Target;   // https://example.com
layout.InteractionAt(0, 1)?.Target;   // null
layout.At(1, 7);                      // the continuation of 字: Text "", Width 0
```

Points are listed apart, in `layout.Points`, each with the row and column it stands before.

### Pictures

A picture laid out over rows of cells (an `ImageMarkup` with a `Row`) has blank cells, each with
`Style.Picture` set. `Cell.PictureColumn` is the cell's column within the picture, from its left
edge, or from `PictureRow.FirstColumn` for a cropped placement. With `Picture.Row.Row` it says which
part of the picture to draw in that cell.

```csharp
var picture = new ImageMarkup("map.png", "A map") { Row = new PictureRow(0, 3, 6) };
var row = MarkupText.Concat([MarkupText.Plain("> "), MarkupText.Wrap(picture, "      ")]);
CellRenderer.Layout(row, 0, display, grid).Rows[0].Cells.Select(c => c.Style.Picture is null ? "." : c.PictureColumn.ToString());
// . . 0 1 2 3 4 5
```

Word wrap never breaks at a space inside a picture.

## Back to ANSI

A terminal output takes the decoded line as it is, through `Render(MarkupFormat.Ansi)` with a
registry for that client. `ToColorDepth` and `ToTerminalFeatures` turn capabilities into what
`AnsiOutputOptions` takes:

```csharp
var caps = OutputCapabilities.Browser with { Colors = ColorLevel.Standard, Interactions = InteractionKinds.None };
var ansi = MarkupRegistry.Empty.WithAnsi().WithAnsiOutput(new AnsiOutputOptions(caps.ToColorDepth(), caps.ToTerminalFeatures()));

var rich = Decoder.DecodeAll("\e[38;2;255;100;0morange\e[0m \e]8;;https://example.com\e\\link\e]8;;\e\\", decoders)[0].Content;
rich.Render(MarkupFormat.Ansi, MarkupRegistry.Empty.WithAnsi());   // \e[38;2;255;100;0morange\e[0m \e]8;;https://example.com\alink\e]8;;\a
rich.Render(MarkupFormat.Ansi, ansi);                              // \e[1;31morange\e[0m link
```

At `Standard` the orange became the nearest standard colour, bright red, written as bold red; with no
`Url` interaction the link is its text.

## Telling the server

The capabilities you render with are also what the client tells the server it accepts. Put them in
the decoder's identity, so answers and MTTS match what is drawn:

```csharp
var window = OutputCapabilities.CellGrid;
var logView = OutputCapabilities.Browser with { Colors = ColorLevel.Xterm256 };

var identity = new ClientIdentity { Output = window.Union(logView) };
identity.ToMtts();   // Ansi, Vt100, Utf8, Colors256, OscColorPalette, TrueColor, Mslp
```

Use `Union` when one session is shown in two places and the server is told once; render each place
with its own capabilities, so each drops what it cannot show. `ToMtts` is covered in
[Decoding](decoding.md#mtts).
