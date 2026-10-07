// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.SpaceTrack;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Data.CelesTrak;

/// <summary>
/// Reads the full catalogue and element-set histories from Space-Track, inside its hard limits.
/// </summary>
/// <remarks>
/// <para>
/// Space-Track carries what CelesTrak does not: the whole 18th Space Defense Squadron catalogue and,
/// above all, past element sets. The divergence loop — propagate an old set to a later set's epoch
/// and compare — needs a history, and <see cref="SeedHistoryAsync"/> is how one reaches
/// <see cref="SnapshotStore"/> without waiting months for the archive to accumulate it.
/// </para>
/// <para>
/// Three things stand between a caller and the network, in order. The <see cref="ResponseCache"/>
/// answers whatever it holds that is younger than its minimum age, which must be at least an hour
/// because Space-Track asks that <c>gp</c> be queried no more often than that. The
/// <see cref="SpaceTrackRateLimiter"/> refuses anything that would reach 30 requests in a minute or
/// 300 in an hour, counting the login. And the credential store is read only when a login is
/// actually needed, so an application running from its cache never touches it.
/// </para>
/// <para>
/// The session is a cookie, kept here rather than in the transport's cookie container, so the
/// client behaves the same whatever handler it is given; <see cref="CreateHttpClient"/> builds one
/// with its own cookie handling switched off. A query answered 401 means the session expired: the
/// client logs in again once and retries, and both requests go through the limiter.
/// </para>
/// <para>
/// A request that fails — refused by the limiter, rejected at login, unreachable, or answered with
/// something that is not an element set — falls back to whatever the cache holds at any age, as
/// <see cref="CelesTrakClient"/> does. Only an empty cache lets the failure through.
/// </para>
/// </remarks>
public sealed class SpaceTrackClient : IDisposable
{
	/// <summary>The shortest cache window Space-Track's guidelines allow for <c>gp</c>.</summary>
	public static readonly TimeSpan MinimumCacheAge = TimeSpan.FromHours(1);

	private const string BaseAddress = "https://www.space-track.org";

	private const string LoginPath = "/ajaxauth/login";

	private const string QueryPrefix = "/basicspacedata/query/class/";

	private readonly HttpClient http;

	private readonly ISpaceTrackCredentialSource credentials;

	private readonly SemaphoreSlim network = new(1, 1);

	private string? sessionCookie;

	/// <summary>Creates a client.</summary>
	/// <param name="http">The transport. Its <see cref="HttpClient.BaseAddress"/> is ignored.</param>
	/// <param name="cache">The cache. Its minimum age must be at least <see cref="MinimumCacheAge"/>.</param>
	/// <param name="limiter">The rate limiter. Share one across every client in the process.</param>
	/// <param name="credentials">Where the account comes from.</param>
	/// <exception cref="ArgumentNullException">An argument is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The cache would let <c>gp</c> be refetched within the hour.</exception>
	public SpaceTrackClient(HttpClient http, ResponseCache cache, SpaceTrackRateLimiter limiter, ISpaceTrackCredentialSource credentials)
	{
		Ensure.NotNull(http);
		Ensure.NotNull(cache);
		Ensure.NotNull(limiter);
		Ensure.NotNull(credentials);

		if (cache.MinimumAge < MinimumCacheAge)
		{
			throw new ArgumentOutOfRangeException(
				nameof(cache),
				cache.MinimumAge,
				"Space-Track asks that gp be queried no more than once an hour, so the cache must hold responses at least that long.");
		}

		this.http = http;
		this.credentials = credentials;
		Cache = cache;
		Limiter = limiter;
	}

	/// <summary>Gets the cache this client reads and writes through.</summary>
	public ResponseCache Cache { get; }

	/// <summary>Gets the rate limiter every request goes through.</summary>
	public SpaceTrackRateLimiter Limiter { get; }

	/// <summary>Releases the gate that serialises requests. The transport is the caller's.</summary>
	public void Dispose() => network.Dispose();

	/// <summary>
	/// Creates a transport suited to this client: no cookie container of its own, since the session
	/// cookie is kept by the client.
	/// </summary>
	/// <returns>The transport, which the caller owns.</returns>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Reliability", "CA2000:Dispose objects before losing scope",
		Justification = "The handler is owned and disposed by the HttpClient it is handed to.")]
	public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler { UseCookies = false }, disposeHandler: true);

	/// <summary>Reads the current element set for one catalogued object.</summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The element sets the service returned: one, or none for an unknown number.</returns>
	/// <exception cref="SpaceTrackException">The request failed and nothing was cached.</exception>
	public async Task<IReadOnlyList<ElementSet>> GetObjectAsync(int noradCatalogId, CancellationToken cancellationToken = default) =>
		OmmJson.Read(await GetRawObjectAsync(noradCatalogId, cancellationToken).ConfigureAwait(false));

	/// <summary>
	/// Reads the current element set of every object on orbit that has been updated in the last 30
	/// days, which is the query Space-Track recommends for the full catalogue.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The element sets, ascending by catalogue number.</returns>
	/// <exception cref="SpaceTrackException">The request failed and nothing was cached.</exception>
	public async Task<IReadOnlyList<ElementSet>> GetCatalogueAsync(CancellationToken cancellationToken = default) =>
		OmmJson.Read(await GetRawCatalogueAsync(cancellationToken).ConfigureAwait(false));

	/// <summary>Reads every past element set for one object, oldest epoch first.</summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <param name="from">The first epoch day to include, or null for no lower bound.</param>
	/// <param name="to">The day after the last epoch day to include, or null for no upper bound.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The history.</returns>
	/// <exception cref="ArgumentException"><paramref name="to"/> is not after <paramref name="from"/>.</exception>
	/// <exception cref="SpaceTrackException">The request failed and nothing was cached.</exception>
	public async Task<IReadOnlyList<ElementSet>> GetHistoryAsync(
		int noradCatalogId, DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default) =>
		OmmJson.Read(await GetRawHistoryAsync(noradCatalogId, from, to, cancellationToken).ConfigureAwait(false));

	/// <summary>
	/// Reads one object's history and archives every set the snapshot store does not already hold.
	/// </summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <param name="store">The archive.</param>
	/// <param name="from">The first epoch day to include, or null for no lower bound.</param>
	/// <param name="to">The day after the last epoch day to include, or null for no upper bound.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>How many sets were new to the archive.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
	/// <exception cref="SpaceTrackException">The request failed and nothing was cached.</exception>
	/// <remarks>
	/// The store is given the source's own records, so what it archives is what Space-Track served,
	/// beside whatever CelesTrak has served for the same object.
	/// </remarks>
	public async Task<int> SeedHistoryAsync(
		int noradCatalogId, SnapshotStore store, DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(store);

		return store.Add(await GetRawHistoryAsync(noradCatalogId, from, to, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>Reads one object's current element set as the service wrote it.</summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The response body.</returns>
	public Task<string> GetRawObjectAsync(int noradCatalogId, CancellationToken cancellationToken = default) =>
		FetchAsync(ObjectQuery(noradCatalogId), cancellationToken);

	/// <summary>Reads the full catalogue as the service wrote it.</summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The response body.</returns>
	public Task<string> GetRawCatalogueAsync(CancellationToken cancellationToken = default) =>
		FetchAsync(CatalogueQuery(), cancellationToken);

	/// <summary>Reads one object's history as the service wrote it.</summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <param name="from">The first epoch day to include, or null for no lower bound.</param>
	/// <param name="to">The day after the last epoch day to include, or null for no upper bound.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The response body.</returns>
	/// <exception cref="ArgumentException"><paramref name="to"/> is not after <paramref name="from"/>.</exception>
	public Task<string> GetRawHistoryAsync(
		int noradCatalogId, DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default) =>
		FetchAsync(HistoryQuery(noradCatalogId, from, to), cancellationToken);

	/// <summary>The path for one object's current element set.</summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <returns>The path, already escaped.</returns>
	internal static string ObjectQuery(int noradCatalogId) =>
		FormattableString.Invariant($"{QueryPrefix}gp/NORAD_CAT_ID/{noradCatalogId}/format/json");

	/// <summary>The path for the full catalogue, as Space-Track's own guidance writes it.</summary>
	/// <returns>The path, already escaped.</returns>
	internal static string CatalogueQuery() =>
		$"{QueryPrefix}gp/decay_date/null-val/epoch/%3Enow-30/orderby/norad_cat_id/format/json";

	/// <summary>The path for one object's element-set history.</summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <param name="from">The first epoch day to include, or null.</param>
	/// <param name="to">The day after the last, or null.</param>
	/// <returns>The path, already escaped.</returns>
	/// <exception cref="ArgumentException"><paramref name="to"/> is not after <paramref name="from"/>.</exception>
	internal static string HistoryQuery(int noradCatalogId, DateOnly? from, DateOnly? to)
	{
		if (from is not null && to is not null && to <= from)
		{
			throw new ArgumentException("The end of the range must be after its start.", nameof(to));
		}

		// Space-Track's range operator is "--", and an open end is written with its "<" and ">"
		// operators instead. Days, not instants, because that is the resolution the cache key wants:
		// a range ending "now" would be a new key, and a new request, every time it was asked.
		string range = (from, to) switch
		{
			(null, null) => string.Empty,
			({ } start, null) => $"/EPOCH/%3E{Day(start)}",
			(null, { } end) => $"/EPOCH/%3C{Day(end)}",
			({ } start, { } end) => $"/EPOCH/{Day(start)}--{Day(end)}",
		};

		return FormattableString.Invariant(
			$"{QueryPrefix}gp_history/NORAD_CAT_ID/{noradCatalogId}{range}/orderby/EPOCH%20asc/format/json");
	}

	/// <summary>Reads the session cookie out of a login response's <c>Set-Cookie</c> headers.</summary>
	/// <param name="response">The login response.</param>
	/// <returns>The <c>Cookie</c> header to send, or null when the response set none.</returns>
	internal static string? CookieFrom(HttpResponseMessage response)
	{
		if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
		{
			return null;
		}

		string[] pairs = [.. cookies
			.Select(cookie => cookie.Split(';', 2)[0].Trim())
			.Where(pair => pair.Contains('=', StringComparison.Ordinal))];

		return pairs.Length == 0 ? null : string.Join("; ", pairs);
	}

	private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	/// <summary>
	/// Returns a cached response when one is fresh enough, and otherwise asks the service.
	/// </summary>
	/// <param name="query">The query path, which with a prefix is also the cache key.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The response body.</returns>
	private async Task<string> FetchAsync(string query, CancellationToken cancellationToken)
	{
		// Prefixed so a cache directory shared with CelesTrak can never answer one source's query
		// with the other's response.
		string key = "spacetrack:" + query;

		string? fresh = Cache.Read(key);

		if (fresh is not null)
		{
			return fresh;
		}

		// One request at a time: the limit is per account, and two callers racing to log in would
		// spend two requests on one session.
		await network.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			// Whoever held the gate may have just fetched this very key.
			fresh = Cache.Read(key);

			if (fresh is not null)
			{
				return fresh;
			}

			string body = await QueryAsync(query, cancellationToken).ConfigureAwait(false);

			// Parsed before it is cached. Space-Track reports a broken limit or a bad query as a JSON
			// object with an "error" member, which is not an element set and must never displace one.
			_ = OmmJson.Read(body);

			Cache.Write(key, body);
			return body;
		}
		catch (Exception failure) when (
			failure is HttpRequestException or JsonException or SpaceTrackException
			|| (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested))
		{
			string? stale = Cache.ReadAtAnyAge(key);

			if (stale is not null)
			{
				return stale;
			}

			if (failure is SpaceTrackException)
			{
				throw;
			}

			throw new SpaceTrackException(
				FormattableString.Invariant($"Space-Track could not answer {query} and nothing is cached."),
				failure);
		}
		finally
		{
			_ = network.Release();
		}
	}

	/// <summary>Sends one query, logging in first if there is no session and again if it expired.</summary>
	/// <param name="query">The query path.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The response body.</returns>
	private async Task<string> QueryAsync(string query, CancellationToken cancellationToken)
	{
		bool loggedInForThisQuery = false;

		if (sessionCookie is null)
		{
			await LogInAsync(cancellationToken).ConfigureAwait(false);
			loggedInForThisQuery = true;
		}

		while (true)
		{
			Acquire();

			using HttpRequestMessage request = new(HttpMethod.Get, new Uri(BaseAddress + query));
			request.Headers.Add("Cookie", sessionCookie);

			using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

			if (response.StatusCode == HttpStatusCode.Unauthorized && !loggedInForThisQuery)
			{
				// The session expired. Once, not in a loop: a second 401 straight after a successful
				// login is not an expiry, and retrying it would spend the limit on nothing.
				sessionCookie = null;
				await LogInAsync(cancellationToken).ConfigureAwait(false);
				loggedInForThisQuery = true;
				continue;
			}

			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				sessionCookie = null;
			}

			_ = response.EnsureSuccessStatusCode();

			return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>Logs in and keeps the session cookie.</summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	private async Task LogInAsync(CancellationToken cancellationToken)
	{
		SpaceTrackCredentials account = await credentials.GetAsync(cancellationToken).ConfigureAwait(false)
			?? throw new SpaceTrackException(
				"No Space-Track account is stored. Store the password in the operating system's credential store; see OsCredentialStore for the command on each platform.");

		Acquire();

		using FormUrlEncodedContent form = new([
			new("identity", account.Identity),
			new("password", account.Password),
		]);

		using HttpResponseMessage response = await http
			.PostAsync(new Uri(BaseAddress + LoginPath), form, cancellationToken)
			.ConfigureAwait(false);

		_ = response.EnsureSuccessStatusCode();

		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		// Space-Track answers bad credentials with a 200 and {"Login":"Failed"}. The message names the
		// identity and never the password.
		if (body.Contains("Failed", StringComparison.OrdinalIgnoreCase))
		{
			throw new SpaceTrackException(
				FormattableString.Invariant($"Space-Track refused the login for {account.Identity}."));
		}

		sessionCookie = CookieFrom(response)
			?? throw new SpaceTrackException("Space-Track accepted the login but set no session cookie.");
	}

	/// <summary>Takes one slot from the limiter, or refuses the request without sending it.</summary>
	private void Acquire()
	{
		if (!Limiter.TryAcquire(out TimeSpan retryAfter))
		{
			throw new SpaceTrackRateLimitException(
				FormattableString.Invariant(
					$"Space-Track's rate limit would be exceeded; the request was not sent. Room again in {retryAfter.TotalSeconds:F0} s."),
				retryAfter);
		}
	}
}
