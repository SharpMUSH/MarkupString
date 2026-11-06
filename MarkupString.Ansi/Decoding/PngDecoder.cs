using System.Buffers.Binary;
using System.IO.Compression;
namespace MarkupString.Ansi.Decoding;

/// <summary>
/// Decodes a PNG to RGBA: every colour type and bit depth, transparency (<c>tRNS</c>) and Adam7 interlacing.
/// Ancillary chunks other than <c>tRNS</c> are skipped, gamma included.
/// </summary>
internal static class PngDecoder
{
	private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	// Adam7: start column, start row, column step, row step.
	private static readonly (int X, int Y, int Dx, int Dy)[] Passes =
	[
		(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2),
	];

	/// <summary>Whether <paramref name="data"/> starts like a PNG.</summary>
	public static bool IsPng(ReadOnlySpan<byte> data) => data.StartsWith(Signature);

	/// <summary>Reads the size from the header alone.</summary>
	public static bool TryReadSize(ReadOnlySpan<byte> data, out int width, out int height)
	{
		width = height = 0;
		if (data.Length < 24 || !IsPng(data)) return false;
		width = BinaryPrimitives.ReadInt32BigEndian(data[16..]);
		height = BinaryPrimitives.ReadInt32BigEndian(data[20..]);
		return width > 0 && height > 0;
	}

	/// <summary>Decodes <paramref name="data"/>, refusing a picture of more than <paramref name="maxPixels"/>.</summary>
	public static bool TryDecode(ReadOnlySpan<byte> data, long maxPixels, out int width, out int height, out byte[] rgba)
	{
		width = height = 0;
		rgba = [];
		try
		{
			return Decode(data, maxPixels, out width, out height, out rgba);
		}
		catch (InvalidDataException)
		{
			return false;
		}
	}

	private static bool Decode(ReadOnlySpan<byte> data, long maxPixels, out int width, out int height, out byte[] rgba)
	{
		width = height = 0;
		rgba = [];
		if (!IsPng(data)) return false;

		var offset = Signature.Length;
		int depth = 0, colorType = -1, interlace = 0;
		byte[]? palette = null;
		byte[]? transparency = null;
		using var compressed = new MemoryStream();

		while (offset + 8 <= data.Length)
		{
			var length = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
			if (length < 0 || offset + 12L + length > data.Length) return false;
			var type = data.Slice(offset + 4, 4);
			var body = data.Slice(offset + 8, length);
			offset += 12 + length;

			if (type.SequenceEqual("IHDR"u8))
			{
				if (length < 13) return false;
				width = BinaryPrimitives.ReadInt32BigEndian(body);
				height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
				depth = body[8];
				colorType = body[9];
				interlace = body[12];
				if (width <= 0 || height <= 0 || (long)width * height > maxPixels) return false;
				if (body[10] != 0 || body[11] != 0 || interlace > 1) return false;
				if (!Valid(colorType, depth)) return false;
			}
			else if (type.SequenceEqual("PLTE"u8))
			{
				palette = body.ToArray();
			}
			else if (type.SequenceEqual("tRNS"u8))
			{
				transparency = body.ToArray();
			}
			else if (type.SequenceEqual("IDAT"u8))
			{
				compressed.Write(body);
			}
			else if (type.SequenceEqual("IEND"u8))
			{
				break;
			}
		}

		if (colorType < 0 || (colorType == 3 && palette is null)) return false;

		compressed.Position = 0;
		using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
		var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
		var bitsPerPixel = channels * depth;
		var pixelBytes = Math.Max(1, bitsPerPixel / 8);

		var expected = 0L;
		foreach (var (x, y, dx, dy) in interlace == 1 ? Passes : [(0, 0, 1, 1)])
		{
			var columns = (width - x + dx - 1) / dx;
			var rows = (height - y + dy - 1) / dy;
			if (columns > 0 && rows > 0) expected += rows * (1 + ((long)columns * bitsPerPixel + 7) / 8);
		}

		if (expected > int.MaxValue) return false;
		var raw = new byte[expected];
		var read = 0;
		while (read < raw.Length)
		{
			var n = inflater.Read(raw, read, raw.Length - read);
			if (n == 0) return false;
			read += n;
		}

		rgba = new byte[(long)width * height * 4];
		var at = 0;
		foreach (var (x0, y0, dx, dy) in interlace == 1 ? Passes : [(0, 0, 1, 1)])
		{
			var columns = (width - x0 + dx - 1) / dx;
			var rows = (height - y0 + dy - 1) / dy;
			if (columns <= 0 || rows <= 0) continue;

			var stride = (int)(((long)columns * bitsPerPixel + 7) / 8);
			var previous = new byte[stride];
			var current = new byte[stride];
			for (var row = 0; row < rows; row++)
			{
				var filter = raw[at];
				raw.AsSpan(at + 1, stride).CopyTo(current);
				at += 1 + stride;
				if (!Unfilter(filter, current, previous, pixelBytes)) return false;

				var y = y0 + row * dy;
				for (var column = 0; column < columns; column++)
				{
					var x = x0 + column * dx;
					Pixel(current, column, colorType, depth, palette, transparency, rgba.AsSpan((y * width + x) * 4, 4));
				}

				(previous, current) = (current, previous);
			}
		}

		return true;
	}

	private static bool Valid(int colorType, int depth) => colorType switch
	{
		0 => depth is 1 or 2 or 4 or 8 or 16,
		3 => depth is 1 or 2 or 4 or 8,
		2 or 4 or 6 => depth is 8 or 16,
		_ => false,
	};

	private static bool Unfilter(byte filter, Span<byte> line, ReadOnlySpan<byte> previous, int bpp)
	{
		switch (filter)
		{
			case 0:
				return true;
			case 1:
				for (var i = bpp; i < line.Length; i++) line[i] += line[i - bpp];
				return true;
			case 2:
				for (var i = 0; i < line.Length; i++) line[i] += previous[i];
				return true;
			case 3:
				for (var i = 0; i < line.Length; i++)
				{
					var left = i >= bpp ? line[i - bpp] : 0;
					line[i] += (byte)((left + previous[i]) >> 1);
				}

				return true;
			case 4:
				for (var i = 0; i < line.Length; i++)
				{
					int a = i >= bpp ? line[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
					int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
					line[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
				}

				return true;
			default:
				return false;
		}
	}

	private static void Pixel(ReadOnlySpan<byte> line, int column, int colorType, int depth, byte[]? palette, byte[]? transparency, Span<byte> output)
	{
		switch (colorType)
		{
			case 0:
				{
					var value = Sample(line, column, depth);
					var gray = Scale(value, depth);
					output[0] = output[1] = output[2] = gray;
					output[3] = transparency is { Length: >= 2 } && BinaryPrimitives.ReadUInt16BigEndian(transparency) == value ? (byte)0 : (byte)255;
					break;
				}
			case 2:
				{
					int r = Sample(line, column * 3, depth), g = Sample(line, column * 3 + 1, depth), b = Sample(line, column * 3 + 2, depth);
					output[0] = Scale(r, depth);
					output[1] = Scale(g, depth);
					output[2] = Scale(b, depth);
					output[3] = transparency is { Length: >= 6 }
						&& BinaryPrimitives.ReadUInt16BigEndian(transparency) == r
						&& BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) == g
						&& BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4)) == b
							? (byte)0
							: (byte)255;
					break;
				}
			case 3:
				{
					var index = Sample(line, column, depth);
					if (index * 3 + 2 < palette!.Length)
					{
						output[0] = palette[index * 3];
						output[1] = palette[index * 3 + 1];
						output[2] = palette[index * 3 + 2];
					}

					output[3] = transparency is not null && index < transparency.Length ? transparency[index] : (byte)255;
					break;
				}
			case 4:
				output[0] = output[1] = output[2] = Scale(Sample(line, column * 2, depth), depth);
				output[3] = Scale(Sample(line, column * 2 + 1, depth), depth);
				break;
			default:
				for (var i = 0; i < 4; i++) output[i] = Scale(Sample(line, column * 4 + i, depth), depth);
				break;
		}
	}

	/// <summary>Sample <paramref name="index"/> of a line, at its depth.</summary>
	private static int Sample(ReadOnlySpan<byte> line, int index, int depth) => depth switch
	{
		16 => BinaryPrimitives.ReadUInt16BigEndian(line[(index * 2)..]),
		8 => line[index],
		_ => (line[index * depth / 8] >> (8 - depth - index * depth % 8)) & ((1 << depth) - 1),
	};

	private static byte Scale(int value, int depth) => depth switch
	{
		16 => (byte)(value >> 8),
		8 => (byte)value,
		_ => (byte)(value * 255 / ((1 << depth) - 1)),
	};
}
