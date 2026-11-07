namespace MarkupString.Decoding;

/// <summary>
/// What a handler may do while a <see cref="Decoder"/> reads: write text under the current markup, change that
/// markup, end lines, place pictures and raise events. One per decoder.
/// </summary>
/// <remarks>
/// <para>
/// Text is written under a <see cref="MarkupSet"/> built from two things. <em>Pens</em> are named slots that a
/// protocol sets and replaces, as SGR replaces the current style. <em>Elements</em> are a stack a tag protocol
/// opens and closes. A pen sits inside every element, so an SGR colour written inside an MXP <c>&lt;COLOR&gt;</c>
/// wins, as it does in an MXP client.
/// </para>
/// </remarks>
public sealed class DecodeContext
{
	private readonly Decoder _decoder;
	private readonly Dictionary<Type, object> _states = [];
	private readonly List<(string Slot, IMarkup Markup)> _pens = [];
	private readonly List<IMarkup> _elements = [];
	private readonly Dictionary<string, ITextProtocol> _protocols = new(StringComparer.OrdinalIgnoreCase);
	private MarkupSet? _set;
	private bool _setValid;
	private ITextProtocol? _active;
	private long _sequence;

	internal DecodeContext(Decoder decoder)
	{
		_decoder = decoder;
		Line = new LineBuilder(decoder.Options.MaxLineLength);
	}

	internal LineBuilder Line { get; }

	internal string? ActiveProtocolName { get; private set; }

	/// <summary>The decoder's options.</summary>
	public DecoderOptions Options => _decoder.Options;

	/// <summary>Who the client is, as the host last set it.</summary>
	public ClientIdentity Identity => _decoder.Identity;

	/// <summary>Counts of what was dropped.</summary>
	public DecodeDiagnostics Diagnostics => _decoder.Diagnostics;

	/// <summary>The pane the lines being read are for, or null for the main output.</summary>
	public string? Destination { get; set; }

	/// <summary>Whether nothing has been written to the current line.</summary>
	public bool LineIsEmpty => Line.IsEmpty;

	/// <summary>The column the next character is written at, in cells.</summary>
	public int Column => Line.Cursor;

	/// <summary>The elements open now, outermost first.</summary>
	public IReadOnlyList<IMarkup> Elements => _elements;

	/// <summary>
	/// This decoder's instance of a plugin's own state, made on first use. A plugin keeps its per-connection state
	/// here, because the plugin itself is shared by every decoder of its registry.
	/// </summary>
	public T State<T>() where T : class, new()
	{
		if (_states.TryGetValue(typeof(T), out var state)) return (T)state;
		var made = new T();
		_states[typeof(T)] = made;
		return made;
	}

	/// <summary>Sets pen <paramref name="slot"/> to <paramref name="markup"/>, or clears it with null.</summary>
	public void SetPen(string slot, IMarkup? markup)
	{
		ArgumentNullException.ThrowIfNull(slot);
		var index = _pens.FindIndex(pen => pen.Slot == slot);
		if (markup is null)
		{
			if (index < 0) return;
			_pens.RemoveAt(index);
		}
		else if (index < 0)
		{
			_pens.Add((slot, markup));
		}
		else if (Equals(_pens[index].Markup, markup))
		{
			return;
		}
		else
		{
			_pens[index] = (slot, markup);
		}

		_setValid = false;
	}

	/// <summary>What pen <paramref name="slot"/> holds, or null.</summary>
	public IMarkup? Pen(string slot)
	{
		foreach (var pen in _pens)
		{
			if (pen.Slot == slot) return pen.Markup;
		}

		return null;
	}

	/// <summary>Opens <paramref name="markup"/> over the text that follows, until <see cref="Close"/>.</summary>
	/// <returns>False when <see cref="DecoderOptions.MaxOpenElements"/> are already open; nothing is opened.</returns>
	public bool Open(IMarkup markup)
	{
		ArgumentNullException.ThrowIfNull(markup);
		if (_elements.Count >= Options.MaxOpenElements)
		{
			Diagnostics.LimitsReached++;
			return false;
		}

		_elements.Add(markup);
		_setValid = false;
		return true;
	}

	/// <summary>
	/// Closes <paramref name="markup"/> (the same instance <see cref="Open"/> was given) and every element opened
	/// after it, as HTML closes elements left open inside one.
	/// </summary>
	/// <returns>False when it is not open.</returns>
	public bool Close(IMarkup markup)
	{
		var index = _elements.FindLastIndex(open => ReferenceEquals(open, markup));
		if (index < 0) return false;
		_elements.RemoveRange(index, _elements.Count - index);
		_setValid = false;
		return true;
	}

	/// <summary>Closes the elements from <paramref name="count"/> on, leaving the first <paramref name="count"/> open.</summary>
	public void CloseTo(int count)
	{
		if (count < 0) count = 0;
		if (count >= _elements.Count) return;
		_elements.RemoveRange(count, _elements.Count - count);
		_setValid = false;
	}

	/// <summary>The markup text written now is under, innermost first, or null for none.</summary>
	public MarkupSet? CurrentMarkup
	{
		get
		{
			if (_setValid) return _set;
			if (_pens.Count == 0 && _elements.Count == 0)
			{
				_set = null;
			}
			else
			{
				var layers = new List<IMarkup>(_pens.Count + _elements.Count);
				for (var i = _pens.Count - 1; i >= 0; i--) layers.Add(_pens[i].Markup);
				for (var i = _elements.Count - 1; i >= 0; i--) layers.Add(_elements[i]);
				_set = MarkupSet.Of(layers);
			}

			_setValid = true;
			return _set;
		}
	}

	/// <summary>
	/// Writes printable text, passing it to the registry's print filters first (a character set, picture
	/// placeholders). A text protocol writes its literal text with this.
	/// </summary>
	public void Print(ReadOnlySpan<char> text)
	{
		if (text.IsEmpty) return;
		foreach (var filter in _decoder.Registry.PrintFilters)
		{
			if (filter(text, this)) return;
		}

		Write(text);
	}

	/// <summary>Writes text under the current markup, as it stands.</summary>
	public void Write(ReadOnlySpan<char> text) => Write(text, null);

	/// <summary>Writes text under the current markup with <paramref name="inner"/> inside it.</summary>
	public void Write(ReadOnlySpan<char> text, IMarkup? inner)
	{
		if (text.IsEmpty) return;
		var set = CurrentMarkup;
		if (inner is not null)
		{
			if (set is null)
			{
				set = MarkupSet.Of(inner);
			}
			else
			{
				var layers = new IMarkup[set.Count + 1];
				layers[0] = inner;
				for (var i = 0; i < set.Count; i++) layers[i + 1] = set[i];
				set = MarkupSet.Of(layers.AsSpan());
			}
		}
		// A line too long for the limit is ended where the limit falls, and the rest starts the next.
		while (Line.Length + text.Length > Line.MaxLength)
		{
			var room = Math.Max(0, Line.MaxLength - Line.Length);
			if (room > 0 && char.IsHighSurrogate(text[room - 1])) room--;
			if (room == 0 && Line.IsEmpty) room = Math.Min(text.Length, Math.Max(1, Line.MaxLength));
			if (room > 0) Line.Write(text[..room].ToString(), set);
			Diagnostics.LimitsReached++;
			EndLine();
			text = text[room..];
			if (text.IsEmpty) return;
		}

		Line.Write(text.ToString(), set);
	}

	/// <summary>The line read so far, not yet ended.</summary>
	public MarkupText PendingLine => Line.Peek();

	/// <summary>
	/// Puts <paramref name="text"/> into the line at display column <paramref name="column"/>, moving what is there to
	/// the right, and the cursor with it when it is past that column. A column past the end puts it at the end.
	/// </summary>
	public void Insert(int column, MarkupText text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (text.Length == 0) return;
		var line = Line.Peek();
		if (line.Length + text.Length > Line.MaxLength)
		{
			// Too long to fit: the line stays as it was, and so does the cursor.
			Diagnostics.LimitsReached++;
			return;
		}

		var index = DisplayWidth.IndexAtWidth(line.Text, Math.Clamp(column, 0, line.DisplayWidth));
		var cursor = Line.Cursor;
		Line.Take();
		Line.Put(line.Insert(index, text));
		Line.Cursor = cursor >= column ? cursor + text.DisplayWidth : cursor;
	}

	/// <summary>Writes already built text at the cursor.</summary>
	public void Write(MarkupText text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (text.Length == 0) return;
		if (!Line.Put(text)) Diagnostics.LimitsReached++;
	}

	/// <summary>Writes a point: a bell, a sound, a clear. It takes no cells.</summary>
	public void Point(IPointMarkup point)
	{
		ArgumentNullException.ThrowIfNull(point);
		Line.Put(MarkupText.Point(point));
	}

	/// <summary>Moves the cursor to column 0 of the current line; what is written next overwrites.</summary>
	public void CarriageReturn() => Line.Cursor = 0;

	/// <summary>Moves the cursor <paramref name="cells"/> to the left, stopping at column 0.</summary>
	public void Backspace(int cells = 1) => Line.Cursor = Math.Max(0, Line.Cursor - Math.Max(0, cells));

	/// <summary>Moves the cursor to <paramref name="column"/> of the current line, which is padded with spaces when it is shorter.</summary>
	public void MoveToColumn(int column) => Line.Cursor = Math.Clamp(column, 0, Options.MaxLineLength);

	/// <summary>Erases from the cursor to the end of the line.</summary>
	public void EraseToEndOfLine() => Line.EraseFrom(Line.Cursor);

	/// <summary>Erases from the start of the line to the cursor, leaving blanks.</summary>
	public void EraseToStartOfLine() => Line.EraseTo(Line.Cursor);

	/// <summary>Erases the whole line; the cursor stays where it is.</summary>
	public void EraseLine()
	{
		var cursor = Line.Cursor;
		Line.EraseFrom(0);
		Line.Cursor = cursor;
	}

	/// <summary>Ends the current line.</summary>
	public void EndLine() => EndLine(LineKind.Output);

	internal void EndLine(LineKind kind)
	{
		_active?.EndLine(this);
		foreach (var handler in _decoder.Registry.LineEndHandlers) handler(this);
		var content = Line.Take();
		_decoder.Complete(new DisplayLine(content, kind) { Sequence = _sequence++, Destination = Destination });
	}

	/// <summary>
	/// Places a picture <paramref name="rows"/> tall and <paramref name="columns"/> wide at the cursor, as a terminal
	/// draws one: its first row beside the text already on the line, each further row on a line of its own
	/// indented to the same column. The cursor then stands below the picture, at its right edge when
	/// <paramref name="cursorRight"/> (Kitty) or at column 0 (sixel).
	/// </summary>
	/// <param name="picture">The picture; each row is written under a copy with its <see cref="ImageMarkup.Row"/> set.</param>
	/// <param name="rows">Its height in cells.</param>
	/// <param name="columns">Its width in cells.</param>
	/// <param name="cursorRight">Where the cursor goes afterwards.</param>
	/// <param name="firstColumn">The first column of the picture shown, for a cropped placement.</param>
	public void PlacePicture(ImageMarkup picture, int rows, int columns, bool cursorRight, int firstColumn = 0)
	{
		ArgumentNullException.ThrowIfNull(picture);
		if (rows <= 0 || columns <= 0) return;

		var column = Line.Cursor;
		var blank = new string(' ', columns);
		for (var row = 0; row < rows; row++)
		{
			if (row > 0) Line.Cursor = column;
			var cells = picture with { Row = new PictureRow(row, rows, columns) { FirstColumn = firstColumn } };
			Write(blank, cells);
			EndLine();
		}

		Line.Cursor = cursorRight ? column + columns : 0;
	}

	/// <summary>Raises an event.</summary>
	public void Raise(DecodeEvent decodeEvent)
	{
		ArgumentNullException.ThrowIfNull(decodeEvent);
		_decoder.Raise(decodeEvent);
	}

	/// <summary>Raises a <see cref="ReplyEvent"/>: text for the client to send back to the server.</summary>
	public void Reply(string text)
	{
		ArgumentException.ThrowIfNullOrEmpty(text);
		_decoder.Raise(new ReplyEvent(text));
	}

	/// <summary>Whether the text protocol named <paramref name="name"/> is reading the text.</summary>
	public bool IsActive(string name) => ActiveProtocolName is not null && string.Equals(ActiveProtocolName, name, StringComparison.OrdinalIgnoreCase);

	/// <summary>Turns a text protocol on, turning off any other.</summary>
	public bool EnableProtocol(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (IsActive(name)) return true;
		if (!_decoder.Registry.TextProtocols.TryGetValue(name, out var create)) return false;

		if (_active is not null) DisableProtocol(ActiveProtocolName!);
		if (!_protocols.TryGetValue(name, out var protocol)) _protocols[name] = protocol = create();
		_active = protocol;
		ActiveProtocolName = name;
		Raise(new ProtocolEvent(name, true));
		return true;
	}

	/// <summary>Turns a text protocol off.</summary>
	public void DisableProtocol(string name)
	{
		if (!IsActive(name)) return;
		var protocol = _active!;
		_active = null;
		ActiveProtocolName = null;
		protocol.Reset(this);
		Raise(new ProtocolEvent(name, false));
	}

	internal void Feed(ReadOnlySpan<char> text)
	{
		if (_active is not null) _active.Feed(text, this);
		else Print(text);
	}

	internal void SequenceSeen()
	{
		if (_active is ISequenceObserver observer) observer.SequenceSeen(this);
	}

	internal void ResetAll()
	{
		_active?.Reset(this);
		_active = null;
		ActiveProtocolName = null;
		_states.Clear();
		_pens.Clear();
		_elements.Clear();
		_setValid = false;
		Destination = null;
		Line.Take();
	}
}
