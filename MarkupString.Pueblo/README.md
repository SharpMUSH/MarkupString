# MarkupString.Pueblo

Pueblo for [`MarkupString`](https://www.nuget.org/packages/MarkupString): writes core's shared
vocabulary — sounds, pictures, panes, clearing the screen and prefetching — in the Pueblo client's own
HTML extensions.

You say a thing once; this package is how a Pueblo client hears it. The same text goes to an MXP client
through `MarkupString.Mxp`, to a browser through `MarkupString.Html`, and to a terminal, which gets a
picture's description, a pane's text, and nothing for a sound.

```sh
dotnet add package MarkupString.Pueblo
```

## Usage

```csharp
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Pueblo;

var registry = MarkupRegistry.Empty.WithAnsi().WithHtml().WithPueblo();

var line = MarkupText.Concat([
  MarkupText.Sound("door.wav", volume: 80),
  MarkupText.Pane(MarkupText.Plain("North: the gate"), "map", "The Map")]);

line.Render(MarkupFormat.Pueblo, registry);
// <img xch_sound="play" href="door.wav" xch_volume="80"><xch_pane action="redirect" name="map" panetitle="The Map">North: the gate<xch_pane action="redirect" name="_previous">
```

## Elements

| Factory | Written as |
|---|---|
| `MarkupText.Sound(source, volume, repeats)`, `Music(...)` | `<img xch_sound="play" href= xch_volume=>`, or `"loop"` for `repeats: SoundMarkup.Forever` |
| `MarkupText.StopSound(channel)` | `<img xch_sound="stop" xch_device="wave"\|"midi">` |
| `MarkupText.Image(source, description, width, height, align)` | `<img src= alt= width= height= align=>` |
| `MarkupText.Pane(content, name, title)` | `<xch_pane action="redirect" name= panetitle=>content<xch_pane action="redirect" name="_previous">` |
| `MarkupText.ClearScreen()` | `<xch_page clear="text">` |
| `MarkupText.Prefetch(source)` | `<xch_prefetch href= xch_prob="100">` |

Pueblo has two players, one for wave files and one for MIDI, and picks between them by the file; it
has no music channel as such, and plays a sound once or loops it, without a count. It has no gauges,
status bar or variables, and those write their text.

The Pueblo handshake — `PUEBLOCLIENT`, and the sequence that moves a client into HTML — belongs to the
telnet layer, not to text. Names and attributes are the client's own, from its source
([uecasm/pueblo](https://github.com/uecasm/pueblo): `ChSound.cpp`, `ChPaneTag.cpp`, `ChHtmlPane.cpp`,
`ChHeadElement.cpp`).
