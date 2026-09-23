// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// A disk cache that will not let a response be refetched before a minimum age has passed.
/// </summary>
/// <remarks>
/// <para>
/// CelesTrak's usage guidelines ask consumers to cache and to refetch infrequently. This enforces
/// that rather than documenting it, because a showcase that hammers a free service run by one
/// person is a bad advertisement for everything else in it. The client cannot opt out: the only
/// path to the network goes through <see cref="Read"/> returning nothing.
/// </para>
/// <para>
/// The policy has to live entirely on this side, because there is nothing on the other side to
/// negotiate with. Measured against the live service, <c>gp.php</c> returns no <c>Last-Modified</c>,
/// no <c>ETag</c> and no <c>Cache-Control</c>, so a conditional request is not available and there
/// is no way to ask cheaply whether anything changed. A timer is the whole of what can be done.
/// </para>
/// <para>
/// Stale entries are kept rather than evicted. An element set from last week still propagates, and
/// an application that cannot start without a network is worse than one that starts with a warning
/// — so <see cref="ReadAtAnyAge"/> exists for the offline path and says in its name that the caller
/// is accepting something old.
/// </para>
/// </remarks>
/// <param name="directory">The directory to hold cached responses in.</param>
/// <param name="minimumAge">How old an entry must be before the network may be asked again.</param>
/// <param name="time">The clock, so the freshness window can be tested without waiting on it.</param>
public sealed class ResponseCache(string directory, TimeSpan minimumAge, TimeProvider time)
{
	/// <summary>The suffix every cached body is written with.</summary>
	private const string BodySuffix = ".json";

	/// <summary>The suffix every cached body's fetch time is written with.</summary>
	private const string StampSuffix = ".fetched";

	/// <summary>Gets the directory cached responses are held in.</summary>
	public string Directory { get; } = directory;

	/// <summary>Gets how old an entry must be before the network may be asked again.</summary>
	public TimeSpan MinimumAge { get; } = minimumAge;

	/// <summary>
	/// Reads a cached response, but only if it is younger than <see cref="MinimumAge"/>.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <returns>The body, or <see langword="null"/> when there is nothing fresh enough.</returns>
	public string? Read(string key)
	{
		DateTimeOffset? fetched = FetchedAt(key);

		if (fetched is null || time.GetUtcNow() - fetched.Value >= MinimumAge)
		{
			return null;
		}

		return ReadAtAnyAge(key);
	}

	/// <summary>
	/// Reads a cached response whatever its age, for the path where the network is unavailable.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <returns>The body, or <see langword="null"/> when the key has never been cached.</returns>
	public string? ReadAtAnyAge(string key)
	{
		string path = PathFor(key, BodySuffix);

		return File.Exists(path) ? File.ReadAllText(path) : null;
	}

	/// <summary>Gets when a key was last fetched, or null when it never was.</summary>
	/// <param name="key">The cache key.</param>
	/// <returns>The fetch time, in UTC.</returns>
	public DateTimeOffset? FetchedAt(string key)
	{
		string path = PathFor(key, StampSuffix);

		if (!File.Exists(path))
		{
			return null;
		}

		return DateTimeOffset.TryParse(
			File.ReadAllText(path),
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out DateTimeOffset parsed)
			? parsed
			: null;
	}

	/// <summary>Stores a response and stamps it with the current time.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="body">The response body.</param>
	public void Write(string key, string body)
	{
		System.IO.Directory.CreateDirectory(Directory);
		File.WriteAllText(PathFor(key, BodySuffix), body);
		File.WriteAllText(PathFor(key, StampSuffix), time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
	}

	/// <summary>
	/// Turns a cache key into a file path.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <param name="suffix">The file suffix.</param>
	/// <returns>The path.</returns>
	/// <remarks>
	/// The key goes through a hash rather than into the name, because a key is a query string and a
	/// query string contains characters a file name may not. The readable part is kept as a prefix
	/// so the cache directory can still be understood by looking at it.
	/// </remarks>
	private string PathFor(string key, string suffix)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
		string readable = new([.. key.Select(c => char.IsLetterOrDigit(c) ? c : '-')]);

		if (readable.Length > 48)
		{
			readable = readable[..48];
		}

		return Path.Join(Directory, $"{readable}-{Convert.ToHexString(hash)[..16]}{suffix}");
	}
}
