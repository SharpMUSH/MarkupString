using System.Text;
namespace MarkupString.Decoding;

/// <summary>What a <see cref="TagScanner"/> finds.</summary>
public interface ITagSink
{
	/// <summary>Whether <c>&lt;</c> and <c>&amp;</c> start markup now; when false they are text.</summary>
	bool ReadsMarkup { get; }

	/// <summary>Text.</summary>
	void Text(ReadOnlySpan<char> text);

	/// <summary>A tag: what was between <c>&lt;</c> and <c>&gt;</c>.</summary>
	void Tag(ReadOnlySpan<char> body);

	/// <summary>An entity: what was between <c>&amp;</c> and <c>;</c>.</summary>
	void Entity(ReadOnlySpan<char> name);
}

/// <summary>
/// Splits a tag protocol's text (MXP, Pueblo's HTML) into text, tags and entities, across as many feeds as it
/// arrives in. Comments (<c>&lt;!-- --&gt;</c>) are dropped. A tag or entity too long to be one is text.
/// </summary>
public sealed class TagScanner
{
	private enum State
	{
		Text,
		Tag,
		Quoted,
		Comment,
		Entity,
	}

	private readonly StringBuilder _pending = new();
	private State _state;
	private char _quote;

	/// <summary>The longest tag read as one.</summary>
	public int MaxTagLength { get; init; } = 8192;

	/// <summary>The longest entity name read as one.</summary>
	public int MaxEntityLength { get; init; } = 32;

	/// <summary>Whether a tag or an entity is half read.</summary>
	public bool InMarkup => _state != State.Text;

	/// <summary>Reads <paramref name="text"/>.</summary>
	public void Feed(ReadOnlySpan<char> text, ITagSink sink)
	{
		ArgumentNullException.ThrowIfNull(sink);
		var start = 0;
		for (var i = 0; i < text.Length; i++)
		{
			var c = text[i];
			switch (_state)
			{
				case State.Text:
					if (!sink.ReadsMarkup || (c != '<' && c != '&')) continue;
					if (i > start) sink.Text(text[start..i]);
					_pending.Clear();
					_state = c == '<' ? State.Tag : State.Entity;
					start = i + 1;
					continue;

				case State.Tag:
					if (c is '"' or '\'')
					{
						_quote = c;
						_state = State.Quoted;
					}
					else if (c == '>')
					{
						_state = State.Text;
						start = i + 1;
						sink.Tag(Take());
						continue;
					}

					_pending.Append(c);
					if (_pending.Length == 3 && _pending.Equals("!--"))
					{
						_pending.Clear();
						_state = State.Comment;
					}
					else if (_pending.Length > MaxTagLength)
					{
						Abandon('<', sink);
						start = i + 1;
					}

					continue;

				case State.Quoted:
					_pending.Append(c);
					if (c == _quote) _state = State.Tag;
					else if (_pending.Length > MaxTagLength)
					{
						Abandon('<', sink);
						start = i + 1;
					}

					continue;

				case State.Comment:
					_pending.Append(c);
					if (_pending.Length > 3) _pending.Remove(0, _pending.Length - 3);
					if (_pending.Equals("-->"))
					{
						_pending.Clear();
						_state = State.Text;
						start = i + 1;
					}

					continue;

				case State.Entity:
					if (c == ';')
					{
						_state = State.Text;
						start = i + 1;
						sink.Entity(Take());
						continue;
					}

					if (char.IsAsciiLetterOrDigit(c) || c is '#' or '_' or '-' or '.')
					{
						_pending.Append(c);
						if (_pending.Length > MaxEntityLength)
						{
							Abandon('&', sink);
							start = i + 1;
						}

						continue;
					}

					// Not an entity after all: the ampersand and what followed are text, this character included.
					Abandon('&', sink);
					start = i;
					i--;
					continue;
			}
		}

		if (_state == State.Text && start < text.Length) sink.Text(text[start..]);
	}

	/// <summary>Ends what is half read, as text: a line ended inside a tag.</summary>
	public void Flush(ITagSink sink)
	{
		ArgumentNullException.ThrowIfNull(sink);
		switch (_state)
		{
			case State.Tag or State.Quoted:
				Abandon('<', sink);
				break;
			case State.Entity:
				Abandon('&', sink);
				break;
			case State.Comment:
				// A comment may run over lines; it waits for its end.
				return;
		}
	}

	/// <summary>Forgets what is half read.</summary>
	public void Reset()
	{
		_pending.Clear();
		_state = State.Text;
	}

	private string Take()
	{
		var taken = _pending.ToString();
		_pending.Clear();
		return taken;
	}

	private void Abandon(char opener, ITagSink sink)
	{
		var text = opener + Take();
		_state = State.Text;
		sink.Text(text);
	}
}
