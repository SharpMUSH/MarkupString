namespace MarkupString.Display;

/// <summary>How many colours an output can show.</summary>
public enum ColorLevel
{
	/// <summary>No colour, and no attributes either.</summary>
	None,

	/// <summary>No colour, but attributes such as bold and underline.</summary>
	Attributes,

	/// <summary>The sixteen standard colours.</summary>
	Standard,

	/// <summary>The xterm 256-colour palette.</summary>
	Xterm256,

	/// <summary>Any RGB colour.</summary>
	TrueColor,
}

/// <summary>Text attributes, as an output can draw them and a <see cref="DisplayStyle"/> carries them.</summary>
[Flags]
public enum TextAttributes
{
	/// <summary>None.</summary>
	None = 0,

	/// <summary>Bold, or bright.</summary>
	Bold = 1 << 0,

	/// <summary>Faint, or dim.</summary>
	Faint = 1 << 1,

	/// <summary>Italic.</summary>
	Italic = 1 << 2,

	/// <summary>Underlined.</summary>
	Underline = 1 << 3,

	/// <summary>Blinking.</summary>
	Blink = 1 << 4,

	/// <summary>Foreground and background swapped.</summary>
	Inverse = 1 << 5,

	/// <summary>Hidden: drawn in the background colour, but still text.</summary>
	Conceal = 1 << 6,

	/// <summary>Struck through.</summary>
	Strikethrough = 1 << 7,

	/// <summary>A line over the text.</summary>
	Overline = 1 << 8,

	/// <summary>Every attribute.</summary>
	All = Bold | Faint | Italic | Underline | Blink | Inverse | Conceal | Strikethrough | Overline,
}

/// <summary>What an output does when the text it shows is clicked.</summary>
[Flags]
public enum InteractionKinds
{
	/// <summary>Nothing is clickable.</summary>
	None = 0,

	/// <summary>A link opens an address.</summary>
	Url = 1,

	/// <summary>A link sends a command to the game.</summary>
	Command = 2,

	/// <summary>A link puts a command in the input line without sending it.</summary>
	Prompt = 4,

	/// <summary>A link offers a menu of commands.</summary>
	Menu = 8,

	/// <summary>Every kind.</summary>
	All = Url | Command | Prompt | Menu,
}

/// <summary>The ways an output can draw a picture, best first.</summary>
[Flags]
public enum PictureMethods
{
	/// <summary>Pictures are shown as their text.</summary>
	None = 0,

	/// <summary>The output draws pixels itself: a window, or a browser.</summary>
	Bitmap = 1,

	/// <summary>Kitty graphics drawn with Unicode placeholders.</summary>
	KittyPlaceholders = 2,

	/// <summary>iTerm2 inline images.</summary>
	InlineImages = 4,

	/// <summary>Sixel graphics.</summary>
	Sixel = 8,

	/// <summary>Coloured half blocks, two pixels to a cell.</summary>
	HalfBlocks = 16,
}

/// <summary>How much of Unicode an output can draw.</summary>
public enum UnicodeLevel
{
	/// <summary>Everything, wide characters and emoji included.</summary>
	Full,

	/// <summary>Everything that is one cell wide.</summary>
	Narrow,

	/// <summary>ASCII only; the rest is folded with <see cref="AsciiFold"/>.</summary>
	Ascii,
}

/// <summary>
/// What one output can show. A renderer reads it to decide how far to degrade what it was given, and a client
/// reads it to decide what to tell the server it accepts.
/// </summary>
public sealed record OutputCapabilities
{
	/// <summary>How many colours it shows.</summary>
	public ColorLevel Colors { get; init; } = ColorLevel.TrueColor;

	/// <summary>The attributes it draws. Blink is often left out by choice.</summary>
	public TextAttributes Attributes { get; init; } = TextAttributes.All;

	/// <summary>What happens when text is clicked.</summary>
	public InteractionKinds Interactions { get; init; } = InteractionKinds.All;

	/// <summary>How it draws pictures.</summary>
	public PictureMethods Pictures { get; init; }

	/// <summary>Whether a moving picture plays rather than showing its first frame.</summary>
	public bool MovingPictures { get; init; }

	/// <summary>Whether it plays sounds.</summary>
	public bool Sound { get; init; }

	/// <summary>Whether it shows text in named panes apart from the main output.</summary>
	public bool Panes { get; init; }

	/// <summary>How much of Unicode it draws.</summary>
	public UnicodeLevel Unicode { get; init; } = UnicodeLevel.Full;

	/// <summary>Plain text and nothing else.</summary>
	public static OutputCapabilities PlainText { get; } = new()
	{
		Colors = ColorLevel.None,
		Attributes = TextAttributes.None,
		Interactions = InteractionKinds.None,
	};

	/// <summary>A grid of cells the client draws itself, such as a window drawn with Skia.</summary>
	public static OutputCapabilities CellGrid { get; } = new()
	{
		Pictures = PictureMethods.Bitmap | PictureMethods.HalfBlocks,
		MovingPictures = true,
		Sound = true,
		Panes = true,
	};

	/// <summary>A web page.</summary>
	public static OutputCapabilities Browser { get; } = new()
	{
		Pictures = PictureMethods.Bitmap,
		MovingPictures = true,
		Sound = true,
		Panes = true,
	};

	/// <summary>
	/// What both this output and <paramref name="other"/> can show: the lesser colour level and Unicode level,
	/// and the shared attributes, interactions and picture methods.
	/// </summary>
	public OutputCapabilities Intersect(OutputCapabilities other)
	{
		ArgumentNullException.ThrowIfNull(other);
		return new OutputCapabilities
		{
			Colors = (ColorLevel)Math.Min((int)Colors, (int)other.Colors),
			Attributes = Attributes & other.Attributes,
			Interactions = Interactions & other.Interactions,
			Pictures = Pictures & other.Pictures,
			MovingPictures = MovingPictures && other.MovingPictures,
			Sound = Sound && other.Sound,
			Panes = Panes && other.Panes,
			Unicode = (UnicodeLevel)Math.Max((int)Unicode, (int)other.Unicode),
		};
	}

	/// <summary>
	/// What both this output and <paramref name="other"/> together can show, for a client that shows one session in
	/// two places and tells the server once.
	/// </summary>
	public OutputCapabilities Union(OutputCapabilities other)
	{
		ArgumentNullException.ThrowIfNull(other);
		return new OutputCapabilities
		{
			Colors = (ColorLevel)Math.Max((int)Colors, (int)other.Colors),
			Attributes = Attributes | other.Attributes,
			Interactions = Interactions | other.Interactions,
			Pictures = Pictures | other.Pictures,
			MovingPictures = MovingPictures || other.MovingPictures,
			Sound = Sound || other.Sound,
			Panes = Panes || other.Panes,
			Unicode = (UnicodeLevel)Math.Min((int)Unicode, (int)other.Unicode),
		};
	}

	/// <summary>The best way it draws a picture, or <see cref="PictureMethods.None"/>.</summary>
	public PictureMethods BestPictureMethod =>
		(Pictures & PictureMethods.Bitmap) != 0 ? PictureMethods.Bitmap
		: (Pictures & PictureMethods.KittyPlaceholders) != 0 ? PictureMethods.KittyPlaceholders
		: (Pictures & PictureMethods.InlineImages) != 0 ? PictureMethods.InlineImages
		: (Pictures & PictureMethods.Sixel) != 0 ? PictureMethods.Sixel
		: (Pictures & PictureMethods.HalfBlocks) != 0 ? PictureMethods.HalfBlocks
		: PictureMethods.None;
}
