using System.Buffers;
using System.Globalization;
using System.Text;
namespace MarkupString.Ansi;

/// <summary>
/// Draws one row of a picture's cells (<see cref="PictureCellsMarkup"/>) in a terminal, in whichever way
/// <see cref="AnsiOutputOptions.Features"/> says the client draws pictures.
/// </summary>
/// <remarks>
/// Every run of a row carries the same layer; the first draws the whole row and the rest draw nothing,
/// so the row is the same number of cells whichever way it is drawn.
/// </remarks>
internal static class TerminalPictureWriter
{
	private const string Esc = "\e";
	private const string StringTerminator = "\e\\";

	/// <summary>Kitty's placeholder character.</summary>
	private const int Placeholder = 0x10EEEE;

	/// <summary>Kitty's base64 chunk ceiling.</summary>
	private const int KittyChunk = 4096;

	/// <summary>
	/// The diacritics Kitty reads as a row or column number, in order: the first is 0. From kitty's
	/// <c>gen/rowcolumn-diacritics.txt</c>; a picture larger than this many cells either way is not drawn.
	/// </summary>
	private static readonly int[] Diacritics =
	[
		0x0305, 0x030D, 0x030E, 0x0310, 0x0312, 0x033D, 0x033E, 0x033F, 0x0346, 0x034A, 0x034B, 0x034C,
		0x0350, 0x0351, 0x0352, 0x0357, 0x035B, 0x0363, 0x0364, 0x0365, 0x0366, 0x0367, 0x0368, 0x0369,
		0x036A, 0x036B, 0x036C, 0x036D, 0x036E, 0x036F, 0x0483, 0x0484, 0x0485, 0x0486, 0x0487, 0x0592,
		0x0593, 0x0594, 0x0595, 0x0597, 0x0598, 0x0599, 0x059C, 0x059D, 0x059E, 0x059F, 0x05A0, 0x05A1,
		0x05A8, 0x05A9, 0x05AB, 0x05AC, 0x05AF, 0x05C4, 0x0610, 0x0611, 0x0612, 0x0613, 0x0614, 0x0615,
		0x0616, 0x0617, 0x0657, 0x0658, 0x0659, 0x065A, 0x065B, 0x065D, 0x065E, 0x06D6, 0x06D7, 0x06D8,
		0x06D9, 0x06DA, 0x06DB, 0x06DC, 0x06DF, 0x06E0, 0x06E1, 0x06E2, 0x06E4, 0x06E7, 0x06E8, 0x06EB,
		0x06EC, 0x0730, 0x0732, 0x0733, 0x0735, 0x0736, 0x073A, 0x073D, 0x073F, 0x0740, 0x0741, 0x0743,
		0x0745, 0x0747, 0x0749, 0x074A, 0x07EB, 0x07EC, 0x07ED, 0x07EE, 0x07EF, 0x07F0, 0x07F1, 0x07F3,
		0x0816, 0x0817, 0x0818, 0x0819, 0x081B, 0x081C, 0x081D, 0x081E, 0x081F, 0x0820, 0x0821, 0x0822,
		0x0823, 0x0825, 0x0826, 0x0827, 0x0829, 0x082A, 0x082B, 0x082C, 0x082D, 0x0951, 0x0953, 0x0954,
		0x0F82, 0x0F83, 0x0F86, 0x0F87, 0x135D, 0x135E, 0x135F, 0x17DD, 0x193A, 0x1A17, 0x1A75, 0x1A76,
		0x1A77, 0x1A78, 0x1A79, 0x1A7A, 0x1A7B, 0x1A7C, 0x1B6B, 0x1B6D, 0x1B6E, 0x1B6F, 0x1B70, 0x1B71,
		0x1B72, 0x1B73, 0x1CD0, 0x1CD1, 0x1CD2, 0x1CDA, 0x1CDB, 0x1CE0, 0x1DC0, 0x1DC1, 0x1DC3, 0x1DC4,
		0x1DC5, 0x1DC6, 0x1DC7, 0x1DC8, 0x1DC9, 0x1DCB, 0x1DCC, 0x1DD1, 0x1DD2, 0x1DD3, 0x1DD4, 0x1DD5,
		0x1DD6, 0x1DD7, 0x1DD8, 0x1DD9, 0x1DDA, 0x1DDB, 0x1DDC, 0x1DDD, 0x1DDE, 0x1DDF, 0x1DE0, 0x1DE1,
		0x1DE2, 0x1DE3, 0x1DE4, 0x1DE5, 0x1DE6, 0x1DFE, 0x20D0, 0x20D1, 0x20D4, 0x20D5, 0x20D6, 0x20D7,
		0x20DB, 0x20DC, 0x20E1, 0x20E7, 0x20E9, 0x20F0, 0x2CEF, 0x2CF0, 0x2CF1, 0x2DE0, 0x2DE1, 0x2DE2,
		0x2DE3, 0x2DE4, 0x2DE5, 0x2DE6, 0x2DE7, 0x2DE8, 0x2DE9, 0x2DEA, 0x2DEB, 0x2DEC, 0x2DED, 0x2DEE,
		0x2DEF, 0x2DF0, 0x2DF1, 0x2DF2, 0x2DF3, 0x2DF4, 0x2DF5, 0x2DF6, 0x2DF7, 0x2DF8, 0x2DF9, 0x2DFA,
		0x2DFB, 0x2DFC, 0x2DFD, 0x2DFE, 0x2DFF, 0xA66F, 0xA67C, 0xA67D, 0xA6F0, 0xA6F1, 0xA8E0, 0xA8E1,
		0xA8E2, 0xA8E3, 0xA8E4, 0xA8E5, 0xA8E6, 0xA8E7, 0xA8E8, 0xA8E9, 0xA8EA, 0xA8EB, 0xA8EC, 0xA8ED,
		0xA8EE, 0xA8EF, 0xA8F0, 0xA8F1, 0xAAB0, 0xAAB2, 0xAAB3, 0xAAB7, 0xAAB8, 0xAABE, 0xAABF, 0xAAC1,
		0xFE20, 0xFE21, 0xFE22, 0xFE23, 0xFE24, 0xFE25, 0xFE26, 0x10A0F, 0x10A38, 0x1D185, 0x1D186, 0x1D187,
		0x1D188, 0x1D189, 0x1D1AA, 0x1D1AB, 0x1D1AC, 0x1D1AD, 0x1D242, 0x1D243, 0x1D244
	];

	/// <summary>The way this client draws pictures, best first, or none.</summary>
	internal static TerminalFeatures Method(TerminalFeatures features) =>
		(features & TerminalFeatures.KittyGraphics) != 0 ? TerminalFeatures.KittyGraphics
		: (features & TerminalFeatures.InlineImages) != 0 ? TerminalFeatures.InlineImages
		: (features & TerminalFeatures.Sixel) != 0 ? TerminalFeatures.Sixel
		: (features & TerminalFeatures.BlockArt) != 0 ? TerminalFeatures.BlockArt
		: TerminalFeatures.None;

	/// <summary>Whether <paramref name="cells"/> can be drawn by <paramref name="method"/> at all.</summary>
	internal static bool CanDraw(TerminalFeatures method, PictureCellsMarkup cells) =>
		cells.Columns > 0 && cells.Rows > 0 && cells.Row >= 0 && cells.Row < cells.Rows
		&& (method != TerminalFeatures.KittyGraphics || (cells.Columns <= Diacritics.Length && cells.Rows <= Diacritics.Length));

	/// <summary>
	/// Writes the row <paramref name="cells"/> stands for. The terminal is in <paramref name="effective"/> before
	/// and is left in it after.
	/// </summary>
	/// <param name="method">How the client draws pictures.</param>
	/// <param name="cells">The row.</param>
	/// <param name="picture">The pixels.</param>
	/// <param name="effective">The run's style.</param>
	/// <param name="options">What the client is sent.</param>
	/// <param name="output">Where the row's own text goes.</param>
	/// <param name="ahead">
	/// Where a picture's transmission goes, ahead of the row's text: it can be megabytes, and depends on no style
	/// or wrapper, so it is written once to the final output rather than copied through the run's buffers.
	/// </param>
	internal static void Write(
		TerminalFeatures method,
		PictureCellsMarkup cells,
		TerminalPicture picture,
		in AnsiStyle effective,
		AnsiOutputOptions options,
		IBufferWriter<char> output,
		IBufferWriter<char> ahead)
	{
		switch (method)
		{
			case TerminalFeatures.KittyGraphics:
				WriteKitty(cells, picture, effective, options, output, ahead);
				break;
			case TerminalFeatures.InlineImages:
			case TerminalFeatures.Sixel:
				WriteOverlay(method, cells, picture, options, output, ahead);
				break;
			case TerminalFeatures.BlockArt:
				WriteBlockArt(cells, picture, effective, options.ColorDepth, output);
				break;
		}
	}

	/// <summary>
	/// The Kitty image id for <paramref name="picture"/> drawn in <paramref name="cells"/>: a hash of its key and
	/// its size in cells, since each size is its own virtual placement. 24 bits, so it fits a truecolor
	/// foreground; never zero, which Kitty reads as no id.
	/// </summary>
	internal static uint KittyId(TerminalPicture picture, PictureCellsMarkup cells)
	{
		var hash = 2166136261u;
		foreach (var c in picture.Key) hash = (hash ^ c) * 16777619u;
		hash = (hash ^ (uint)cells.Columns) * 16777619u;
		hash = (hash ^ (uint)cells.Rows) * 16777619u;
		var id = (hash ^ (hash >> 24)) & 0xFFFFFF;
		return id == 0 ? 1 : id;
	}

	private static void WriteKitty(PictureCellsMarkup cells, TerminalPicture picture, in AnsiStyle effective, AnsiOutputOptions options,
		IBufferWriter<char> output, IBufferWriter<char> ahead)
	{
		var id = KittyId(picture, cells);
		if (options.Pictures?.MarkTransmitted(id) != false)
			ahead.Write(PictureEncodings.GetOrAdd(picture, new KittyKey(id, cells.Columns, cells.Rows, options.CellWidth, options.CellHeight), EncodeKitty));

		// The image id is the placeholder's foreground. It is written at truecolor whatever the client's depth:
		// it is not a colour anyone sees, and a terminal that reads Kitty graphics reads 24-bit SGR.
		var placeholder = new AnsiStyle { Foreground = new AnsiColor.Rgb((byte)(id >> 16), (byte)(id >> 8), (byte)id) };
		SgrWriter.Transition(effective, placeholder, output);

		// Each cell is the placeholder and two diacritics, at most six UTF-16 units.
		var span = output.GetSpan(cells.Columns * 6);
		var written = 0;
		var row = new Rune(Diacritics[cells.Row]);
		for (var column = 0; column < cells.Columns; column++)
		{
			written += new Rune(Placeholder).EncodeToUtf16(span[written..]);
			written += row.EncodeToUtf16(span[written..]);
			written += new Rune(Diacritics[column]).EncodeToUtf16(span[written..]);
		}
		output.Advance(written);
		SgrWriter.Transition(placeholder, effective, output);
	}

	private readonly record struct KittyKey(uint Id, int Columns, int Rows, int CellWidth, int CellHeight);

	/// <summary>
	/// The picture as a Kitty image with a virtual placement of the cells' size, sent quietly (<c>q=2</c>) so
	/// nothing comes back into the player's input. Kitty fits it to the cells keeping its shape; it is scaled
	/// down first to no more pixels than those cells hold. The whole transmission, every chunk, is one string,
	/// so a connection shown the picture after the first is sent a copy.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Sent as PNG (<c>f=100</c>) rather than zlib-compressed raw pixels (<c>f=32,o=z</c>): the PNG's row
	/// filtering and dropped alpha make it about half the size for the same compression time, and every
	/// terminal that speaks the protocol decodes PNG.
	/// </para>
	/// <para>
	/// A moving picture's first frame is the image; each later frame follows as a whole frame written over its
	/// base (<c>a=f,X=1</c>) with its own duration (<c>z</c>), then the first frame's duration is set and the
	/// animation started looping (<c>a=a,s=3,v=1</c>). The terminal plays it from then on; nothing more is sent.
	/// </para>
	/// </remarks>
	private static string EncodeKitty(TerminalPicture picture, KittyKey key)
	{
		var (width, height) = PictureScaler.FitWithin(picture.Width, picture.Height,
			key.Columns * key.CellWidth, key.Rows * key.CellHeight, upscale: false);
		var text = new StringBuilder();
		AppendKittyChunks(text, picture.Rgba.Span, picture, width, height,
			string.Create(CultureInfo.InvariantCulture, $"a=T,U=1,i={key.Id},f=100,c={key.Columns},r={key.Rows},q=2"), "");

		if (picture.Frames.Count > 1)
		{
			for (var frame = 1; frame < picture.Frames.Count; frame++)
			{
				AppendKittyChunks(text, picture.Frames[frame].Rgba.Span, picture, width, height,
					string.Create(CultureInfo.InvariantCulture, $"a=f,i={key.Id},f=100,X=1,z={KittyGap(picture.Frames[frame].Duration)},q=2"),
					"a=f,");
			}

			text.Append(CultureInfo.InvariantCulture,
				$"{Esc}_Ga=a,i={key.Id},r=1,z={KittyGap(picture.Frames[0].Duration)},q=2{StringTerminator}");
			text.Append(CultureInfo.InvariantCulture, $"{Esc}_Ga=a,i={key.Id},s=3,v=1,q=2{StringTerminator}");
		}

		return text.ToString();
	}

	/// <summary>
	/// A frame's duration as Kitty's gap in milliseconds: at least one, since Kitty ignores a zero gap and
	/// reads a negative one as no gap at all.
	/// </summary>
	private static int KittyGap(TimeSpan duration) => (int)Math.Clamp(duration.TotalMilliseconds, 1, int.MaxValue);

	/// <summary>
	/// <paramref name="rgba"/> scaled to <paramref name="width"/> × <paramref name="height"/>, as a PNG in base64
	/// chunks of at most <see cref="KittyChunk"/>: the first carrying <paramref name="first"/>, the rest
	/// <paramref name="rest"/> (which a frame needs, <c>a=f,</c>) and each whether more follow.
	/// </summary>
	private static void AppendKittyChunks(StringBuilder text, ReadOnlySpan<byte> rgba, TerminalPicture picture, int width, int height,
		string first, string rest)
	{
		var pixels = PictureScaler.Scale(rgba, picture.Width, picture.Height, width, height);
		var payload = Convert.ToBase64String(PngWriter.Encode(pixels, width, height));
		text.EnsureCapacity(text.Length + payload.Length + 96 + payload.Length / KittyChunk * 20);

		var offset = 0;
		do
		{
			var length = Math.Min(KittyChunk, payload.Length - offset);
			var more = offset + length < payload.Length ? 1 : 0;
			text.Append(Esc + "_G");
			if (offset == 0)
			{
				text.Append(first).Append(",m=").Append(more);
			}
			else
			{
				text.Append(rest).Append("m=").Append(more).Append(",q=2");
			}

			text.Append(';').Append(payload, offset, length).Append(StringTerminator);
			offset += length;
		}
		while (offset < payload.Length);
	}

	/// <summary>
	/// iTerm2 and sixel pictures are pixels laid over the screen, not text, so they are drawn over cells the text
	/// leaves alone. On the picture's first row the cursor makes room below for every row (<c>ESC D</c>, which
	/// scrolls at the bottom of the screen and keeps the column, and one row more for a sixel terminal that moves
	/// down past the picture), comes back up, and draws the picture between a cursor save and restore. Every row
	/// then steps over the picture's cells (<c>CSI n C</c>) rather than writing spaces that would erase it.
	/// </summary>
	private static void WriteOverlay(TerminalFeatures method, PictureCellsMarkup cells, TerminalPicture picture, AnsiOutputOptions options,
		IBufferWriter<char> output, IBufferWriter<char> ahead)
	{
		if (cells.Row == 0)
		{
			for (var row = 0; row < cells.Rows; row++) ahead.Write(Esc + "D");
			ahead.Write($"{Esc}[{cells.Rows}A");
			ahead.Write(Esc + "7");
			var key = new OverlayKey(method, cells.Columns, cells.Rows, options.CellWidth, options.CellHeight);
			ahead.Write(PictureEncodings.GetOrAdd(picture, key,
				static (picture, key) => key.Method == TerminalFeatures.InlineImages ? EncodeInlineImage(picture, key) : EncodeSixel(picture, key)));
			ahead.Write(Esc + "8");
		}

		output.Write($"{Esc}[{cells.Columns}C");
	}

	private readonly record struct OverlayKey(TerminalFeatures Method, int Columns, int Rows, int CellWidth, int CellHeight);

	/// <summary>An iTerm2 inline image, a PNG sized in cells, its shape kept.</summary>
	private static string EncodeInlineImage(TerminalPicture picture, OverlayKey key)
	{
		var (width, height) = PictureScaler.FitWithin(picture.Width, picture.Height,
			key.Columns * key.CellWidth, key.Rows * key.CellHeight, upscale: false);
		var png = PngWriter.Encode(PictureScaler.Scale(picture.Rgba.Span, picture.Width, picture.Height, width, height), width, height);
		return string.Create(CultureInfo.InvariantCulture,
			$"{Esc}]1337;File=inline=1;size={png.Length};width={key.Columns};height={key.Rows};preserveAspectRatio=1:{Convert.ToBase64String(png)}\a");
	}

	/// <summary>
	/// A sixel picture exactly the cells' size in pixels — its height down to a whole number of six-pixel bands
	/// so it never spills into the row below — the picture centred in it, the rest left transparent.
	/// </summary>
	private static string EncodeSixel(TerminalPicture picture, OverlayKey key)
	{
		var boxWidth = key.Columns * key.CellWidth;
		var boxHeight = Math.Max(6, key.Rows * key.CellHeight / 6 * 6);
		return SixelWriter.Encode(PictureScaler.Letterbox(picture, boxWidth, boxHeight), boxWidth, boxHeight);
	}

	/// <summary>
	/// One row of cells as upper and lower half blocks, each cell two pixels tall: the upper pixel the
	/// foreground of <c>▀</c>, the lower its background. A transparent half shows the run's own background.
	/// Every row of the picture is made at once and kept, since the rows are drawn one after another.
	/// </summary>
	private static void WriteBlockArt(PictureCellsMarkup cells, TerminalPicture picture, in AnsiStyle effective, AnsiColorDepth depth, IBufferWriter<char> output)
	{
		var rows = PictureEncodings.GetOrAdd(picture, new BlockKey(cells.Columns, cells.Rows, depth, effective), EncodeBlockArt);
		output.Write(rows[cells.Row]);
	}

	private readonly record struct BlockKey(int Columns, int Rows, AnsiColorDepth Depth, AnsiStyle Effective);

	/// <summary>Every row of the picture as half blocks, each starting and ending in the run's own style.</summary>
	private static string[] EncodeBlockArt(TerminalPicture picture, BlockKey key)
	{
		var width = key.Columns;
		var pixels = PictureScaler.Letterbox(picture, width, key.Rows * 2);
		var plain = key.Effective with { Foreground = null };
		var rows = new string[key.Rows];
		using var line = new PooledCharWriter(width * 40);

		for (var row = 0; row < key.Rows; row++)
		{
			line.Clear();
			var current = key.Effective;
			var top = row * 2 * width * 4;
			var bottom = top + width * 4;
			for (var column = 0; column < width; column++)
			{
				var upper = Pixel(pixels, top + column * 4);
				var lower = Pixel(pixels, bottom + column * 4);
				var (style, glyph) = (upper, lower) switch
				{
					({ } u, { } l) => (plain with { Foreground = u, Background = l }, '▀'),
					({ } u, null) => (plain with { Foreground = u }, '▀'),
					(null, { } l) => (plain with { Foreground = l }, '▄'),
					_ => (plain, ' '),
				};
				style = style.AtDepth(key.Depth);
				SgrWriter.Transition(current, style, line);
				current = style;
				line.GetSpan(1)[0] = glyph;
				line.Advance(1);
			}

			SgrWriter.Transition(current, key.Effective, line);
			rows[row] = line.WrittenSpan.ToString();
		}

		return rows;
	}

	/// <summary>The colour of the pixel at <paramref name="offset"/>, or null when it is mostly transparent.</summary>
	private static AnsiColor? Pixel(byte[] pixels, int offset) =>
		pixels[offset + 3] < 128 ? null : new AnsiColor.Rgb(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
}
