using System.Runtime.CompilerServices;
namespace MarkupString.Ansi;

/// <summary>
/// What a picture has been encoded as, kept with the picture so every connection shown it at the same size
/// shares one encoding. A host typically holds one <see cref="TerminalPicture"/> per picture and shows it to
/// many connections; resizing, compressing and encoding it is far dearer than copying the result.
/// </summary>
/// <remarks>
/// The encodings live exactly as long as the picture does (a <see cref="ConditionalWeakTable{TKey,TValue}"/>),
/// so a host that drops a picture drops them too. Each picture keeps at most <see cref="PerPicture"/>, the
/// least recently used going first, since a picture is seldom shown at more than a size or two.
/// </remarks>
internal static class PictureEncodings
{
	/// <summary>How many encodings one picture keeps.</summary>
	internal const int PerPicture = 64;

	private static readonly ConditionalWeakTable<TerminalPicture, Cache> Caches = new();

	/// <summary>
	/// The encoding of <paramref name="picture"/> that <paramref name="key"/> names, made by <paramref name="make"/>
	/// the first time. Two threads asking at once may both make it; one result is kept.
	/// </summary>
	internal static T GetOrAdd<TKey, T>(TerminalPicture picture, TKey key, Func<TerminalPicture, TKey, T> make)
		where TKey : notnull
		where T : class
	{
		var cache = Caches.GetValue(picture, static _ => new Cache());
		lock (cache)
		{
			if (cache.TryGet(key) is T held) return held;
		}

		var made = make(picture, key);
		lock (cache)
		{
			if (cache.TryGet(key) is T raced) return raced;
			cache.Add(key, made);
		}

		return made;
	}

	/// <summary>A picture's encodings, most recently used last. Guarded by its own lock.</summary>
	private sealed class Cache
	{
		private readonly List<(object Key, object Value)> _entries = [];

		public object? TryGet(object key)
		{
			for (var i = _entries.Count - 1; i >= 0; i--)
			{
				if (!_entries[i].Key.Equals(key)) continue;
				var entry = _entries[i];
				_entries.RemoveAt(i);
				_entries.Add(entry);
				return entry.Value;
			}

			return null;
		}

		public void Add(object key, object value)
		{
			if (_entries.Count >= PerPicture) _entries.RemoveAt(0);
			_entries.Add((key, value));
		}
	}
}
