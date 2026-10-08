// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Elements;

/// <summary>
/// Reads element sets from CelesTrak, through a cache that will not let it ask too often.
/// </summary>
/// <remarks>
/// <para>
/// Every path to the network goes through <see cref="ResponseCache"/>, which refuses to let a
/// response be refetched before its minimum age. That is not advisory and there is no parameter to
/// turn it off: CelesTrak is free, is run by one person, and asks consumers to cache.
/// </para>
/// <para>
/// A request that fails falls back to whatever the cache holds at any age, so an application
/// started without a network shows last week's catalogue rather than an error page, and the result
/// says it is stale so the application can say so too. Only a cache with nothing in it at all lets
/// the failure through.
/// </para>
/// </remarks>
/// <param name="http">The transport. Its <see cref="HttpClient.BaseAddress"/> is ignored.</param>
/// <param name="cache">The cache, which owns the refetch policy.</param>
public sealed class CelesTrakClient(HttpClient http, ResponseCache cache)
{
	/// <summary>The general perturbations endpoint, which serves current element sets.</summary>
	private const string ElementsEndpoint = "https://celestrak.org/NORAD/elements/gp.php";

	/// <summary>Gets the cache this client reads and writes through.</summary>
	public ResponseCache Cache { get; } = cache;

	/// <summary>
	/// Reads the current element set for one catalogued object.
	/// </summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>
	/// The element sets the service returned, usually exactly one, and whether they are a stale
	/// copy served because the service could not be reached.
	/// </returns>
	/// <exception cref="CelesTrakException">
	/// The service has no element set for the object, or the request failed and nothing was cached.
	/// </exception>
	/// <exception cref="OperationCanceledException">The caller cancelled.</exception>
	public async Task<Fetched<IReadOnlyList<ElementSet>>> GetObjectAsync(int noradCatalogId, CancellationToken cancellationToken = default) =>
		(await GetRawObjectAsync(noradCatalogId, cancellationToken).ConfigureAwait(false)).Map(OmmJson.Read);

	/// <summary>
	/// Reads the current element sets for a named group, such as <c>active</c> or <c>stations</c>.
	/// </summary>
	/// <param name="group">The group name.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The element sets the service returned, and whether they are a stale copy.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="group"/> is null.</exception>
	/// <exception cref="CelesTrakException">
	/// The service does not know the group, or the request failed and nothing was cached.
	/// </exception>
	/// <exception cref="OperationCanceledException">The caller cancelled.</exception>
	public async Task<Fetched<IReadOnlyList<ElementSet>>> GetGroupAsync(string group, CancellationToken cancellationToken = default) =>
		(await GetRawGroupAsync(group, cancellationToken).ConfigureAwait(false)).Map(OmmJson.Read);

	/// <summary>
	/// Reads one object's element set as the service wrote it, without parsing.
	/// </summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The response body, and whether it is a stale copy.</returns>
	/// <remarks>
	/// The snapshot store archives this rather than a parsed element set, so what it holds is what
	/// the source served rather than this repository's reading of it.
	/// </remarks>
	public Task<Fetched<string>> GetRawObjectAsync(int noradCatalogId, CancellationToken cancellationToken = default) =>
		FetchAsync(FormattableString.Invariant($"CATNR={noradCatalogId}&FORMAT=json"), cancellationToken);

	/// <summary>
	/// Reads a group's element sets as the service wrote them, without parsing.
	/// </summary>
	/// <param name="group">The group name.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The response body, and whether it is a stale copy.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="group"/> is null.</exception>
	public Task<Fetched<string>> GetRawGroupAsync(string group, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(group);

		return FetchAsync(FormattableString.Invariant($"GROUP={Uri.EscapeDataString(group)}&FORMAT=json"), cancellationToken);
	}

	/// <summary>
	/// Whether a failure means the service could not answer, so the stale copy may stand in.
	/// </summary>
	/// <param name="failure">The failure.</param>
	/// <returns>True for transport failures, error statuses, timeouts and unparseable bodies.</returns>
	/// <remarks>
	/// A not-found or an invalid query is not among them: the service was reached and gave an
	/// answer, and serving last week's copy of an object it now says does not exist would hide it.
	/// The caller's own cancellation never reaches here, because the shared request does not run on
	/// the caller's token, so a cancelled task can only be a transport timeout.
	/// </remarks>
	private static bool IsUnavailable(Exception failure) =>
		failure is HttpRequestException or JsonException or TaskCanceledException;

	/// <summary>
	/// Returns a cached response when one is fresh enough, and otherwise asks the service.
	/// </summary>
	/// <param name="query">The query string, which is also the cache key.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The response body.</returns>
	private async Task<Fetched<string>> FetchAsync(string query, CancellationToken cancellationToken)
	{
		try
		{
			return await Cache.FetchAsync(
				query,
				(_, token) => DownloadAsync(query, token),
				IsUnavailable,
				cancellationToken).ConfigureAwait(false);
		}
		catch (ResponseUnavailableException unavailable)
		{
			throw new CelesTrakException(
				FormattableString.Invariant($"CelesTrak could not be reached for {query} and nothing is cached."),
				unavailable.InnerException ?? unavailable);
		}
	}

	/// <summary>Asks the service once, and classifies what it says.</summary>
	/// <param name="query">The query string.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A body that parses as element sets.</returns>
	private async Task<string> DownloadAsync(string query, CancellationToken cancellationToken)
	{
		Uri uri = new(FormattableString.Invariant($"{ElementsEndpoint}?{query}"));

		using HttpResponseMessage response = await http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		// Classified before the status is checked. CelesTrak answers an unknown or decayed catalogue
		// number with a 404 and this sentence (it used to be a 200 and the same sentence), and an
		// unknown group with a 200 and "Invalid query:". Either way the service was reached and
		// answered, which is not the same thing as being unreachable.
		if (response.StatusCode == HttpStatusCode.NotFound
			|| body.StartsWith("No GP data found", StringComparison.OrdinalIgnoreCase))
		{
			throw new CelesTrakException(FormattableString.Invariant($"CelesTrak has no element set for {query}."));
		}

		if (body.StartsWith("Invalid query", StringComparison.OrdinalIgnoreCase))
		{
			throw new CelesTrakException(FormattableString.Invariant($"CelesTrak rejected the query {query}: {body.Trim()}"));
		}

		response.EnsureSuccessStatusCode();

		// Parsed before it is written, as IersClient does, so a proxy error page, a rate-limit
		// page or a truncated body served as a 200 never displaces a good cached copy.
		_ = OmmJson.Read(body);

		return body;
	}
}
