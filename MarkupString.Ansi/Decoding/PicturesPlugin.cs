using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using MarkupString.Decoding;
using Decoder = MarkupString.Decoding.Decoder;
namespace MarkupString.Ansi.Decoding;

/// <summary>
/// Pictures from the server: Kitty graphics (<c>APC G</c>, Unicode placeholders included), iTerm2 inline images
/// (<c>OSC 1337 File=</c>, and its multipart form) and sixel (<c>DCS q</c>). Each becomes an
/// <see cref="ImageMarkup"/> over the cells it covers, its pixels kept in the decoder's <see cref="PictureStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// A picture is placed as a terminal places it: its first row beside the text already on the line, each further
/// row on a line of its own. A picture drawn between a cursor save and restore (<c>ESC 7</c> … <c>ESC 8</c>) is
/// drawn over the lines that follow it instead, which is how a game writes a picture beside text.
/// </para>
/// <para>
/// Only pictures sent in the sequence itself are read (Kitty <c>t=d</c>): one that names a file, a temporary file
/// or shared memory is refused, since the server's files are not the client's.
/// </para>
/// </remarks>
public sealed class PicturesPlugin : IDecoderPlugin
{
	/// <summary>The most base64 a chunked picture may add up to.</summary>
	public const int MaxTransmission = 16 * 1024 * 1024;

	private static readonly Dictionary<int, int> DiacriticIndex = BuildDiacriticIndex();

	/// <summary>The one instance.</summary>
	public static PicturesPlugin Instance { get; } = new();

	private PicturesPlugin()
	{
	}

	/// <inheritdoc/>
	public string Name => "pictures";

	/// <summary>The pictures <paramref name="decoder"/> has read.</summary>
	public static PictureStore Pictures(Decoder decoder)
	{
		ArgumentNullException.ThrowIfNull(decoder);
		return decoder.State<PictureState>().Store;
	}

	/// <inheritdoc/>
	public void Register(DecoderBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder
			.OnApc(static (payload, context) => payload.Length > 0 && payload[0] == 'G' && Kitty(payload[1..], context))
			.OnOsc(1337, static (payload, context) => InlineImage(payload, context))
			.OnDcs('q', static (in CsiSequence header, ReadOnlySpan<char> data, DecodeContext context) => Sixel(header, data, context))
			.OnPrint(static (text, context) => Placeholders(text, context))
			.OnLineEnd(static context => DrawOverlays(context));
	}

	// ── Kitty ───────────────────────────────────────────────────────────────────

	private static bool Kitty(ReadOnlySpan<char> payload, DecodeContext context)
	{
		var state = context.State<PictureState>();
		var separator = payload.IndexOf(';');
		var control = separator < 0 ? payload : payload[..separator];
		var data = separator < 0 ? [] : payload[(separator + 1)..];
		var keys = KittyKeys.Parse(control);

		if (state.Chunks is { } chunks)
		{
			// A continuation: only m (and q) count; the keys came with the first chunk.
			if (chunks.Length + data.Length > MaxTransmission)
			{
				state.Chunks = null;
				context.Diagnostics.PicturesFailed++;
				return true;
			}

			chunks.Append(data);
			if (keys.More) return true;
			state.Chunks = null;
			Complete(state.ChunkKeys, chunks.ToString(), context);
			return true;
		}

		if (keys.More)
		{
			state.Chunks = new StringBuilder().Append(data);
			state.ChunkKeys = keys;
			return true;
		}

		Complete(keys, data.ToString(), context);
		return true;
	}

	private static void Complete(KittyKeys keys, string base64, DecodeContext context)
	{
		var state = context.State<PictureState>();
		switch (keys.Action)
		{
			case 'd':
				Delete(keys, state);
				return;
			case 'p':
				if (state.KittyImages.TryGetValue(keys.Id, out var known) && state.Store.TryGet(known.Key, out var stored))
				{
					var columns = keys.Columns > 0 ? keys.Columns : known.Columns;
					var rows = keys.Rows > 0 ? keys.Rows : known.Rows;
					Place(context, stored, columns, rows, cursorRight: true, keys.StayPut);
					KittyReply(context, keys, null);
				}
				else
				{
					KittyReply(context, keys, "ENOENT:no such image");
				}

				return;
			case 't' or 'T' or 'q' or 'f':
				break;
			case 'a':
				Animate(keys, state);
				return;
			default:
				// Composing frames (a=c) is not read.
				context.Diagnostics.UnhandledSequences++;
				return;
		}

		if (keys.Medium != 'd')
		{
			context.Diagnostics.PicturesFailed++;
			KittyReply(context, keys, "EINVAL:only direct transmission is read");
			return;
		}

		var bytes = FromBase64(base64);
		if (bytes is not null && keys.Compression == 'z') bytes = Inflate(bytes, state.Store.MaxPixels * 4 + 1024);
		if (keys.Action == 'f')
		{
			if (bytes is null || DecodePixels(keys, bytes, state.Store.MaxPixels) is not { } frame || !AddFrame(keys, frame, state))
			{
				context.Diagnostics.PicturesFailed++;
				KittyReply(context, keys, "ENOENT:no such image, or a frame that could not be read");
				return;
			}

			KittyReply(context, keys, null);
			return;
		}

		var picture = bytes is null ? null : DecodeKitty(keys, bytes, state.Store);
		if (picture is null)
		{
			context.Diagnostics.PicturesFailed++;
			KittyReply(context, keys, "EBADF:could not decode the picture");
			return;
		}

		if (keys.Action == 'q')
		{
			KittyReply(context, keys, null);
			return;
		}

		var (cellColumns, cellRows) = Cells(picture.Width, picture.Height, keys.Columns, keys.Rows, context.Identity);
		var id = keys.Id;
		if (id == 0 && keys.Number != 0) id = state.NextKittyId++;
		if (id != 0)
		{
			state.KittyImages[id] = new KittyImage(picture.Key, cellColumns, cellRows);
			state.Animations.Remove(id);
		}

		if (keys.Action == 'T' && !keys.Virtual) Place(context, picture, cellColumns, cellRows, cursorRight: true, keys.StayPut);
		KittyReply(context, keys with { Id = id }, null);
	}

	private static StoredPicture? DecodeKitty(KittyKeys keys, byte[] bytes, PictureStore store)
	{
		if (DecodePixels(keys, bytes, store.MaxPixels) is not { } pixels) return null;
		var picture = keys.Format == 100
			? store.Add(pixels.Width, pixels.Height, pixels.Rgba, bytes, "image/png")
			: store.Add(pixels.Width, pixels.Height, pixels.Rgba);

		// The same picture sent again starts over as one frame; its frames follow again if it moves.
		if (picture.IsMoving) store.ReplacePixels(picture, new TerminalPicture(picture.Key, pixels.Width, pixels.Height, pixels.Rgba));
		return picture;
	}

	/// <summary>The pixels a Kitty transmission carries: a PNG, or raw RGB or RGBA <c>s</c> × <c>v</c>.</summary>
	private static (int Width, int Height, byte[] Rgba)? DecodePixels(KittyKeys keys, byte[] bytes, long maxPixels)
	{
		switch (keys.Format)
		{
			case 100:
				return PngDecoder.TryDecode(bytes, maxPixels, out var width, out var height, out var rgba) ? (width, height, rgba) : null;
			case 24 or 32:
				{
					if (keys.Width <= 0 || keys.Height <= 0 || (long)keys.Width * keys.Height > maxPixels) return null;
					var channels = keys.Format / 8;
					if (bytes.Length < (long)keys.Width * keys.Height * channels) return null;
					var pixels = new byte[keys.Width * keys.Height * 4];
					for (var i = 0; i < keys.Width * keys.Height; i++)
					{
						pixels[i * 4] = bytes[i * channels];
						pixels[i * 4 + 1] = bytes[i * channels + 1];
						pixels[i * 4 + 2] = bytes[i * channels + 2];
						pixels[i * 4 + 3] = channels == 4 ? bytes[i * 4 + 3] : (byte)255;
					}

					return (keys.Width, keys.Height, pixels);
				}
			default:
				return null;
		}
	}

	// ── Kitty animation ─────────────────────────────────────────────────────────

	/// <summary>The default gap between frames, as Kitty has it.</summary>
	private const int DefaultGap = 40;

	/// <summary>The most frames a moving picture is read with; later ones are dropped.</summary>
	public const int MaxFrames = 1000;

	/// <summary>
	/// <c>a=f</c>: a frame, made on a copy of frame <c>c</c> (or blank, filled with <c>Y</c>) or written over frame
	/// <c>r</c>, with the data put at <c>x</c>, <c>y</c>, blended over what is there unless <c>X=1</c>.
	/// </summary>
	private static bool AddFrame(KittyKeys keys, (int Width, int Height, byte[] Rgba) data, PictureState state)
	{
		if (!state.KittyImages.TryGetValue(keys.Id, out var known) || !state.Store.TryGet(known.Key, out var stored)
			|| stored.Pixels is not { } pixels)
		{
			return false;
		}

		var animation = Animation(keys.Id, stored, pixels, state);
		var size = stored.Width * stored.Height * 4;

		// For a frame, c and r are frame numbers (from 1), not cells.
		var edit = keys.Rows;
		var baseFrame = keys.Columns;
		byte[] canvas;
		if (edit > 0 && edit <= animation.Frames.Count)
		{
			canvas = (byte[])animation.Frames[edit - 1].Clone();
		}
		else if (baseFrame > 0 && baseFrame <= animation.Frames.Count)
		{
			canvas = (byte[])animation.Frames[baseFrame - 1].Clone();
		}
		else
		{
			canvas = new byte[size];
			if (keys.Background != 0)
			{
				var fill = keys.Background;
				for (var i = 0; i < canvas.Length; i += 4)
				{
					canvas[i] = (byte)(fill >> 24);
					canvas[i + 1] = (byte)(fill >> 16);
					canvas[i + 2] = (byte)(fill >> 8);
					canvas[i + 3] = (byte)fill;
				}
			}
		}

		Composite(canvas, stored.Width, stored.Height, data, keys.X, keys.Y, keys.Replace);

		if (edit > 0 && edit <= animation.Frames.Count)
		{
			animation.Frames[edit - 1] = canvas;
			if (keys.Gap != 0) animation.Gaps[edit - 1] = keys.Gap;
		}
		else
		{
			if (animation.Frames.Count >= MaxFrames) return true;
			animation.Frames.Add(canvas);
			animation.Gaps.Add(keys.Gap != 0 ? keys.Gap : DefaultGap);
		}

		Publish(stored, animation, state.Store);
		return true;
	}

	/// <summary><c>a=a</c>: a frame's gap (<c>r</c>, <c>z</c>). Playing, stopping and looping are the client's.</summary>
	private static void Animate(KittyKeys keys, PictureState state)
	{
		if (!state.KittyImages.TryGetValue(keys.Id, out var known) || !state.Store.TryGet(known.Key, out var stored)
			|| stored.Pixels is not { } pixels)
		{
			return;
		}

		var frame = keys.Rows;
		if (frame <= 0 || keys.Gap == 0) return;
		var animation = Animation(keys.Id, stored, pixels, state);
		if (frame > animation.Frames.Count) return;
		animation.Gaps[frame - 1] = keys.Gap;
		Publish(stored, animation, state.Store);
	}

	private static KittyAnimation Animation(uint id, StoredPicture stored, TerminalPicture pixels, PictureState state)
	{
		if (state.Animations.TryGetValue(id, out var animation)) return animation;
		animation = new KittyAnimation();
		if (pixels.Frames.Count > 0)
		{
			foreach (var frame in pixels.Frames)
			{
				animation.Frames.Add(frame.Rgba.ToArray());
				animation.Gaps.Add((int)frame.Duration.TotalMilliseconds);
			}
		}
		else
		{
			animation.Frames.Add(pixels.Rgba.ToArray());
			animation.Gaps.Add(DefaultGap);
		}

		state.Animations[id] = animation;
		return animation;
	}

	private static void Publish(StoredPicture stored, KittyAnimation animation, PictureStore store)
	{
		// A negative gap is a frame Kitty skips; it is left out.
		var frames = new List<TerminalPictureFrame>(animation.Frames.Count);
		for (var i = 0; i < animation.Frames.Count; i++)
		{
			if (animation.Gaps[i] < 0 && i > 0) continue;
			frames.Add(new TerminalPictureFrame(animation.Frames[i], TimeSpan.FromMilliseconds(Math.Max(0, animation.Gaps[i]))));
		}

		store.ReplacePixels(stored, new TerminalPicture(stored.Key, stored.Width, stored.Height, frames));
	}

	private static void Composite(byte[] canvas, int width, int height, (int Width, int Height, byte[] Rgba) data, int left, int top, bool replace)
	{
		for (var y = 0; y < data.Height; y++)
		{
			var cy = top + y;
			if (cy < 0 || cy >= height) continue;
			for (var x = 0; x < data.Width; x++)
			{
				var cx = left + x;
				if (cx < 0 || cx >= width) continue;
				var s = (y * data.Width + x) * 4;
				var d = (cy * width + cx) * 4;
				var alpha = data.Rgba[s + 3];
				if (replace || alpha == 255)
				{
					Array.Copy(data.Rgba, s, canvas, d, 4);
					continue;
				}

				if (alpha == 0) continue;

				// Source over destination, straight alpha.
				var a = alpha / 255.0;
				var da = canvas[d + 3] / 255.0;
				var oa = a + da * (1 - a);
				for (var c = 0; c < 3; c++)
				{
					var value = (data.Rgba[s + c] * a + canvas[d + c] * da * (1 - a)) / oa;
					canvas[d + c] = (byte)Math.Round(value);
				}

				canvas[d + 3] = (byte)Math.Round(oa * 255);
			}
		}
	}

	private static void Delete(KittyKeys keys, PictureState state)
	{
		switch (keys.Delete)
		{
			case 'a' or 'A' or '\0':
				state.KittyImages.Clear();
				state.Animations.Clear();
				break;
			case 'i' or 'I' or 'n' or 'N':
				state.KittyImages.Remove(keys.Id);
				state.Animations.Remove(keys.Id);
				break;
		}
	}

	private static void KittyReply(DecodeContext context, KittyKeys keys, string? error)
	{
		if (keys.Id == 0 && keys.Number == 0) return;
		if (keys.Quiet >= 2 || (error is null && keys.Quiet >= 1)) return;
		if ((context.Identity.Answers & ClientAnswers.Pictures) == 0) return;

		var reply = new StringBuilder("\e_G");
		if (keys.Id != 0) reply.Append(CultureInfo.InvariantCulture, $"i={keys.Id}");
		if (keys.Number != 0) reply.Append(keys.Id != 0 ? "," : "").Append(CultureInfo.InvariantCulture, $"I={keys.Number}");
		reply.Append(';').Append(error ?? "OK").Append("\e\\");
		context.Reply(reply.ToString());
	}

	private static bool Placeholders(ReadOnlySpan<char> text, DecodeContext context)
	{
		if (text.IndexOf('\uDBFB') < 0) return false;

		var state = context.State<PictureState>();
		var at = 0;
		while (at < text.Length)
		{
			var start = IndexOfPlaceholder(text, at);
			if (start < 0)
			{
				context.Write(text[at..]);
				break;
			}

			if (start > at) context.Write(text[at..start]);

			// One stretch of placeholders on the same row, in consecutive columns, is one write.
			var id = PlaceholderId(context);
			int row = -1, column = -1, count = 0, first = 0;
			var i = start;
			while (i < text.Length && IsPlaceholderAt(text, i))
			{
				i += 2;
				var cellRow = Diacritic(text, ref i);
				var cellColumn = Diacritic(text, ref i);
				Diacritic(text, ref i);

				if (cellRow < 0) cellRow = row < 0 ? 0 : row;
				if (cellColumn < 0) cellColumn = column < 0 ? 0 : column + 1;
				if (count > 0 && (cellRow != row || cellColumn != column + 1))
				{
					WritePlaceholders(context, state, id, row, first, count);
					count = 0;
				}

				if (count == 0) first = cellColumn;
				row = cellRow;
				column = cellColumn;
				count++;
			}

			WritePlaceholders(context, state, id, row, first, count);
			at = i;
		}

		return true;
	}

	private static void WritePlaceholders(DecodeContext context, PictureState state, uint id, int row, int first, int count)
	{
		if (count == 0) return;
		var blank = new string(' ', count);
		if (state.KittyImages.TryGetValue(id, out var known) && state.Store.TryGet(known.Key, out var stored) && row < known.Rows)
		{
			var image = Markup(stored) with { Row = new PictureRow(row, known.Rows, known.Columns) { FirstColumn = first } };
			context.Write(blank, image);
		}
		else
		{
			context.Write(blank);
		}
	}

	private static uint PlaceholderId(DecodeContext context) =>
		context.Pen(VtPens.Style) is AnsiMarkup { Style.Foreground: { } color }
			? color switch
			{
				AnsiColor.Rgb rgb => (uint)(rgb.R << 16 | rgb.G << 8 | rgb.B),
				AnsiColor.Xterm xterm => xterm.Index,
				AnsiColor.Standard standard => (uint)(standard.Index + (standard.Bright ? 8 : 0)),
				_ => 0u,
			}
			: 0u;

	private static int IndexOfPlaceholder(ReadOnlySpan<char> text, int from)
	{
		for (var i = from; i < text.Length - 1; i++)
		{
			if (IsPlaceholderAt(text, i)) return i;
		}

		return -1;
	}

	private static bool IsPlaceholderAt(ReadOnlySpan<char> text, int i) => i + 1 < text.Length && text[i] == '\uDBFB' && text[i + 1] == '\uDEEE';

	private static int Diacritic(ReadOnlySpan<char> text, ref int i)
	{
		if (i >= text.Length || Rune.DecodeFromUtf16(text[i..], out var rune, out var used) != System.Buffers.OperationStatus.Done) return -1;
		if (!DiacriticIndex.TryGetValue(rune.Value, out var index)) return -1;
		i += used;
		return index;
	}

	private static Dictionary<int, int> BuildDiacriticIndex()
	{
		var index = new Dictionary<int, int>(TerminalPictureWriter.Diacritics.Length);
		for (var i = 0; i < TerminalPictureWriter.Diacritics.Length; i++) index[TerminalPictureWriter.Diacritics[i]] = i;
		return index;
	}

	// ── iTerm2 ──────────────────────────────────────────────────────────────────

	private static bool InlineImage(ReadOnlySpan<char> payload, DecodeContext context)
	{
		var state = context.State<PictureState>();
		if (payload.StartsWith("File=", StringComparison.Ordinal))
		{
			var colon = payload.IndexOf(':');
			if (colon < 0) return false;
			ShowInlineImage(InlineArguments.Parse(payload[5..colon]), FromBase64(payload[(colon + 1)..].ToString()), context);
			return true;
		}

		if (payload.StartsWith("MultipartFile=", StringComparison.Ordinal))
		{
			state.Multipart = InlineArguments.Parse(payload[14..]);
			state.MultipartData = new StringBuilder();
			return true;
		}

		if (payload.StartsWith("FilePart=", StringComparison.Ordinal))
		{
			if (state.MultipartData is not { } parts) return true;
			if (parts.Length + payload.Length > MaxTransmission)
			{
				state.Multipart = null;
				state.MultipartData = null;
				context.Diagnostics.PicturesFailed++;
				return true;
			}

			parts.Append(payload[9..]);
			return true;
		}

		if (payload.SequenceEqual("FileEnd"))
		{
			if (state.Multipart is { } arguments && state.MultipartData is { } data)
			{
				ShowInlineImage(arguments, FromBase64(data.ToString()), context);
			}

			state.Multipart = null;
			state.MultipartData = null;
			return true;
		}

		return false;
	}

	private static void ShowInlineImage(InlineArguments arguments, byte[]? bytes, DecodeContext context)
	{
		if (bytes is null)
		{
			context.Diagnostics.PicturesFailed++;
			return;
		}

		if (!arguments.Inline)
		{
			context.Raise(new DownloadEvent(arguments.Name, bytes.Length));
			return;
		}

		var store = context.State<PictureState>().Store;
		StoredPicture? picture = null;
		if (PngDecoder.TryDecode(bytes, store.MaxPixels, out var width, out var height, out var rgba))
		{
			picture = store.Add(width, height, rgba, bytes, "image/png");
		}
		else if (Sniff(bytes) is { } file && (long)file.Width * file.Height <= store.MaxPixels)
		{
			// Not decodable here, but a browser shows it.
			picture = store.Add(file.Width, file.Height, null, bytes, file.MediaType);
		}

		if (picture is null)
		{
			context.Diagnostics.PicturesFailed++;
			return;
		}

		var identity = context.Identity;
		var columns = arguments.Width.ToCells(identity.Columns, identity.CellWidth);
		var rows = arguments.Height.ToCells(identity.Rows, identity.CellHeight);
		if (!arguments.PreserveAspectRatio && columns > 0 && rows > 0)
		{
			Place(context, picture, Math.Min(columns, identity.Columns), rows, cursorRight: true, stayPut: false, arguments.Name);
			return;
		}

		var (cellColumns, cellRows) = Cells(picture.Width, picture.Height, columns, rows, identity);
		Place(context, picture, cellColumns, cellRows, cursorRight: true, stayPut: false, arguments.Name);
	}

	/// <summary>The size and type of a GIF, JPEG or WebP, read from its header.</summary>
	private static (int Width, int Height, string MediaType)? Sniff(ReadOnlySpan<byte> data)
	{
		if (data.Length >= 10 && data.StartsWith("GIF8"u8))
		{
			return (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]), "image/gif");
		}

		if (data.Length >= 30 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
		{
			var chunk = data[12..16];
			if (chunk.SequenceEqual("VP8 "u8))
				return (BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF, "image/webp");
			if (chunk.SequenceEqual("VP8L"u8) && data.Length >= 25)
			{
				var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
				return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1, "image/webp");
			}

			if (chunk.SequenceEqual("VP8X"u8))
				return ((data[24] | data[25] << 8 | data[26] << 16) + 1, (data[27] | data[28] << 8 | data[29] << 16) + 1, "image/webp");
		}

		if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
		{
			var i = 2;
			while (i + 9 < data.Length && data[i] == 0xFF)
			{
				var marker = data[i + 1];
				var length = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
				if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
				{
					return (BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]), "image/jpeg");
				}

				i += 2 + length;
			}
		}

		return null;
	}

	// ── Sixel ───────────────────────────────────────────────────────────────────

	private static bool Sixel(in CsiSequence header, ReadOnlySpan<char> data, DecodeContext context)
	{
		var store = context.State<PictureState>().Store;
		if (!SixelDecoder.TryDecode(data, header.Get(1) == 1, store.MaxPixels, out var width, out var height, out var rgba))
		{
			context.Diagnostics.PicturesFailed++;
			return true;
		}

		var picture = store.Add(width, height, rgba);
		var (columns, rows) = Cells(width, height, 0, 0, context.Identity);
		Place(context, picture, columns, rows, cursorRight: false, stayPut: false);
		return true;
	}

	// ── Placing ─────────────────────────────────────────────────────────────────

	/// <summary>The cells a picture takes: as asked, else its pixels over the cell size, no wider than the pane.</summary>
	private static (int Columns, int Rows) Cells(int width, int height, int columns, int rows, ClientIdentity identity)
	{
		var cellWidth = Math.Max(1, identity.CellWidth);
		var cellHeight = Math.Max(1, identity.CellHeight);
		if (columns <= 0 && rows <= 0)
		{
			columns = (width + cellWidth - 1) / cellWidth;
			rows = (height + cellHeight - 1) / cellHeight;
		}
		else if (rows <= 0)
		{
			rows = (int)Math.Ceiling((double)columns * cellWidth * height / width / cellHeight);
		}
		else if (columns <= 0)
		{
			columns = (int)Math.Ceiling((double)rows * cellHeight * width / height / cellWidth);
		}

		if (identity.Columns > 0 && columns > identity.Columns)
		{
			rows = Math.Max(1, (int)Math.Ceiling((double)rows * identity.Columns / columns));
			columns = identity.Columns;
		}

		return (Math.Max(1, columns), Math.Max(1, rows));
	}

	private static ImageMarkup Markup(StoredPicture picture, string? description = null) =>
		new(picture.Source, description, picture.Width, picture.Height);

	private static void Place(DecodeContext context, StoredPicture picture, int columns, int rows, bool cursorRight, bool stayPut, string? description = null)
	{
		var image = Markup(picture, description);
		var vt = context.State<VtState>();
		if (vt.SavedColumn is not null || stayPut)
		{
			// Drawn over the lines that follow, which the server leaves room for.
			context.State<PictureState>().Overlays.Add(new Overlay(image, rows, columns, context.Column));
			return;
		}

		context.PlacePicture(image, rows, columns, cursorRight);
	}

	private static void DrawOverlays(DecodeContext context)
	{
		var overlays = context.State<PictureState>().Overlays;
		if (overlays.Count == 0) return;

		var cursor = context.Column;
		for (var i = 0; i < overlays.Count; i++)
		{
			var overlay = overlays[i];
			context.MoveToColumn(overlay.Column);
			context.Write(new string(' ', overlay.Columns), overlay.Image with { Row = new PictureRow(overlay.Next, overlay.Rows, overlay.Columns) });
			overlay.Next++;
		}

		overlays.RemoveAll(overlay => overlay.Next >= overlay.Rows);
		context.MoveToColumn(cursor);
	}

	// ── Helpers ─────────────────────────────────────────────────────────────────

	private static byte[]? FromBase64(string base64)
	{
		try
		{
			return Convert.FromBase64String(base64);
		}
		catch (FormatException)
		{
			return null;
		}
	}

	private static byte[]? Inflate(byte[] data, long limit)
	{
		try
		{
			using var input = new MemoryStream(data);
			using var inflater = new ZLibStream(input, CompressionMode.Decompress);
			using var output = new MemoryStream();
			var buffer = new byte[81920];
			int read;
			while ((read = inflater.Read(buffer, 0, buffer.Length)) > 0)
			{
				output.Write(buffer, 0, read);
				if (output.Length > limit) return null;
			}

			return output.ToArray();
		}
		catch (InvalidDataException)
		{
			return null;
		}
	}

	internal readonly record struct KittyImage(string Key, int Columns, int Rows);

	internal sealed class KittyAnimation
	{
		public List<byte[]> Frames { get; } = [];

		public List<int> Gaps { get; } = [];
	}

	internal sealed class Overlay(ImageMarkup image, int rows, int columns, int column)
	{
		public ImageMarkup Image { get; } = image;

		public int Rows { get; } = rows;

		public int Columns { get; } = columns;

		public int Column { get; } = column;

		public int Next { get; set; }
	}

	internal readonly record struct KittyKeys
	{
		public char Action { get; init; }

		public char Medium { get; init; }

		public char Compression { get; init; }

		public char Delete { get; init; }

		public int Format { get; init; }

		public uint Id { get; init; }

		public uint Number { get; init; }

		public int Width { get; init; }

		public int Height { get; init; }

		public int Columns { get; init; }

		public int Rows { get; init; }

		public int Quiet { get; init; }

		public bool More { get; init; }

		public bool Virtual { get; init; }

		public bool StayPut { get; init; }

		public int X { get; init; }

		public int Y { get; init; }

		public bool Replace { get; init; }

		public uint Background { get; init; }

		public int Gap { get; init; }

		public static KittyKeys Parse(ReadOnlySpan<char> control)
		{
			var keys = new KittyKeys { Action = 't', Medium = 'd', Format = 32 };
			foreach (var range in control.Split(','))
			{
				var pair = control[range];
				if (pair.Length < 3 || pair[1] != '=') continue;
				var value = pair[2..];
				int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number);
				keys = pair[0] switch
				{
					'a' => keys with { Action = value[0] },
					't' => keys with { Medium = value[0] },
					'o' => keys with { Compression = value[0] },
					'd' => keys with { Delete = value[0] },
					'f' => keys with { Format = number },
					'i' => keys with { Id = (uint)Math.Max(0, number) },
					'I' => keys with { Number = (uint)Math.Max(0, number) },
					's' => keys with { Width = number },
					'v' => keys with { Height = number },
					'c' => keys with { Columns = number },
					'r' => keys with { Rows = number },
					'q' => keys with { Quiet = number },
					'm' => keys with { More = number == 1 },
					'U' => keys with { Virtual = number == 1 },
					'C' => keys with { StayPut = number == 1 },
					'x' => keys with { X = number },
					'y' => keys with { Y = number },
					'X' => keys with { Replace = number == 1 },
					'Y' => keys with { Background = uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var fill) ? fill : 0 },
					'z' => keys with { Gap = number },
					_ => keys,
				};
			}

			return keys;
		}
	}

	internal readonly record struct InlineSize(int Value, char Unit)
	{
		public static InlineSize Auto => default;

		public int ToCells(int paneCells, int cellPixels) => Unit switch
		{
			'c' => Value,
			'p' => (Value + cellPixels - 1) / Math.Max(1, cellPixels),
			'%' => Math.Max(1, paneCells * Value / 100),
			_ => 0,
		};

		public static InlineSize Parse(ReadOnlySpan<char> text)
		{
			if (text.IsEmpty || text.SequenceEqual("auto")) return Auto;
			var unit = 'c';
			if (text.EndsWith("px", StringComparison.Ordinal))
			{
				unit = 'p';
				text = text[..^2];
			}
			else if (text.EndsWith("%", StringComparison.Ordinal))
			{
				unit = '%';
				text = text[..^1];
			}

			return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? new InlineSize(value, unit) : Auto;
		}
	}

	internal sealed record InlineArguments(string? Name, bool Inline, InlineSize Width, InlineSize Height, bool PreserveAspectRatio)
	{
		public static InlineArguments Parse(ReadOnlySpan<char> text)
		{
			string? name = null;
			var inline = false;
			InlineSize width = default, height = default;
			var preserve = true;
			foreach (var range in text.Split(';'))
			{
				var pair = text[range];
				var equals = pair.IndexOf('=');
				if (equals < 0) continue;
				var key = pair[..equals];
				var value = pair[(equals + 1)..];
				if (key.SequenceEqual("name"))
				{
					var bytes = FromBase64(value.ToString());
					name = bytes is null ? null : Encoding.UTF8.GetString(bytes);
				}
				else if (key.SequenceEqual("inline")) inline = value.SequenceEqual("1");
				else if (key.SequenceEqual("width")) width = InlineSize.Parse(value);
				else if (key.SequenceEqual("height")) height = InlineSize.Parse(value);
				else if (key.SequenceEqual("preserveAspectRatio")) preserve = !value.SequenceEqual("0");
			}

			return new InlineArguments(name, inline, width, height, preserve);
		}
	}
}

/// <summary>The pictures plugin's state for one decoder.</summary>
internal sealed class PictureState
{
	public PictureStore Store { get; } = new();

	public Dictionary<uint, PicturesPlugin.KittyImage> KittyImages { get; } = [];

	public Dictionary<uint, PicturesPlugin.KittyAnimation> Animations { get; } = [];

	public uint NextKittyId { get; set; } = 0x7F000000;

	public StringBuilder? Chunks { get; set; }

	public PicturesPlugin.KittyKeys ChunkKeys { get; set; }

	public PicturesPlugin.InlineArguments? Multipart { get; set; }

	public StringBuilder? MultipartData { get; set; }

	public List<PicturesPlugin.Overlay> Overlays { get; } = [];
}

/// <summary>Registers the pictures plugin.</summary>
public static class PictureDecoding
{
	/// <summary>The registry with <see cref="PicturesPlugin"/>.</summary>
	public static DecoderRegistry WithPictures(this DecoderRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);
		return registry.With(PicturesPlugin.Instance);
	}

	/// <summary>The pictures <paramref name="decoder"/> has read.</summary>
	public static PictureStore Pictures(this Decoder decoder) => PicturesPlugin.Pictures(decoder);
}
