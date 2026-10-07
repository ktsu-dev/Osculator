// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Horizons;

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Data.CelesTrak;

/// <summary>
/// Reads state vectors from JPL Horizons, through the same cache the other clients use.
/// </summary>
/// <param name="http">The transport. Its <see cref="HttpClient.BaseAddress"/> is ignored.</param>
/// <param name="cache">The cache, which owns the refetch policy.</param>
/// <remarks>
/// <para>
/// Two uses, per the spec: the Sun and the Moon for a numerical force model's third-body terms, and
/// published vectors for a handful of spacecraft as a truth source. Horizons needs no account.
/// </para>
/// <para>
/// The rules are the CelesTrak client's. Every path to the network goes through
/// <see cref="ResponseCache"/>, so a table is not asked for again inside the cache's minimum age;
/// a request that fails falls back to a cached copy at any age, so the application works offline;
/// and a body is parsed before it is written, so a bad download never displaces a good copy. The
/// cache key is the whole query, so a different span or step is a different entry.
/// </para>
/// <para>
/// One request is in flight at a time, across every instance in the process. JPL runs Horizons as a
/// shared public service with no published request quota, and one at a time is the conservative
/// reading of being a polite client of it. The limit belongs to the service rather than to a client
/// object, which is why the gate is static. A caller queued
/// behind another asking for the same table finds it in the cache when its turn comes, rather than
/// asking a second time.
/// </para>
/// <para>
/// A table for a fixed span of the past changes only when JPL publishes a new planetary ephemeris,
/// so a long minimum age loses nothing. The window is the caller's, as it is for IERS.
/// </para>
/// </remarks>
public sealed class HorizonsClient(HttpClient http, ResponseCache cache)
{
	/// <summary>The Horizons API endpoint.</summary>
	public const string Endpoint = "https://ssd.jpl.nasa.gov/api/horizons.api";

	private const string CacheKeyPrefix = "horizons?";

	private static readonly SemaphoreSlim OneAtATime = new(1, 1);

	/// <summary>Gets the cache this client reads and writes through.</summary>
	public ResponseCache Cache { get; } = cache;

	/// <summary>
	/// Reads a table of state vectors, from the cache when it is fresh enough.
	/// </summary>
	/// <param name="request">What to ask for.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The table.</returns>
	/// <exception cref="ArgumentException">The request cannot be asked of Horizons.</exception>
	/// <exception cref="HorizonsException">
	/// Horizons answered with something other than a table, or could not be reached and nothing is cached.
	/// </exception>
	/// <exception cref="OperationCanceledException">The caller cancelled.</exception>
	public async Task<HorizonsEphemeris> GetVectorsAsync(HorizonsRequest request, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(request);

		string query = request.ToQuery();
		string key = CacheKeyPrefix + query;

		string? fresh = Cache.Read(key);

		if (fresh is not null)
		{
			return HorizonsVectorTable.Parse(fresh);
		}

		await OneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			// Whoever held the gate may just have fetched this very table.
			fresh = Cache.Read(key);

			if (fresh is not null)
			{
				return HorizonsVectorTable.Parse(fresh);
			}

			return await FetchAsync(query, key, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			OneAtATime.Release();
		}
	}

	private async Task<HorizonsEphemeris> FetchAsync(string query, string key, CancellationToken cancellationToken)
	{
		Uri uri = new($"{Endpoint}?{query}");

		try
		{
			using HttpResponseMessage response = await http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
			string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				// Horizons explains a refused request in a JSON 'error' member, often under a 400.
				// That is an answer and is raised as one; anything else is an outage.
				ThrowIfRefusal(body);
				response.EnsureSuccessStatusCode();
			}

			// Parsed before it is written, so a proxy page, a truncated table or a refusal never
			// displaces a good cached copy.
			HorizonsEphemeris ephemeris = HorizonsVectorTable.Parse(body);

			Cache.Write(key, body);
			return ephemeris;
		}
		catch (Exception failure) when (
			failure is HttpRequestException or JsonException or FormatException
			|| (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested))
		{
			// A timeout falls back; the caller's own cancellation does not, because returning stale
			// data from a call the caller abandoned would report it as a success.
			string? stale = Cache.ReadAtAnyAge(key);

			return stale is not null
				? HorizonsVectorTable.Parse(stale)
				: throw new HorizonsException($"Horizons could not be reached for {query} and nothing is cached.", failure);
		}
	}

	private static void ThrowIfRefusal(string body)
	{
		try
		{
			_ = HorizonsVectorTable.Parse(body);
		}
		catch (Exception notARefusal) when (notARefusal is JsonException or FormatException)
		{
			// Not an answer from Horizons, so the status code is what to report.
		}
	}
}
