namespace MarkupString.Decoding;

/// <summary>
/// The line being read: pieces of text appended at the cursor, and overwritten in place when the cursor has been
/// moved back (a carriage return, a backspace), so a progress bar redrawn over itself ends as its last state.
/// </summary>
internal sealed class LineBuilder(int maxLength)
{
	private readonly List<MarkupText> _parts = [];
	private int _width;
	private int _length;
	private int _cursor;

	public bool IsEmpty => _parts.Count == 0;

	/// <summary>The column the next text is written at. Past the end, the gap is filled with spaces when text comes.</summary>
	public int Cursor
	{
		get => _cursor;
		set => _cursor = Math.Max(0, value);
	}

	public bool Write(string text, MarkupSet? markups) =>
		Put(markups is null ? MarkupText.Plain(text) : MarkupText.Wrap(markups, text));

	/// <summary>Writes <paramref name="text"/> at the cursor and moves the cursor past it.</summary>
	/// <returns>False when the line was full and the text was dropped.</returns>
	public bool Put(MarkupText text)
	{
		if (_length + text.Length > maxLength) return false;

		if (_cursor > _width)
		{
			var gap = MarkupText.Plain(new string(' ', _cursor - _width));
			Append(gap);
		}

		var width = text.DisplayWidth;
		if (_cursor == _width)
		{
			Append(text);
			_cursor = _width;
			return true;
		}

		// Overwrite the cells from the cursor on.
		var line = Materialise();
		var before = line.Substring(0, DisplayWidth.IndexAtWidth(line.Text, _cursor));
		var after = line.Substring(DisplayWidth.IndexAtWidth(line.Text, _cursor + width));
		Replace(MarkupText.Concat([before, text, after]));
		_cursor = before.DisplayWidth + width;
		return true;
	}

	/// <summary>Removes everything from column <paramref name="column"/> on.</summary>
	public void EraseFrom(int column)
	{
		if (column >= _width) return;
		var line = Materialise();
		Replace(line.Substring(0, DisplayWidth.IndexAtWidth(line.Text, column)));
	}

	/// <summary>Blanks everything up to and including column <paramref name="column"/>.</summary>
	public void EraseTo(int column)
	{
		if (_width == 0) return;
		var line = Materialise();
		var end = Math.Min(_width, column + 1);
		var after = line.Substring(DisplayWidth.IndexAtWidth(line.Text, end));
		Replace(MarkupText.Concat(MarkupText.Plain(new string(' ', end)), after));
	}

	/// <summary>The line as it stands.</summary>
	public MarkupText Peek() => _parts.Count switch
	{
		0 => MarkupText.Empty,
		1 => _parts[0],
		_ => Materialise(),
	};

	/// <summary>The line, which is then empty with the cursor at column 0.</summary>
	public MarkupText Take()
	{
		var line = Peek();
		_parts.Clear();
		_width = 0;
		_length = 0;
		_cursor = 0;
		return line;
	}

	private void Append(MarkupText text)
	{
		_parts.Add(text);
		_width += text.DisplayWidth;
		_length += text.Length;
	}

	private MarkupText Materialise()
	{
		if (_parts.Count == 1) return _parts[0];
		var line = MarkupText.Concat(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_parts));
		_parts.Clear();
		_parts.Add(line);
		return line;
	}

	private void Replace(MarkupText line)
	{
		_parts.Clear();
		_width = 0;
		_length = 0;
		if (line.Length > 0) Append(line);
	}
}
