using MarkupString.Ansi;
using MarkupString.Html;

namespace MarkupString.Web;

/// <summary>
/// Where the browser side of MarkupString is, and what it is: a stylesheet for every class
/// <see cref="MarkupFormat.Html"/> writes, and the script module that makes the elements it writes act.
/// </summary>
/// <remarks>
/// An ASP.NET Core or Blazor app serves both from the package as static web assets, at
/// <see cref="StylesheetPath"/> and <see cref="ScriptPath"/>. A host that serves pages some other way (a
/// WebView, a server writing its own responses) takes <see cref="Stylesheet"/> and <see cref="Script"/>.
/// </remarks>
public static class MarkupWeb
{
	/// <summary>The path, relative to the app's base, the package's files are served under.</summary>
	public const string ContentRoot = "_content/MarkupString.Web/";

	/// <summary>The stylesheet's path: <see cref="Stylesheet"/> as a file.</summary>
	public const string StylesheetPath = ContentRoot + "markupstring.css";

	/// <summary>The script module's path: <see cref="Script"/> as a file.</summary>
	public const string ScriptPath = ContentRoot + "markupstring.js";

	/// <summary>The rules for the elements the script acts on: menus, expired links, gauges, the bell.</summary>
	public static string RuntimeStylesheet { get; } = Resource("runtime.css");

	/// <summary>
	/// The whole stylesheet: <see cref="AnsiCss.Fixed"/>, <see cref="LayoutCss.Fixed"/> and
	/// <see cref="RuntimeStylesheet"/>. The file at <see cref="StylesheetPath"/> is the same text.
	/// </summary>
	public static string Stylesheet { get; } = AnsiCss.Fixed + LayoutCss.Fixed + RuntimeStylesheet;

	/// <summary>The script module, an ES module that exports <c>attach</c> and <c>attachDotNet</c>.</summary>
	public static string Script { get; } = Resource("markupstring.js");

	private static string Resource(string name)
	{
		using var stream = typeof(MarkupWeb).Assembly.GetManifestResourceStream("MarkupString.Web." + name)
			?? throw new InvalidOperationException($"MarkupString.Web is missing its {name}.");
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}
}
