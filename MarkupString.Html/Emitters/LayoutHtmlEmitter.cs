using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using MarkupString.Layout;

namespace MarkupString.Html;

/// <summary>Draws one kind of <see cref="Block"/> as HTML, its children through <see cref="HtmlLayoutWriter.Block(Block)"/>.</summary>
/// <typeparam name="T">The block type.</typeparam>
/// <param name="block">The block.</param>
/// <param name="html">Where to write, and the look and registry it is drawn under.</param>
public delegate void BlockHtmlRenderer<in T>(T block, HtmlLayoutWriter html) where T : Block;

/// <summary>
/// Writes a layout's blocks as HTML: each through the renderer registered for its type, and a block
/// with none as the lines it draws, in a <c>&lt;pre&gt;</c>, so nothing is ever left out.
/// </summary>
public sealed class HtmlLayoutWriter
{
	private readonly LayoutHtmlEmitter _emitter;
	private readonly IBufferWriter<char> _output;

	internal HtmlLayoutWriter(LayoutHtmlEmitter emitter, MarkupRegistry registry, IBufferWriter<char> output, int width)
	{
		_emitter = emitter;
		Registry = registry;
		_output = output;
		Width = width;
	}

	/// <summary>The look the block is drawn under: a <see cref="Themed"/> block changes it for what is inside.</summary>
	public LayoutContext Context { get; private set; } = LayoutContext.Default;

	/// <summary>The registry the render runs against.</summary>
	public MarkupRegistry Registry { get; }

	/// <summary>The width the whole layout was laid out at, which a block drawn as text is drawn at.</summary>
	public int Width { get; }

	/// <summary>Writes raw HTML.</summary>
	public void Write(string html) => _output.Write(html);

	/// <summary>Writes <paramref name="text"/> with its markup, as HTML.</summary>
	public void Text(MarkupText text) => MarkupTextRenderer.RenderFragment(text, MarkupFormat.Html, Registry, _output);

	/// <summary>Writes plain text, escaped.</summary>
	public void Encode(string text) => MarkupTextRenderer.EncodeText(text, TextEncoding.Html, _output);

	/// <summary>Writes an attribute value, escaped, without its quotes.</summary>
	public void Attribute(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		foreach (var c in value)
		{
			switch (c)
			{
				case '&': _output.Write("&amp;"); break;
				case '"': _output.Write("&quot;"); break;
				case '\'': _output.Write("&#39;"); break;
				case '<': _output.Write("&lt;"); break;
				case '>': _output.Write("&gt;"); break;
				default:
					if (!char.IsControl(c)) _output.Write([c]);
					break;
			}
		}
	}

	/// <summary>Writes a block through its renderer.</summary>
	public void Block(Block block)
	{
		ArgumentNullException.ThrowIfNull(block);
		if (_emitter.RendererFor(block.GetType()) is { } renderer) renderer(block, this);
		else AsText(block);
	}

	/// <summary>Writes a block under <paramref name="context"/>, and goes back to the context before.</summary>
	public void Block(Block block, LayoutContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		var before = Context;
		Context = context;
		try
		{
			Block(block);
		}
		finally
		{
			Context = before;
		}
	}

	/// <summary>Writes a block as the lines it draws at the layout's width, in a <c>&lt;pre&gt;</c>.</summary>
	public void AsText(Block block)
	{
		Write("<pre class=\"ms-pre\">");
		Text(MarkupText.Join(MarkupText.NewLine, Context.Lines(block, Width)));
		Write("</pre>");
	}

	/// <summary>Whether the page may load <paramref name="source"/>: an http(s) or relative address the host allows.</summary>
	public bool AllowsImage(string source) => _emitter.IsShowable(source);
}

/// <summary>
/// Draws a <see cref="LayoutMarkup"/> as page structure: a box as a <c>&lt;fieldset&gt;</c> with its
/// title as the legend, columns as a wrapping flex row, a figure as an image floated beside its text.
/// The classes it writes are styled by <see cref="LayoutCss.Fixed"/>.
/// </summary>
/// <remarks>
/// Widths become hints: an item asks for its terminal width in <c>ch</c> and the row wraps when the
/// page is narrower, so a 78-column box reads on a phone. Border characters are not drawn; the
/// border is CSS, and <see cref="BorderStyle.Name"/> picks its look.
/// </remarks>
internal sealed class LayoutHtmlEmitter : IBlockEmitter
{
	private readonly FrozenDictionary<Type, Action<Block, HtmlLayoutWriter>> _renderers;
	private readonly Func<string, bool>? _allowImage;

	private LayoutHtmlEmitter(FrozenDictionary<Type, Action<Block, HtmlLayoutWriter>> renderers, Func<string, bool>? allowImage)
	{
		_renderers = renderers;
		_allowImage = allowImage;
	}

	public static LayoutHtmlEmitter Default { get; } = new(LayoutHtml.BuiltIns.ToFrozenDictionary(), null);

	public Type MarkupType => typeof(LayoutMarkup);

	public MarkupFormat Format => MarkupFormat.Html;

	/// <summary>This emitter drawing <paramref name="type"/> with <paramref name="renderer"/>.</summary>
	public LayoutHtmlEmitter With(Type type, Action<Block, HtmlLayoutWriter> renderer)
	{
		var renderers = new Dictionary<Type, Action<Block, HtmlLayoutWriter>>(_renderers) { [type] = renderer };
		return new(renderers.ToFrozenDictionary(), _allowImage);
	}

	/// <summary>This emitter holding pictures to <paramref name="allowImage"/>.</summary>
	public LayoutHtmlEmitter WithImages(Func<string, bool> allowImage) => new(_renderers, allowImage);

	/// <summary>The emitter <paramref name="registry"/> draws layouts with, or the default one.</summary>
	public static LayoutHtmlEmitter In(MarkupRegistry registry) =>
		registry.FindBlockEmitter(typeof(LayoutMarkup), MarkupFormat.Html) as LayoutHtmlEmitter ?? Default;

	public Action<Block, HtmlLayoutWriter>? RendererFor(Type type) => _renderers.TryGetValue(type, out var renderer) ? renderer : null;

	public bool TryEmit(IBlockMarkup markup, MarkupText region, MarkupRegistry registry, IBufferWriter<char> output)
	{
		if (markup is not LayoutMarkup layout) return false;
		output.Write("<div class=\"ms-layout\" style=\"max-width:");
		output.Write(layout.Width.ToString(CultureInfo.InvariantCulture));
		output.Write("ch\">");
		new HtmlLayoutWriter(this, registry, output, layout.Width).Block(layout.Root);
		output.Write("</div>");
		return true;
	}

	/// <summary>Whether the page may load <paramref name="source"/>: an http(s) or relative address the host allows.</summary>
	public bool IsShowable(string source)
	{
		if (string.IsNullOrWhiteSpace(source) || source.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;

		// On Unix a rooted path parses as an absolute file: address and "//host" as one on another
		// host, so both are decided before the parser sees them.
		if (source.StartsWith("//", StringComparison.Ordinal) || source.StartsWith('\\')) return false;
		if (!source.StartsWith('/'))
		{
			if (!Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out var uri)) return false;
			if (uri.IsAbsoluteUri && uri.Scheme is not ("http" or "https")) return false;
		}
		return _allowImage?.Invoke(source) ?? true;
	}
}
