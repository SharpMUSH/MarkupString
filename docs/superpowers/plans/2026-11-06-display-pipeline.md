# Display pipeline: conversion plan

**Goal:** Every protocol a game sends (ANSI and its extensions first, then MXP and Pueblo) reaches every output a
SharpMUSH client draws on (an outer terminal, a cell grid, web components, an HTML string) as the best thing that
output can show, through one decoder and one model.

**Spec:** `docs/superpowers/specs/2026-11-06-display-pipeline-design.md`. This plan works out the conversions the spec
leaves open, in particular pictures, and orders the work.

**Status:** draft for review, 2026-11-06.

## 1. The rule: decode, never pass through

The tempting shortcut for "Kitty in, Kitty out" is to copy the sequence to the outer terminal. That breaks:

| Why passthrough fails | Where it shows |
|---|---|
| Image ids collide. The server numbers its images; so does the client (SharpMUTerm hands out ids from `0xC00000`, and SharpConsoleUI numbers its own) and so does `TerminalPictureWriter.KittyId`. | A game's image 1 replaces the client's map. |
| A placement is drawn at the server's idea of the cursor. The client's cursor is in a pane, in a layout, under a header. | The picture lands on the input line or a sidebar. |
| Scrollback reflows. A pane resized after the picture arrived moves the text but not a placed picture. | Pictures float over the wrong lines. Only Unicode placeholders survive this, because they are text. |
| The outer terminal is not the one the server was told about. The client advertised its own capabilities; the outer terminal may be a different one, or tmux. | Sixel printed as garbage in Alacritty. |
| `t=f`, `t=t` and `t=s` tell the terminal to read a local file or shared memory object. Passed through, the server reads the player's disk. | Security hole. |
| Queries (`a=q`, DA1, `CSI 16 t`) are answered by the outer terminal, but its reply arrives on the client's input stream, which SharpConsoleUI owns. | Replies lost, or typed into the command line. |

So every sequence ends at the decoder, becomes model, and each target writes its own output from the model. The
one optimisation allowed is *encoded passthrough*: a picture that arrived as PNG bytes can be sent to an output
that takes PNG (Kitty `f=100`, iTerm2, a browser) without decoding the pixels. That reuses the bytes; it never
reuses the sequence.

```text
 source sequence ──► decoder plugin ──► model (MarkupText + PictureStore + events) ──► target encoder ──► output
   Kitty APC          kitty                ImageMarkup{Row} over blank cells             Kitty placeholders
   sixel DCS          sixel                + PictureAsset(key, pixels | bytes | url)     sixel / iTerm2 / ▀▄
   MXP <IMAGE>        mxp                                                                Skia bitmap
   …                                                                                     <img src=blob/data/url>
```

## 2. Targets

Five targets. They differ in who owns the screen and what they can draw, and a client may use more than one
(SharpMUTerm has T1 and T3; the portal has T5).

| Id | Target | Example | Owns interaction | Pictures as | Colour |
|---|---|---|---|---|---|
| T1 | Outer terminal, client owns the screen | SharpMUTerm TUI inside kitty, Windows Terminal | client (mouse reporting) | Kitty placeholders, iTerm2 inline, sixel, half blocks, chosen by `TerminalProfile` | by profile |
| T2 | Dumb ANSI sink | a log file, `cat` of a capture, a proxy to a telnet user | the terminal, if anything | as T1 but only what that terminal reads, usually none | `AnsiColorDepth` |
| T3 | Cell grid drawn by the client | SharpMUTerm window (`CellGrid` + Skia) | client (hit test) | bitmap slices in cells | truecolor |
| T4 | Web components | SharpClient `OutputView` | client (`@onclick`) | `<img>` from URL, blob or data URI | truecolor via CSS |
| T5 | HTML string | SharpMUSH portal `TerminalLine.Html` | page script (`xch_cmd`) | `<img>` as T4 | truecolor via CSS |

T2 is MarkupString's existing `MarkupFormat.Ansi` output and needs nothing new beyond the picture source.

## 3. Conversion tables

Each cell says what the target receives. "Text" means the fallback text the decoder kept under the markup: alt
text for a picture, the label for a link. Nothing in a cell is ever dropped without leaving its text.

### 3.1 Pictures

| Source | T1 Kitty | T1 iTerm2 / sixel | T1 blocks only | T2 | T3 | T4 / T5 |
|---|---|---|---|---|---|---|
| Kitty transmit and display (`a=T`, direct `t=d`, `f=24/32/100`, `o=z`, chunked `m=1`) | placeholders, retransmitted once under the client's id | encoded by `TerminalPictureWriter` | half blocks | as T1 for its profile | bitmap | PNG (encoded passthrough for `f=100`, `PngWriter` for raw) |
| Kitty transmit (`a=t`) then put (`a=p`, `i`/`p`, `c`/`r`) | as above, at the put | as above | as above | as above | as above | as above |
| Kitty virtual placement (`U=1`) plus `U+10EEEE` placeholder text | placeholder cells re-keyed to the client's id; the picture is already text-shaped | the same rows, drawn | half blocks | as T1 | bitmap slices | `<img>` over the rows' box; a cropped placement is a CSS `object-position` crop |
| Kitty animation (`a=f` frames, `a=a`) | frames, if `MovingPictures` | iTerm2 GIF if `MovingPictures`; else first frame | first frame | as T1 | frames (host animates) | animated GIF via `GifWriter`, or first frame |
| Kitty delete (`a=d`, lower or upper case) | see [3.1.3](#313-delete-and-replace) | | | | | |
| Kitty query (`a=q`) | answered per [section 5](#5-what-the-client-answers) | | | | | |
| Kitty `t=f`, `t=t`, `t=s` | refused with an error reply; nothing read | | | | | |
| Sixel (`DCS P1;P2;P3 q … ST`) | decoded to pixels, then as Kitty direct | iTerm2 PNG, or sixel re-encoded at the client's size and palette | half blocks | as T1 | bitmap | PNG |
| iTerm2 inline (`OSC 1337 ; File=inline=1;… : base64`, single or `MultipartFile`/`FilePart`/`FileEnd`) | Kitty `f=100` when PNG, else decoded | iTerm2: original bytes; sixel: decoded | half blocks | as T1 | bitmap (host decodes) | original bytes as blob or data URI, when the browser reads the type |
| iTerm2 download (`inline=0`) | never saved; `DownloadOffered` event with name and size, text "[file: name]" | | | | | |
| MXP `<IMAGE>` (URL + FName, `W`/`H` in px, `c` or `%`, `ALIGN`) | fetched by the host, decoded, then as Kitty direct | as left | half blocks | text | bitmap | `<img src>` with the address itself, after `UrlSafety`; no fetch by the client code |
| Pueblo / HTML `<img src>` | as MXP | as MXP | as MXP | text | bitmap | as MXP |
| Half-block, quadrant, sextant or braille art (plain text) | text | text | text | text | text; T3 draws block elements as shapes, as `CellRenderer` already does | text in a monospace span |

Text art is never upgraded to a picture. It is text the server chose; its colours go through the colour table.

#### 3.1.1 Where a picture sits in a line model

All five targets are line-oriented scrollback, not a fixed screen. A picture placed at the cursor becomes rows of
blank cells under `ImageMarkup { Row = PictureRow(i, rows, columns) }`, which is how MarkupString's `Figure` already
reserves space, so every target that can draw it draws it and every other one shows the cells' text.

| Source placement | Becomes |
|---|---|
| Cursor at the start of a line, picture `r` rows tall | `r` lines, each `columns` blank cells under the picture's row `i` |
| Cursor mid-line at column `x` (Kitty `C=0`: the spec moves the cursor right by `c` and down by `r`) | the text before it ends its line; then the `r` rows; the cursor is then column `x + c` of a new line below them, so text that follows starts there after blank cells, and a line with nothing written stays empty, as it would on kitty's screen |
| Kitty `C=1` (cursor does not move) | as above, but on kitty the text that follows overprints the picture; a line model cannot overprint, so it goes below the picture and the line gets an `Overlapped` flag |
| Sixel (cursor ends on the line below the picture, or to its right with private mode 8452) | below: a new line at column 0. Right of the last row (8452): text there would sit beside the picture, which the line model cannot hold, so it also starts a new line, with the `Overlapped` flag |
| Unicode placeholders | already cells: each run of `U+10EEEE` becomes blanks under `ImageMarkup.Row`, with its row and starting column read from the diacritics |
| MXP / Pueblo image in running text | as "cursor mid-line"; an image with no size until fetched is a one-row label (its alt text) until the host has the size, then a `PictureSized` event lets the client re-lay that line |

The last row is what SharpMUTerm already does with `InlineImageHost` (rows inserted under the line once the
picture is fetched); the plan moves that logic into the shared picture service.

`PictureRow` needs one new field for placeholders that show part of a picture: `FirstColumn`, the column of the
picture the run starts at (Kitty lets a placement be cropped, and a run of placeholders can start at any
column).

#### 3.1.2 Sizing

Cells are what matter in every target but T4/T5, and the source and the target can disagree about how many pixels
a cell is.

1. A size the source gave in cells (Kitty `c`/`r`, iTerm2 `width=N`, MXP `W=Nc`) is kept: the server laid text
   out around it.
2. Else a size in pixels (Kitty `s`/`v` or the image's own, iTerm2 `Npx`, MXP `W=N`) is turned into cells with
   the *target's* cell metrics.
3. A percentage (iTerm2 `N%`, MXP `W=N%`) is a share of the pane's columns, or of a row cap for height, as
   SharpMUTerm's `InlineImageLayout` does.
4. The result keeps the picture's aspect unless the source said otherwise (iTerm2 `preserveAspectRatio=0`, MXP
   both `W` and `H`), then shrinks to fit the pane width and a maximum height (SharpMUTerm uses 24 rows).

Cell metrics, per target: T1 asks the outer terminal (`CSI 16 t`) where the host can read the reply, else assumes
8×16, the 1:2 ratio SharpMUTerm chose because it keeps a square picture square under both Kitty and half blocks.
T3 takes them from its font. T4/T5 measure one monospace cell in CSS and fall back to 8×16; the picture itself
is sized in CSS pixels with `max-width: 100%`.

Pixels are never stretched past the source's resolution unless the source asked for a larger box.

#### 3.1.3 Delete and replace

A terminal is one screen; a client has scrollback. Kitty's delete means "take it off the screen", and for a
history that is ambiguous. The decoder raises `PictureWithdrawn(key, freeData)` and the client chooses:

| Policy | Effect | Default for |
|---|---|---|
| `KeepInHistory` | old lines keep the picture; uppercase delete only frees it when the store's byte budget needs the room, after which old lines show their text | all clients |
| `ReplaceInPlace` | a new picture under the same server id and placement id replaces the old one wherever it is shown | a pane or status area, later |
| `Remove` | lines that showed it show their text from now on | opt-in |

The store never reuses a key. Retransmitting under the same server id gives a new generation
(`{connection}/kitty/{id}/{generation}`), so caches and Kitty ids derived from the key cannot show stale pixels.

#### 3.1.4 Decoding and fetching

MarkupString stays free of image libraries. It ships what the terminal protocols need, and the host supplies the
rest:

| Format | Who decodes | Note |
|---|---|---|
| Raw RGB/RGBA (Kitty `f=24/32`), zlib (`o=z`) | MarkupString (`System.IO.Compression`) | inflate is capped at `s × v × bytes-per-pixel` |
| PNG | MarkupString, new small decoder | Kitty `f=100` is the common case; PNG is fully specified and needs only zlib |
| GIF | MarkupString, new decoder next to `GifWriter` | for iTerm2 and animation |
| Sixel | MarkupString, new decoder next to `SixelWriter` | HLS and RGB colour registers, repeat, raster attributes, `P2=1` transparent background |
| JPEG, WebP, anything else | host `IPictureDecoder` | SharpMUTerm: StbImageSharp plus ImageSharp's WebP module (its `ImageDecoder`); Avalonia: Skia; browser: not needed |

Header sniffing (PNG `IHDR`, GIF logical screen, JPEG `SOF`, WebP `VP8`/`VP8L`/`VP8X`) gives the size without
decoding, so a line can be laid out at once and the pixels decoded off the parse thread.

Fetching a URL is always the host's (`IPictureFetcher`), because it reveals the player's address and needs the
rules SharpMUTerm's `WebImageLoader` has: http(s) and `data:` only, an image content type, a byte cap, a timeout,
two concurrent fetches, a queue cap.

#### 3.1.5 The picture store

```csharp
public sealed class PictureAsset
{
    public string Key { get; }                         // never reused
    public PictureOrigin Origin { get; }               // Inline (bytes arrived in band) or Address (a URL)
    public string? Address { get; }
    public PictureSize? NaturalSize { get; }           // pixels, from the header or the source
    public PictureState State { get; }                 // Declared, Received, Decoded, Failed, Released
}

public interface IPictureStore : ITerminalPictureSource // so the existing ANSI writers draw from it unchanged
{
    PictureAsset Declare(PictureDeclaration declaration);
    bool TryGetEncoded(string key, out ReadOnlyMemory<byte> bytes, out string mediaType);
    bool TryGetPixels(string key, [NotNullWhen(true)] out TerminalPicture? picture);
    event Action<PictureAsset>? Changed;               // Decoded, Sized, Released: the client repaints
}
```

One store per session, with an LRU byte budget for pixels and one for encoded bytes, and one picture larger than
the budget used once and not kept, as `InlineImageHost` does. A decoded in-band picture's `ImageMarkup.Source` is
`picture:{key}`; MarkupString's HTML emitter currently writes `Source` into `src` as is, so it gains an
`IPictureAddressProvider` option that turns `picture:` keys into a blob URL or a data URI, and refuses to write a
`picture:` address it cannot resolve.

### 3.2 Colour

| Source | T1 / T2 | T3 | T4 / T5 |
|---|---|---|---|
| SGR 30–37, 90–97, 40–47, 100–107 | kept as standard colours, so the outer terminal's own palette shows them | theme palette (`IColorResolver`) | theme palette as CSS variables (`AnsiCss`) |
| `38;5;n`, `48;5;n` | at the profile's depth (`AnsiStyle.AtDepth`) | resolved through the xterm table | resolved |
| `38;2;r;g;b` and `38:2::r:g:b` | at the profile's depth (redmean nearest) | as given | as given |
| SGR 39, 49 (default) | default | the theme's default | the theme's default |
| OSC 4 (set palette entry), OSC 104 (reset) | the connection's palette override; standard colours that it redefines are written as RGB so the outer terminal's palette is not changed | override applied in the resolver | override applied |
| OSC 10/11/12 set default fg/bg/cursor | ignored by default (a player's theme wins), opt-in per world | | |
| Contrast floor | — | `Contrast.Legible` against the plane, as SharpMUTerm's `MarkupFormatter` does today | same function, so all clients agree |

A palette change never touches the outer terminal. That is the server reaching past the client into the player's
terminal, which the client exists to mediate.

### 3.3 Attributes

| SGR | T1 / T2 | T3 | T4 / T5 |
|---|---|---|---|
| 1 bold, 2 faint, 3 italic, 9 strike, 53 overline | as is, if the profile has them | font weight/style, decorations | CSS |
| 4 underline; `4:2` double, `4:3` curly, `4:4` dotted, `4:5` dashed; 21 double | the kind if the profile has it, else 4 | decoration kind | `text-decoration-style` |
| 58/59 underline colour | if supported, else dropped | decoration colour | `text-decoration-color` |
| 5/6 blink | the player's setting (SharpMUTerm "allow blink"), default off | same | `ms-blink` class, off unless the page turns it on |
| 7 inverse | inverse | colours swapped at resolve time | colours swapped at resolve time |
| 8 conceal | conceal | foreground drawn as background; text still selectable and searchable | `color: transparent` |
| 22–29, 55 off codes | as given | | |

### 3.4 Links and commands

| Source | T1 | T2 | T3 | T4 | T5 |
|---|---|---|---|---|---|
| OSC 8 URL (with `id`) | client hit test; also OSC 8 to the outer terminal when the profile has `Hyperlinks`, so its own hover and open work | OSC 8 if `Hyperlinks`, else text | interaction in the cell table, grouped by `id` for hover | `<a href>` after `UrlSafety` | `<a href>` |
| MXP `<A HREF>`, Pueblo `<A HREF>`, HTML `<a>` | as OSC 8 | as OSC 8 | as OSC 8 | as OSC 8 | as OSC 8 |
| MXP `<SEND>`, Pueblo `XCH_CMD`, MSLP (`OSC 68`) | client hit test sends the command; never written as MSLP to the outer terminal, which would send it nowhere | MSLP if the sink advertised it, else text | interaction | `<button>` | `ms-cmd-link` with `xch_cmd` |
| MXP `<SEND PROMPT>` | puts the text in the command line | text | interaction | `<button>` that fills the input | as T4 |
| MXP `<SEND href="a|b">` menu | context menu | first choice, or text | menu | menu | `ms-cmd-menu` with the choices as data |
| MXP `<EXPIRE>` | links in that group stop being clickable and keep their text | | | | |
| A command link from an MXP open line | never made: it is literal text (the security rule in the spec) | | | | |

### 3.5 Sound and attention

| Source | T1 | T2 | T3 | T4 / T5 |
|---|---|---|---|---|
| BEL | `BellMarkup` point; the client's attention setting decides, and BEL reaches the outer terminal only if the player allows it | BEL | client's setting | `ms-bell` |
| MXP `<SOUND>`, `<MUSIC>`, Pueblo `xch_sound` | host audio (SharpMUTerm.Audio) through `IPictureFetcher`'s rules for the file | nothing | host audio | `<audio preload="none">` |
| MSP `!!SOUND(…)` text triggers | not decoded in v1; a later plugin can claim the text | | | |

### 3.6 Screen controls

These decide whether the line model holds. Everything here is decoded, and nothing is forwarded.

| Source | Line model (default) | Screen tier (opt-in, later) |
|---|---|---|
| LF | ends the line (the telnet layer usually ended it already) | cursor down |
| CR without LF | column 0 of the current line; following text overwrites cells, so a progress bar redraws in place | as a terminal |
| BS | one cell left; next character overwrites (`a BS _` overstrike is not turned into underline) | as a terminal |
| HT | kept as a tab; renderers expand to 8 with `TextLayout` | tab stops |
| FF, `CSI 2 J`, `CSI H CSI J` | `ClearScreenMarkup` point; the client decides (the portal already styles `ms-clear`) | clear |
| `CSI K` (erase in line) | clears the current line from the cursor, in the line builder | as a terminal |
| Cursor moves `CUU/CUD/CUF/CUB/CUP`, `DECSTBM`, `?1049h` alternate screen | dropped, as all three clients do today; counted in diagnostics | drive a `ScreenBuffer` |
| `ESC ( 0` / `ESC ) 0` DEC special graphics, `SO`/`SI` | mapped to Unicode box drawing, so old MUD maps draw | same |
| OSC 0/2 title | `TitleChanged` event; never forwarded | same |
| OSC 52 clipboard | `ClipboardRequest` event; refused unless the player allows that world | same |
| DCS/APC/PM/SOS nobody claimed | consumed, capped, dropped | same |

### 3.7 Structure

| Source | Model | Targets |
|---|---|---|
| Telnet GA / EOR (from the host) | `DisplayLine.Kind = Prompt` | SharpMUTerm prompt row, portal prompt row |
| MXP `<FRAME>` / `<DEST>`, Pueblo `xch_pane` | `PaneMarkup` / `DisplayLine.Destination`, `FrameOpened` events | client pane; a target without panes shows the text in the main view |
| MXP room tags (`ESC [10z`–`12z`, `<RName>`, `<RDesc>`, `<RExits>`) | `RoomPartMarkup` data layer | invisible; read by SharpMUTerm's `MxpRoomSource` |
| MXP `<VAR>`, `<GAUGE>`, `<STAT>`, `<!ENTITY>` | `VariableMarkup`, `GaugeMarkup`, `StatusMarkup`, `VariableSet` events | client gauges; else text |
| MXP `<RELOCATE>`, `<USER>`, `<PASSWORD>` | the existing vocabulary points, raised as events | the client asks the player; never automatic |

## 4. Capabilities

```csharp
public sealed record OutputCapabilities
{
    public AnsiColorDepth Colors { get; init; }
    public TextAttributes Attributes { get; init; }
    public UnderlineKinds Underlines { get; init; }
    public InteractionKinds Interactions { get; init; }    // Url, Command, Prompt, Menu
    public PictureMethods Pictures { get; init; }          // Bitmap, KittyPlaceholders, InlineImage, Sixel, HalfBlocks
    public bool MovingPictures { get; init; }
    public bool Sound { get; init; }
    public bool Panes { get; init; }
    public UnicodeLevel Unicode { get; init; }             // Full, NoWide, Ascii
    public CellMetrics? Cell { get; init; }

    public static OutputCapabilities FromTerminal(TerminalProfile profile, TerminalFeatures chosen, AnsiColorDepth depth);
    public static OutputCapabilities CellGrid { get; }     // T3
    public static OutputCapabilities Browser { get; }      // T4, T5
}
```

`FromTerminal` maps `TerminalFeatures` one to one (`KittyGraphics` → `KittyPlaceholders`, and so on), so the
terminal knowledge already in `TerminalProfile` is the only place it lives. A client that shows output in two
places at once (a T1 pane and a T3 window over the same session) negotiates with the server on the union and
renders each target with its own capabilities; the decoder's output is the same for both.

`Effective = output ∩ what the decoder can read ∩ what the player turned on`. Pictures count as available when the
output can draw *some* picture method better than text, because the decoder transcodes: a client in Alacritty
(half blocks only) can still advertise Kitty input, receive a picture and show it as half blocks. Whether
half blocks are worth asking for is the player's choice (`PictureFloor`: off, half blocks, native only).

## 5. What the client answers

A server learns what to send by asking. The decoder answers as the virtual terminal the client presents, built
from `Effective`, and sends nothing a host has not turned on.

| Server sends | Answer | Built from |
|---|---|---|
| Telnet TTYPE cycle | client name, then a terminal name, then `MTTS n` | `ToMtts`: 1 ANSI, 2 VT100, 4 UTF-8, 8 256 colours, 32 OSC colour palette, 64 screen reader, 256 truecolor, 1024 MSLP, from `Effective`; sent by the telnet layer |
| MXP `<SUPPORT tags>` | `<SUPPORTS +B +COLOR -IMAGE …>` on a secure line | the MXP plugin's handlers ∩ `Effective` (no `+IMAGE` without pictures) |
| MXP `<VERSION>` | `<VERSION MXP=1.0 STYLE=… CLIENT=… VERSION=…>` | host strings |
| Kitty `a=q` (and feature query `e=`) | `OK` for `t=d` with the formats the store decodes; error for `t=f/t/s`; nothing when `q=2`, no `OK` when `q=1` | `Effective.Pictures` and the decoder list |
| DA1 `CSI c` | `CSI ? 62 ; 22 c`, plus `; 4` when sixel input is on | decoder list |
| XTVERSION `CSI > q` | `DCS > | ClientName Version ST` | host strings |
| `CSI 14 t`, `16 t`, `18 t` | text area pixels, cell pixels, rows and columns of the pane | target cell metrics and pane size |
| `CSI 6 n` (cursor position) | the pane's last row, the current line's column + 1 | line builder |
| OSC 10/11 `?` | the theme's default foreground/background as `rgb:rrrr/gggg/bbbb` | `IColorResolver` |
| Pueblo `This world is Pueblo …` | `PUEBLOCLIENT` with the md5, by the telnet layer | host |

Answers leave through `DecodeEvent.ReplyToServer`; the decoder never writes to the socket. Each answer has a
switch, and the defaults are on for MXP, Kitty and the size queries and off for cursor position, since a server
that relies on it assumes a full screen.

## 6. Security limits

Defaults, all configurable in `DecodePolicy`:

| Limit | Default | Why |
|---|---|---|
| CSI parameters | 32 | `vte`'s limit |
| OSC payload, other than pictures | 4 KiB | OSC 8 URIs are capped at 2083 bytes by VTE and iTerm2 |
| One picture, encoded | 16 MiB | a large PNG; iTerm2 caps one sequence at 1 MiB, mintty at about 4.4 MB |
| One picture, decoded | 4096 × 4096 and 64 MiB of pixels | a zlib or PNG bomb stops at the cap |
| Chunked transfers open at once | 4 | a server cannot hold memory with half-sent images |
| Pictures per line | 8 | |
| MXP element stack, definitions, entity depth | 64, 512, 8 | |
| Tag, entity, escape sequence | 4096, 32, 128 chars | SharpMUTerm's current caps |

Every limit hit drops the item, keeps its text, and counts it in diagnostics, so a player can tell why a picture
did not show.

## 7. Work plan

Each task lists what it produces and when it is done. Tests are TUnit in `MarkupString.Tests`; new public API goes
in `PublicAPI.Unshipped.txt`; each phase ends with a `CHANGELOG.md` entry and a minor version.

### Phase 0: corpus and harness

- **0.1 Capture corpus.** A folder of real sessions as raw text with line and prompt boundaries marked: PennMUSH,
  TinyMUX, a Diku with MXP, a Pueblo world, a world sending Kitty or sixel (or a synthetic one), and the existing
  fixtures from SharpMUTerm's and SharpClient's parser tests. Done when each file has a short note of where it came
  from and what it exercises.
- **0.2 Snapshot harness.** Feeds a corpus file through a decoder and writes approved snapshots of the decoded
  lines (as MarkupString JSON), of cells, and of spans. Done when a changed snapshot fails the build.
- **0.3 Chunking and fuzz harness.** Every case run whole and split at every index; a fuzzer over random bytes and
  mutated corpus. Done when it runs in CI with a time budget.

### Phase 1: VT decoding (MarkupString, MarkupString.Ansi)

- **1.1 `VtTokenizer`** (Williams state machine, span based, stateful across `Feed`, `:` subparameters, every cap
  in section 6, newline aborts a sequence). Tests: the state table, chunking, fuzz.
- **1.2 Decoder core**: `DecoderRegistry`, `DecoderBuilder`, `DecodeContext`, newest-first dispatch with
  fall-through, `DecodePolicy`, `DecodeEvent`, `DisplayLine`, `LineBuilder` with CR overwrite, BS, `CSI K`, `EndLine`,
  `EndPrompt`.
- **1.3 `vt` plugin**: full SGR, C0, DEC special graphics and `SO`/`SI`, clear screen as a point, title and
  palette events, OSC 52 event.
- **1.4 `osc8` and `mslp` plugins**, with `AnsiStyle.LinkId`.
- **1.5 Vocabulary gaps** from the spec (conceal, underline kinds and colour, `LinkKind.Prompt`,
  `CommandMenuMarkup`, `RoomPartMarkup`, `ElementFlagMarkup`, `HighlightMarkup`), with codecs, emitters where a
  format has a spelling, and the JSON round trip.
- **1.6 `AnsiEscapeParser.Parse`** on the decoder, its tests unchanged and passing.
- **1.7 Round-trip property**: `Decode(Render(x, Ansi)) ≈ x` for generated `MarkupText`.

Done when SharpMUTerm's `AnsiParser` tests and SharpClient's SGR tests, ported, pass against the decoder.

### Phase 2: capabilities and answers

- **2.1 `OutputCapabilities`** with `FromTerminal`, `CellGrid`, `Browser`; `Effective`.
- **2.2 Answer builders**: `ToMtts`, DA1, XTVERSION, window and cell size, OSC 10/11, cursor position, each behind
  a switch, raised as `ReplyToServer`.

Done when a table test checks every answer in section 5 for three profiles (kitty, Alacritty, browser).

### Phase 3: structured renderers (MarkupString, MarkupString.Ansi, MarkupString.Html)

- **3.1 `DisplayStyle`, `IStyleContributor`, `IColorResolver`**, contributors for `AnsiMarkup`, `HtmlMarkup` and
  the vocabulary; palette override from OSC 4.
- **3.2 `SpanRenderer`** producing `DisplaySpan`s with `ms-*` classes.
- **3.3 `CellRenderer`** (`Cell`, `ICellSurface`, layout through `TextLayout`, interaction table, hit test).
- **3.4 Parity test**: one line as cells and as spans gives the same text, style runs and interaction ranges, over
  the corpus.

### Phase 4: pictures, model and outputs

- **4.1 `PictureAsset`, `IPictureStore`** (budgets, keys, generations, `Changed`), implementing
  `ITerminalPictureSource`.
- **4.2 Decoders**: raw, zlib, PNG, GIF, header sniffers; `IPictureDecoder` and `IPictureFetcher` for the host.
- **4.3 Sizing** (section 3.1.2), ported from `InlineImageLayout` with its tests.
- **4.4 `PictureRow.FirstColumn`**, and the ANSI picture writers honouring it.
- **4.5 Target encoders**: encoded passthrough to Kitty `f=100` and iTerm2; `IPictureAddressProvider` in the HTML
  emitter and `SpanRenderer`; picture cells in `CellRenderer`.

Done when a picture declared by hand reaches T1 (each method), T3, T4 and T5 from the same `MarkupText`, in snapshot
tests.

### Phase 5: picture protocols in (MarkupString.Ansi)

- **5.1 `kitty` plugin**: actions `t`, `T`, `p`, `q`, `d`; `t=d` only; formats 24, 32, 100; `o=z`; chunking; ids
  remapped to store keys; replies; placement to rows (section 3.1.1); `PictureWithdrawn`.
- **5.2 Kitty placeholders in**: read `U+10EEEE` runs (image id from the foreground, third diacritic, placement id
  from the underline colour, row and column inference), turn them into `ImageMarkup.Row` cells.
- **5.3 `sixel` plugin**: decoder, placement, DA1 bit.
- **5.4 `iterm-image` plugin**: single and multipart, units, `inline=0` as `DownloadOffered`.
- **5.5 Kitty animation**: whole frames (`a=f` without composition) and `a=a` play and loop; anything else shows
  the first frame.

Done when each source row of table 3.1 has a snapshot test per target column.

### Phase 6: MXP and Pueblo in (MarkupString.Mxp, MarkupString.Pueblo, MarkupString.Html)

- **6.1 `TagTokenizer`** shared by all three.
- **6.2 `mxp` plugin**, ported from SharpMUTerm's `MxpParser` and `MxpParser.Definitions` with their tests: line
  modes, open and secure tags, definitions, entities, links and menus, frames, variables, gauges, room tags,
  `SUPPORT` and `VERSION`, `<IMAGE>` and `<SOUND>` through the picture store and fetcher.
- **6.3 `pueblo` plugin**, ported from `PuebloParser` with its tests.
- **6.4 `html` plugin** through `HtmlTagPolicy`.
- **6.5 Round trip** for `Mxp` and `Pueblo`, as 1.7.

### Phase 7: consumers

Each step is its own PR in that repository, behind a setting until the snapshots match.

- **SharpMUTerm**: an `ILineParser` adapter over the decoder yielding `StyledLine`, swapped in for `AnsiParser`,
  `MxpParser` and `PuebloParser` in `WorldSession`; then `InlineImageHost` on the picture store; then a SharpConsoleUI
  emitter replacing `MarkupFormatter`; then window output panes on `CellRenderer`. The three parsers are deleted
  once the adapter has shipped one release.
- **SharpClient**: decoder in place of `AnsiParser` and `MxpParserState`; `OutputView` on `SpanRenderer`;
  triggers on `DisplayLine` plain text.
- **SharpMUSH portal**: plain-text frames decoded instead of ANSI-stripped; `IPictureAddressProvider` for pictures
  that arrive in band.

### Phase 8: later

Kitty frame composition, the screen tier, MSP, MXP `ISMAP`, `ReplaceInPlace` panes.

## 8. Risks

| Risk | Mitigation |
|---|---|
| Ported MXP behaviour drifts from SharpMUTerm's during the move | port tests first; SharpMUTerm switches by setting and compares snapshots on the corpus before deleting its parser |
| Kitty id collisions between the client's framework, the client and the writer's hash | a configurable id range on the output side (`KittyIds`), as `InlineImageHost.FirstKittyId` does now |
| Re-laying a line when a fetched picture's size arrives moves the scrollback under the reader | only lines below the viewport's top are re-laid in place; a line above it keeps its label until scrolled to |
| Decoding pictures on the parse thread stalls text | sniff on the thread, decode off it; text is never held for a picture |
| Answering queries makes a server send things the player did not want | every answer is switchable and the picture floor is the player's |
| Line model cannot show a server that draws a screen | counted in diagnostics; the screen tier is the answer, opt-in |

## 9. Decisions needed

1. Default delete policy: `KeepInHistory` (recommended) or `Remove`.
2. Whether to answer `CSI 6 n` at all. Recommended off.
3. Picture floor default: half blocks allowed (recommended, it is what SharpMUTerm does in Alacritty today) or native
   only.
4. Whether MarkupString ships the PNG, GIF and sixel decoders (recommended, needed for Kitty `f=100` and sixel
   with no host library) or leaves all decoding to the host.
