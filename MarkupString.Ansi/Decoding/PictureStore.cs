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
/// Where decoded pictures are kept, by <see cref="StoredPicture.Key"/>: a hash of the picture, so the same picture
/// is kept once however often, and by however many connections, it is sent. Give one to
/// <see cref="PictureDecoding.WithPictures(MarkupString.Decoding.DecoderRegistry, IPictureCache)"/> to share it
/// between connections, or to keep pictures somewhere of your own. <see cref="MemoryPictureCache"/> is the default.
/// </summary>
/// <remarks>
/// Decoders on different threads may share one cache, so an implementation must be safe to call from several
/// threads at once. A picture is not changed once it is in a cache, except one that a connection made for its own
/// moving picture, under a key of its own; <see cref="Set"/> is called again when that one gains a frame.
/// </remarks>
public interface IPictureCache
{
	/// <summary>The picture with <paramref name="key"/>, if it is held.</summary>
	bool TryGet(string key, [NotNullWhen(true)] out StoredPicture? picture);

	/// <summary>Holds <paramref name="picture"/>, or takes note that its <see cref="StoredPicture.Size"/> changed.</summary>
	void Set(StoredPicture picture);

	/// <summary>Forgets the picture with <paramref name="key"/>.</summary>
	bool Remove(string key);
}

/// <summary>Pictures in memory, within a budget of bytes; the least recently used go first. Safe to share.</summary>
public sealed class MemoryPictureCache : IPictureCache
{
	private sealed class Entry(StoredPicture picture)
	{
		public StoredPicture Picture { get; } = picture;

		public long Size { get; set; }
	}

	private readonly Dictionary<string, LinkedListNode<Entry>> _pictures = new(StringComparer.Ordinal);
	private readonly LinkedList<Entry> _order = [];
	private readonly Lock _lock = new();
	private long _budget = 64L * 1024 * 1024;

	/// <summary>The most bytes held. 64 MiB unless set.</summary>
	public long Budget
	{
		get => _budget;
		set
		{
			lock (_lock)
			{
				_budget = value;
				Evict(null);
			}
		}
	}

	/// <summary>The bytes held now.</summary>
	public long Used { get; private set; }

	/// <summary>The pictures held now.</summary>
	public int Count
	{
		get
		{
			lock (_lock) return _pictures.Count;
		}
	}

	/// <inheritdoc/>
	public bool TryGet(string key, [NotNullWhen(true)] out StoredPicture? picture)
	{
		ArgumentNullException.ThrowIfNull(key);
		lock (_lock)
		{
			if (_pictures.TryGetValue(key, out var node))
			{
				_order.Remove(node);
				_order.AddFirst(node);
				picture = node.Value.Picture;
				return true;
			}
		}

		picture = null;
		return false;
	}

	/// <inheritdoc/>
	public void Set(StoredPicture picture)
	{
		ArgumentNullException.ThrowIfNull(picture);
		lock (_lock)
		{
			if (_pictures.TryGetValue(picture.Key, out var node))
			{
				_order.Remove(node);
				Used -= node.Value.Size;
			}
			else
			{
				node = new LinkedListNode<Entry>(new Entry(picture));
				_pictures[picture.Key] = node;
			}

			node.Value.Size = picture.Size;
			Used += node.Value.Size;
			_order.AddFirst(node);
			Evict(picture);
		}
	}

	/// <inheritdoc/>
	public bool Remove(string key)
	{
		ArgumentNullException.ThrowIfNull(key);
		lock (_lock) return RemoveHeld(key);
	}

	private bool RemoveHeld(string key)
	{
		if (!_pictures.Remove(key, out var node)) return false;
		_order.Remove(node);
		Used -= node.Value.Size;
		return true;
	}

	/// <summary>Drops the least recently used until the budget is met, keeping the newest and <paramref name="keep"/>.</summary>
	private void Evict(StoredPicture? keep)
	{
		while (Used > _budget && _order.Count > 1 && _order.Last!.Value.Picture != keep) RemoveHeld(_order.Last.Value.Picture.Key);
	}
}

/// <summary>
/// The pictures one connection's decoder read, kept in an <see cref="IPictureCache"/>. It is also the
/// <see cref="ITerminalPictureSource"/> that draws them again in a terminal. Get a decoder's with
/// <see cref="PicturesPlugin.Pictures(MarkupString.Decoding.Decoder)"/>.
/// </summary>
/// <remarks>
/// A picture the server deletes (Kitty <c>a=d</c>) is only forgotten by its id: lines already read still show it,
/// until the cache lets it go. A line whose picture is gone shows its text instead.
/// </remarks>
public sealed class PictureStore : ITerminalPictureSource
{
	/// <summary>The scheme of a <see cref="StoredPicture.Source"/>.</summary>
	public const string Scheme = "picture:";

	private readonly HashSet<string> _read = new(StringComparer.Ordinal);
	private readonly HashSet<uint> _transmitted = [];

	/// <summary>A store of its own, in a new <see cref="MemoryPictureCache"/>.</summary>
	public PictureStore()
		: this(null)
	{
	}

	/// <summary>A store that keeps its pictures in <paramref name="cache"/>, or in a new <see cref="MemoryPictureCache"/>.</summary>
	public PictureStore(IPictureCache? cache) => Cache = cache ?? new MemoryPictureCache();

	/// <summary>Where the pictures are kept.</summary>
	public IPictureCache Cache { get; }

	/// <summary>The largest picture decoded, in pixels.</summary>
	public long MaxPixels { get; set; } = 4096L * 4096;

	/// <summary>How many pictures this connection has read, held or not.</summary>
	public int Count => _read.Count;

	/// <summary>
	/// Raised when a picture is first stored, so a host can serve it from an address of its own. One already in a
	/// shared cache is not raised again.
	/// </summary>
	public event Action<StoredPicture>? Added;

	/// <summary>Stores decoded pixels.</summary>
	public StoredPicture Add(int width, int height, byte[] rgba) => Add(width, height, rgba, default, null);

	/// <summary>Stores a file, with its pixels when they were decoded.</summary>
	public StoredPicture Add(int width, int height, byte[]? rgba, ReadOnlyMemory<byte> encoded, string? mediaType)
	{
		var key = KeyOf(encoded.IsEmpty ? rgba : encoded.Span);
		_read.Add(key);
		if (Cache.TryGet(key, out var existing)) return existing;

		var pixels = rgba is null ? null : new TerminalPicture(key, width, height, rgba);
		var picture = new StoredPicture(key, width, height, pixels, encoded, mediaType);
		Cache.Set(picture);
		Added?.Invoke(picture);
		return picture;
	}

	/// <summary>
	/// The picture already held for the file <paramref name="encoded"/>, so it need not be decoded again; never one
	/// larger than <see cref="MaxPixels"/>.
	/// </summary>
	internal bool TryGetFile(ReadOnlySpan<byte> encoded, [NotNullWhen(true)] out StoredPicture? picture)
	{
		var key = KeyOf(encoded);
		if (Cache.TryGet(key, out picture) && (long)picture.Width * picture.Height <= MaxPixels)
		{
			_read.Add(key);
			return true;
		}

		picture = null;
		return false;
	}

	/// <summary>
	/// A copy of <paramref name="still"/> under a key of its own, for this connection to add frames to, so the
	/// still picture, which other lines and connections may show, stays as it is.
	/// </summary>
	internal StoredPicture Fork(StoredPicture still)
	{
		var key = $"{still.Key}-{Guid.NewGuid():N}";
		var pixels = still.Pixels is { } source ? new TerminalPicture(key, source.Width, source.Height, source.Rgba) : null;
		var picture = new StoredPicture(key, still.Width, still.Height, pixels, default, null);
		_read.Add(key);
		Cache.Set(picture);
		Added?.Invoke(picture);
		return picture;
	}

	/// <summary>Replaces a picture's pixels, as when a moving picture gains a frame.</summary>
	internal void ReplacePixels(StoredPicture picture, TerminalPicture pixels)
	{
		picture.ReplacePixels(pixels);
		if (Cache.TryGet(picture.Key, out var held) && held == picture) Cache.Set(picture);
	}

	/// <summary>The picture with <paramref name="key"/>, if it is still held.</summary>
	public bool TryGet(string key, [NotNullWhen(true)] out StoredPicture? picture) => Cache.TryGet(key, out picture);

	/// <summary>The picture <paramref name="image"/> shows, if it is one of these and still held.</summary>
	public bool TryGet(ImageMarkup image, [NotNullWhen(true)] out StoredPicture? picture)
	{
		ArgumentNullException.ThrowIfNull(image);
		picture = null;
		return image.Source.StartsWith(Scheme, StringComparison.Ordinal) && TryGet(image.Source[Scheme.Length..], out picture);
	}

	/// <summary>Forgets the picture with <paramref name="key"/>, for every connection that shares the cache.</summary>
	public bool Remove(string key)
	{
		_read.Remove(key);
		return Cache.Remove(key);
	}

	/// <inheritdoc/>
	public bool TryGetPicture(ImageMarkup image, [NotNullWhen(true)] out TerminalPicture? picture)
	{
		picture = TryGet(image, out var stored) ? stored.Pixels : null;
		return picture is not null;
	}

	/// <inheritdoc/>
	public bool MarkTransmitted(uint imageId) => _transmitted.Add(imageId);

	/// <summary>The key of a picture whose file, or else pixels, are <paramref name="data"/>.</summary>
	internal static string KeyOf(ReadOnlySpan<byte> data)
	{
		Span<byte> hash = stackalloc byte[32];
		SHA256.HashData(data, hash);
		return Convert.ToHexStringLower(hash[..12]);
	}
}
