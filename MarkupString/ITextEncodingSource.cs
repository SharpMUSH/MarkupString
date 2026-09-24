namespace MarkupString;

/// <summary>
/// A markup that decides how the text it covers is encoded, in place of the format's own
/// <see cref="MarkupFormat.Encoding"/>.
/// </summary>
/// <remarks>
/// <para>An encoding belongs to a format — it is why Pueblo output turns <c>&lt;</c> into
/// <c>&amp;lt;</c> and ends a line with <c>&lt;BR&gt;</c> — but a region of the text can need a
/// different one. Pueblo's <c>&lt;xch_mudtext&gt;</c> is the case this exists for: inside it the client
/// is back on MUD-text conventions and reads a newline as a break itself, so the <c>&lt;BR&gt;</c> the
/// format would write doubles it.</para>
/// <para>The innermost layer that answers wins, the way the innermost styling does. Text between runs
/// carries no markup and always takes the format's own encoding.</para>
/// </remarks>
public interface ITextEncodingSource : IMarkup
{
	/// <summary>
	/// The encoding for text this layer covers in <paramref name="format"/>, or false to leave the
	/// format's own.
	/// </summary>
	bool TryGetEncoding(MarkupFormat format, out TextEncoding encoding);
}
