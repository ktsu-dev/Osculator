// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Cddis;

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Data.Sp3;

/// <summary>
/// Fetches ILRS laser-ranging precise orbits from NASA's CDDIS archive, through the same disk cache
/// the other clients use.
/// </summary>
/// <param name="http">The transport. Its <see cref="HttpClient.BaseAddress"/> is ignored.</param>
/// <param name="cache">The cache, which owns the refetch policy.</param>
/// <param name="tokens">Where the Earthdata Login token comes from; <see cref="OsCredentialStore"/> in practice.</param>
/// <remarks>
/// <para>
/// These orbits are the truth the M4 gate measures SGP4 against: good to two or three centimetres,
/// against an SGP4 error of kilometres. CDDIS serves them only to an Earthdata Login account, so
/// every request carries a bearer token from <paramref name="tokens"/>, and the token is read only
/// when a request is actually about to be made — a machine that fetched a week once can read it
/// again with no token and no network.
/// </para>
/// <para>
/// Two kinds of thing are fetched, and they are cached differently because they age differently.
/// A week's <em>directory listing</em> can change — a centre reissues a week as a new version — so it
/// goes through <see cref="ResponseCache.Read"/> and is asked for again only after the cache's
/// minimum age. An <em>orbit file</em>, named with its version, never changes once published, so a
/// cached copy is used at any age and the archive is never asked for the same file twice. A failed
/// listing request falls back to the last listing at any age, so the offline path reaches the
/// cached file.
/// </para>
/// <para>
/// A refused token does not look like a refused request. Without a valid token CDDIS answers with a
/// redirect to the Earthdata login page, and an HTTP client that follows redirects ends on a 200 and
/// an HTML form. The client therefore checks where the response actually came from as well as its
/// status, and parses every body before writing it, so a login page never reaches the cache. The
/// transport is expected to follow redirects as <see cref="HttpClient"/> does by default; it drops
/// the <c>Authorization</c> header when the redirect leaves the host, so the token is never sent to a
/// host it was not meant for.
/// </para>
/// <para>
/// One request is in flight at a time across the process, as for Horizons: CDDIS publishes no
/// quota, and one at a time is the polite reading of a shared archive.
/// </para>
/// </remarks>
public sealed partial class CddisClient(HttpClient http, ResponseCache cache, IEarthdataTokenSource tokens)
{
	/// <summary>The host the archive is served from. A response from anywhere else is a login redirect.</summary>
	public const string ArchiveHost = "cddis.nasa.gov";

	/// <summary>The primary ILRS combination centre.</summary>
	public const string PrimaryCentre = "ilrsa";

	private const string CacheKeyPrefix = "cddis/slr/";

	/// <summary>CDDIS answers a directory URL with this suffix with a plain-text list of its files.</summary>
	private const string ListSuffix = "*?list";

	private static readonly SemaphoreSlim OneAtATime = new(1, 1);

	/// <summary>Gets the cache this client reads and writes through.</summary>
	public ResponseCache Cache { get; } = cache;

	/// <summary>
	/// Gets the precise orbit covering a day, from the cache when it can.
	/// </summary>
	/// <param name="satellite">The satellite as the archive names it, such as <c>lageos1</c>.</param>
	/// <param name="day">A UTC day inside the week wanted.</param>
	/// <param name="centre">The producing centre; <see cref="PrimaryCentre"/> unless the backup is wanted.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The orbit file that was read, and its contents.</returns>
	/// <exception cref="ArgumentException">The satellite or centre is not one the archive has.</exception>
	/// <exception cref="CddisException">
	/// The orbit could not be fetched — no token, a refused token, a week not yet published, or an
	/// unreachable archive — and nothing usable is cached.
	/// </exception>
	/// <exception cref="OperationCanceledException">The caller cancelled.</exception>
	public async Task<IlrsOrbit> GetOrbitAsync(
		string satellite,
		DateOnly day,
		string centre = PrimaryCentre,
		CancellationToken cancellationToken = default)
	{
		IlrsOrbitFile.RequireSatellite(satellite);
		Ensure.NotNull(centre);

		if (!CentrePattern().IsMatch(centre))
		{
			throw new ArgumentException($"'{centre}' is not a centre name; expected something like '{PrimaryCentre}'.", nameof(centre));
		}

		DateOnly weekEnd = IlrsOrbitFile.WeekEnding(day);

		await OneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			string listing = await GetListingAsync(satellite, weekEnd, cancellationToken).ConfigureAwait(false);

			IlrsOrbitFile file = IlrsOrbitFile.Latest(listing, centre, satellite, weekEnd)
				?? throw new CddisException(
					$"The {satellite} directory for the week ending {weekEnd:yyyy-MM-dd} has no readable {centre} orbit. "
					+ "Older weeks are compressed with Unix compress (.Z), which is not supported.");

			return new IlrsOrbit(file, await GetFileAsync(file, cancellationToken).ConfigureAwait(false));
		}
		finally
		{
			OneAtATime.Release();
		}
	}

	private async Task<string> GetListingAsync(string satellite, DateOnly weekEnd, CancellationToken cancellationToken)
	{
		string key = $"{CacheKeyPrefix}{satellite}/{weekEnd.ToString("yyMMdd", CultureInfo.InvariantCulture)}/listing";

		string? fresh = Cache.Read(key);

		if (fresh is not null)
		{
			return fresh;
		}

		Uri address = new(IlrsOrbitFile.DirectoryAddress(satellite, weekEnd), ListSuffix);

		try
		{
			byte[] body = await RequestAsync(address, cancellationToken).ConfigureAwait(false);
			string listing = Encoding.UTF8.GetString(body);

			// Validated before it is written, for the same reason a body is parsed before it is cached
			// anywhere else here: a page that names no orbit file is not a listing, and caching it
			// would hide the week for the whole refetch window.
			if (!IlrsOrbitFile.Parse(listing).Any())
			{
				throw new FormatException($"The listing of {address} names no orbit file.");
			}

			Cache.Write(key, listing);
			return listing;
		}
		catch (Exception failure) when (IsFallbackable(failure, cancellationToken))
		{
			// Offline, or refused: a listing of any age still leads to a file that may be on disk.
			string? stale = Cache.ReadAtAnyAge(key);

			return stale ?? throw Explain(failure, address);
		}
	}

	private async Task<Sp3File> GetFileAsync(IlrsOrbitFile file, CancellationToken cancellationToken)
	{
		string key = CacheKeyPrefix + file.Name;

		// A versioned file is immutable, so a cached copy is good at any age.
		string? cached = Cache.ReadAtAnyAge(key);

		if (cached is not null)
		{
			return Sp3Parser.Parse(cached);
		}

		try
		{
			byte[] body = await RequestAsync(file.Address, cancellationToken).ConfigureAwait(false);
			string text = file.IsGzip ? Gunzip(body) : Encoding.ASCII.GetString(body);

			// Parsed before it is written, so a truncated download or a login page never lands in the
			// cache under a name that says it is a finished, immutable file.
			Sp3File orbit = Sp3Parser.Parse(text);

			Cache.Write(key, text);
			return orbit;
		}
		catch (Exception failure) when (IsFallbackable(failure, cancellationToken))
		{
			throw Explain(failure, file.Address);
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Reliability", "CA2000:Dispose objects before losing scope",
		Justification = "The request is disposed by the using declaration; the analyzer does not see through the object initializer.")]
	private async Task<byte[]> RequestAsync(Uri address, CancellationToken cancellationToken)
	{
		EarthdataToken token = ReadToken();

		using HttpRequestMessage request = new(HttpMethod.Get, address);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

		using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

		string? servedBy = response.RequestMessage?.RequestUri?.Host;

		if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
			|| (servedBy is not null && !string.Equals(servedBy, ArchiveHost, StringComparison.OrdinalIgnoreCase)))
		{
			throw new CddisException(
				"CDDIS did not accept the Earthdata token: the request was refused or sent to the login page. "
				+ "Check the token stored in the OS credential store under '" + OsCredentialStore.ServiceName + "' has not expired.");
		}

		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			throw new CddisException(
				$"CDDIS has no {address.AbsolutePath}. ILRS publishes a week about ten days after it ends, so a recent week may not exist yet.");
		}

		response.EnsureSuccessStatusCode();

		return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
	}

	private EarthdataToken ReadToken()
	{
		EarthdataToken? token;

		try
		{
			token = tokens.GetToken();
		}
		catch (InvalidOperationException unreadable)
		{
			throw new CddisException(unreadable.Message, unreadable);
		}

		return token ?? throw new CddisException(
			"No Earthdata Login token is stored, so CDDIS cannot be asked. Store one in the OS credential store under '"
			+ OsCredentialStore.ServiceName + "'; OsCredentialStore's documentation gives the command for each platform.");
	}

	private static string Gunzip(byte[] body)
	{
		using MemoryStream compressed = new(body);
		using GZipStream gzip = new(compressed, CompressionMode.Decompress);
		using StreamReader reader = new(gzip, Encoding.ASCII);

		return reader.ReadToEnd();
	}

	private static bool IsFallbackable(Exception failure, CancellationToken cancellationToken) =>
		failure is HttpRequestException or FormatException or InvalidDataException or CddisException
		|| (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested);

	private static CddisException Explain(Exception failure, Uri address) =>
		failure as CddisException
			?? new CddisException($"Could not fetch {address} and nothing usable is cached.", failure);

	[GeneratedRegex("^[a-z0-9]+$", RegexOptions.CultureInvariant)]
	private static partial Regex CentrePattern();
}

/// <summary>
/// A precise orbit read from the archive, with the file it came from.
/// </summary>
/// <param name="Source">The file, including the version that was the newest when the week was listed.</param>
/// <param name="Orbit">Its contents.</param>
public sealed record IlrsOrbit(IlrsOrbitFile Source, Sp3File Orbit);
