# Decoding server output

A `Decoder` reads what a server sends (text with ANSI and VT sequences mixed in) and turns it into
`MarkupText` lines. Styling becomes markup runs, so a decoded line renders, measures, wraps and
serializes like any other `MarkupText`. Rendering a line for an output is covered in
[Display](display.md).

```csharp
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;

MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi();

// Once, at startup: which protocols to read.
var registry = DecoderRegistry.Empty.WithVt();

// One per connection: it holds that connection's style, half-read sequences and pending line.
var decoder = registry.CreateDecoder(new DecoderOptions
{
    Identity = new ClientIdentity { Name = "MyClient", Version = "2.1", Columns = 100, Rows = 40 },
});

var replies = new List<string>();   // send each of these to the server
decoder.EventRaised += e =>
{
    switch (e)
    {
        case ReplyEvent reply: replies.Add(reply.Text); break;
        case TitleEvent title: Console.WriteLine($"title: {title.Title}"); break;
    }
};

foreach (var line in decoder.Feed("\e]2;The Hall\a\e[1;33mWelcome\e[0m back.\r\nExits: north\e[c\r\n> "))
{
    Console.WriteLine($"{line.Sequence}: {line.Content.Render(MarkupFormat.Ansi)}");
}

// The telnet layer saw GA or EOR.
if (decoder.EndPrompt() is { } prompt) Console.WriteLine($"prompt: {prompt.Content}");
```

```
title: The Hall
0: \e[1;33mWelcome\e[0m back.
1: Exits: north
prompt: >
```

Output in this guide shows ESC as `\e` and BEL as `\a`. Events are raised while `Feed` reads,
before it returns the lines. The `\e[c` on the second line
asked for device attributes, so `replies` holds `\e[?62;22c`.

The registry is immutable and shared. A decoder is not safe to use from two threads at once.

## Lines

`Feed` returns the lines its text completed, which may be none. A line ends at a line feed in the
text. Whatever is left is the pending line (`PendingLine`, `HasPendingLine`), and a sequence cut in
half by a packet boundary is finished by the next `Feed`:

```csharp
var decoder = registry.CreateDecoder();
decoder.Feed("\e[3");             // no lines
decoder.Feed("1mred\n");          // \e[31mred\e[0m
decoder.Feed("still red\n");      // \e[31mstill red\e[0m
```

The style carries from line to line, as it does on a terminal. Each line is rendered closed, so it
stands alone in scrollback.

Two calls end a line for the host:

| Call | When | Returns |
|---|---|---|
| `EndLine()` | Your telnet layer strips line terminators and knows where a line ended | The line, even when empty |
| `EndPrompt()` | Telnet GA or EOR | The line with `Kind = LineKind.Prompt`, or null when nothing was read |

Both abandon a sequence still in flight. `Decoder.DecodeAll(text, registry)` decodes a whole string
at once and includes a last line that has no line break:

```csharp
foreach (var line in Decoder.DecodeAll("one\ntwo", registry))
    Console.WriteLine($"{line.Kind} {line.Content}");
```

```
Output one
Output two
```

A `DisplayLine` carries `Content`, `Kind`, `Sequence` (its number within the decoder, from 0, for
keying a view) and `Destination` (the pane it was sent to, or null for the main output).

`PendingLine` is the text read so far on a line that has not ended, as `MarkupText`, and
`OpenElements` lists the elements a text protocol has open, innermost last. A host that keeps state
for as long as an element stays open (an MXP room-name flag that spans several lines, say) checks
`OpenElements` after each line.

`Reset()` forgets the style, open elements, half-read sequences and the pending line. Set
`decoder.Identity` again when the pane is resized; answers read it each time.

## Events

Anything that is not text to show is raised through `EventRaised`, as it is found.

| Event | Raised for |
|---|---|
| `ReplyEvent(Text)` | An answer to a query. Send `Text` to the server exactly as it is. |
| `TitleEvent(Title, Icon)` | OSC 0, 1, 2. `Icon` is true for 0 and 1. |
| `PaletteEvent(Slot, Index, Color)` | OSC 4, 10, 11, 12 set a colour; 104, 110, 111, 112 reset one (`Color` null). |
| `ClipboardEvent(Selection, Text)` | OSC 52. `Text` is null when the server asked to read the clipboard. |
| `ProtocolEvent(Name, Active)` | A text protocol was turned on or off. |
| `VariableEvent(Name, Value)` | A server variable (MXP). |
| `GaugeEvent(Variable, Maximum, Caption, Color, Status)` | An MXP `GAUGE` (`Status` false) or `STAT` (`Status` true) over an entity. |
| `DownloadEvent(Name, Size)` | A file offered to save (iTerm2 `inline=0`). The decoder never saves it. |

The decoder acts on none of these. Refuse a clipboard write unless the player allowed it.

## What the terminal plugins read

`WithVt()` adds five plugins. `VtDecoding.Terminal` is a registry with just these.

### `SgrPlugin` (`CSI … m`)

| Codes | Effect |
|---|---|
| empty, `0` | Reset |
| `1`, `2`, `3` | Bold, faint, italic |
| `4`, `21` | Underline. `4:0` turns it off; `4:1` to `4:5` are all drawn as one underline. |
| `5`, `6` | Blink |
| `7`, `8`, `9` | Inverse, conceal, strikethrough |
| `22` | Neither bold nor faint |
| `23`, `24`, `25`, `27`, `28`, `29` | Turn off italic, underline, blink, inverse, conceal, strikethrough |
| `30`–`37`, `90`–`97` | Standard foreground, bright for 90–97 |
| `40`–`47`, `100`–`107` | Standard background, bright for 100–107 |
| `38`, `48` | Extended colour: `5;n`, `2;r;g;b`, and the colon forms `5:n`, `2:r:g:b`, `2::r:g:b`, `2:cs:r:g:b` |
| `39`, `49` | Default foreground, background |
| `53`, `55` | Overline on, off |
| `58` | Underline colour: its parameters are read and dropped |

Any other code is ignored. An extended colour out of range leaves the colour as it was, and so does one
with too few components, whose components are not read as codes of their own. The style
is written under the `VtPens.Style` pen as an `AnsiMarkup`.

### `ControlsPlugin`

| Input | Effect |
|---|---|
| BEL | A `BellMarkup` point |
| CR | Cursor to column 0 |
| BS | Cursor one cell left, stopping at 0 |
| HT | Cursor to the next multiple of 8 (`ControlsPlugin.TabStop`) |
| FF | A `ClearScreenMarkup` point |
| VT | Ends the line |
| `CSI K` | `0` erase to end of line, `1` blank to the cursor, `2` erase the line. Other values are not claimed. |
| `CSI J` | `2` or `3` writes a `ClearScreenMarkup` point. `0` and `1` are claimed and do nothing: a scrolling pane has no screen below the cursor. |
| `CSI G`, ``CSI ` `` | Cursor to column n (from 1) |
| `CSI C`, `CSI D` | Cursor n cells right, left |
| `ESC 7`, `ESC 8` | Save the cursor's column, and go back to it |
| `CSI A`, `ESC D`, `ESC M` | Claimed and ignored: a scrolling pane cannot go up a line, or down one without ending it |

A line feed always ends the line, with or without this plugin. DEL is dropped. C1 controls
(U+0080 to U+009F) are text, as xterm treats them in UTF-8. CAN and SUB cancel a sequence in flight.

### `CharsetPlugin`

`ESC ( 0` and `ESC ) 0` designate the DEC special graphics set into G0 or G1; `ESC ( B` and
`ESC ) B` put ASCII back. SO and SI shift between G0 and G1. While graphics is in force, `_` to `~`
become line drawing and symbols:

```csharp
Decoder.DecodeAll("\e(0lqqk\e(B ok", registry)[0].Content;  // ┌──┐ ok
```

### `OscPlugin`

| OSC | Effect |
|---|---|
| `0`, `1`, `2` | `TitleEvent`, control characters removed |
| `4;n;spec…` | `PaletteEvent` for each index and colour pair |
| `104` | Reset the listed entries, or every entry (`Index = -1`) when empty |
| `10`, `11`, `12` | Set the foreground, background, cursor colour, or answer `?` (10 and 11 only). `10;a;b` sets 10 and 11 at once. |
| `110`, `111`, `112` | Reset them |
| `52;sel;base64` | `ClipboardEvent` with the decoded text, or a null text for `?`. Invalid base64 is dropped. |
| `8;params;uri` | A link over the text that follows, until an empty uri. An address longer than 2083 characters, or one `DecoderOptions.Links` refuses (`UrlPolicy.Safe` unless set, which refuses `javascript:` and the like), ends the link instead. An `id=` among the params becomes a `LinkGroupMarkup`, and so `Interaction.Group`: the text under it is one link even across lines. |
| `68;1;SEND;command` | An MSLP command link over the underlined text that follows. Turning the underline off ends it, and so does the end of the line. |

Colours are X11 specs: `rgb:r/g/b` with one to four hex digits a channel, or `#rgb`, `#rrggbb`,
`#rrrgggbbb`, `#rrrrggggbbbb`. `OscPlugin.ParseColor` and `FormatColor` are public. Links are
written under the `VtPens.Link` pen, an OSC 8 `id` under `VtPens.LinkGroup`.

### `AnswersPlugin`

| Query | Reply | Allowed by |
|---|---|---|
| `CSI c`, `CSI 0 c` | `\e[?62;22c`, or `\e[?62;4;22c` when `Identity.Output.Pictures` has `Sixel` | `Identity` |
| `CSI > q` | `\eP>\|Name Version\e\\` (XTVERSION) | `Identity` |
| `CSI 5 n` | `\e[0n` | `Identity` |
| `CSI 14 t`, `16 t`, `18 t` | The pane in pixels, a cell in pixels, the pane in cells | `Size` |
| `OSC 10 ; ?`, `OSC 11 ; ?` | `Identity.Foreground`, `Background` as `rgb:rrrr/gggg/bbbb`, when set | `Colors` |
| `CSI 6 n` | `\e[{Rows};{column + 1}R` | `CursorPosition` |

A query whose answer is not allowed is still claimed, and nothing is sent.

## Overwriting

A carriage return or backspace moves the cursor back, and what is written next overwrites cells in
place. A progress bar redrawn over itself ends as its last state.

| Input | Line |
|---|---|
| `Loading 10%\rLoading 100%` | `Loading 100%` |
| `abcdef\rXY` | `XYcdef` |
| `abc\b\bX` | `aXc` |
| `hello world\r\e[Kbye` | `bye` |
| `a\tb` | `a       b` |
| `a\e[10Gb` | `a        b` |
| `漢字\rX` | `X 字` |

Moving the cursor past the end of the line pads with spaces only when text follows, so a tab at the
end of a line leaves no trailing blanks. The style of overwritten cells is replaced with the new text's. A wide character that is only partly
overwritten is blanked where it is not covered, as a terminal blanks it.

## Limits and diagnostics

Every buffer has a cap. `VtLimits` holds the tokenizer's:

| Property | Default | |
|---|---|---|
| `MaxParameters` | 32 | Parameters in a CSI or DCS header, subparameters included |
| `MaxParameterValue` | 65535 | Larger values are clamped |
| `MaxStringLength` | 16 MiB | An OSC or DCS payload |
| `MaxApcLength` | 64 KiB | An APC payload |

A sequence that overflows is read to its end and dropped. A line feed ends any sequence in flight,
so a bare `ESC ]` typed into a channel cannot swallow the rest of the session.

`DecoderOptions` holds the decoder's:

| Property | Default | |
|---|---|---|
| `MaxOpenElements` | 64 | Elements a text protocol may have open at once |
| `MaxLineLength` | 65536 | Characters in a line. A longer line is ended at the limit and the rest starts the next line. |
| `Links` | `UrlPolicy.Safe` | Which link addresses a server's links may have; any other is read as text |
| `Meters` | null | The `IMeterFactory` the diagnostics are reported to; null uses a shared meter |

`decoder.Diagnostics` counts what was dropped:

```csharp
var decoder = registry.CreateDecoder();
decoder.Feed("\e[?25lHello\e]0;never closed\nworld\u0001\n");
// AbandonedSequences = 1   the OSC cut off by the line feed
// UnhandledSequences = 1   CSI ? 25 l, which no plugin claims
// UnhandledControls  = 1   U+0001
```

`LimitsReached` counts lines broken at the limit and elements refused. `PicturesFailed` counts pictures that
could not be decoded. Each count is also reported as the metric `markupstring.decoding.dropped` on the
`MarkupString.Decoding` meter, tagged with its `kind`; see [Dependency injection](dependency-injection.md#metrics).

`DecoderRegistry` is an `IDecoderFactory`, so code that makes decoders can take the interface and a
test or host can give it another.

## Client identity and answers

`ClientIdentity` is what the decoder answers with: `Name`, `Version`, `Columns`, `Rows`,
`CellWidth` and `CellHeight` (8 by 16 by default), `Foreground`, `Background`, and `Output`, the
`OutputCapabilities` of where the session is shown.

`Answers` says which questions get an answer. `ClientAnswers.Default` is everything except
`CursorPosition`. A server that sends `CSI 6 n` assumes a full screen it can address, and a scrolling
pane is not one: the reply always gives the last row. Turn it on for a server you know uses it only
to find the column:

```csharp
var decoder = registry.CreateDecoder(new DecoderOptions
{
    Identity = new ClientIdentity
    {
        Answers = ClientAnswers.Default | ClientAnswers.CursorPosition,
        Background = new RgbColor(0, 0, 0),
    },
});
decoder.Feed("\e[c\e[>q\e[18t\e]11;?\a> \e[6n");
```

The replies, in order:

```
\e[?62;22c
\eP>|MarkupString 1.0\e\
\e[8;24;80t
\e]11;rgb:0000/0000/0000\e\
\e[24;3R
```

`ClientAnswers.None` answers nothing.

## MTTS

`ToMtts` gives the bits for the last step of the telnet TTYPE cycle, from `Output`:

| Bit | Set when |
|---|---|
| `Vt100`, `Utf8` | Always, but `Utf8` is cleared for `UnicodeLevel.Ascii` |
| `Ansi` | `Colors` is `Standard` or better |
| `Colors256`, `OscColorPalette` | `Xterm256` or better |
| `TrueColor` | `TrueColor` |
| `Mslp` | `Interactions` has `Command` |
| `ScreenReader`, `Ssl` | You pass `screenReader: true`, `tls: true` |

```csharp
var identity = new ClientIdentity { Output = OutputCapabilities.Browser };
identity.ToMtts(tls: true);        // Ansi, Vt100, Utf8, Colors256, OscColorPalette, TrueColor, Mslp, Ssl
(int)identity.ToMtts(tls: true);   // 3375, sent as "MTTS 3375"
```

The default `Output` is `OutputCapabilities.PlainText`, which gives only `Vt100, Utf8`.

## Writing a plugin

A plugin is an `IDecoderPlugin`: a name and a `Register` method that adds handlers to a
`DecoderBuilder`.

```csharp
sealed class CursorPlugin : IDecoderPlugin
{
    public string Name => "cursor";

    // A scrolling pane has no cursor to show or hide: claim CSI ? 25 h and CSI ? 25 l so they are not counted.
    public void Register(DecoderBuilder builder) => builder
        .OnCsi('h', static (in CsiSequence sequence, DecodeContext context) => sequence.Get(0) == 25, prefix: '?')
        .OnCsi('l', static (in CsiSequence sequence, DecodeContext context) => sequence.Get(0) == 25, prefix: '?');
}

var registry = DecoderRegistry.Empty.WithVt().With(new CursorPlugin());
```

| Method | Handles |
|---|---|
| `OnCsi(final, handler, prefix, intermediates)` | A control sequence |
| `OnEsc(final, handler, intermediates)` | An escape sequence |
| `OnOsc(number, handler)` | An OSC; the handler gets what follows the number and `;` |
| `OnDcs(final, handler, prefix, intermediates)` | A device control string |
| `OnApc(handler)` | Every APC, until one handler claims it |
| `OnControl(c, handler)` | A C0 control |
| `OnPrint(filter)` | Printable text before it is written; return true when the filter wrote it |
| `OnLineEnd(action)` | Every line end, after the active text protocol's `EndLine` |
| `TextProtocol(name, create)` | A protocol carried in the text, such as MXP |

`CsiSequence.Get(index, fallback)` reads a parameter, giving `fallback` when it is missing or empty.
`IsSubparameter(index)` says whether it followed a `:`. `Is(final, prefix)` tests the sequence's name.

`registry.With(plugin)` replaces a plugin of the same name. `Has(name)` and `Plugins` list what is
installed.

### Handler order

For one sequence, the newest handler is tried first: handlers from a plugin added later run before
an earlier plugin's, and within a plugin a later call runs before an earlier one. A handler returns
true to claim the sequence and false to pass it to the next older handler. A sequence nobody claims
is counted in `UnhandledSequences`.

This lets a plugin change one case of a sequence and leave the rest alone:

```csharp
sealed class BellCount
{
    public int Rung { get; set; }
}

sealed class QuietBellPlugin : IDecoderPlugin
{
    public string Name => "quiet-bell";

    // Added after WithVt, so it sees each bell before ControlsPlugin does.
    public void Register(DecoderBuilder builder) => builder.OnControl('\a', static (_, context) =>
    {
        var bells = context.State<BellCount>();
        bells.Rung++;
        return bells.Rung > 1;   // true swallows it; false lets ControlsPlugin write the bell
    });
}
```

Line end handlers are the exception: they all run, in the order they were added.

### Per-connection state

The plugin instance is shared by every decoder of the registry, so it holds no state of its own.
`context.State<T>()` gives this decoder's instance of `T`, made on first use and cleared by
`Reset()`. The host can read the same instance with `decoder.State<T>()`.

### Pens and elements

Text is written under the markup in force, which is built from two things:

- **Pens** are named slots a protocol sets and replaces: `context.SetPen(slot, markup)`, `Pen(slot)`,
  and `SetPen(slot, null)` to clear. SGR is a pen (`VtPens.Style`); so is a link (`VtPens.Link`).
- **Elements** are a stack a tag protocol opens and closes: `Open(markup)`, `Close(markup)` with
  the same instance, which also closes anything opened after it, and `CloseTo(count)`. `Open` returns
  false at `MaxOpenElements`.

Pens sit inside every element, so an SGR colour inside an element wins, as it does in an MXP client.

Besides `Print`, a handler can `Write` text as it is (skipping print filters), `Write(text, inner)`
with one more layer inside, `Point(point)`, move the cursor (`CarriageReturn`, `Backspace`,
`MoveToColumn`), erase (`EraseToEndOfLine`, `EraseToStartOfLine`, `EraseLine`), `EndLine()`, set
`Destination` for the lines that follow, `Raise(event)` and `Reply(text)`.

### Text protocols

A text protocol reads every piece of printable text while it is on, and prints what is literal with
`context.Print`. Only one is on at a time. A protocol that also implements `ISequenceObserver` is told
each time an escape sequence arrives while it is on; MXP uses this to spend a pending `ESC [ 4 z`.
`context.Insert(column, text)` puts text into the line being read at a display column. This one makes `*text*` bold:

```csharp
sealed class StarsPlugin : IDecoderPlugin
{
    public string Name => "stars";

    public void Register(DecoderBuilder builder) => builder.TextProtocol("stars", () => new StarsProtocol());
}

sealed class StarsProtocol : ITextProtocol
{
    private readonly IMarkup _bold = AnsiMarkup.Create(bold: true);
    private bool _open;

    public void Feed(ReadOnlySpan<char> text, DecodeContext context)
    {
        while (true)
        {
            var star = text.IndexOf('*');
            if (star < 0)
            {
                context.Print(text);
                return;
            }

            context.Print(text[..star]);
            if (_open) Close(context);
            else _open = context.Open(_bold);
            text = text[(star + 1)..];
        }
    }

    public void EndLine(DecodeContext context) => Close(context);

    public void Reset(DecodeContext context) => Close(context);

    private void Close(DecodeContext context)
    {
        if (_open) context.Close(_bold);
        _open = false;
    }
}
```

The registry offers the protocol; the decoder makes an instance the first time it is turned on,
typically after telnet negotiates it:

```csharp
var decoder = DecoderRegistry.Empty.WithVt().With(new StarsPlugin()).CreateDecoder();
decoder.EnableProtocol("stars");   // raises ProtocolEvent { Name = stars, Active = True }
decoder.Feed("a *bold \e[31mred\e[0m bold* plain *open\n")[0].Content.Render(MarkupFormat.Ansi);
```

```
a \e[1mbold \e[31mred\e[0m\e[1m bold\e[0m plain \e[1mopen\e[0m
```

The SGR reset inside the element ends the red pen, not the bold element. `EndLine` closed the
element left open, so the next line starts plain. `EnableProtocol` returns false when the registry
offers no protocol of that name; `ActiveProtocol` names the one that is on.

## Pictures

`WithPictures()` adds `PicturesPlugin`, which reads Kitty graphics (`APC G`), iTerm2 inline images
(`OSC 1337 File=`, and `MultipartFile=`/`FilePart=`/`FileEnd`) and sixel (`DCS q`). Each picture
becomes an `ImageMarkup` over the cells it covers, one `PictureRow` per line, so a cell renderer
draws it and the ANSI writer can send it on to another terminal in whatever way that terminal
draws pictures. Kitty to Kitty, sixel to Kitty, and iTerm2 to half blocks all go through the same
markup.

```csharp
var registry = DecoderRegistry.Empty.WithVt().WithPictures();
var decoder = registry.CreateDecoder(new DecoderOptions
{
    Identity = new ClientIdentity { CellWidth = 8, CellHeight = 16, Columns = 100 },
});

var lines = decoder.Feed(wire);
var store = decoder.Pictures();                       // this connection's PictureStore
foreach (var image in lines.SelectMany(l => l.Content.Runs).SelectMany(r => r.Markups).OfType<ImageMarkup>())
{
    if (store.TryGet(image, out var picture)) Console.WriteLine($"{picture.Width}x{picture.Height} {picture.MediaType}");
}
```

The pixels live in the decoder's `PictureStore`, and an image's `Source` is `picture:` and a key
(a hash of the picture, so the same picture sent twice is stored once). The store:

- keeps its pictures in an `IPictureCache`: by default a `MemoryPictureCache` of its own, which holds
  `Budget` bytes (64 MiB) and drops the least recently used first. A line whose picture is gone shows
  its text instead.
- shares one cache between connections with `WithPictures(cache)`, so a picture every player is sent
  is decoded and kept once. A file already in the cache is not decoded again. The cache can be your
  own (on disk, in Redis); it must be safe to call from several threads. A connection that adds
  frames to a picture does so on a copy under a key of its own, so the still picture other lines
  show does not change.
- refuses a picture of more than `MaxPixels` (4096 × 4096).
- is the `ITerminalPictureSource` to give `AnsiOutputOptions.Pictures`, so decoded pictures can be
  written to a terminal again.
- gives `StoredPicture.ToDataUrl()` for an `<img>`, and raises `Added` so a host can serve pictures
  from its own address instead. A picture already in a shared cache is not raised again.

```csharp
var cache = new MemoryPictureCache { Budget = 256L * 1024 * 1024 };   // one for the whole server
var registry = DecoderRegistry.Empty.WithVt().WithPictures(cache);
var decoder = registry.CreateDecoder();                               // per connection, same cache
```

PNG and sixel are decoded here, and Kitty's raw RGB and RGBA, zlib-compressed or not. A GIF, JPEG or
WebP sent as an iTerm2 image is kept as the file with no pixels: a browser shows it, a terminal gets
its text.

A picture's size in cells comes from its pixels and `ClientIdentity.CellWidth` and `CellHeight`,
unless the sequence says otherwise (Kitty `c` and `r`, iTerm2 `width` and `height` in cells, `px`
or `%`). It is never wider than `ClientIdentity.Columns`.

Placement follows the terminal: the first row beside the text already on the line, each further row
on its own line, the cursor below the picture afterwards. A picture drawn between `ESC 7` and
`ESC 8`, or Kitty's `C=1`, is drawn over the lines that follow instead, which is how a server writes
a picture beside text and how this package's own ANSI writer sends iTerm2 and sixel pictures.

Kitty specifics:

- Only `t=d` (the picture in the sequence) is read. A file, temporary file or shared memory is
  refused with `EINVAL`, since the server's files are not the client's.
- `a=t`, `a=T`, `a=p`, `a=q` and `a=d` are read, and so are animation frames. `a=f` adds a frame,
  or edits frame `r`: built on a copy of frame `c` (or a blank filled with `Y`), its data put at `x`,
  `y` and blended over what is there unless `X=1`, shown for `z` ms (40 unless given). `a=a` with `r`
  and `z` sets a frame's time. Playing, stopping and looping are left to the client, and so is
  composing (`a=c`), which is counted as unhandled. At most `PicturesPlugin.MaxFrames` (1000) frames
  are kept. A picture with frames is a `StoredPicture` whose `IsMoving` is true; its `Pixels.Frames`
  grow as frames arrive, and `Encoded` and `ToDataUrl()` give it as a looping GIF.
- Unicode placeholders (U+10EEEE with row and column diacritics, the image id in the foreground
  colour) draw a picture transmitted with `U=1`. That is how this package's writer sends Kitty
  pictures, so its output reads back cell for cell.
- Replies (`\e_Gi=…;OK\e\\`) follow `q` and need `ClientAnswers.Pictures`. A picture sent with
  neither `i` nor `I` gets none.

An iTerm2 file with `inline=0` is not shown: a `DownloadEvent` says it was offered. Nothing is saved.

## MXP

`MarkupString.Mxp` adds `WithMxp()` for a `DecoderRegistry`. MXP is a text protocol, off until the
host turns it on after telnet negotiates option 91:

```csharp
using MarkupString.Mxp.Decoding;

var registry = DecoderRegistry.Empty.WithVt().WithMxp(new MxpOptions { ShowRefusedTags = true });
var decoder = registry.CreateDecoder();
decoder.EnableProtocol(MxpDecoding.Protocol);

decoder.Feed("\e[1z<SEND href=\"buy sword|look sword\" hint=\"Sword|Buy|Look\">sword</SEND>\n");
```

Line modes (`ESC [ n z`) decide what a line may use. Until MXP is on they are claimed and ignored.

| Mode | |
|---|---|
| `0`, `1`, `2` | This line is open, secure or locked |
| `3` | Close every MXP element and go back to open |
| `4` | The next tag is secure. Any text, entity or escape sequence before it spends this; a second `ESC [ 4 z` arms it again. |
| `5`, `6`, `7` | Open, secure or locked from now on |
| `10`, `11`, `12`, `19` | This line is a room name, description, exits or welcome text: an `MxpFlagMarkup` over it |
| `20`–`99` | This line is the element the server tied to that number with `TAG=` |

Any other number in `10`–`99`, and `ESC [ z` with no number, changes nothing. Leaving open mode in the
middle of a line (modes `1`, `2`, `6`, `7` or a line tag) closes the elements the open part opened.

On an open line only the open tags work: `B`, `I`, `U`, `S`, `H`, `C`/`COLOR`, `FONT`, `BR`, `SBR`,
`P`, `NOBR`. A secure tag there is dropped and counted in `UnhandledSequences`. On a locked line `<`
and `&` are text. Elements opened on an open line close at its end, and `A` and `SEND` always end
with their line.

`MxpOptions` changes two things:

| Property | Default | |
|---|---|---|
| `ShowRefusedTags` | false | Show a refused tag (a secure tag or a definition on an open line, the close of a secure tag nothing opened) as the text it was, instead of dropping it |
| `Supports` | null | The tags and attributes `<SUPPORT>` is answered with, such as `{ ["send"] = ["href", "hint"] }`. Null answers with every tag the package reads. `Answer(asked)` gives the same answer to a host that answers MXP support itself. |

What the tags become:

| Tag | Markup |
|---|---|
| `B`, `I`, `U`, `S`, `H`, `COLOR`, `FONT` | An `AnsiMarkup`. SGR inside an MXP colour wins, as in an MXP client. |
| `A href hint` | A URL link, when `DecoderOptions.Links` allows the address |
| `SEND href hint prompt` | A command link. With several commands (`a\|b`) or `PROMPT`, a `CommandMenuMarkup` as well. With no `href`, or `&text;` in it, the command is the tag's text. One command takes the whole hint. With several, one more hint than commands makes the first the link's hint and the rest the menu labels; as many hints as commands are the labels alone. |
| `IMAGE fname url t w h align` | An `ImageMarkup` at `url`, then the `t` directory, then `fname`. Sized in cells (`W=4c H=2c`), it is drawn over the cells the server leaves blank for it; when those cells hold text, it goes inline at the tag instead. Sized in cells one way only, the other size is converted to pixels with the identity's cell size. |
| `SOUND`, `MUSIC` | `SoundMarkup` (with `Priority` from `P` and `Type` from `T`) or `SoundStopMarkup` points, at `u`, then `t`, then the file |
| `FRAME`, `DEST` | A `PaneMarkup` over the text in `DEST`, titled from its `FRAME`. `FRAME ACTION=REDIRECT` (or the `REDIRECT` flag) sends the lines after it to that frame through `DisplayLine.Destination`, until a redirect to `_top` or `_previous` or `FRAME … CLOSE`. |
| `GAUGE`, `STAT` | A `GaugeEvent` |
| `VAR name` | A `VariableMarkup` over its text, and a `VariableEvent` |
| `EXPIRE`, `RELOCATE`, `USER`, `PASSWORD` | Their points |
| `VERSION`, `SUPPORT` | Answered (`\e[1z<VERSION …>`, `\e[1z<SUPPORTS +send …>`) when `ClientAnswers.Mxp` allows. `<VERSION n>` from the server sets the `STYLE` the answer reports. |

`<!ELEMENT>` defines an element from other tags, with `ATT` attributes substituted for `&name;`,
`FLAG` for an `MxpFlagMarkup`, `OPEN`, `EMPTY` and `TAG`. `<!ENTITY>` defines an entity, used as
`&name;` and published as a `VariableEvent` unless `PRIVATE`. `<!ATTLIST>` and `<!TAG>` are read
too. To serialise lines with `MxpFlagMarkup`, add `MxpDecoding.FlagCodec` to the `MarkupRegistry`.

An element defined on an open line plays back as open text, so a server cannot launder a secure tag
through one; it plays back secure only when used on a secure line or by a line tag. Definitions are
bounded by `MxpPlugin` constants:

| Constant | Value | |
|---|---|---|
| `MaxElements` | 256 | Elements defined at once |
| `MaxEntities` | 512 | Entities defined at once |
| `MaxDefinitionLength` | 1024 | Characters in an element's definition or an entity's value |
| `MaxExpansionDepth` | 4 | Elements expanding inside each other (an element that uses itself stops here) |
| `MaxExpansionsPerLine` | 64 | Custom elements expanded on one line |

A definition past a limit is refused and counted in `LimitsReached`. The built-in tags and the entities
the decoder already knows (`lt`, `nbsp` and the rest) cannot be redefined. `<!ENTITY name value REMOVE>` takes `value`
out of a `|` list, and `DELETE` deletes the entity.

What `MarkupFormat.Mxp` writes reads back to the same markup.

## Pueblo

`MarkupString.Pueblo` adds `WithPueblo()`. Turn the protocol on when the server greets the client
(`PuebloDecoding.IsGreeting(line)`) and the client has answered `PUEBLOCLIENT`.

The server starts in MUD text, where tags are text. `</xch_mudtext>` or `<img xch_mode=html>`
switches to HTML, where a line ends at `<br>` and a bare line feed is whitespace; `<xch_mudtext>`
switches back, and its region is a `PreformattedMarkup`. ANSI is read in both.

| Tag | Markup |
|---|---|
| `b`, `i`, `u`, `s`, `font color` | An `AnsiMarkup` |
| `a xch_cmd xch_hint`, `a href` | A command or URL link. `xch_mode=prompt` puts the command on the input line, as a `CommandMenuMarkup` with `Prompt`. |
| `send href` | A command link |
| `img src alt` | An `ImageMarkup` over the alt text |
| `img xch_sound=play\|loop\|stop href xch_volume` | `SoundMarkup` or `SoundStopMarkup` |
| `xch_pane action=redirect name panetitle` | A `PaneMarkup` until the next redirect |
| `xch_page clear=text`, `xch_prefetch href` | `ClearScreenMarkup`, `PrefetchMarkup` |
| `br`, `p`, `hr`, `pre` | Line breaks, and a `PreformattedMarkup` for `pre` |

`strong` and `em` close `b` and `i`, and `strike` and `del` close `s`. Links end with their line.

What `MarkupFormat.Pueblo` writes reads back to the same markup.
