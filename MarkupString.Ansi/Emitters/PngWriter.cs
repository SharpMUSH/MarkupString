using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
namespace MarkupString.Ansi;

/// <summary>
/// A small, fast PNG writer: 8-bit RGB, or RGBA when any pixel is not opaque, every row but the first
/// filtered Up, in one IDAT.
/// </summary>
/// <remarks>
/// Measured on 512x384 pictures, Up filtering roughly halves the compressed size against no filter and
/// makes zlib faster too, and dropping an alpha channel nobody uses saves a quarter of the input. zlib
/// level 2 is the knee: within 5-15% of level 6's size at a third to a half of its time. A per-row filter
/// choice (as libpng makes) would compress a little better at several times the cost; fpng makes the same
/// Up-only choice.
/// </remarks>
internal static class PngWriter
{
	private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	private static readonly uint[] CrcTable = BuildCrcTable();

	/// <summary>The zlib level pictures are compressed at.</summary>
	internal const int CompressionLevel = 2;

	/// <summary><paramref name="rgba"/>, <paramref name="width"/> by <paramref name="height"/>, as a PNG file.</summary>
	internal static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
	{
		var opaque = true;
		for (var i = 3; i < rgba.Length && opaque; i += 4) opaque = rgba[i] == 255;
		var channels = opaque ? 3 : 4;
		var stride = width * channels;
		var rawLength = (stride + 1) * height;
		var raw = ArrayPool<byte>.Shared.Rent(rawLength);
		var previous = ArrayPool<byte>.Shared.Rent(stride);
		var current = ArrayPool<byte>.Shared.Rent(stride);
		try
		{
			for (var y = 0; y < height; y++)
			{
				var source = rgba.Slice(y * width * 4, width * 4);
				var row = current.AsSpan(0, stride);
				if (opaque)
				{
					for (int i = 0, o = 0; i < source.Length; i += 4, o += 3)
					{
						row[o] = source[i];
						row[o + 1] = source[i + 1];
						row[o + 2] = source[i + 2];
					}
				}
				else
				{
					source.CopyTo(row);
				}

				var line = raw.AsSpan(y * (stride + 1), stride + 1);
				if (y == 0)
				{
					line[0] = 0; // None
					row.CopyTo(line[1..]);
				}
				else
				{
					line[0] = 2; // Up: each byte less the one above it.
					var above = previous.AsSpan(0, stride);
					var filtered = line[1..];
					var i = 0;
					if (Vector.IsHardwareAccelerated)
					{
						for (; i <= stride - Vector<byte>.Count; i += Vector<byte>.Count)
							(new Vector<byte>(row[i..]) - new Vector<byte>(above[i..])).CopyTo(filtered[i..]);
					}

					for (; i < stride; i++) filtered[i] = (byte)(row[i] - above[i]);
				}

				(previous, current) = (current, previous);
			}

			byte[] compressed;
			using (var memory = new MemoryStream(rawLength / 2 + 64))
			{
				using (var zlib = new ZLibStream(memory, new ZLibCompressionOptions { CompressionLevel = CompressionLevel }, leaveOpen: true))
					zlib.Write(raw, 0, rawLength);
				compressed = memory.ToArray();
			}

			var header = new byte[13];
			BinaryPrimitives.WriteInt32BigEndian(header, width);
			BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
			header[8] = 8; // bit depth
			header[9] = (byte)(opaque ? 2 : 6); // RGB or RGBA

			using var png = new MemoryStream(compressed.Length + 64);
			png.Write(Signature);
			WriteChunk(png, "IHDR"u8, header);
			WriteChunk(png, "IDAT"u8, compressed);
			WriteChunk(png, "IEND"u8, []);
			return png.ToArray();
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(raw);
			ArrayPool<byte>.Shared.Return(previous);
			ArrayPool<byte>.Shared.Return(current);
		}
	}

	private static void WriteChunk(Stream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
	{
		Span<byte> word = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
		png.Write(word);
		png.Write(type);
		png.Write(data);
		var crc = Crc(Crc(0xFFFFFFFFu, type), data) ^ 0xFFFFFFFFu;
		BinaryPrimitives.WriteUInt32BigEndian(word, crc);
		png.Write(word);
	}

	private static uint Crc(uint crc, ReadOnlySpan<byte> data)
	{
		foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
		return crc;
	}

	private static uint[] BuildCrcTable()
	{
		var table = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			var c = n;
			for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
			table[n] = c;
		}
		return table;
	}
}
