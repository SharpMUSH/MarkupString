using System.Collections.Immutable;
namespace MarkupString;

/// <summary>What reading a stored <see cref="MarkupText"/> found, from best to worst.</summary>
public enum MarkupReadStatus
{
	/// <summary>Every part of the payload was understood, and writing the value back keeps all of it.</summary>
	Complete,

	/// <summary>
	/// Some layers are of a kind no codec in the reading registry claims. They are held as
	/// <see cref="UnknownMarkup"/> and written back verbatim, so writing the value back keeps all of it.
	/// </summary>
	Opaque,

	/// <summary>
	/// The payload is well formed but carries something this reader does not understand and would
	/// not write back: a wire version it does not know, a property it does not know on the envelope
	/// or on a layer whose kind it does know, a layout block of an unknown kind. The value, when
	/// there is one, is fit to show but not to save in place of the original.
	/// </summary>
	Unsupported,

	/// <summary>
	/// The payload is not what any writer produces: not JSON, not an object, content after the
	/// value, a field of the wrong type, a cover that overruns the text, a codec that failed. The
	/// value, when there is one, is what survived, fit to show but not to save in place of the original.
	/// </summary>
	Damaged,
}

/// <summary>
/// The result of <see cref="MarkupTextSerializer.Inspect(string)"/>: the value read, how much of
/// the payload it holds, and why.
/// </summary>
/// <remarks>
/// <see cref="MarkupTextSerializer.Deserialize(string)"/> is for showing text: it recovers what it
/// can and never says what it left out. A caller that may write the value back to where it came
/// from reads with <c>Inspect</c> and saves only when <see cref="CanRewrite"/> is true; otherwise
/// it keeps the stored bytes as they are.
/// </remarks>
public sealed class MarkupReadResult
{
	internal MarkupReadResult(MarkupReadStatus status, MarkupText? value, ImmutableArray<string> diagnostics)
	{
		Status = status;
		Value = value;
		Diagnostics = diagnostics;
	}

	/// <summary>How much of the payload <see cref="Value"/> holds.</summary>
	public MarkupReadStatus Status { get; }

	/// <summary>
	/// The text read, the same text <see cref="MarkupTextSerializer.Deserialize(string)"/> returns.
	/// For a wire version this reader does not know it is the payload's plain text only. Null when
	/// the payload is not a JSON object, so there is no text to show.
	/// </summary>
	public MarkupText? Value { get; }

	/// <summary>One line for each thing the reader did not understand or could not keep, in payload order.</summary>
	public ImmutableArray<string> Diagnostics { get; }

	/// <summary>
	/// Whether serialising <see cref="Value"/> keeps everything the payload held. True for
	/// <see cref="MarkupReadStatus.Complete"/> and <see cref="MarkupReadStatus.Opaque"/>.
	/// </summary>
	public bool CanRewrite => Status is MarkupReadStatus.Complete or MarkupReadStatus.Opaque;
}
