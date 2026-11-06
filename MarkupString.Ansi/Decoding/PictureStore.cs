using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
namespace MarkupString.Ansi.Decoding;

/// <summary>
/// A picture a decoder read from the server: its pixels when it could decode them, and the file as it came when
/// there was one. <see cref="ImageMarkup"/>s refer to it by <see cref="Source"/>.
/// </summary>
public sealed class StoredPicture
{
	private readonly ReadOnlyMemory<byte> _file;
	private readonly string? _fileType;
	private ReadOnlyMemory<byte> _encoded;

	internal StoredPicture(string key, int width, int height, TerminalPicture? pixels, ReadOnlyMemory<byte> file, string? fileType)
	{
		Key = key;
		Width = width;
		Height = height;
		Pixels = pixels;
		_file = file;
		_fileType = file.IsEmpty ? null : fileType;
	}

	/// <summary>What identifies it: a hash of its contents, so the same picture sent twice is stored once.</summary>
	public string Key { get; }

	/// <summary>Its width in pixels.</summary>
	public int Width { get; }

	/// <summary>Its height in pixels.</summary>
	public int Height { get; }

	/// <summary>What an <see cref="ImageMarkup.Source"/> that shows it says: <c>picture:</c> and the key.</summary>
	public string Source => PictureStore.Scheme + Key;

	/// <summary>
	/// Its pixels, or null for a file this package cannot decode (a JPEG, a GIF), which only a browser can show. A
	/// moving picture's frames are in <see cref="TerminalPicture.Frames"/>; they can grow after the picture is first
	/// stored, as the server sends them.
	/// </summary>
	public TerminalPicture? Pixels { get; private set; }

	/// <summary>Whether it moves: it has more than one frame.</summary>
	public bool IsMoving => Pixels is { Frames.Count: > 1 };

	/// <summary>
	/// The type of <see cref="Encoded"/>: the file's own, such as <c>image/png</c>; <c>image/gif</c> for a moving
	/// picture sent as frames; <c>image/png</c> for still pixels.
	/// </summary>
	public string MediaType => IsMoving ? "image/gif" : _fileType ?? "image/png";

	/// <summary>What the server sent, as a file: its own for a file, a looping GIF for frames, a PNG for still pixels.</summary>
	public ReadOnlyMemory<byte> Encoded
	{
		get
		{
			if (!_encoded.IsEmpty) return _encoded;
			if (IsMoving) return _encoded = GifWriter.Encode(Pixels!.Frames, Width, Height);
			if (!_file.IsEmpty) return _file;
			return _encoded = Pixels is { } pixels ? PngWriter.Encode(pixels.Rgba.Span, pixels.Width, pixels.Height) : default;
		}
	}

	/// <summary>What the server sent as the file, or empty when it sent bare pixels.</summary>
	public ReadOnlyMemory<byte> File => _file;

	/// <summary>How many bytes it holds.</summary>
	public long Size => _file.Length + PixelBytes(Pixels);

	/// <summary>The picture as a <c>data:</c> address, for an <c>&lt;img&gt;</c>.</summary>
	public string ToDataUrl() => $"data:{MediaType};base64,{Convert.ToBase64String(Encoded.Span)}";

	internal void ReplacePixels(TerminalPicture pixels)
	{
		Pixels = pixels;
		_encoded = default;
	}

	internal static long PixelBytes(TerminalPicture? pixels) =>
		pixels is null ? 0 : pixels.Frames.Count > 0 ? pixels.Frames.Sum(frame => (long)frame.Rgba.Length) : pixels.Rgba.Length;
}

/// <summary>
/// The pictures one connection's decoder read, within a budget of bytes; the least recently used go first. It is
/// also the <see cref="ITerminalPictureSource"/> that draws them again in a terminal. Get a decoder's with
/// <see cref="PicturesPlugin.Pictures(MarkupString.Decoding.Decoder)"/>.
/// </summary>
/// <remarks>
/// A picture the server deletes (Kitty <c>a=d</c>) is only forgotten by its id: lines already read still show it,
/// until the budget pushes it out. A line whose picture is gone shows its text instead.
/// </remarks>
public sealed class PictureStore : ITerminalPictureSource
{
	/// <summary>The scheme of a <see cref="StoredPicture.Source"/>.</summary>
	public const string Scheme = "picture:";

	private readonly Dictionary<string, LinkedListNode<StoredPicture>> _pictures = new(StringComparer.Ordinal);
	private readonly LinkedList<StoredPicture> _order = [];
	private readonly HashSet<uint> _transmitted = [];

	/// <summary>The most bytes held. 64 MiB unless set.</summary>
	public long Budget { get; set; } = 64L * 1024 * 1024;

	/// <summary>The largest picture decoded, in pixels.</summary>
	public long MaxPixels { get; set; } = 4096L * 4096;

	/// <summary>The bytes held now.</summary>
	public long Used { get; private set; }

	/// <summary>The pictures held now.</summary>
	public int Count => _pictures.Count;

	/// <summary>Raised when a picture is stored, so a host can serve it from an address of its own.</summary>
	public event Action<StoredPicture>? Added;

	/// <summary>Stores decoded pixels.</summary>
	public StoredPicture Add(int width, int height, byte[] rgba) => Add(width, height, rgba, default, null);

	/// <summary>Stores a file, with its pixels when they were decoded.</summary>
	public StoredPicture Add(int width, int height, byte[]? rgba, ReadOnlyMemory<byte> encoded, string? mediaType)
	{
		var key = Hash(encoded.IsEmpty ? rgba : encoded.Span);
		if (_pictures.TryGetValue(key, out var existing))
		{
			_order.Remove(existing);
			_order.AddFirst(existing);
			return existing.Value;
		}

		var pixels = rgba is null ? null : new TerminalPicture(key, width, height, rgba);
		var picture = new StoredPicture(key, width, height, pixels, encoded, mediaType);
		_pictures[key] = _order.AddFirst(picture);
		Used += picture.Size;
		while (Used > Budget && _order.Count > 1) Remove(_order.Last!.Value.Key);
		Added?.Invoke(picture);
		return picture;
	}

	/// <summary>Replaces a held picture's pixels, as when a moving picture gains a frame.</summary>
	internal void ReplacePixels(StoredPicture picture, TerminalPicture pixels)
	{
		var held = _pictures.ContainsKey(picture.Key);
		if (held) Used -= picture.Size;
		picture.ReplacePixels(pixels);
		if (!held) return;
		Used += picture.Size;
		while (Used > Budget && _order.Count > 1 && _order.Last!.Value != picture) Remove(_order.Last.Value.Key);
	}

	/// <summary>The picture with <paramref name="key"/>, if it is still held.</summary>
	public bool TryGet(string key, [NotNullWhen(true)] out StoredPicture? picture)
	{
		if (_pictures.TryGetValue(key, out var node))
		{
			picture = node.Value;
			return true;
		}

		picture = null;
		return false;
	}

	/// <summary>The picture <paramref name="image"/> shows, if it is one of these and still held.</summary>
	public bool TryGet(ImageMarkup image, [NotNullWhen(true)] out StoredPicture? picture)
	{
		ArgumentNullException.ThrowIfNull(image);
		picture = null;
		return image.Source.StartsWith(Scheme, StringComparison.Ordinal) && TryGet(image.Source[Scheme.Length..], out picture);
	}

	/// <summary>Forgets the picture with <paramref name="key"/>.</summary>
	public bool Remove(string key)
	{
		if (!_pictures.Remove(key, out var node)) return false;
		_order.Remove(node);
		Used -= node.Value.Size;
		return true;
	}

	/// <inheritdoc/>
	public bool TryGetPicture(ImageMarkup image, [NotNullWhen(true)] out TerminalPicture? picture)
	{
		picture = TryGet(image, out var stored) ? stored.Pixels : null;
		return picture is not null;
	}

	/// <inheritdoc/>
	public bool MarkTransmitted(uint imageId) => _transmitted.Add(imageId);

	private static string Hash(ReadOnlySpan<byte> data)
	{
		Span<byte> hash = stackalloc byte[32];
		SHA256.HashData(data, hash);
		return Convert.ToHexStringLower(hash[..12]);
	}
}
