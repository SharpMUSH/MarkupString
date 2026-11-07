# MarkupString.Mxp

MXP for [`MarkupString`](https://www.nuget.org/packages/MarkupString): writes core's shared
vocabulary — sounds, music, pictures, panes, gauges, status, variables, link expiry, relocation and
login prompts — as MXP's own elements, and holds each to what the client said it supports.

You say a thing once; this package is how an MXP client hears it. The same text goes to a Pueblo
client through `MarkupString.Pueblo`, to a browser through `MarkupString.Html`, and to a terminal,
which gets a picture's description, a pane's text, and nothing for a sound.

```sh
dotnet add package MarkupString.Mxp
```

## Usage

```csharp
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Mxp;

var registry = MarkupRegistry.Empty.WithAnsi().WithHtml().WithMxp().WithMxpSecureLines();

var line = MarkupText.Concat([
  MarkupText.Sound("https://example.test/sounds/door.wav", volume: 80),
  MarkupText.Pane(MarkupText.Plain("North: the gate"), "map", "The Map")]);

line.Render(MarkupFormat.Mxp, registry);
// ESC[1z<SOUND door.wav V=80 U=https://example.test/sounds/><FRAME map TITLE="The Map"><DEST map>North: the gate</DEST>
```

These are MXP's secure elements, which a client acts on only on a line opened in secure mode, so render
through a registry with `WithMxpSecureLines()`.

## Reading MXP

`WithMxp()` on a `DecoderRegistry` reads MXP from a server: line modes, open and secure tags, `SEND`
menus, custom elements and entities. Turn it on with `decoder.EnableProtocol(MxpDecoding.Protocol)`
once telnet negotiates it. See [Decoding](https://github.com/SharpMUSH/MarkupString/blob/main/docs/decoding.md#mxp).

## What the client supports

MXP lets a server ask which elements a client renders (`<SUPPORT>`), and a client that answered
`-image` should not be sent one. Pass the answer as a predicate over the names in
`MxpRegistration.Elements`:

```csharp
var answered = new HashSet<string>(["SOUND", "MUSIC", "VAR"], StringComparer.OrdinalIgnoreCase);
var registry = MarkupRegistry.Empty.WithAnsi().WithMxp(answered.Contains).WithMxpSecureLines();
```

An element the client refused is written as a format without MXP writes it: nothing for a sound, the
description for a picture, the text in the main window for a pane. With no predicate every element is
written. Asking belongs to the telnet layer, and the answer to one connection.

For a client that refuses `IMAGE` but renders terminal graphics, also configure
`WithAnsiOutput(new AnsiOutputOptions(depth, features) { Pictures = source })`. Laid-out image rows
then use the enabled Sixel, iTerm2, Kitty or block-art renderer while links and other supported elements
remain MXP. Without pixels or an enabled graphics method, the picture keeps its text art. A client
that supports `IMAGE` continues receiving MXP images.

## Elements

| Factory | Written as |
|---|---|
| `MarkupText.Sound(source, volume, repeats)` | `<SOUND file V= L= U=>` |
| `MarkupText.Music(source, volume, repeats, continues)` | `<MUSIC file V= L= C=1 U=>` |
| `MarkupText.StopSound(channel)` | `<SOUND Off>`, `<MUSIC Off>` |
| `MarkupText.Image(source, description, width, height, align)` | `<IMAGE file URL= W= H= ALIGN=>` |
| a laid-out `Figure` | `<IMAGE file URL=>` on its first row, its other cells blank |
| `MarkupText.Pane(content, name, title)` | `<FRAME name TITLE=><DEST name>content</DEST>` |
| `MarkupText.ExpireLinks(group)` | `<EXPIRE group>` |
| `MarkupText.Relocate(host, port, quiet)` | `<RELOCATE host port QUIET>` |
| `MarkupText.LoginPrompt(field)` | `<USER>`, `<PASSWORD>` |
| `MarkupText.Variable(content, name)` | `<VAR name>content</VAR>` |
| `MarkupText.Gauge(content, variable, maximum, caption, color)` | `<GAUGE variable MAX= CAPTION= COLOR=>` |
| `MarkupText.Status(content, variable, maximum, caption)` | `<STAT variable MAX= CAPTION=>` |

An absolute address is split into the file and the directory MXP downloads it from (`U=`, `URL=`).
Syntax is from the [MXP specification](https://www.zuggsoft.com/zmud/mxp.htm).
