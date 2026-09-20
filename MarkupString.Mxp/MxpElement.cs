using System.Collections.Immutable;
using System.Text;

namespace MarkupString.Mxp;

/// <summary>One argument of an <see cref="MxpElement"/>: positional when <see cref="Name"/> is null.</summary>
/// <param name="Name">The argument's name, or <see langword="null"/> for a positional one.</param>
/// <param name="Value">The value, unquoted and unencoded; the empty string for a flag such as <c>ISMAP</c>.</param>
public readonly record struct MxpArgument(string? Name, string Value)
{
	/// <summary>The argument as it is written in a tag, quoted when the value needs it.</summary>
	public override string ToString() =>
		Name is null ? Quote(Value) : Value.Length == 0 ? Name : Name + "=" + Quote(Value);

	/// <summary>
	/// MXP separates arguments with whitespace and reads a quoted value as one, so a value carrying
	/// whitespace or a quote of its own is quoted and its quotes written as entities. Everything else is
	/// left as the caller wrote it.
	/// </summary>
	private static string Quote(string value)
	{
		if (value.Length == 0) return "\"\"";

		var needsQuotes = false;
		foreach (var c in value)
		{
			if (c is '"' or '\'' || char.IsWhiteSpace(c))
			{
				needsQuotes = true;
				break;
			}
		}

		return needsQuotes ? "\"" + value.Replace("\"", "&quot;") + "\"" : value;
	}
}

/// <summary>
/// One of MXP's own elements — a sound, an image, a gauge, a frame — as a markup layer.
/// <see cref="MxpElements"/> builds the ones the specification defines; this type is also the way to
/// write one it does not.
/// </summary>
/// <remarks>
/// <para>An element that wraps nothing (<c>SOUND</c>, <c>IMAGE</c>, <c>EXPIRE</c>, …) rides on a
/// zero-width carrier, the way a bell rides on its own character: it is a point
/// in the text rather than a property of any of it. An element that wraps content (<c>FRAME</c>,
/// <c>VAR</c>) marks the content it applies to and is closed after it.</para>
/// <para>A format that cannot express an element writes nothing at all, carrier included, so the same
/// text is safe to send to every client. What a client can actually render is not asked here: MXP's
/// <c>&lt;SUPPORT&gt;</c> exchange answers that, and deciding what to do about the answer belongs to the
/// application.</para>
/// </remarks>
/// <param name="Name">The element's name, e.g. <c>SOUND</c>.</param>
/// <param name="Arguments">Its arguments, in the order they are written.</param>
/// <param name="WrapsContent">Whether the element closes after the text it marks, rather than standing alone.</param>
public sealed record MxpElement(string Name, ImmutableArray<MxpArgument> Arguments, bool WrapsContent) : IMarkup
{
	/// <summary>
	/// The character a standalone element rides on: a zero-width space, which measures nothing and is
	/// never written out — every format either writes the element or writes nothing.
	/// </summary>
	public const string Carrier = "\u200b";

	/// <summary>Creates an element and the text it stands in, for one that wraps nothing.</summary>
	/// <exception cref="ArgumentException"><paramref name="name"/> is not an element name.</exception>
	public static MarkupText Standalone(string name, params ReadOnlySpan<MxpArgument> arguments) =>
		MarkupText.Wrap(new MxpElement(Checked(name), [.. arguments], false), Carrier);

	/// <summary>Creates an element wrapping <paramref name="content"/>.</summary>
	/// <exception cref="ArgumentException"><paramref name="name"/> is not an element name.</exception>
	public static MarkupText Wrapping(string name, MarkupText content, params ReadOnlySpan<MxpArgument> arguments) =>
		MarkupText.Wrap(new MxpElement(Checked(name), [.. arguments], true), content);

	/// <summary>The tag as MXP reads it: <c>&lt;NAME arg arg=value&gt;</c>.</summary>
	public override string ToString()
	{
		var written = new StringBuilder("<").Append(Name);
		foreach (var argument in Arguments) written.Append(' ').Append(argument.ToString());
		return written.Append('>').ToString();
	}

	/// <summary>An element name is a letter, then letters, digits or hyphens — the same shape a tag has.</summary>
	public static bool IsValidName(string name)
	{
		if (string.IsNullOrEmpty(name) || !IsAsciiLetter(name[0])) return false;

		foreach (var c in name)
		{
			if (!IsAsciiLetter(c) && !char.IsAsciiDigit(c) && c != '-') return false;
		}

		return true;
	}

	private static bool IsAsciiLetter(char c) => char.IsAsciiLetter(c);

	private static string Checked(string name) =>
		IsValidName(name)
			? name
			: throw new ArgumentException($"'{name}' is not an element name.", nameof(name));
}
