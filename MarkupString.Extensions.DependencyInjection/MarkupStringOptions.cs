using MarkupString.Display;
namespace MarkupString.Extensions.DependencyInjection;

/// <summary>
/// The settings <see cref="ServiceCollectionExtensions.AddMarkupString"/> reads: set in code, or bound from a
/// configuration section (<c>"MarkupString"</c> in <c>appsettings.json</c>).
/// </summary>
public sealed class MarkupStringOptions
{
	/// <summary>How each connection's output is read.</summary>
	public DecodingSettings Decoding { get; set; } = new();

	/// <summary>How pictures are kept.</summary>
	public PictureSettings Pictures { get; set; } = new();

	/// <summary>How styles are drawn.</summary>
	public DisplaySettings Display { get; set; } = new();

	/// <summary>
	/// Link schemes allowed besides <c>http</c>, <c>https</c>, <c>mailto</c>, <c>ftp</c>, <c>ftps</c> and <c>tel</c>,
	/// such as <c>mudclient</c>. <c>javascript</c>, <c>data</c>, <c>vbscript</c> and <c>file</c> are never allowed.
	/// </summary>
	public List<string> LinkSchemes { get; set; } = [];
}

/// <summary>How each connection's output is read.</summary>
public sealed class DecodingSettings
{
	/// <summary>The longest line, in characters.</summary>
	public int MaxLineLength { get; set; } = 64 * 1024;

	/// <summary>The most elements a text protocol may have open at once.</summary>
	public int MaxOpenElements { get; set; } = 64;

	/// <summary>Whether a server may be told the cursor position (<c>CSI 6 n</c>).</summary>
	public bool AnswerCursorPosition { get; set; }
}

/// <summary>How pictures are kept.</summary>
public sealed class PictureSettings
{
	/// <summary>Whether pictures are read at all.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>The bytes the shared <see cref="Ansi.Decoding.MemoryPictureCache"/> holds. 64 MiB unless set.</summary>
	public long Budget { get; set; } = 64L * 1024 * 1024;

	/// <summary>The largest picture decoded, in pixels.</summary>
	public long MaxPixels { get; set; } = 4096L * 4096;
}

/// <summary>How styles are drawn.</summary>
public sealed class DisplaySettings
{
	/// <summary>The output drawn for unless a caller says otherwise: <c>Browser</c>, <c>CellGrid</c> or <c>PlainText</c>.</summary>
	public string Output { get; set; } = "Browser";

	/// <summary>The most colours shown, or null for what <see cref="Output"/> shows.</summary>
	public ColorLevel? Colors { get; set; }

	/// <summary>Whether bold standard colours are drawn bright, as many MU* clients do.</summary>
	public bool BoldIsBright { get; set; }
}
