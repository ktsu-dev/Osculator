// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System;
using System.Collections.Generic;
using System.Net.Http;
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
/// started without a network shows last week's catalogue rather than an error page. Only a cache
/// with nothing in it at all lets the failure through.
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
	/// <returns>The element sets the service returned, usually exactly one.</returns>
	/// <exception cref="HttpRequestException">The request failed and nothing was cached.</exception>
	public async Task<IReadOnlyList<ElementSet>> GetObjectAsync(int noradCatalogId, CancellationToken cancellationToken = default) =>
		OmmJson.Read(await GetRawObjectAsync(noradCatalogId, cancellationToken).ConfigureAwait(false));

	/// <summary>
	/// Reads the current element sets for a named group, such as <c>active</c> or <c>stations</c>.
	/// </summary>
	/// <param name="group">The group name.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The element sets the service returned.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="group"/> is null.</exception>
	/// <exception cref="HttpRequestException">The request failed and nothing was cached.</exception>
	public async Task<IReadOnlyList<ElementSet>> GetGroupAsync(string group, CancellationToken cancellationToken = default) =>
		OmmJson.Read(await GetRawGroupAsync(group, cancellationToken).ConfigureAwait(false));

	/// <summary>
	/// Reads one object's element set as the service wrote it, without parsing.
	/// </summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The response body.</returns>
	/// <remarks>
	/// The snapshot store archives this rather than a parsed element set, so what it holds is what
	/// the source served rather than this repository's reading of it.
	/// </remarks>
	public Task<string> GetRawObjectAsync(int noradCatalogId, CancellationToken cancellationToken = default) =>
		FetchAsync(FormattableString.Invariant($"CATNR={noradCatalogId}&FORMAT=json"), cancellationToken);

	/// <summary>
	/// Reads a group's element sets as the service wrote them, without parsing.
	/// </summary>
	/// <param name="group">The group name.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The response body.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="group"/> is null.</exception>
	public Task<string> GetRawGroupAsync(string group, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(group);

		return FetchAsync(FormattableString.Invariant($"GROUP={Uri.EscapeDataString(group)}&FORMAT=json"), cancellationToken);
	}

	/// <summary>
	/// Returns a cached response when one is fresh enough, and otherwise asks the service.
	/// </summary>
	/// <param name="query">The query string, which is also the cache key.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The response body.</returns>
	private async Task<string> FetchAsync(string query, CancellationToken cancellationToken)
	{
		string? fresh = Cache.Read(query);

		if (fresh is not null)
		{
			return fresh;
		}

		Uri uri = new(FormattableString.Invariant($"{ElementsEndpoint}?{query}"));

		try
		{
			using HttpResponseMessage response = await http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
			response.EnsureSuccessStatusCode();

			string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			// CelesTrak answers an unknown catalogue number with a body rather than a status code,
			// so a successful request is not on its own a successful lookup.
			if (body.StartsWith("No GP data found", StringComparison.OrdinalIgnoreCase))
			{
				throw new CelesTrakException(FormattableString.Invariant($"CelesTrak has no element set for {query}."));
			}

			Cache.Write(query, body);
			return body;
		}
		catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
		{
			// Last week's elements still propagate. An application that cannot start without a
			// network is worse than one that starts with something old and says so.
			string? stale = Cache.ReadAtAnyAge(query);

			return stale ?? throw new CelesTrakException(
				FormattableString.Invariant($"CelesTrak could not be reached for {query} and nothing is cached."),
				failure);
		}
	}
}
