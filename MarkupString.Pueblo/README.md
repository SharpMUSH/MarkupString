# MarkupString.Pueblo

Pueblo's own extensions for [`MarkupString`](https://www.nuget.org/packages/MarkupString): its panes,
page control, sounds, prefetching and mode switches — the `xch_` vocabulary a Pueblo client reads and
nothing else does.

```sh
dotnet add package MarkupString.Pueblo
```

It is one of MarkupString's three **tag dialects**, beside
[`MarkupString.Html`](https://www.nuget.org/packages/MarkupString.Html) (plain HTML, for browsers) and
[`MarkupString.Mxp`](https://www.nuget.org/packages/MarkupString.Mxp) (MXP's own elements). Each writes
its own tags; none of them reads another's.

## What belongs here, and what does not

Pueblo renders an HTML subset, so **plain HTML is not here**: `<b>`, `<pre>`, `<font>` and the rest are
[`MarkupString.Html`](https://www.nuget.org/packages/MarkupString.Html)'s `HtmlMarkup`, which Pueblo
reads as HTML. **Styling and links are not here either**: bold, colour and a command link travel as
`AnsiMarkup` and are already written per dialect — `<A XCH_CMD>` for Pueblo, `<SEND HREF>` for MXP.

What is here is the part that is Pueblo's alone, and means nothing to a browser or an MXP client.

## Usage

```csharp
MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml().WithPueblo();

var line = MarkupText.Concat(
  PuebloElements.Sound("door.wav", volume: 80),
  MarkupText.Plain("The door creaks open."));

line.Render(MarkupFormat.Pueblo);  // <img xch_sound="door.wav" xch_volume="80">The door creaks open.
line.Render(MarkupFormat.Mxp);     // The door creaks open.
line.Render(MarkupFormat.Ansi);    // The door creaks open.
```

| | Pueblo | HTML | everything else |
|---|---|---|---|
| `Image` | `<img>` with `xch_cmd`, `xch_hint`, `xch_graph` | `<img>`, when the source is an absolute URL | nothing |
| `Sound`, `Alert`, `Speech` | `<img xch_sound=…>` and its siblings | nothing — the file is the world's, not an address | nothing |
| `Pane` | `<xch_pane>` around its content | a span around its content | the content |
| `Page`, `Mode`, `MudText`, `Prefetch` | the tag | nothing | nothing |

An element that wraps nothing is a point in the text, carried on a zero-width space; a format that
cannot express it writes **nothing at all**, carrier included, so the same text is safe to send to every
client.

## What this package does not do

The Pueblo handshake — the hello, `PUEBLOCLIENT`, and the sequence that moves a client into HTML — is
the telnet layer's, in
[TelnetNegotiationCore](https://github.com/HarryCordewener/TelnetNegotiationCore). This package renders
what it is given.

## Licence

Apache-2.0. Source, guides and issues: [SharpMUSH/MarkupString](https://github.com/SharpMUSH/MarkupString).
