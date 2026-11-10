// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Iers;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.CelesTrak;

/// <summary>
/// Fetches the IERS leap-second file, through the same cache the Earth orientation series uses.
/// </summary>
/// <param name="http">The transport.</param>
/// <param name="cache">Where fetched copies live.</param>
/// <remarks>
/// <para>
/// The file changes twice a year at most, when the IERS publishes Bulletin C, and a leap second is
/// announced about six months ahead. So the cache window can be long, and when nothing can be
/// fetched or cached at all, <see cref="LeapSeconds.BuiltIn"/> is still the right answer for every
/// instant up to the present — which is why that is the last fallback here rather than an exception.
/// </para>
/// <para>
/// The same discipline as <see cref="IersClient"/>: every fetch goes through
/// <see cref="ResponseCache.FetchAsync"/>, so failures are throttled and concurrent callers share one
/// request; a body is parsed before it is written, and one whose last entry is earlier than the cached
/// copy's or the built-in table's is refused as truncated, so it never displaces a good copy; and the
/// caller's own cancellation is never answered from the cache.
/// </para>
/// </remarks>
public sealed class LeapSecondClient(HttpClient http, ResponseCache cache)
{
	/// <summary>The IERS leap-second file, TAI − UTC since 1972 with its expiry date.</summary>
	public const string Endpoint = "https://hpiers.obspm.fr/iers/bul/bulc/Leap_Second.dat";

	private const string CacheKey = "iers/Leap_Second";

	/// <summary>
	/// Gets the leap-second table: fresh from the cache, else fetched, else stale from the cache,
	/// else the table compiled into the application.
	/// </summary>
	/// <param name="cancellationToken">Cancels the fetch.</param>
	/// <returns>The table.</returns>
	public async Task<LeapSeconds> GetTableAsync(CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(cache);

		try
		{
			Fetched<string> fetched = await cache.FetchAsync(
				CacheKey,
				DownloadAsync,
				failure => failure is HttpRequestException or FormatException or TaskCanceledException,
				cancellationToken).ConfigureAwait(false);

			return LeapSeconds.Parse(fetched.Value);
		}
		catch (ResponseUnavailableException)
		{
			return LeapSeconds.BuiltIn;
		}
	}

	/// <summary>Downloads the file and refuses a copy that ends earlier than one already known.</summary>
	/// <param name="cached">The copy currently cached at any age, or null.</param>
	/// <param name="cancellationToken">Cancels the download.</param>
	/// <returns>The body to cache.</returns>
	/// <exception cref="FormatException">The body is not a usable table, or ends early.</exception>
	private async Task<string> DownloadAsync(string? cached, CancellationToken cancellationToken)
	{
		string body = await http.GetStringAsync(new Uri(Endpoint), cancellationToken).ConfigureAwait(false);

		// Parsing catches a redirect or a login page. It does not catch a download cut at a line
		// boundary: any run of rows in date order parses, and the header keeps its expiry date. The
		// table only ever grows at its end, so one whose last leap second is earlier than the cached
		// copy's, or than the last one compiled in, is a truncated one.
		LeapSeconds table = LeapSeconds.Parse(body);
		int last = table.Entries[^1].ModifiedJulianDay;
		int floor = LeapSeconds.BuiltIn.Entries[^1].ModifiedJulianDay;

		if (TryParse(cached) is LeapSeconds held && held.Entries[^1].ModifiedJulianDay > floor)
		{
			floor = held.Entries[^1].ModifiedJulianDay;
		}

		if (last < floor)
		{
			throw new FormatException(FormattableString.Invariant(
				$"The downloaded leap-second table ends at MJD {last}, earlier than MJD {floor}; treating it as truncated."));
		}

		return body;
	}

	/// <summary>Parses a cached copy, or gives null when there is none or it no longer parses.</summary>
	/// <param name="cached">The cached body.</param>
	/// <returns>The table.</returns>
	private static LeapSeconds? TryParse(string? cached)
	{
		if (cached is null)
		{
			return null;
		}

		try
		{
			return LeapSeconds.Parse(cached);
		}
		catch (FormatException)
		{
			return null;
		}
	}
}
