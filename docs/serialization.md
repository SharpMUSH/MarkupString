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
else, because the version decides what the other properties mean. It does not fail the read: it
shows the payload's `"t"` as plain text, and reads nothing else, since a palette or cover from
another version may not mean what version 1 means by it. `Inspect` reports the same value as
`Unsupported`, so a caller keeps the stored row rather than saving the plain text over it.

That fallback is a rule every later version has to keep: **`"t"` is always the plain text of the
whole document**, so a reader that knows only an earlier version always has something to show.
It is the same idea as the plain-text `body` a Matrix client falls back on for an event type it
does not know.

The wire version is separate from the package version: a release that adds a markup kind or a
property does not change it. It changes only if the envelope itself changes in a way a version 1
reader would misread, and a reader that knows the new version keeps reading version 1.

## Upgrading stored text

`MarkupTextSerializer.VersionOf(stored)` reports the wire version a stored text was written in
without parsing the rest of it: 1 when there is no `"v"`, null when the payload is not a JSON object
or its `"v"` is not a whole number. `NeedsUpgrade(stored)` is true when that version is earlier than
`WireVersion`. Version 1 is the only version so far, so it is false for every row there is.

Both are there so that a store can find what needs upgrading, at startup or in a scan, before a
later version exists. When one does, it comes with one upgrade step from the version before it,
and the steps run in order, so a world from any earlier version reaches the current one without
a step for every pair of versions. Each step keeps to these rules:

- **It works on the stored JSON**, not through `MarkupText`, so that nothing the object model does
  not hold is lost on the way.
- **It is idempotent.** A payload already in the step's target version comes back byte for byte,
  so a migration can stop and resume, or run twice, without harm.
- **It changes nothing it cannot read.** A payload in a newer version, or one that is damaged, is
  reported and left as it is.
- **It keeps `"t"`**, the plain text of the whole document, as every version does.
- **It ships with fixtures** written by the release before it, checked from the old version to the
  new one and read back.

Running the steps over a world is the store's work, not this package's: taking a backup first,
working in batches with a checkpoint, reporting what it skipped, and recording which version a
world needs before an older build opens it. Readers keep reading every earlier version all the
same, since backups, messages in flight and plugins never pass through a migration.

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

`Value` is the same text `Deserialize` returns, fit to show. It is null only when the payload is
not a JSON object, so there is no text in it. A codec that throws fails neither read: its layer is
kept as an `UnknownMarkup` holding the raw object, renders as nothing and writes back as it was,
and `Inspect` reports the result as `Damaged`.

To find what a codec would leave out, `Inspect` writes each layer it read straight back and
compares the two. A codec may write a value in a newer form than the one it read, such as
`"bo":0` as `"bo":1`, so values are not compared, only their kind. Every property read must be
written, each value must keep its kind (an object stays an object, an array keeps its length, a
number stays a number), and the `"k"` and `"t"` discriminators must keep their values. The one
exception is the ANSI codec's legacy colour arrays, such as `"f":[31]`, which it writes back as the
colour they mean. This catches a game's own codecs as well as
the shipped ones, without each codec having to report what it skipped.

`Inspect` does more work than `Deserialize`, since it writes every layer a second time. Use it on
the path that saves a value back, not on every read.

## Validation

Both `Deserialize` overloads reject trailing content after the document, and they throw for a
payload that is not JSON or not an object, since there is no text in it to show. Nothing else
fails a read. A run cover that overflows the text is clipped rather than rejected, so a truncated or
slightly-wrong `r` degrades to less markup instead of an exception; `Inspect` reports it as
`Damaged`.

## Writing a codec

See [Custom markup kinds](custom-markup.md#4-the-codec). Two rules:

- **Pick a `Kind` unlikely to clash.** It is the discriminator; two codecs claiming the same string
  means one of them loses at read time.
- **Never change what a `Kind` means.** Add properties, tolerate their absence when reading, and
  take a new `Kind` if the shape has to change incompatibly — old rows will outlive the change.
