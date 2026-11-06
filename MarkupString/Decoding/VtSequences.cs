namespace MarkupString.Decoding;

/// <summary>What a <see cref="VtTokenizer"/> hands on as it reads.</summary>
public interface IVtTokenSink
{
	/// <summary>Printable text with no controls in it.</summary>
	void Print(ReadOnlySpan<char> text);

	/// <summary>A C0 control character other than <c>ESC</c>: a line feed, a bell, a backspace.</summary>
	void Execute(char control);

	/// <summary>A complete control sequence.</summary>
	void Csi(in CsiSequence sequence);

	/// <summary>A complete escape sequence.</summary>
	void Esc(in EscSequence sequence);

	/// <summary>An operating system command's payload, the number and its <c>;</c> included.</summary>
	void Osc(ReadOnlySpan<char> payload);

	/// <summary>A device control string: its header, read as a control sequence, and its data.</summary>
	void Dcs(in CsiSequence header, ReadOnlySpan<char> data);

	/// <summary>An application program command's payload.</summary>
	void Apc(ReadOnlySpan<char> payload);

	/// <summary>A sequence that was malformed, too long, or cut off by a line ending.</summary>
	void Abandoned(VtSequenceKind kind);
}

/// <summary>
/// A control sequence: an optional private prefix (<c>&lt;</c> to <c>?</c>), parameters, up to two intermediates,
/// and a final character. A parameter left empty reads as <see cref="Omitted"/>.
/// </summary>
public readonly ref struct CsiSequence
{
	/// <summary>The value of a parameter that was left empty.</summary>
	public const int Omitted = -1;

	private readonly ReadOnlySpan<int> _parameters;
	private readonly ReadOnlySpan<bool> _subparameters;

	/// <summary>A sequence from its parts.</summary>
	public CsiSequence(char prefix, ReadOnlySpan<int> parameters, ReadOnlySpan<bool> subparameters, ReadOnlySpan<char> intermediates, char final)
	{
		if (parameters.Length != subparameters.Length)
			throw new ArgumentException("There is one subparameter flag per parameter.", nameof(subparameters));
		Prefix = prefix;
		_parameters = parameters;
		_subparameters = subparameters;
		Intermediates = intermediates;
		Final = final;
	}

	/// <summary>The private prefix, or <c>'\0'</c>.</summary>
	public char Prefix { get; }

	/// <summary>The intermediate characters, between the parameters and the final character.</summary>
	public ReadOnlySpan<char> Intermediates { get; }

	/// <summary>The final character, which with the prefix and intermediates names the sequence.</summary>
	public char Final { get; }

	/// <summary>Every parameter, subparameters included, <see cref="Omitted"/> where empty.</summary>
	public ReadOnlySpan<int> Parameters => _parameters;

	/// <summary>How many parameters there are, subparameters included.</summary>
	public int Count => _parameters.Length;

	/// <summary>Parameter <paramref name="index"/>, or <paramref name="fallback"/> when it is missing or empty.</summary>
	public int Get(int index, int fallback = 0) =>
		index < _parameters.Length && _parameters[index] != Omitted ? _parameters[index] : fallback;

	/// <summary>Whether parameter <paramref name="index"/> follows a <c>:</c>, making it a subparameter of the one before.</summary>
	public bool IsSubparameter(int index) => index < _subparameters.Length && _subparameters[index];

	/// <summary>Whether this is the sequence <paramref name="final"/> with <paramref name="prefix"/> and no intermediates.</summary>
	public bool Is(char final, char prefix = '\0') => Final == final && Prefix == prefix && Intermediates.IsEmpty;
}

/// <summary>An escape sequence: <c>ESC</c>, up to two intermediates, and a final character.</summary>
public readonly ref struct EscSequence
{
	/// <summary>A sequence from its parts.</summary>
	public EscSequence(ReadOnlySpan<char> intermediates, char final)
	{
		Intermediates = intermediates;
		Final = final;
	}

	/// <summary>The intermediate characters, such as the <c>(</c> of <c>ESC ( 0</c>.</summary>
	public ReadOnlySpan<char> Intermediates { get; }

	/// <summary>The final character.</summary>
	public char Final { get; }
}
