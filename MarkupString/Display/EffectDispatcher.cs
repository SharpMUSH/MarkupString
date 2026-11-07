namespace MarkupString.Display;

/// <summary>Acts on output effects of the kinds it knows: plays sounds, sets a title, counts a bell.</summary>
public interface IEffectHandler
{
	/// <summary>Acts on <paramref name="effect"/>, or leaves it.</summary>
	/// <returns>True when it was handled, so no older handler is asked.</returns>
	bool Handle(OutputEffect effect);
}

/// <summary>
/// Hands each <see cref="OutputEffect"/> to a list of <see cref="IEffectHandler"/>s, newest first, until one
/// handles it, as a decoder tries its plugins. A handler added later can so take over one case and leave
/// the rest to the handlers before it.
/// </summary>
/// <param name="handlers">The handlers, oldest first, as a container lists them.</param>
public sealed class EffectDispatcher(IEnumerable<IEffectHandler> handlers)
{
	private readonly IEffectHandler[] _handlers = [.. (handlers ?? throw new ArgumentNullException(nameof(handlers))).Reverse()];

	/// <summary>Raised for an effect no handler took.</summary>
	public event Action<OutputEffect>? Unhandled;

	/// <summary>Hands <paramref name="effect"/> to the handlers.</summary>
	/// <returns>True when one handled it.</returns>
	public bool Dispatch(OutputEffect effect)
	{
		foreach (var handler in _handlers)
		{
			if (handler.Handle(effect)) return true;
		}

		Unhandled?.Invoke(effect);
		return false;
	}

	/// <summary>Hands each of <paramref name="effects"/> to the handlers, in order.</summary>
	public void Dispatch(IEnumerable<OutputEffect> effects)
	{
		ArgumentNullException.ThrowIfNull(effects);
		foreach (var effect in effects) Dispatch(effect);
	}
}
