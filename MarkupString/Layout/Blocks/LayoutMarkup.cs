namespace MarkupString.Layout;

/// <summary>
/// The layer <see cref="BlockLayout.Build"/> puts over a laid-out block: the tree it was laid out
/// from, over the lines it was laid out as. A terminal writes the lines; HTML draws the tree.
/// </summary>
/// <remarks>
/// <para>Two layers are equal when they were laid out at the same width over the same text, whatever
/// their trees, so a block that spans several styled runs is one region to the renderer however it
/// was read back.</para>
/// </remarks>
public sealed class LayoutMarkup : IBlockMarkup, IEquatable<LayoutMarkup>
{
	/// <summary>Creates the layer for <paramref name="root"/> laid out at <paramref name="width"/> as <paramref name="text"/>.</summary>
	public LayoutMarkup(LayoutNode root, int width, bool fluid, ReadOnlySpan<char> text)
		: this(root, width, fluid, text.Length, Fingerprint(text))
	{
	}

	/// <summary>Creates the layer from a fingerprint already taken, as a reader does.</summary>
	public LayoutMarkup(LayoutNode root, int width, bool fluid, int length, ulong hash)
	{
		ArgumentNullException.ThrowIfNull(root);
		Root = root;
		Width = width;
		Fluid = fluid;
		Length = length;
		Hash = hash;
	}

	/// <summary>The tree.</summary>
	public LayoutNode Root { get; }

	/// <summary>The width the text was laid out at.</summary>
	public int Width { get; }

	/// <summary>Whether the block may be laid out again at a reader's own width (<see cref="BlockLayout.Relayout"/>).</summary>
	public bool Fluid { get; }

	/// <summary>The length of the text the block was laid out as.</summary>
	public int Length { get; }

	/// <summary>The fingerprint of that text.</summary>
	public ulong Hash { get; }

	/// <inheritdoc/>
	public bool Covers(ReadOnlySpan<char> text) => text.Length == Length && Fingerprint(text) == Hash;

	/// <summary>A 64-bit FNV-1a hash of <paramref name="text"/>'s UTF-16 code units, stable across processes.</summary>
	public static ulong Fingerprint(ReadOnlySpan<char> text)
	{
		var hash = 14695981039346656037UL;
		foreach (var c in text)
		{
			hash ^= c;
			hash *= 1099511628211UL;
		}
		return hash;
	}

	/// <inheritdoc/>
	public bool Equals(LayoutMarkup? other) =>
		other is not null && Width == other.Width && Fluid == other.Fluid && Length == other.Length && Hash == other.Hash;

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is LayoutMarkup other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode() => HashCode.Combine(Width, Fluid, Length, Hash);
}
