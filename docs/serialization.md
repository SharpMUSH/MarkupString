# Serialization

`MarkupTextSerializer` reads and writes a compact JSON shape designed for storage and for the wire
— a game database, a message bus, a cache — where the same text is written once and read many
times, by processes that may not all have the same packages installed.

```csharp
var json = MarkupTextSerializer.Serialize(text);              // MarkupRegistry.Default
var json = MarkupTextSerializer.Serialize(text, registry);
MarkupTextSerializer.Serialize(text, utf8BufferWriter);       // straight to UTF-8, no string

var back = MarkupTextSerializer.Deserialize(json);
var back = MarkupTextSerializer.Deserialize(utf8Bytes, registry);
```

## The shape

```json
{"t":"Hello, world!","p":[null,[{"k":"ansi","f":9}]],"r":[7,0,5,1,1,0]}
```

| Key | |
|---|---|
| `t` | the plain text; omitted when empty |
| `p` | the palette of **distinct** `MarkupSet`s. Index 0 is always `null`, meaning "no markup"; every other entry is an array of markup objects, innermost first |
| `r` | a flat `[length, paletteIndex, …]` cover of `t`. Starts are the running sum, so no start, end or total length is stored; unmarked stretches take slot 0 |

Both `p` and `r` are omitted when nothing carries markup, so plain text — the overwhelmingly
common case in a game database — costs its characters plus eight bytes:

```json
{"t":"bare"}
```

The palette holds one entry per *distinct* set, not per run, so a document that repeats the same
three styles a thousand times stores three of them. Non-ASCII text is written as literal UTF-8
rather than `\uXXXX` escapes, which matters for the games written in CJK and Cyrillic.

## Forward compatibility

Each markup object carries a `"k"` kind discriminator, written by the serializer; a codec writes
and reads only its own properties.

A kind that **no codec in the reading registry claims** does not fail and is not dropped. It
becomes an `UnknownMarkup` holding the raw JSON object, renders as nothing (its body still comes
out), and is written back **verbatim** on the next save:

```csharp
var withSpoilers = MarkupRegistry.Empty.WithAnsi().WithHtml().WithSpoilers();
var withoutThem  = MarkupRegistry.Empty.WithAnsi().WithHtml();

var json = MarkupTextSerializer.Serialize(text, withSpoilers);
// {"t":"he was dead all along","p":[null,[{"k":"spoiler","why":"ending"}]],"r":[21,1]}

var read = MarkupTextSerializer.Deserialize(json, withoutThem);
read.Render(MarkupFormat.Html, withoutThem);          // he was dead all along
MarkupTextSerializer.Serialize(read, withoutThem);    // byte-identical to the original
```

That is the property that makes a rolling deployment safe: an old reader that meets markup from a
newer writer neither loses it nor corrupts it. That holds for whole layers of an unknown kind. A
new property on a kind the reader does know is dropped when that layer is written back, and so is
a layout block of an unknown kind; `Inspect` (below) reports both, so a caller can keep the
original instead of saving the loss.

Writing is stricter than reading. A markup with **no codec at write time** throws
`InvalidOperationException` naming the type, because silently dropping markup you asked to store is
a different class of problem from not styling it:

```
No markup codec is registered for SpoilerMarkup. Add one with MarkupRegistry.With(IMarkupCodec).
```

## Wire version

The shape above is wire version 1, `MarkupTextSerializer.WireVersion`. Version 1 writes no `"v"`
property, and every released writer since 1.0.0 has written it. A reader treats a missing `"v"`, or
`"v":1`, as version 1.

Any other `"v"` is a format this reader does not know. The reader checks it before it reads anything
else, because the version decides what the other properties mean. `Deserialize` throws a
`JsonException` for an unknown version, and `Inspect` reports it as `Unsupported` with no value. A
row in an unknown version is never read as version 1 and never saved back as an empty or plain text.

The wire version is separate from the package version: a release that adds a markup kind or a
property does not change it. It changes only if the envelope itself changes in a way a version 1
reader would misread, and a reader that knows the new version keeps reading version 1.

## Reading stored text for saving: `Inspect`

`Deserialize` is for showing text. It recovers what it can from a damaged or partly understood row
and does not say what it left out. A caller that might save the value back over the original
should read with `Inspect`, which reads the same way and also reports what it found:

```csharp
var result = MarkupTextSerializer.Inspect(stored, registry);

if (result.CanRewrite)
	stored = MarkupTextSerializer.Serialize(Edit(result.Value!), registry);
else
	Log(result.Status, result.Diagnostics);   // keep the stored bytes; show result.Value if there is one
```

| `Status` | What it means | `CanRewrite` |
|---|---|---|
| `Complete` | Every part of the payload was understood, and writing it back keeps all of it. | yes |
| `Opaque` | Some layers are of kinds no codec in the registry claims. They are kept as `UnknownMarkup` and written back verbatim. | yes |
| `Unsupported` | Well formed, but it holds something this reader does not know and would not write back: an unknown wire version, an unknown envelope property, an unknown property on a known kind, a layout block of an unknown kind, a palette entry nothing uses. | no |
| `Damaged` | Not what any writer produces: not JSON, not an object, content after the value, a field of the wrong type, a cover that overruns the text or points past the palette, a codec that throws. | no |

When several things are wrong, the worst one decides `Status`, and `Diagnostics` lists them all.

`Value` is null only when nothing could be read without guessing: the payload is not a JSON object,
or its wire version is unknown. Otherwise it is the same text `Deserialize` returns, fit to show.
A codec that throws does not fail `Inspect`: its layer is kept as an `UnknownMarkup` and the result
is `Damaged`. `Deserialize` still lets the exception through.

To find what a codec would leave out, `Inspect` writes each layer it read straight back and
compares the two. A codec may write a value in a newer form than the one it read, such as a legacy
colour array as a palette index, so values are not compared. Structure is compared: every property
read must be written, an object must stay an object, an array must keep its length, and the
`"k"` and `"t"` discriminators must keep their values. This catches a game's own codecs as well as
the shipped ones, without each codec having to report what it skipped.

`Inspect` does more work than `Deserialize`, since it writes every layer a second time. Use it on
the path that saves a value back, not on every read.

## Validation

Both `Deserialize` overloads reject trailing content after the document and an unknown wire
version. A run cover that overflows the text is clipped rather than rejected, so a truncated or
slightly-wrong `r` degrades to less markup instead of an exception; `Inspect` reports it as
`Damaged`.

## Writing a codec

See [Custom markup kinds](custom-markup.md#4-the-codec). Two rules:

- **Pick a `Kind` unlikely to clash.** It is the discriminator; two codecs claiming the same string
  means one of them loses at read time.
- **Never change what a `Kind` means.** Add properties, tolerate their absence when reading, and
  take a new `Kind` if the shape has to change incompatibly — old rows will outlive the change.
