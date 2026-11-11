namespace MarkupString;

/// <summary>Which link addresses may become something a client navigates to.</summary>
public interface IUrlPolicy
{
	/// <summary>Whether <paramref name="url"/> may be a link. One that may not is shown as its text.</summary>
	bool IsAllowed(string url);
}

/// <summary>
/// The usual <see cref="IUrlPolicy"/>: relative addresses and <c>http</c>, <c>https</c>, <c>mailto</c>, <c>ftp</c>,
/// <c>ftps</c> and <c>tel</c>, with whatever other schemes it is made with. <c>javascript:</c>, <c>data:</c>,
/// <c>vbscript:</c> and <c>file:</c> are never allowed. <see cref="RefusingControlCharacters"/> also refuses an
/// address holding a control character, or with a space at either end.
/// </summary>
public sealed class UrlPolicy : IUrlPolicy
{
	private static readonly string[] Usual = ["http", "https", "mailto", "ftp", "ftps", "tel"];
	private static readonly string[] Never = ["javascript", "data", "vbscript", "file"];

	private readonly HashSet<string> _schemes;

	private UrlPolicy(IEnumerable<string> schemes) => _schemes = new HashSet<string>(schemes, StringComparer.OrdinalIgnoreCase);

	private UrlPolicy(HashSet<string> schemes, bool refusesControlCharacters)
	{
		_schemes = schemes;
		RefusesControlCharacters = refusesControlCharacters;
	}

	/// <summary>The usual schemes only.</summary>
	public static UrlPolicy Safe { get; } = new(Usual);

	/// <summary>The usual schemes and <paramref name="schemes"/> (such as <c>mudclient</c>), without the colon.</summary>
	public static UrlPolicy WithSchemes(params IEnumerable<string> schemes)
	{
		ArgumentNullException.ThrowIfNull(schemes);
		return new UrlPolicy(Usual.Concat(schemes.Where(scheme => !Never.Contains(scheme, StringComparer.OrdinalIgnoreCase))));
	}

	/// <summary>The schemes it allows.</summary>
	public IReadOnlyCollection<string> Schemes => _schemes;

	/// <summary>Whether it refuses an address holding a control character, or with a space at either end.</summary>
	public bool RefusesControlCharacters { get; }

	/// <summary>
	/// This policy, also refusing an address that holds a control character (U+0000–U+001F, U+007F–U+009F) or
	/// starts or ends with a space. A browser removes tabs and newlines from an address, and controls and
	/// spaces around it, before reading its scheme, so <c>java&amp;#9;script:alert(1)</c> from an MXP or Pueblo
	/// link passes as a relative address here and runs as <c>javascript:</c> when clicked. Off unless asked for,
	/// so addresses a host already accepts keep working.
	/// </summary>
	public UrlPolicy RefusingControlCharacters() => RefusesControlCharacters ? this : new UrlPolicy(_schemes, refusesControlCharacters: true);

	/// <inheritdoc/>
	public bool IsAllowed(string url)
	{
		if (string.IsNullOrWhiteSpace(url)) return false;
		if (RefusesControlCharacters && HasControlOrEdgeSpace(url)) return false;
		if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri)) return false;

		// A relative URL or a bare fragment cannot name a scheme, so there is none to distrust.
		if (!uri.IsAbsoluteUri) return true;
		return _schemes.Contains(uri.Scheme);
	}

	private static bool HasControlOrEdgeSpace(string url) =>
		url.AsSpan().ContainsAnyInRange('\u0000', '\u001F')
		|| url.AsSpan().ContainsAnyInRange('\u007F', '\u009F')
		|| url[0] == ' '
		|| url[^1] == ' ';
}
