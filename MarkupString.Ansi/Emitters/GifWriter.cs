namespace MarkupString.Ansi;

/// <summary>
/// A small GIF89a writer for moving pictures: every frame a whole picture, one shared palette, looping
/// for ever. For terminals that animate a GIF they are sent (iTerm2, WezTerm) but take no frames one by one.
/// </summary>
/// <remarks>
/// <para>
/// One palette for every frame, made by median cut over the colours of all of them, so a colour does not
/// change from frame to frame and nothing shimmers; up to 255 colours, and one more index for transparency
/// when a pixel is less than half opaque. Colours are binned to five bits a channel, so the palette is chosen
/// from at most 32768 bins and each pixel is mapped by a table look-up rather than a nearest-colour search.
/// There is no dithering: dither noise differs from frame to frame and crawls when the picture moves.
/// </para>
/// <para>
/// Each frame is drawn on a cleared canvas (disposal 2), since every frame is the whole picture and a
/// transparent pixel must not show the frame before it.
/// </para>
/// </remarks>
internal static class GifWriter
{
	private const int Bins = 32768;

	/// <summary>
	/// <paramref name="frames"/>, each <paramref name="width"/> × <paramref name="height"/> RGBA shown for its
	/// duration, as a looping GIF file.
	/// </summary>
	internal static byte[] Encode(IReadOnlyList<TerminalPictureFrame> frames, int width, int height)
	{
		var histogram = new int[Bins];
		var transparent = false;
		foreach (var frame in frames)
		{
			var pixels = frame.Rgba.Span;
			for (var i = 0; i < pixels.Length; i += 4)
			{
				if (pixels[i + 3] < 128) transparent = true;
				else histogram[Bin(pixels, i)]++;
			}
		}

		var (palette, lookup) = MedianCut(histogram, transparent ? 255 : 256);
		var transparentIndex = palette.Count;
		var colours = palette.Count + (transparent ? 1 : 0);
		var bits = 1;
		while (1 << bits < colours) bits++;

		using var output = new MemoryStream();
		output.Write("GIF89a"u8);
		WriteShort(output, width);
		WriteShort(output, height);
		output.WriteByte((byte)(0x80 | 0x70 | (bits - 1))); // a global colour table of 2^bits entries
		output.WriteByte(0); // background index
		output.WriteByte(0); // pixel aspect ratio
		for (var i = 0; i < 1 << bits; i++)
		{
			var (r, g, b) = i < palette.Count ? palette[i] : ((byte)0, (byte)0, (byte)0);
			output.WriteByte(r);
			output.WriteByte(g);
			output.WriteByte(b);
		}

		// NETSCAPE2.0: loop for ever.
		output.Write([0x21, 0xFF, 0x0B, .. "NETSCAPE2.0"u8, 0x03, 0x01, 0x00, 0x00, 0x00]);

		var indices = new byte[width * height];
		var minimumCodeSize = Math.Max(2, bits);
		foreach (var frame in frames)
		{
			var pixels = frame.Rgba.Span;
			for (int i = 0, p = 0; p < indices.Length; i += 4, p++)
				indices[p] = pixels[i + 3] < 128 ? (byte)transparentIndex : lookup[Bin(pixels, i)];

			// Graphic control: restore to background after the frame, the delay in hundredths, transparency.
			var centiseconds = (int)Math.Clamp(Math.Round(frame.Shown.TotalMilliseconds / 10), 2, ushort.MaxValue);
			output.Write([0x21, 0xF9, 0x04, (byte)((2 << 2) | (transparent ? 1 : 0))]);
			WriteShort(output, centiseconds);
			output.WriteByte(transparent ? (byte)transparentIndex : (byte)0);
			output.WriteByte(0);

			output.WriteByte(0x2C);
			WriteShort(output, 0);
			WriteShort(output, 0);
			WriteShort(output, width);
			WriteShort(output, height);
			output.WriteByte(0); // no local colour table, not interlaced
			output.WriteByte((byte)minimumCodeSize);
			WriteLzw(output, indices, minimumCodeSize);
		}

		output.WriteByte(0x3B);
		return output.ToArray();
	}

	private static int Bin(ReadOnlySpan<byte> pixels, int offset) =>
		(pixels[offset] >> 3) << 10 | (pixels[offset + 1] >> 3) << 5 | pixels[offset + 2] >> 3;

	private static void WriteShort(Stream output, int value)
	{
		output.WriteByte((byte)value);
		output.WriteByte((byte)(value >> 8));
	}

	/// <summary>
	/// A palette of at most <paramref name="maxColours"/> for the bins in <paramref name="histogram"/>, and each
	/// bin's index in it: the box of bins with the widest channel, weighted by how many pixels it holds, is split
	/// at its median until there are enough boxes, and each box's colour is the average of its pixels.
	/// </summary>
	private static (List<(byte R, byte G, byte B)> Palette, byte[] Lookup) MedianCut(int[] histogram, int maxColours)
	{
		var used = new List<int>();
		for (var bin = 0; bin < Bins; bin++)
			if (histogram[bin] > 0) used.Add(bin);

		var boxes = new List<(int Start, int Length)>();
		var bins = used.ToArray();
		if (bins.Length > 0) boxes.Add((0, bins.Length));

		while (boxes.Count < maxColours)
		{
			var best = -1;
			long bestScore = 0;
			var bestChannel = 0;
			for (var b = 0; b < boxes.Count; b++)
			{
				var (start, length) = boxes[b];
				if (length < 2) continue;
				var (channel, range) = WidestChannel(bins.AsSpan(start, length));
				long pixels = 0;
				for (var i = start; i < start + length; i++) pixels += histogram[bins[i]];
				var score = range * pixels;
				if (range > 0 && score > bestScore)
				{
					(best, bestScore, bestChannel) = (b, score, channel);
				}
			}

			if (best < 0) break;

			var (boxStart, boxLength) = boxes[best];
			var span = bins.AsSpan(boxStart, boxLength);
			var shift = 10 - bestChannel * 5;
			span.Sort((x, y) => ((x >> shift) & 31).CompareTo((y >> shift) & 31));

			long total = 0;
			foreach (var bin in span) total += histogram[bin];
			long running = 0;
			var split = 1;
			for (var i = 0; i < span.Length - 1; i++)
			{
				running += histogram[span[i]];
				split = i + 1;
				if (running * 2 >= total) break;
			}

			boxes[best] = (boxStart, split);
			boxes.Add((boxStart + split, boxLength - split));
		}

		var palette = new List<(byte, byte, byte)>(boxes.Count);
		var lookup = new byte[Bins];
		foreach (var (start, length) in boxes)
		{
			long r = 0, g = 0, b = 0, count = 0;
			for (var i = start; i < start + length; i++)
			{
				var bin = bins[i];
				long weight = histogram[bin];
				r += (((bin >> 10) & 31) * 8 + 4) * weight;
				g += (((bin >> 5) & 31) * 8 + 4) * weight;
				b += ((bin & 31) * 8 + 4) * weight;
				count += weight;
				lookup[bin] = (byte)palette.Count;
			}

			palette.Add(((byte)(r / count), (byte)(g / count), (byte)(b / count)));
		}

		return (palette, lookup);
	}

	/// <summary>Which channel (0 red, 1 green, 2 blue) spans the most bins in <paramref name="bins"/>, and by how much.</summary>
	private static (int Channel, int Range) WidestChannel(ReadOnlySpan<int> bins)
	{
		var (best, bestRange) = (0, -1);
		for (var channel = 0; channel < 3; channel++)
		{
			var shift = 10 - channel * 5;
			int low = 31, high = 0;
			foreach (var bin in bins)
			{
				var value = (bin >> shift) & 31;
				low = Math.Min(low, value);
				high = Math.Max(high, value);
			}

			if (high - low > bestRange) (best, bestRange) = (channel, high - low);
		}

		return (best, bestRange);
	}

	/// <summary>
	/// <paramref name="indices"/> LZW-compressed as GIF image data, in sub-blocks of at most 255 bytes and a
	/// terminator. The code table is cleared when it is full.
	/// </summary>
	private static void WriteLzw(Stream output, ReadOnlySpan<byte> indices, int minimumCodeSize)
	{
		var clear = 1 << minimumCodeSize;
		var end = clear + 1;
		var codes = new Dictionary<int, int>();
		var codeSize = minimumCodeSize + 1;
		var last = end;
		var bits = new BitWriter(output);

		bits.Write(clear, codeSize);
		var current = (int)indices[0];
		for (var i = 1; i < indices.Length; i++)
		{
			var next = indices[i];
			var key = current << 8 | next;
			if (codes.TryGetValue(key, out var known))
			{
				current = known;
				continue;
			}

			bits.Write(current, codeSize);
			codes[key] = ++last;
			if (last >= 1 << codeSize) codeSize++;
			if (last == 4095)
			{
				bits.Write(clear, codeSize);
				codes.Clear();
				codeSize = minimumCodeSize + 1;
				last = end;
			}

			current = next;
		}

		// A decoder adds a table entry on reading this last code too, and widens its codes if that fills the
		// size; the clear that follows has to be written at the width it will read.
		bits.Write(current, codeSize);
		if (++last >= 1 << codeSize) codeSize++;
		bits.Write(clear, codeSize);
		bits.Write(end, minimumCodeSize + 1);
		bits.Flush();
		output.WriteByte(0);
	}

	/// <summary>Packs codes least significant bit first into GIF sub-blocks.</summary>
	private sealed class BitWriter(Stream output)
	{
		private readonly byte[] _block = new byte[255];
		private int _length;
		private int _buffer;
		private int _count;

		public void Write(int code, int size)
		{
			_buffer |= code << _count;
			_count += size;
			while (_count >= 8)
			{
				Put((byte)_buffer);
				_buffer >>= 8;
				_count -= 8;
			}
		}

		public void Flush()
		{
			if (_count > 0) Put((byte)_buffer);
			_buffer = _count = 0;
			if (_length > 0) WriteBlock();
		}

		private void Put(byte value)
		{
			_block[_length++] = value;
			if (_length == _block.Length) WriteBlock();
		}

		private void WriteBlock()
		{
			output.WriteByte((byte)_length);
			output.Write(_block, 0, _length);
			_length = 0;
		}
	}
}
