using System.Buffers.Binary;
using System.IO.Compression;
namespace MarkupString.Ansi;

/// <summary>The smallest PNG writer that does the job: 8-bit RGBA, one IDAT, no filtering.</summary>
internal static class PngWriter
{
	private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	private static readonly uint[] CrcTable = BuildCrcTable();

	/// <summary><paramref name="rgba"/>, <paramref name="width"/> by <paramref name="height"/>, as a PNG file.</summary>
	internal static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
	{
		var stride = width * 4;
		var raw = new byte[(stride + 1) * height];
		for (var y = 0; y < height; y++)
			rgba.Slice(y * stride, stride).CopyTo(raw.AsSpan(y * (stride + 1) + 1));

		byte[] compressed;
		using (var memory = new MemoryStream())
		{
			using (var zlib = new ZLibStream(memory, CompressionLevel.Optimal, leaveOpen: true))
				zlib.Write(raw);
			compressed = memory.ToArray();
		}

		var header = new byte[13];
		BinaryPrimitives.WriteInt32BigEndian(header, width);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
		header[8] = 8; // bit depth
		header[9] = 6; // RGBA

		using var png = new MemoryStream();
		png.Write(Signature);
		WriteChunk(png, "IHDR"u8, header);
		WriteChunk(png, "IDAT"u8, compressed);
		WriteChunk(png, "IEND"u8, []);
		return png.ToArray();
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
