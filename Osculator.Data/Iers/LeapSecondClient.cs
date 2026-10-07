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
/// The same discipline as <see cref="IersClient"/>: a body is parsed before it is written, so a
/// truncated download never displaces a good cached copy, and the caller's own cancellation is never
/// answered from the cache.
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

		string? fresh = cache.Read(CacheKey);

		if (fresh is not null)
		{
			return LeapSeconds.Parse(fresh);
		}

		try
		{
			string body = await http.GetStringAsync(new Uri(Endpoint), cancellationToken).ConfigureAwait(false);
			LeapSeconds table = LeapSeconds.Parse(body);
			cache.Write(CacheKey, body);

			return table;
		}
		catch (Exception failure) when (
			failure is HttpRequestException or FormatException
			|| (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested))
		{
			string? stale = cache.ReadAtAnyAge(CacheKey);

			return stale is not null ? LeapSeconds.Parse(stale) : LeapSeconds.BuiltIn;
		}
	}
}
