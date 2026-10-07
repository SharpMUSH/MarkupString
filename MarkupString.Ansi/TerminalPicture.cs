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
		: this(key, width, height, rgba, [])
	{
	}

	/// <summary>
	/// Creates a moving picture from <paramref name="frames"/>, each a whole frame as it is shown. The first
	/// frame is the picture's <see cref="Rgba"/>, which is what a terminal that cannot animate draws.
	/// </summary>
	/// <param name="key">What identifies these frames, such as a hash of the file.</param>
	/// <param name="width">Its width in pixels.</param>
	/// <param name="height">Its height in pixels.</param>
	/// <param name="frames">The frames in order, each <paramref name="width"/> × <paramref name="height"/> × 4 bytes; at least one.</param>
	/// <exception cref="ArgumentException"><paramref name="key"/> is empty, there are no frames, or a frame is not the size the dimensions give.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
	public TerminalPicture(string key, int width, int height, IReadOnlyList<TerminalPictureFrame> frames)
		: this(key, width, height, FirstOf(frames), frames.Count > 1 ? frames : [])
	{
	}

	private TerminalPicture(string key, int width, int height, ReadOnlyMemory<byte> rgba, IReadOnlyList<TerminalPictureFrame> frames)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		foreach (var pixels in frames.Select(frame => frame.Rgba).Prepend(rgba))
		{
			if ((long)width * height * 4 != pixels.Length)
				throw new ArgumentException($"A {width}x{height} picture is {(long)width * height * 4} bytes of RGBA, not {pixels.Length}.", nameof(rgba));
		}

		Key = key;
		Width = width;
		Height = height;
		Rgba = rgba;
		Frames = frames;
	}

	private static ReadOnlyMemory<byte> FirstOf(IReadOnlyList<TerminalPictureFrame> frames)
	{
		ArgumentNullException.ThrowIfNull(frames);
		if (frames.Count == 0) throw new ArgumentException("A picture has at least one frame.", nameof(frames));
		return frames[0].Rgba;
	}

	/// <summary>What identifies these pixels.</summary>
	public string Key { get; }

	/// <summary>Its width in pixels.</summary>
	public int Width { get; }

	/// <summary>Its height in pixels.</summary>
	public int Height { get; }

	/// <summary>The pixels: RGBA, row by row from the top left. For a moving picture, its first frame.</summary>
	public ReadOnlyMemory<byte> Rgba { get; }

	/// <summary>Every frame of a moving picture, the first included; empty for a still one.</summary>
	public IReadOnlyList<TerminalPictureFrame> Frames { get; }
}

/// <summary>One frame of a moving picture: the whole picture as it is shown, and for how long.</summary>
/// <param name="Rgba">The pixels: RGBA, row by row from the top left.</param>
/// <param name="Duration">
/// How long the frame is shown before the next. 10 milliseconds or less is shown for 100, as browsers do: a GIF
/// saved with no delay, or one too short to see, was made to be played that way.
/// </param>
public readonly record struct TerminalPictureFrame(ReadOnlyMemory<byte> Rgba, TimeSpan Duration)
{
	private static readonly TimeSpan Shortest = TimeSpan.FromMilliseconds(10);
	private static readonly TimeSpan Unset = TimeSpan.FromMilliseconds(100);

	/// <summary>How long the frame is actually shown.</summary>
	internal TimeSpan Shown => Duration <= Shortest ? Unset : Duration;
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
