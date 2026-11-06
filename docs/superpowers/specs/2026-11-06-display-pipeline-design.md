# Display pipeline: decoding wire protocols and rendering them per output — design

Status: draft for review, 2026-11-06. Nothing here is implemented yet.

## Problem

MarkupString renders one way: a `MarkupText` built on the server goes out as ANSI, HTML, MXP, Pueblo and the
rest. Clients need the opposite direction. They receive a byte stream that mixes text, SGR, OSC, MXP or Pueblo
tags and entities, and have to turn it into something they can draw: terminal cells, a cell grid drawn by
Skia, or HTML spans in a browser.

Each SharpMUSH client does this separately today, and none of them uses MarkupString to do it:

| Client | Decode | Model | Draw |
|---|---|---|---|
| SharpMUTerm (`SharpMUTerm.Core`) | `AnsiParser`, `MxpParser` (2,260 lines with definitions), `PuebloParser` (786 lines), one per session picked by `ContentFormat` in `WorldSession` | `StyledLine` of `StyledSpan(Text, TextStyle, SpanInteraction?)` | TUI: `MarkupFormatter` turns a line into SharpConsoleUI markup (`[#rrggbb bold link=…]`), resolving colours through the theme and a contrast floor. Window: SharpConsoleUI writes into `CellGrid` (`GridCell(Rune, Combiners, Fg, Bg, Decorations, IsWideContinuation)`), and `CellRenderer` paints it with Skia, drawing Kitty placeholder cells (`U+10EEEE`) as picture slices. |
| SharpClient (`SharpClient.Core.Rendering`) | `AnsiParser.Parse(line, MxpParserState?)`: SGR, MXP and Pueblo in one 591-line function set | `StyledSegment(Text, TextStyle){Command, Hint, ImageUrl}` | `OutputView.razor`: one `<span style>` per segment from `SegmentStyle.ToCss`, `<button>` for a command, `<img>` for a picture |
| SharpMUSH portal (`SharpMUSH.Client`) | `TerminalFrameRenderer`: a `markup` frame is a serialized `MarkupText`; a non-envelope frame is plain text with "the legacy ANSI strip" | `MarkupText`, then `TerminalLine(Text, Html)` | `MarkupString` HTML renderer, written into the page as a raw HTML string per line |

The SharpMUTerm source says the cost of this outright: its three escape state machines are "near-copies,
deliberately not shared — a fix to one belongs in all three, with its own test in each suite". SharpClient has
a fourth. Each has its own colour type, palette and style record, and each handles a different subset: SharpClient
reads SGR 0, 1, 4, 7, 22, 24, 27 and colours, so italic, faint and strikethrough are lost; SharpMUTerm reads all of
them and conceal.

The portal is the exception that shows the way out. It already treats `MarkupText` as its display model, because
SharpMUSH sends one. A client connected to any other game gets bytes, and needs a decoder that ends in the same
model.

## Goals

1. One decoder for the wire, shared by all three clients, ending in `MarkupText`.
2. Protocols as plugins: VT/SGR, OSC 8, MSLP, MXP, Pueblo, raw HTML, each registered on its own, so a client
   takes only what it supports and a game can add its own.
3. Renderers as plugins, with three first-class targets: an ANSI terminal, a cell grid, and web spans.
4. Capability negotiation in both directions: what the client tells the server it can show, and how a renderer
   degrades what it was sent to what its output can draw.
5. Keep MarkupString's rules: immutable values, explicit registries, no reflection, Native AOT safe, a layer
   nobody understands passes its text through.

Non-goals: the telnet layer (option negotiation, MCCP, GMCP, charset). TelnetNegotiationCore owns it; the decoder
starts after it, on decoded text. Full terminal emulation of full-screen programs is a later, optional tier
(see [Screen tier](#screen-tier-optional)).

## Prior art and what it settles

| Source | What it settles here |
|---|---|
| Paul Williams' DEC ANSI parser state machine, as used by xterm.js (`EscapeSequenceParser`) and Rust `vte` (Alacritty) | Tokenize once with one state machine (ground, escape, CSI, OSC, DCS, SOS/PM/APC strings), and hand complete sequences to handlers. `vte`'s `Perform` trait is the shape: `print`, `execute`, `csi_dispatch`, `esc_dispatch`, `osc_dispatch`, `hook`/`put`/`unhook` for DCS. |
| xterm.js parser hooks (`registerCsiHandler`, `registerOscHandler`, `registerDcsHandler`, `registerEscHandler`, `registerApcHandler`) | Plugins register by sequence identifier (`{prefix, intermediates, final}` for CSI/DCS, a number for OSC). Handlers are probed newest first; returning `false` passes the sequence on to older handlers, `true` stops. This is the plugin contract for escape sequences. |
| OSC 8 ([egmontkob's spec](https://gist.github.com/egmontkob/eb114294efbcd5adb1944c9f3cb5feda)) | `OSC 8 ; params ; URI ST`, closed by `OSC 8 ; ; ST`; params are `:`-separated `key=value`, only `id` defined. Cells with the same URI and non-empty `id` are one link for hover. VTE and iTerm2 cap the URI at 2083 bytes, VTE caps `id` at 250. The cell renderer's link id comes from here. |
| MXP ([zuggsoft spec](https://www.zuggsoft.com/zmud/mxp.htm)) | Line modes are `ESC [ n z`: 0 open, 1 secure, 2 locked, 3 reset, 4 temp secure, 5–7 lock open/secure/locked, 10–12 room name/description/exits, 19 welcome, 20–99 user-defined. Only formatting tags (B, I, U, S, COLOR, H, FONT) are open; everything else is secure. `<SUPPORT>` is answered `<SUPPORTS +B -IMAGE …>` on a secure line. Line mode is a security boundary, so it belongs inside the MXP plugin, not in the renderer. |
| Pueblo (as implemented in SharpMUTerm's `PuebloParser` and MarkupString's emitter) | HTML subset; command links are `<A XCH_CMD>`, URL links `<A HREF>`; `<xch_mudtext>` suspends `<BR>` line handling; enabling is a session handshake, not markup. |
| Kitty graphics Unicode placeholders, and SharpMUTerm's `CellRenderer` | A picture can live in real text cells (`U+10EEEE`, image id in the foreground, row and column in combining marks), so it wraps, scrolls and clips like text. The cell model needs a picture-slice cell for this. |
| MarkupString's own `TerminalProfile` / `TerminalFeatures` / `AnsiColorDepth` | The degrade ladder for a terminal already exists on the output side: Kitty → iTerm2 inline → sixel → half blocks → text; truecolor → 256 → 16 → attributes → none. Reuse it. |

Lessons from the existing client parsers, which the shared decoder must keep (each is in SharpMUTerm's tests
already):

- The line boundary is what the host says it is. The telnet layer strips the terminator, so the decoder exposes
  `EndLine()` and `EndPrompt()` (for GA/EOR) rather than waiting for `\n`.
- A newline ends any escape sequence in flight. Otherwise a player typing a bare `ESC ]` into a channel swallows
  the rest of the session.
- Every buffer is capped: sequence, tag, entity, OSC payload.
- When MXP leaves open mode, tags opened in open mode are closed. Otherwise `<COLOR FORE=black BACK=black>` in a
  channel paints the rest of the session.
- A secure MXP tag on an open line is literal text, so a player cannot make a `<SEND>` in someone else's client.
- A stray `<` or `&` that cannot start a tag or entity is text.

## Architecture

```text
            host (telnet layer, websocket)
                      │  decoded text chunks, EndLine / EndPrompt, mode events (Pueblo on, MXP on)
                      ▼
 ┌──────────────── Decoder (one per connection) ─────────────────┐
 │  VtTokenizer ──► sequence handlers (CSI / OSC / ESC / DCS / APC) │
 │       │ ground text                                             │
 │       ▼                                                         │
 │  TagTokenizer (only while an SGML protocol is active)           │
 │       └──► tag/entity handlers (MXP, Pueblo, HTML)              │
 │                         │ act on                                │
 │                  DecodeState: style, element stack, line mode   │
 │                         ▼                                       │
 │                  LineBuilder ──► DisplayLine(MarkupText, kind)  │
 └──────────────────────────┬──────────────────────────────────────┘
                            │ DisplayLine stream + DecodeEvents (title, reply-to-server, variables, room, frames)
                            ▼
         scrollback, triggers, search, logging   (client-owned, on MarkupText)
                            │
            ┌───────────────┼──────────────────┐
            ▼               ▼                  ▼
     MarkupFormat.Ansi   CellRenderer       SpanRenderer / MarkupFormat.Html
     (existing)          → ICellSurface     → DisplaySpan list or HTML string
```

Three parts, each with its own plugin point:

1. **Decoder**: wire to `MarkupText`. Plugins are protocol handlers.
2. **Display model**: `MarkupText` with the existing shared vocabulary, a few new markup kinds, and a
   `DisplayLine` envelope.
3. **Renderers**: model to output. Plugins are style contributors (how a markup kind looks) and renderers (what
   the output is).

### 1. Decoder

#### Tokenizer

One `VtTokenizer`, a Williams state machine over `ReadOnlySpan<char>`, kept across `Feed` calls so a sequence can
straddle chunks. It emits tokens and never interprets them:

```csharp
public interface IVtTokenSink
{
    void Print(ReadOnlySpan<char> text);                       // ground text, one run at a time
    void Execute(char control);                                // C0: BEL, BS, HT, CR, FF …
    void Csi(in CsiSequence sequence);                         // params (with ':' subparams), prefix, intermediates, final
    void Esc(in EscSequence sequence);                         // intermediates, final
    void Osc(int command, ReadOnlySpan<char> payload);
    void Dcs(in DcsSequence sequence, ReadOnlySpan<char> data); // buffered, capped
    void Apc(ReadOnlySpan<char> payload);                      // Kitty graphics
}
```

Subparameters matter: `38:2::r:g:b` and `4:3` (curly underline) use `:`. SharpMUTerm's `SgrCodes` reads them;
MarkupString's `AnsiEscapeParser` and SharpClient's parser split on `;` only and drop them.

`AnsiEscapeParser.Parse(string)` keeps its signature and becomes a one-shot decoder with the VT handlers only.

#### Tag tokenizer

MXP, Pueblo and HTML share SGML-ish lexing: `<NAME attr=value attr="quoted">`, `</NAME>`, `<!ELEMENT …>`,
`&name;`, `&#n;`, `&#xh;`. One `TagTokenizer` runs over ground text while any tag protocol is active, with the
caps above, and hands `TagToken`s to tag handlers. Which protocol is active is decoder state, switched by the
host (`EnableProtocol("mxp")` after telnet option 91; Pueblo after its handshake) or by a handler (`ESC [ 6 z`).

#### Handlers (the protocol plugins)

```csharp
public interface IDecoderPlugin
{
    string Name { get; }                                // "vt", "osc8", "mslp", "mxp", "pueblo", "html"
    void Register(DecoderBuilder builder);              // adds handlers below; called once per registry
    DecoderCapabilities Capabilities { get; }           // what it can read, for negotiation
}

public delegate bool CsiHandler(in CsiSequence seq, DecodeContext context);
public delegate bool OscHandler(ReadOnlySpan<char> payload, DecodeContext context);
public delegate bool TagHandler(in TagToken tag, DecodeContext context);
public delegate bool EntityHandler(ReadOnlySpan<char> name, DecodeContext context);

public sealed class DecoderBuilder
{
    public DecoderBuilder OnCsi(CsiIdentifier id, CsiHandler handler);   // { Prefix, Intermediates, Final }
    public DecoderBuilder OnEsc(EscIdentifier id, EscHandler handler);
    public DecoderBuilder OnOsc(int command, OscHandler handler);
    public DecoderBuilder OnDcs(CsiIdentifier id, DcsHandler handler);
    public DecoderBuilder OnApc(ApcHandler handler);
    public DecoderBuilder OnTag(string protocol, string name, TagHandler handler);
    public DecoderBuilder OnEntity(string protocol, EntityHandler handler);
    public DecoderBuilder OnState<T>(Func<T> create) where T : class;    // per-connection state a plugin owns
}
```

Dispatch follows xterm.js: handlers for one identifier are tried newest first, `true` stops, `false` falls through.
An unclaimed sequence is dropped; an unclaimed tag is dropped on a line allowed to use it and literal otherwise
(the MXP plugin decides which, through its line mode).

`DecodeContext` is what a handler may touch:

```csharp
public sealed class DecodeContext
{
    public ref AnsiStyle Style { get; }                 // the SGR state
    public T State<T>() where T : class;                // the plugin's own state (MXP line mode, element definitions)
    public void Open(IMarkup markup);                   // push a layer over text that follows
    public void Close(IMarkup markup);                  // pop to it (closing anything opened since, as HTML does)
    public void Point(IPointMarkup point);              // a sound, a bell, a clear
    public void Text(ReadOnlySpan<char> text);          // text a handler produces (an entity, a literal tag)
    public void EndLine();
    public void Raise(DecodeEvent e);                   // out-of-band: see below
    public IDecodePolicy Policy { get; }                // trust and limits, see Security
}
```

`Open`/`Close` produce `MarkupText` layers, so MXP `<COLOR>`, Pueblo `<FONT>` and SGR all end up as the same
`AnsiMarkup` the server-side code already writes, and the render side needs no protocol knowledge.

`DecodeEvent` covers what is not text: `TitleChanged` (OSC 0/2), `ReplyToServer(string)` (MXP `<SUPPORTS>`,
`<VERSION>`, a DA/XTVERSION answer), `VariableSet(name, value)` (MXP `<VAR>`, used by gauges), `FrameOpened`,
`FrameClosed`, `EntityDefined`, `ElementDefined`, `ProtocolChanged`, `PaletteChanged` (OSC 4/10/11),
`ClipboardRequest` (OSC 52, denied by default). The client decides what to do with each; the decoder never
writes to the socket.

#### Shipped plugins and where they live

Each protocol package gains a decoder next to its emitter, so a package speaks its protocol both ways:

| Plugin | Package | Reads |
|---|---|---|
| `vt` | `MarkupString.Ansi` | SGR in full (including `:` subparams, 8 conceal, 21 double underline, 58/59 underline colour), C0 controls, CSI `J`/`H` as `ClearScreen` points; other CSI dropped (current behaviour) unless the screen tier is on |
| `osc8` | `MarkupString.Ansi` | hyperlinks, with `id` |
| `mslp` | `MarkupString.Ansi` | `OSC 68` command links (what `TerminalFeatures.CommandLinks` writes) |
| `osc-title`, `osc-palette` | `MarkupString.Ansi` | OSC 0/1/2, 4/10/11/104 as events |
| `kitty`, `iterm-image`, `sixel` | `MarkupString.Ansi` | pictures as `ImageMarkup` with inline data, off by default |
| `mxp` | `MarkupString.Mxp` | line modes, open/secure tags, `!ELEMENT`, `!ENTITY`, `SEND` (with menus and `PROMPT`), `A`, `IMAGE`, `SOUND`/`MUSIC`, `FRAME`/`DEST`, `VAR`, `GAUGE`, `STAT`, `EXPIRE`, `RELOCATE`, `USER`/`PASSWORD`, room tags, `SUPPORT`/`VERSION` replies |
| `pueblo` | `MarkupString.Pueblo` | the HTML subset, `XCH_CMD`, `xch_sound`, `xch_pane`, `xch_page clear`, `xch_mudtext` |
| `html` | `MarkupString.Html` | raw HTML through an `HtmlTagPolicy` (the portal's `wshtml()` frames) |

The MXP plugin should be a port of SharpMUTerm's `MxpParser` and its tests, not a new parser: it is the most
complete of the four, and its edge cases are the ones listed above. The same goes for `PuebloParser`.

### 2. Display model

`MarkupText` is the display entity. It already has what a display needs: immutable runs with layered markup,
grapheme-safe slicing, display-cell width, wrapping, JSON that keeps unknown layers, and the shared vocabulary
(`ImageMarkup`, `SoundMarkup`, `PaneMarkup`, `GaugeMarkup`, `StatusMarkup`, `VariableMarkup`, `ClearScreenMarkup`,
`ExpireLinksMarkup`, `RelocateMarkup`, `LoginPromptMarkup`, `BellMarkup`). Using it means the portal's path and the
decoded path end in the same type, and a trigger highlight is one more layer.

Gaps to close in the vocabulary, found by mapping what the client models carry onto it:

| Need | Today | Proposal |
|---|---|---|
| MXP `<SEND>` menu (`href="a|b" hint="…|A|B"`), SharpMUTerm's `SendChoice` | `AnsiMarkup` link is one URL or command | `CommandMenuMarkup(IReadOnlyList<CommandChoice>)` in core |
| `<SEND PROMPT>` (put in the input line, do not send) | none | `LinkKind.Prompt` |
| OSC 8 `id` | none | `AnsiStyle.LinkId` |
| Conceal, double/curly underline, underline colour | none | `AnsiStyle.Concealed`, `UnderlineStyle`, `UnderlineColor` |
| MXP room parts (`ESC[10z`–`12z`, `<RName>`…) used by SharpMUTerm's mapper | none | `RoomPartMarkup(RoomPart)` as an `IDataMarkup`, so it never changes how text looks |
| MXP custom element `FLAG` (`set hp`) | none | `ElementFlagMarkup(string)` as an `IDataMarkup` |
| A trigger highlight, SharpMUTerm's `RuleColor` | none | `HighlightMarkup(colour, scope: Text or Line)` |
| Image size in cells or percent (SharpMUTerm `ImageExtent`) | `ImageMarkup` has pixels | add the unit to `ImageMarkup` |

A line is an envelope around one `MarkupText`:

```csharp
public sealed record DisplayLine(MarkupText Content, LineKind Kind)   // Output, Prompt, Echo, System
{
    public DateTimeOffset Received { get; init; }
    public string? Destination { get; init; }      // MXP <DEST>, Pueblo xch_pane: which pane it belongs in
    public long Sequence { get; init; }            // stable id for scrollback and @key in Blazor
}
```

Scrollback, search, logging and triggers stay client-owned and work on `Content.ToPlainText()` and slices of it,
as they do on `StyledLine.Text` today.

### 3. Renderers

`MarkupFormat` renderers write strings. Cells and component spans are not strings, so they need a second, structured
path that walks the runs once and folds each run's layers into one resolved style.

#### Resolved style and style contributors

```csharp
public readonly record struct DisplayStyle
{
    public DisplayColor Foreground { get; init; }     // already resolved to RGB, or Default
    public DisplayColor Background { get; init; }
    public TextAttributes Attributes { get; init; }   // bold, faint, italic, underline kinds, blink, inverse, conceal, strike, overline
    public DisplayColor UnderlineColor { get; init; }
    public int InteractionId { get; init; }           // 0 = none; index into the line's interaction table
    public int PictureId { get; init; }               // 0 = none
}

public interface IStyleContributor                    // registered per markup type, like an emitter
{
    Type MarkupType { get; }
    void Apply(IMarkup markup, ref StyleBuilder style, in ResolveContext context);
}
```

`ResolveContext` carries the output's capabilities and an `IColorResolver`, which is where SharpMUTerm's theme
lookup and contrast floor (`Contrast.Legible`) plug in, and where SharpClient's phosphor default lives. Today each
client resolves colours in its own renderer.

`AnsiMarkup`, `HtmlMarkup` (`b`, `i`, `u`, `font color`), the shared vocabulary and `HighlightMarkup` ship
contributors. A layer with none contributes nothing and its text still comes out, as in the string renderers.

Interactions (links, command menus, prompt links, image links) go into a per-line table so a renderer can hit-test
without carrying strings in every cell:

```csharp
public sealed record Interaction(InteractionKind Kind, string Target, string? Hint,
                                 IReadOnlyList<CommandChoice>? Choices, string? GroupId);
```

#### Target 1: terminal

The existing `MarkupFormat.Ansi` emitter, unchanged, with `TerminalProfile`, `TerminalFeatures` and
`WithAnsiOutput(depth)`. SharpMUTerm running inside kitty or Windows Terminal reaches the screen through
SharpConsoleUI, so its first step is a `MarkupFormat.Custom("sharpconsoleui")` emitter (in SharpMUTerm, or a
small `MarkupString.SharpConsoleUI` package) that replaces `MarkupFormatter`. It uses the same `IColorResolver`.

#### Target 2: cell grid

New. Lays a `MarkupText` out to a width with the existing wrapping (`TextLayout`, and the internal `LineWrapper`
behind it) and `DisplayWidth`, and writes cells:

```csharp
public readonly record struct Cell(
    string Grapheme,            // whole cluster: base plus combining marks; "" for a wide character's second column
    DisplayStyle Style,
    byte Width,                 // 1 or 2; 0 on the continuation
    PictureSlice Picture);      // (PictureId, Row, Column) when the cell is part of a picture

public interface ICellSurface
{
    int Width { get; }
    void Write(int row, int column, in Cell cell);
    void EndRow(int row, RowInfo info);          // soft-wrapped or hard break, for copy and reflow
}

public sealed class CellRenderer
{
    public CellLayout Layout(DisplayLine line, int width, in ResolveContext context);   // rows, interactions, pictures
    public void Draw(CellLayout layout, ICellSurface surface, int firstRow);
    public Interaction? HitTest(CellLayout layout, int row, int column);
}
```

`Grapheme` is a string rather than SharpMUTerm's `Rune` plus `Combiners`, because a ZWJ emoji or a flag is several
runes and must stay one cell pair. A host that wants runes splits it.

Pictures reuse SharpMUTerm's approach: a picture is laid out as a block of picture cells, so it scrolls and clips
with text. The host decides how to draw a picture cell: Skia draws the bitmap slice; a real terminal gets the
Kitty placeholder, sixel, iTerm2 or half blocks, chosen through `TerminalFeatures` as the ANSI emitter does now.

Adapters: SharpMUTerm's `CellGrid` (window), a SharpConsoleUI control that owns its cells (TUI output panes, phase 3),
or Terminal.Gui.

#### Target 3: web spans

Two forms from the same walk:

- `MarkupFormat.Html` as it is, for the portal, which writes one HTML string per line.
- `SpanRenderer`, for component UIs that attach handlers (SharpClient's `@onclick`):

```csharp
public readonly record struct DisplaySpan(string Text, string? CssClass, string? InlineStyle,
                                          Interaction? Interaction, ImageMarkup? Image, IPointMarkup? Point);

public static IReadOnlyList<DisplaySpan> Spans(this DisplayLine line, in ResolveContext context);
```

Class names and CSS variables come from `AnsiCss` (`ms-*`), so SharpClient and the portal can share one stylesheet
and theme. A `MarkupString.Blazor` package with a `<DisplayLineView>` component is optional and would remove
`OutputView`'s per-segment markup.

### Capability negotiation

Capabilities flow in two directions and meet at one object per connection.

```csharp
public sealed record OutputCapabilities(
    AnsiColorDepth Colors,
    TextAttributes Attributes,         // what it can draw; blink and conceal are often off by choice
    InteractionKinds Interactions,     // Url, Command, Prompt, Menu; none for a log file
    PictureKinds Pictures,             // Bitmap, KittyPlaceholder, Sixel, InlineImage, HalfBlock, AltText
    bool Sound, bool Panes, bool Gauges,
    UnicodeLevel Unicode);             // Full, NoWide, Ascii (AsciiFold)

public sealed record ClientCapabilities(OutputCapabilities Output, DecoderCapabilities Input, UserChoices Choices)
{
    public ClientCapabilities Effective { get; }       // Output ∩ Input ∩ Choices
    public MttsFlags ToMtts();                         // for TelnetNegotiationCore's TTYPE cycle
    public string AnswerMxpSupport(IReadOnlyList<string> asked);   // "<SUPPORTS +B +COLOR -IMAGE …>"
    public string AnswerMxpVersion(string client, string version);
}
```

Inbound: the client should only claim what it can both read and show. A cell grid in a terminal without picture
support answers `-IMAGE` to MXP, so a game that checks sends text instead of a picture nobody will see. This
generalises SharpMUTerm's `MxpParser.AnswerSupport`, which already refuses to claim a tag its handler ignores.
`ToMtts` sets ANSI, UTF-8, 256 colours, truecolor, MSLP, screen reader and the rest from the same object.

Outbound: each renderer takes `OutputCapabilities` in its `ResolveContext` and degrades on its own ladder:

| Feature | Ladder |
|---|---|
| Colour | truecolor → xterm-256 (redmean nearest, `AnsiColor.NearestXterm`) → 16 → attributes only → none |
| Picture | bitmap → Kitty placeholder → iTerm2 inline → sixel → half blocks → alt text |
| Link | clickable with hover group → underlined text → text |
| Command menu | menu → first choice as a link → text |
| Wide or emoji text | as is → `AsciiFold` |
| Sound, pane, gauge | native → text (pane content goes to the main view) → nothing |

The ladders for colour, pictures and Unicode exist in `MarkupString.Ansi` already; this lifts them to a shared
`OutputCapabilities` so the cell and span renderers use the same rules.

### Registration

Same pattern as `MarkupRegistry`: immutable, explicit, built once, AOT safe.

```csharp
var decoders = DecoderRegistry.Empty
    .WithVt()                                // MarkupString.Ansi
    .WithHyperlinks()
    .WithMxp(new MxpDecoderOptions { Client = "SharpMUTerm", Version = v })   // MarkupString.Mxp
    .WithPueblo();                           // MarkupString.Pueblo

var display = DisplayRegistry.Empty
    .WithAnsiStyles()                        // style contributors for AnsiMarkup
    .WithSharedVocabulary()
    .WithHtmlStyles();

// per connection
var decoder = decoders.CreateDecoder(policy: DecodePolicy.ForGame(world));
decoder.LineReady += line => scrollback.Add(line);
decoder.Event += e => session.Handle(e);
decoder.Feed(chunk);
decoder.EndPrompt();                         // on GA / EOR
```

A registry is shared; a `Decoder` holds one connection's state (style, element stack, MXP definitions, line mode)
and is not thread-safe, like the client parsers it replaces.

### Security

The decoder reads text written by other players. `IDecodePolicy` holds the decisions:

- Trust per source: MXP line modes decide what a line may use. A plugin asks `context.Policy` before turning
  text into an interaction, a fetch or an event.
- URLs pass `UrlSafety.IsSafeNavigableUrl` before they become a link or an image.
- Fetching a picture or sound tells its host the player's address (SharpClient's settings already say so), so
  `ImageMarkup` from a decoder is inert until the client chooses to fetch it.
- OSC 52 (clipboard) and title changes are events the client can refuse; clipboard writes are refused by default.
- Caps on every buffer, the element stack, entity expansion depth and the number of MXP definitions.
- Raw HTML only through `HtmlTagPolicy`.

### Screen tier (optional)

MUDs mostly use SGR, but some send cursor addressing for a status area (`DECSTBM` scroll regions, `CUP`, `EL`), and
MOO editors and door games want a real screen. All three clients drop non-SGR CSI today. A later `vt-screen` plugin
can claim those sequences and drive a `ScreenBuffer` of `Cell`s instead of the line builder, with the line flow
continuing below the scroll region. It stays opt-in per world, because a stray cursor move from a game that
assumes 80×24 is worse in a reflowing pane than dropping it.

## Conversions and work plan

What each source becomes on each target, including pictures (Kitty, sixel, iTerm2, MXP), and the order of work, are in
`docs/superpowers/plans/2026-11-06-display-pipeline.md`.

## Testing

- Round trip: for every format with an emitter, `Decode(Render(x, f)) ≈ x` as a property over generated
  `MarkupText` (an extension of `RoundTripProperties`). This tests the decoder and the emitters against each other.
- Port the client suites: SharpMUTerm's `AnsiParser`, `MxpParser` and `PuebloParser` tests and SharpClient's
  parser tests become decoder tests, so nothing they pin is lost.
- Chunking: every test case fed whole and split at every index gives the same lines.
- Fuzzing: random bytes never throw, never exceed caps, and never leave state that swallows the next line.
- Captured transcripts from PennMUSH, TinyMUX, an MXP game and a Pueblo game, checked as approved snapshots of cells
  and spans.
- Renderer parity: the same `DisplayLine` drawn as cells and as spans gives the same text, styles and interaction
  ranges, in the spirit of SharpMUTerm's cell-for-cell comparison of its two front ends.

## Migration

| Phase | MarkupString | SharpMUTerm | SharpClient | Portal |
|---|---|---|---|---|
| 1 | Tokenizer, `vt`, `osc8`, `mslp` decoders; `DisplayLine`; vocabulary gaps; `AnsiEscapeParser` on the new tokenizer | `ILineParser` adapter over the decoder that still yields `StyledLine`, behind a setting | — | Plain-text frames decoded instead of ANSI-stripped |
| 2 | `mxp`, `pueblo` decoders (ported), `ClientCapabilities` | Switch to the adapter; delete the three parsers | Replace `AnsiParser`/`MxpParserState` with the decoder | — |
| 3 | `DisplayStyle`, contributors, `SpanRenderer`, `CellRenderer` | `sharpconsoleui` emitter replaces `MarkupFormatter`; window output panes on `CellRenderer` | `OutputView` on `SpanRenderer` | Optionally the same `SpanRenderer` |
| 4 | `vt-screen` | Opt-in per world | — | — |

Phase 1 changes nothing a client sees and can ship on its own.

## Package shape

| Package | Adds |
|---|---|
| `MarkupString` | Decoder abstractions, `VtTokenizer`, `TagTokenizer`, `DisplayLine`, `DisplayStyle`, `IStyleContributor`, `OutputCapabilities`, `CellRenderer`, `SpanRenderer`, new vocabulary records |
| `MarkupString.Ansi` | `vt`, `osc8`, `mslp`, title, palette, picture decoders; `AnsiMarkup` style contributor |
| `MarkupString.Mxp` | `mxp` decoder, `<SUPPORTS>`/`<VERSION>` answers |
| `MarkupString.Pueblo` | `pueblo` decoder |
| `MarkupString.Html` | `html` decoder through `HtmlTagPolicy`; `HtmlMarkup` style contributor |
| `MarkupString.Blazor` (optional) | `<DisplayLineView>` |

The alternative is one new `MarkupString.Display` package holding the decoders and renderers. It keeps the
existing packages untouched, but splits each protocol across two packages and makes `WithMxp()` mean only half of
MXP. The table above is the recommendation.

## Open questions

1. Is the screen tier in scope for the first version, or only lines?
2. Should the SharpConsoleUI emitter live in SharpMUTerm or in a MarkupString package?
3. Is porting SharpMUTerm's MXP and Pueblo parsers into MarkupString acceptable, given SharpMUTerm is MIT and
   MarkupString is Apache-2.0? Both are permissive and have the same owners, but the notice has to come along.
4. Should `CellRenderer` and `SpanRenderer` sit in core, or does core stay free of any renderer and take them in
   a new package?
