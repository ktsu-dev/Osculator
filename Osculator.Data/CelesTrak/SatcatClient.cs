// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Fetches CelesTrak's satellite catalogue — object type, launch date, radar cross section and
/// decay date — through the same cache the element-set client uses.
/// </summary>
/// <param name="http">The transport.</param>
/// <param name="cache">Where fetched copies live, and what decides when the service may be asked again.</param>
/// <remarks>
/// <para>
/// The file is the whole catalogue in one request, several megabytes, and CelesTrak asks that each
/// dataset be fetched no more than once every few hours. The window is the caller's
/// <see cref="ResponseCache.MinimumAge"/>, so this client has no way to ask sooner.
/// </para>
/// <para>
/// The body is parsed before it is written, as <see cref="CelesTrakClient"/> and the IERS client
/// do, so a captive portal's page or a truncated download never displaces a good cached copy.
/// When a fetch fails the cached copy is used at any age: a catalogue from last week still filters
/// a list correctly for everything launched before last week.
/// </para>
/// </remarks>
public sealed class SatcatClient(HttpClient http, ResponseCache cache)
{
	/// <summary>The whole catalogue, one row per object ever catalogued.</summary>
	public const string SatcatEndpoint = "https://celestrak.org/pub/satcat.csv";

	private const string CacheKey = "celestrak/satcat.csv";

	/// <summary>
	/// Gets the satellite catalogue, from the cache when it is fresh enough.
	/// </summary>
	/// <param name="cancellationToken">Cancels the fetch.</param>
	/// <returns>One record per catalogued object.</returns>
	/// <exception cref="CelesTrakException">The catalogue could not be fetched and nothing is cached.</exception>
	public async Task<IReadOnlyList<SatcatRecord>> GetCatalogueAsync(CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(cache);

		string? fresh = cache.Read(CacheKey);

		if (fresh is not null)
		{
			return SatcatRecord.ParseCsv(fresh);
		}

		try
		{
			string body = await http.GetStringAsync(new Uri(SatcatEndpoint), cancellationToken).ConfigureAwait(false);

			IReadOnlyList<SatcatRecord> records = SatcatRecord.ParseCsv(body);
			cache.Write(CacheKey, body);

			return records;
		}
		catch (Exception failure) when (
			failure is HttpRequestException or FormatException
			|| (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested))
		{
			// A timeout falls back; the caller's own cancellation does not, because returning stale
			// data from a call the caller abandoned would report it as a success.
			string? stale = cache.ReadAtAnyAge(CacheKey);

			return stale is not null
				? SatcatRecord.ParseCsv(stale)
				: throw new CelesTrakException($"Could not fetch {SatcatEndpoint} and nothing is cached.", failure);
		}
	}
}
