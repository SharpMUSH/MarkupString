using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Display;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace MarkupString.Extensions.DependencyInjection;

/// <summary>Registers MarkupString with a service collection.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers, as singletons: the <see cref="MarkupRegistry"/>; the <see cref="DecoderRegistry"/> (VT and
	/// pictures) and an <see cref="IDecoderFactory"/> that makes each connection's decoder with the configured limits;
	/// one <see cref="IPictureCache"/> every connection shares; the <see cref="DisplayRegistry"/> as the
	/// <see cref="IStyleResolver"/>, with default <see cref="DisplayOptions"/>; the <see cref="IUrlPolicy"/>; and an
	/// <see cref="EffectDispatcher"/> over every registered <see cref="IEffectHandler"/>.
	/// Decode metrics go to the host's <see cref="IMeterFactory"/>, and what decoders drop is logged at debug level
	/// to its <see cref="ILoggerFactory"/> (category <c>MarkupString.Decoding</c>), when it has them. A service registered before this
	/// call is kept, so any of them can be replaced. Calling it again adds to the same setup.
	/// </summary>
	public static MarkupStringBuilder AddMarkupString(this IServiceCollection services, Action<MarkupStringOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		var setup = Setup(services);
		services.AddOptions<MarkupStringOptions>();
		if (configure is not null) services.Configure(configure);

		services.TryAddSingleton<IUrlPolicy>(static sp =>
		{
			var options = Options(sp);
			var policy = UrlPolicy.WithSchemes(options.LinkSchemes);
			return options.RefuseControlCharactersInLinks ? policy.RefusingControlCharacters() : policy;
		});
		services.TryAddSingleton<IPictureCache>(static sp => new MemoryPictureCache { Budget = Options(sp).Pictures.Budget });
		services.TryAddSingleton(sp =>
		{
			var options = Options(sp);
			var registry = DecoderRegistry.Empty.WithVt();
			if (options.Pictures.Enabled) registry = registry.WithPictures(sp.GetRequiredService<IPictureCache>());
			return setup.Decoders.Aggregate(registry, static (current, step) => step(current));
		});
		services.TryAddSingleton(static sp => new MarkupDecoderFactory(
			sp.GetRequiredService<DecoderRegistry>(),
			Options(sp),
			sp.GetRequiredService<IUrlPolicy>(),
			sp.GetService<IMeterFactory>(),
			sp.GetService<ILoggerFactory>()));
		services.TryAddSingleton<IDecoderFactory>(static sp => sp.GetRequiredService<MarkupDecoderFactory>());
		services.TryAddSingleton(sp => setup.Display.Aggregate(
			DisplayRegistry.Core.WithAnsi(Options(sp).Display.BoldIsBright), static (current, step) => step(current)));
		services.TryAddSingleton<IStyleResolver>(static sp => sp.GetRequiredService<DisplayRegistry>());
		services.TryAddSingleton(static sp => DisplayDefaults(Options(sp).Display, sp.GetRequiredService<IUrlPolicy>()));
		services.TryAddSingleton(sp => setup.Markup.Aggregate(
			MarkupRegistry.Empty.WithAnsi().WithLinks(sp.GetRequiredService<IUrlPolicy>()), static (current, step) => step(current)));
		services.TryAddSingleton(static sp => new EffectDispatcher(sp.GetServices<IEffectHandler>()));
		return new MarkupStringBuilder(services, setup);
	}

	private static MarkupStringOptions Options(IServiceProvider services) => services.GetRequiredService<IOptions<MarkupStringOptions>>().Value;

	private static DisplayOptions DisplayDefaults(DisplaySettings settings, IUrlPolicy links)
	{
		var capabilities = settings.Output.ToUpperInvariant() switch
		{
			"CELLGRID" => OutputCapabilities.CellGrid,
			"PLAINTEXT" => OutputCapabilities.PlainText,
			"BROWSER" => OutputCapabilities.Browser,
			_ => throw new OptionsValidationException(nameof(DisplaySettings.Output), typeof(MarkupStringOptions),
				[$"MarkupString:Display:Output is '{settings.Output}'; it must be Browser, CellGrid or PlainText."]),
		};
		if (settings.Colors is { } colors) capabilities = capabilities with { Colors = colors };
		return new DisplayOptions { Capabilities = capabilities, Links = links };
	}

	private static MarkupStringSetup Setup(IServiceCollection services)
	{
		foreach (var descriptor in services)
		{
			if (descriptor.ServiceType == typeof(MarkupStringSetup) && descriptor.ImplementationInstance is MarkupStringSetup existing) return existing;
		}

		var setup = new MarkupStringSetup();
		services.AddSingleton(setup);
		return setup;
	}
}

/// <summary>The steps <see cref="MarkupStringBuilder"/> collects, applied when the registries are first resolved.</summary>
internal sealed class MarkupStringSetup
{
	public List<Func<DecoderRegistry, DecoderRegistry>> Decoders { get; } = [];

	public List<Func<DisplayRegistry, DisplayRegistry>> Display { get; } = [];

	public List<Func<MarkupRegistry, MarkupRegistry>> Markup { get; } = [];
}

/// <summary>Adds to what <see cref="ServiceCollectionExtensions.AddMarkupString"/> registered.</summary>
public sealed class MarkupStringBuilder
{
	private readonly MarkupStringSetup _setup;

	internal MarkupStringBuilder(IServiceCollection services, MarkupStringSetup setup)
	{
		Services = services;
		_setup = setup;
	}

	/// <summary>The service collection.</summary>
	public IServiceCollection Services { get; }

	/// <summary>Adds decoder plugins, such as <c>registry =&gt; registry.WithMxp().WithPueblo()</c>.</summary>
	public MarkupStringBuilder AddDecoders(Func<DecoderRegistry, DecoderRegistry> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		_setup.Decoders.Add(configure);
		return this;
	}

	/// <summary>Adds style contributors to the display registry.</summary>
	public MarkupStringBuilder AddDisplay(Func<DisplayRegistry, DisplayRegistry> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		_setup.Display.Add(configure);
		return this;
	}

	/// <summary>Adds emitters and codecs to the markup registry, such as <c>registry =&gt; registry.WithHtml()</c>.</summary>
	public MarkupStringBuilder AddMarkup(Func<MarkupRegistry, MarkupRegistry> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		_setup.Markup.Add(configure);
		return this;
	}

	/// <summary>
	/// Adds <typeparamref name="THandler"/>, a singleton, to the handlers the <see cref="EffectDispatcher"/> hands output
	/// effects to. A handler added later is asked first.
	/// </summary>
	public MarkupStringBuilder AddEffectHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
		where THandler : class, IEffectHandler
	{
		Services.AddSingleton<IEffectHandler, THandler>();
		return this;
	}

	/// <summary>Adds <paramref name="handler"/> to the handlers the <see cref="EffectDispatcher"/> hands output effects to.</summary>
	public MarkupStringBuilder AddEffectHandler(IEffectHandler handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		Services.AddSingleton(handler);
		return this;
	}

	/// <summary>Binds <see cref="MarkupStringOptions"/> to the configuration section at <paramref name="path"/>.</summary>
	public MarkupStringBuilder BindConfiguration(string path = "MarkupString")
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		Services.AddOptions<MarkupStringOptions>().BindConfiguration(path);
		return this;
	}

	/// <summary>Keeps pictures in <typeparamref name="TCache"/>, a singleton, instead of memory.</summary>
	public MarkupStringBuilder UsePictureCache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCache>()
		where TCache : class, IPictureCache
	{
		Services.Replace(ServiceDescriptor.Singleton<IPictureCache, TCache>());
		return this;
	}

	/// <summary>Decides which links are allowed with <typeparamref name="TPolicy"/>, a singleton.</summary>
	public MarkupStringBuilder UseUrlPolicy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPolicy>()
		where TPolicy : class, IUrlPolicy
	{
		Services.Replace(ServiceDescriptor.Singleton<IUrlPolicy, TPolicy>());
		return this;
	}
}

/// <summary>
/// Makes each connection's <see cref="Decoder"/> from the registered <see cref="DecoderRegistry"/>, with the
/// configured limits, link policy, metrics, logging and picture size.
/// </summary>
public sealed partial class MarkupDecoderFactory : IDecoderFactory
{
	private readonly DecoderRegistry _registry;
	private readonly long _maxPixels;
	private readonly ILogger? _logger;

	/// <summary>A factory over <paramref name="registry"/>.</summary>
	public MarkupDecoderFactory(DecoderRegistry registry, MarkupStringOptions options, IUrlPolicy links, IMeterFactory? meters = null,
		ILoggerFactory? loggers = null)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(links);
		_registry = registry;
		_maxPixels = options.Pictures.MaxPixels;
		_logger = loggers?.CreateLogger("MarkupString.Decoding");
		var answers = ClientAnswers.Default;
		answers = options.Decoding.AnswerCursorPosition ? answers | ClientAnswers.CursorPosition : answers & ~ClientAnswers.CursorPosition;
		Defaults = new DecoderOptions
		{
			MaxLineLength = options.Decoding.MaxLineLength,
			MaxOpenElements = options.Decoding.MaxOpenElements,
			Links = links,
			Meters = meters,
			Identity = new ClientIdentity { Answers = answers },
		};
	}

	/// <summary>What each decoder is made with unless a caller gives its own.</summary>
	public DecoderOptions Defaults { get; }

	/// <inheritdoc/>
	public Decoder CreateDecoder(DecoderOptions? options = null)
	{
		var decoder = _registry.CreateDecoder(options ?? Defaults);
		if (_registry.Has(PicturesPlugin.Instance.Name)) decoder.Pictures().MaxPixels = _maxPixels;
		if (_logger is { } logger && logger.IsEnabled(LogLevel.Debug)) decoder.Diagnostics.Dropped += (kind, count) => LogDropped(logger, kind, count);
		return decoder;
	}

	[LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Decoder dropped {Count} × {Kind}")]
	private static partial void LogDropped(ILogger logger, DecodeDrop kind, int count);

	/// <summary>A decoder with the defaults, for a client that is <paramref name="identity"/>.</summary>
	public Decoder CreateDecoderFor(ClientIdentity identity)
	{
		ArgumentNullException.ThrowIfNull(identity);
		return CreateDecoder(Defaults with { Identity = identity });
	}
}
