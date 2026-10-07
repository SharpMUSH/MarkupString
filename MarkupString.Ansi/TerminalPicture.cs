using System.Diagnostics.CodeAnalysis;

namespace MarkupString.Ansi;

/// <summary>
/// A picture's pixels, ready to be drawn in a terminal: 8-bit RGBA, row by row from the top left.
/// Decoding a file and fetching it are the host's; this package only draws what it is given.
/// </summary>
public sealed class TerminalPicture
{
	/// <summary>Creates a picture from <paramref name="rgba"/>, four bytes a pixel.</summary>
	/// <param name="key">
	/// What identifies these pixels, such as a hash of the file. Two pictures with the same key are taken to be
	/// the same picture, which is how a terminal that already holds one is not sent it again.
	/// </param>
	/// <param name="width">Its width in pixels.</param>
	/// <param name="height">Its height in pixels.</param>
	/// <param name="rgba">The pixels, <paramref name="width"/> × <paramref name="height"/> × 4 bytes.</param>
	/// <exception cref="ArgumentException"><paramref name="key"/> is empty, or <paramref name="rgba"/> is not the size the dimensions give.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
	public TerminalPicture(string key, int width, int height, ReadOnlyMemory<byte> rgba)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		if ((long)width * height * 4 != rgba.Length)
			throw new ArgumentException($"A {width}x{height} picture is {(long)width * height * 4} bytes of RGBA, not {rgba.Length}.", nameof(rgba));

		Key = key;
		Width = width;
		Height = height;
		Rgba = rgba;
	}

	/// <summary>What identifies these pixels.</summary>
	public string Key { get; }

	/// <summary>Its width in pixels.</summary>
	public int Width { get; }

	/// <summary>Its height in pixels.</summary>
	public int Height { get; }

	/// <summary>The pixels: RGBA, row by row from the top left.</summary>
	public ReadOnlyMemory<byte> Rgba { get; }
}

/// <summary>
/// Where a terminal render finds a picture's pixels, and what it knows about the one connection it is
/// rendering for. A host builds one per connection and hands it to <see cref="AnsiOutputOptions.Pictures"/>.
/// </summary>
public interface ITerminalPictureSource
{
	/// <summary>
	/// The pixels of <paramref name="image"/>, or false when they are not to hand — not fetched yet, refused, or
	/// not a picture at all — in which case the client is sent the text art instead. Called while rendering, so
	/// it must answer from what it already has rather than fetch.
	/// </summary>
	bool TryGetPicture(ImageMarkup image, [NotNullWhen(true)] out TerminalPicture? picture);

	/// <summary>
	/// Records that the terminal is being sent the Kitty image <paramref name="imageId"/>, and answers whether it
	/// was new: true the first time, so it is transmitted, and false afterwards, when its placeholders alone are
	/// enough. A source that forgets — a restarted host — answers true again, and the terminal replaces the
	/// image it had under that id.
	/// </summary>
	bool MarkTransmitted(uint imageId);
}
