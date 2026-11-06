namespace MarkupString.Decoding;

/// <summary>
/// Splits a stream of text into printable text and terminal control sequences, following the DEC parser state
/// machine described by Paul Williams (the one xterm.js and Rust's <c>vte</c> implement). It recognises sequences
/// and never interprets them: what a sequence means is the <see cref="IVtTokenSink"/>'s business.
/// </summary>
/// <remarks>
/// <para>State carries across <see cref="Feed"/> calls, so a sequence may arrive in pieces.</para>
/// <para>
/// A line feed ends any sequence in flight. Without that, an unterminated string sequence (a bare <c>ESC ]</c> or
/// <c>ESC _</c>, which a player can type into a channel) would swallow the rest of a session. Every buffer is
/// capped by <see cref="VtLimits"/>; a sequence that overflows is consumed to its end and reported abandoned.
/// </para>
/// <para>C1 controls (U+0080 to U+009F) are text, as xterm treats them in UTF-8.</para>
/// </remarks>
public sealed class VtTokenizer
{
	private const char Escape = '\e';
	private const char Bell = '\a';
	private const int Omitted = -1;

	private enum State
	{
		Ground,
		Escape,
		EscapeIntermediate,
		CsiEntry,
		CsiParam,
		CsiIntermediate,
		CsiIgnore,
		OscString,
		DcsEntry,
		DcsParam,
		DcsIntermediate,
		DcsPassthrough,
		DcsIgnore,
		ApcString,
		IgnoredString,
	}

	private readonly VtLimits _limits;
	private readonly int[] _parameters;
	private readonly bool[] _subparameters;
	private readonly char[] _intermediates = new char[2];
	private readonly System.Text.StringBuilder _string = new();

	private State _state;
	private int _parameterCount;
	private int _intermediateCount;
	private char _prefix;
	private bool _parameterStarted;
	private bool _overflow;
	private bool _stringEscape;
	private char _dcsFinal;

	/// <summary>A tokenizer with <see cref="VtLimits.Default"/>.</summary>
	public VtTokenizer() : this(VtLimits.Default)
	{
	}

	/// <summary>A tokenizer with the given limits.</summary>
	public VtTokenizer(VtLimits limits)
	{
		ArgumentNullException.ThrowIfNull(limits);
		_limits = limits;
		_parameters = new int[Math.Max(1, limits.MaxParameters)];
		_subparameters = new bool[_parameters.Length];
	}

	/// <summary>Whether a sequence has started and not yet ended.</summary>
	public bool InSequence => _state != State.Ground;

	/// <summary>Reads <paramref name="text"/>, handing what it finds to <paramref name="sink"/> in order.</summary>
	public void Feed(ReadOnlySpan<char> text, IVtTokenSink sink)
	{
		ArgumentNullException.ThrowIfNull(sink);

		var printStart = -1;
		for (var i = 0; i < text.Length; i++)
		{
			var c = text[i];

			if (_state == State.Ground)
			{
				if (c >= ' ' && c != '\u007f')
				{
					if (printStart < 0) printStart = i;
					continue;
				}

				if (printStart >= 0)
				{
					sink.Print(text[printStart..i]);
					printStart = -1;
				}

				if (c == Escape) EnterEscape();
				else if (c != '\u007f') sink.Execute(c);
				continue;
			}

			Step(c, sink);
		}

		if (printStart >= 0) sink.Print(text[printStart..]);
	}

	/// <summary>
	/// Abandons any sequence in flight, as a line ending does. A host whose telnet layer strips line terminators
	/// calls this at each line boundary.
	/// </summary>
	public void Interrupt(IVtTokenSink sink)
	{
		ArgumentNullException.ThrowIfNull(sink);
		if (_state != State.Ground) Abandon(sink);
	}

	/// <summary>Forgets any sequence in flight without reporting it.</summary>
	public void Reset()
	{
		_state = State.Ground;
		_string.Clear();
	}

	private void Step(char c, IVtTokenSink sink)
	{
		// A line feed ends the line whatever is in flight.
		if (c == '\n')
		{
			Abandon(sink);
			sink.Execute(c);
			return;
		}

		// CAN and SUB cancel a sequence everywhere.
		if (c is '\u0018' or '\u001a')
		{
			Abandon(sink);
			return;
		}

		switch (_state)
		{
			case State.Escape:
				StepEscape(c, sink);
				break;
			case State.EscapeIntermediate:
				StepEscapeIntermediate(c, sink);
				break;
			case State.CsiEntry:
			case State.CsiParam:
			case State.CsiIntermediate:
			case State.CsiIgnore:
				StepCsi(c, sink);
				break;
			case State.OscString:
				StepOsc(c, sink);
				break;
			case State.DcsEntry:
			case State.DcsParam:
			case State.DcsIntermediate:
			case State.DcsIgnore:
				StepDcsHeader(c, sink);
				break;
			case State.DcsPassthrough:
			case State.ApcString:
			case State.IgnoredString:
				StepString(c, sink);
				break;
		}
	}

	private void EnterEscape()
	{
		_state = State.Escape;
		_intermediateCount = 0;
		_overflow = false;
	}

	private void StepEscape(char c, IVtTokenSink sink)
	{
		switch (c)
		{
			case Escape:
				EnterEscape();
				return;
			case '[':
				EnterParameters(State.CsiEntry);
				return;
			case ']':
				EnterString(State.OscString);
				return;
			case 'P':
				EnterParameters(State.DcsEntry);
				return;
			case '_':
				EnterString(State.ApcString);
				return;
			case 'X' or '^':
				EnterString(State.IgnoredString);
				return;
		}

		if (c < ' ')
		{
			sink.Execute(c);
			return;
		}

		if (c <= '/')
		{
			Collect(c);
			_state = State.EscapeIntermediate;
			return;
		}

		DispatchEscape(c, sink);
	}

	private void StepEscapeIntermediate(char c, IVtTokenSink sink)
	{
		if (c == Escape)
		{
			EnterEscape();
			return;
		}

		if (c < ' ')
		{
			sink.Execute(c);
			return;
		}

		if (c <= '/')
		{
			Collect(c);
			return;
		}

		DispatchEscape(c, sink);
	}

	private void DispatchEscape(char c, IVtTokenSink sink)
	{
		_state = State.Ground;
		if (c > '~' || _overflow)
		{
			sink.Abandoned(VtSequenceKind.Escape);
			return;
		}

		sink.Esc(new EscSequence(_intermediates.AsSpan(0, _intermediateCount), c));
	}

	private void EnterParameters(State state)
	{
		_state = state;
		_parameterCount = 0;
		_intermediateCount = 0;
		_prefix = '\0';
		_parameterStarted = false;
		_overflow = false;
	}

	private void EnterString(State state)
	{
		_state = state;
		_string.Clear();
		_stringEscape = false;
		_overflow = false;
	}

	private void StepCsi(char c, IVtTokenSink sink)
	{
		if (c == Escape)
		{
			sink.Abandoned(VtSequenceKind.Csi);
			EnterEscape();
			return;
		}

		if (c < ' ')
		{
			sink.Execute(c);
			return;
		}

		if (ReadHeader(c, State.CsiEntry, State.CsiParam, State.CsiIntermediate, State.CsiIgnore)) return;

		_state = State.Ground;
		if (c is < '@' or > '~' || _overflow)
		{
			sink.Abandoned(VtSequenceKind.Csi);
			return;
		}

		sink.Csi(Header(c));
	}

	private void StepDcsHeader(char c, IVtTokenSink sink)
	{
		if (c == Escape)
		{
			sink.Abandoned(VtSequenceKind.Dcs);
			EnterEscape();
			return;
		}

		// Controls inside a DCS header are ignored, as the state machine has it.
		if (c < ' ') return;

		if (ReadHeader(c, State.DcsEntry, State.DcsParam, State.DcsIntermediate, State.DcsIgnore)) return;

		if (c is < '@' or > '~')
		{
			_overflow = true;
		}

		_dcsFinal = c;
		_string.Clear();
		_stringEscape = false;
		_state = State.DcsPassthrough;
	}

	/// <summary>
	/// Reads one character of a CSI or DCS header: a private prefix, a parameter digit or separator, or an
	/// intermediate. Returns false when <paramref name="c"/> is the final character.
	/// </summary>
	private bool ReadHeader(char c, State entry, State param, State intermediate, State ignore)
	{
		if (_state == ignore)
		{
			// Ignore everything up to and including the final character.
			if (c is >= '@' and <= '~')
			{
				_overflow = true;
				return false;
			}

			return true;
		}

		if (c is >= '<' and <= '?')
		{
			if (_state == entry)
			{
				_prefix = c;
				_state = param;
			}
			else
			{
				_state = ignore;
			}

			return true;
		}

		if (c is >= '0' and <= '9' or ';' or ':')
		{
			if (_state == intermediate)
			{
				_state = ignore;
				return true;
			}

			_state = param;
			ReadParameter(c);
			return true;
		}

		if (c is >= ' ' and <= '/')
		{
			Collect(c);
			_state = intermediate;
			return true;
		}

		return false;
	}

	private void ReadParameter(char c)
	{
		if (c is ';' or ':')
		{
			if (!_parameterStarted) StartParameter(subparameter: false);
			// The separator closes this parameter; the next one starts now, empty.
			if (_parameterCount >= _parameters.Length)
			{
				_overflow = true;
				return;
			}

			_parameters[_parameterCount] = Omitted;
			_subparameters[_parameterCount] = c == ':';
			_parameterCount++;
			_parameterStarted = true;
			return;
		}

		if (!_parameterStarted) StartParameter(subparameter: false);
		if (_overflow) return;

		var index = _parameterCount - 1;
		var current = _parameters[index] == Omitted ? 0 : _parameters[index];
		_parameters[index] = Math.Min(_limits.MaxParameterValue, (current * 10) + (c - '0'));
	}

	private void StartParameter(bool subparameter)
	{
		if (_parameterCount >= _parameters.Length)
		{
			_overflow = true;
			return;
		}

		_parameters[_parameterCount] = Omitted;
		_subparameters[_parameterCount] = subparameter;
		_parameterCount++;
		_parameterStarted = true;
	}

	private CsiSequence Header(char final) => new(
		_prefix,
		_parameters.AsSpan(0, _parameterCount),
		_subparameters.AsSpan(0, _parameterCount),
		_intermediates.AsSpan(0, _intermediateCount),
		final);

	private void Collect(char c)
	{
		if (_intermediateCount < _intermediates.Length) _intermediates[_intermediateCount++] = c;
		else _overflow = true;
	}

	private void StepOsc(char c, IVtTokenSink sink)
	{
		if (c == Bell)
		{
			EndOsc(sink);
			return;
		}

		if (_stringEscape)
		{
			_stringEscape = false;
			if (c == '\\')
			{
				EndOsc(sink);
				return;
			}

			// ESC followed by anything else ends the string and starts a new escape.
			sink.Abandoned(VtSequenceKind.Osc);
			EnterEscape();
			StepEscape(c, sink);
			return;
		}

		if (c == Escape)
		{
			_stringEscape = true;
			return;
		}

		if (c < ' ') return;
		Append(c, _limits.MaxStringLength);
	}

	private void EndOsc(IVtTokenSink sink)
	{
		_state = State.Ground;
		if (_overflow)
		{
			sink.Abandoned(VtSequenceKind.Osc);
		}
		else
		{
			sink.Osc(Chunk());
		}

		_string.Clear();
	}

	private void StepString(char c, IVtTokenSink sink)
	{
		if (_stringEscape)
		{
			_stringEscape = false;
			if (c == '\\')
			{
				EndString(sink);
				return;
			}

			sink.Abandoned(Kind());
			EnterEscape();
			StepEscape(c, sink);
			return;
		}

		if (c == Escape)
		{
			_stringEscape = true;
			return;
		}

		// BEL is not a terminator for these by the standard, but tmux and some programs send it.
		if (c == Bell)
		{
			EndString(sink);
			return;
		}

		if (_state == State.IgnoredString) return;
		if (c < ' ') return;

		Append(c, _state == State.ApcString ? _limits.MaxApcLength : _limits.MaxStringLength);
	}

	private void EndString(IVtTokenSink sink)
	{
		var state = _state;
		_state = State.Ground;
		if (state == State.IgnoredString)
		{
			_string.Clear();
			return;
		}

		if (_overflow)
		{
			sink.Abandoned(state == State.ApcString ? VtSequenceKind.Apc : VtSequenceKind.Dcs);
		}
		else if (state == State.ApcString)
		{
			sink.Apc(Chunk());
		}
		else
		{
			sink.Dcs(Header(_dcsFinal), Chunk());
		}

		_string.Clear();
	}

	private void Append(char c, int max)
	{
		if (_overflow) return;
		if (_string.Length >= max)
		{
			_overflow = true;
			_string.Clear();
			return;
		}

		_string.Append(c);
	}

	private ReadOnlySpan<char> Chunk()
	{
		// One chunk is the common case; a multi-chunk builder is copied once.
		foreach (var chunk in _string.GetChunks())
		{
			if (chunk.Length == _string.Length) return chunk.Span;
			break;
		}

		return _string.ToString();
	}

	private VtSequenceKind Kind() => _state switch
	{
		State.ApcString => VtSequenceKind.Apc,
		State.OscString => VtSequenceKind.Osc,
		State.DcsPassthrough or State.DcsEntry or State.DcsParam or State.DcsIntermediate or State.DcsIgnore => VtSequenceKind.Dcs,
		State.CsiEntry or State.CsiParam or State.CsiIntermediate or State.CsiIgnore => VtSequenceKind.Csi,
		_ => VtSequenceKind.Escape,
	};

	private void Abandon(IVtTokenSink sink)
	{
		if (_state is State.Ground) return;
		var kind = Kind();
		var ignored = _state == State.IgnoredString;
		_state = State.Ground;
		_string.Clear();
		if (!ignored) sink.Abandoned(kind);
	}
}

/// <summary>The kinds of sequence a <see cref="VtTokenizer"/> recognises.</summary>
public enum VtSequenceKind
{
	/// <summary>A two-character escape, <c>ESC</c> then a final character, with intermediates.</summary>
	Escape,

	/// <summary>A control sequence, <c>ESC [</c>.</summary>
	Csi,

	/// <summary>An operating system command, <c>ESC ]</c>.</summary>
	Osc,

	/// <summary>A device control string, <c>ESC P</c>.</summary>
	Dcs,

	/// <summary>An application program command, <c>ESC _</c>.</summary>
	Apc,
}

/// <summary>How far a <see cref="VtTokenizer"/> lets a sequence grow.</summary>
public sealed record VtLimits
{
	/// <summary>The defaults: 32 parameters, as <c>vte</c> has, a 16 MiB string for a sixel or iTerm2 picture, and 64 KiB for an APC.</summary>
	public static VtLimits Default { get; } = new();

	/// <summary>The most parameters a CSI or DCS header may have, subparameters included.</summary>
	public int MaxParameters { get; init; } = 32;

	/// <summary>The largest value a parameter is read as; larger values are clamped to it.</summary>
	public int MaxParameterValue { get; init; } = 65535;

	/// <summary>The longest OSC or DCS payload, in characters.</summary>
	public int MaxStringLength { get; init; } = 16 * 1024 * 1024;

	/// <summary>The longest APC payload, in characters. Kitty sends pictures in chunks of 4096.</summary>
	public int MaxApcLength { get; init; } = 64 * 1024;
}
