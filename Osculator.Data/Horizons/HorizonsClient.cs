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
/// <see cref="ResponseCache.FetchAsync"/>, so a table is not asked for again inside the cache's
/// minimum age, overlapping callers for one table share one request, and a request that fails falls
/// back to a cached copy at any age, flagged <see cref="Fetched{T}.IsStale"/>, and is not sent again
/// until the cache's back-off has passed. A body is parsed before it is written, so a bad download
/// never displaces a good copy. A refusal is an answer: never cached, never throttled, never
/// answered from an older copy. The cache key is the whole query, so a different span or step is a
/// different entry.
/// </para>
/// <para>
/// One request is in flight at a time, across every instance in the process. JPL runs Horizons as a
/// shared public service with no published request quota, and one at a time is the conservative
/// reading of being a polite client of it. The limit belongs to the service rather than to a client
/// object, which is why the gate is static. A caller asking
/// for the same table joins the request already made rather than queueing a second one.
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
	/// <returns>The table, with when it was fetched and whether it is a stale copy.</returns>
	/// <exception cref="ArgumentException">The request cannot be asked of Horizons.</exception>
	/// <exception cref="HorizonsException">
	/// Horizons answered with something other than a table, or could not be reached and nothing is cached.
	/// </exception>
	/// <exception cref="OperationCanceledException">The caller cancelled.</exception>
	public async Task<Fetched<HorizonsEphemeris>> GetVectorsAsync(HorizonsRequest request, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(request);

		string query = request.ToQuery();

		try
		{
			Fetched<string> body = await Cache.FetchAsync(
				CacheKeyPrefix + query,
				(_, token) => DownloadAsync(query, token),
				IsUnavailable,
				cancellationToken).ConfigureAwait(false);

			return body.Map(HorizonsVectorTable.Parse);
		}
		catch (ResponseUnavailableException unavailable)
		{
			throw new HorizonsException(
				$"Horizons could not be reached for {query} and nothing is cached.",
				unavailable.InnerException ?? unavailable);
		}
	}

	/// <summary>
	/// Whether a failure means Horizons could not answer, so the stale copy may stand in and the
	/// query is not asked again until the cache's back-off has passed.
	/// </summary>
	/// <param name="failure">The failure.</param>
	/// <returns>True for transport failures, error statuses, timeouts and unreadable bodies.</returns>
	/// <remarks>
	/// A <see cref="HorizonsException"/> is not among them: Horizons was reached and refused the
	/// request or answered without a table, and last week's copy would hide what it said.
	/// </remarks>
	private static bool IsUnavailable(Exception failure) =>
		failure is HttpRequestException or JsonException or FormatException or TaskCanceledException;

	/// <summary>Asks Horizons once, and classifies what it says.</summary>
	/// <param name="query">The query string.</param>
	/// <param name="cancellationToken">Ends the request on a transport timeout.</param>
	/// <returns>A body that parses as a vector table.</returns>
	private async Task<string> DownloadAsync(string query, CancellationToken cancellationToken)
	{
		Uri uri = new($"{Endpoint}?{query}");

		await OneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);

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
			_ = HorizonsVectorTable.Parse(body);

			return body;
		}
		finally
		{
			OneAtATime.Release();
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
