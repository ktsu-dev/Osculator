// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Iers;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Data.CelesTrak;

/// <summary>
/// Fetches the IERS Earth orientation series, through the same cache the CelesTrak client uses.
/// </summary>
/// <param name="http">The transport.</param>
/// <param name="cache">Where fetched copies live.</param>
/// <remarks>
/// <para>
/// The series is open — no account, unlike the laser-ranging archives — and about 4 MB, one row
/// per day since 1973. It changes once a day at most: the IERS finalises values about a week in
/// arrears and re-forecasts the rest. Refetching it more often than daily gains nothing and costs
/// someone else's bandwidth, which is why the cache window belongs in the caller's
/// <see cref="ResponseCache"/> rather than here.
/// </para>
/// <para>
/// Sharing <see cref="ResponseCache"/> with CelesTrak is deliberate and safe: it keys on the
/// caller's string, and this one is not a catalogue number.
/// </para>
/// </remarks>
public sealed class IersClient(HttpClient http, ResponseCache cache)
{
	/// <summary>The combined series of final and predicted values, 1973 to about a year ahead.</summary>
	public const string FinalsEndpoint = "https://datacenter.iers.org/data/csv/finals2000A.all.csv";

	private const string CacheKey = "iers/finals2000A.all";

	/// <summary>
	/// Gets the Earth orientation table, from the cache when it is fresh enough.
	/// </summary>
	/// <param name="cancellationToken">Cancels the fetch.</param>
	/// <returns>The parsed table, and whether it is a stale copy served because the fetch failed.</returns>
	/// <exception cref="IersException">The series could not be fetched and nothing is cached.</exception>
	/// <exception cref="OperationCanceledException">The caller cancelled.</exception>
	public async Task<Fetched<EarthOrientationTable>> GetTableAsync(CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(cache);

		try
		{
			Fetched<string> fetched = await cache.FetchAsync(
				CacheKey,
				DownloadAsync,
				failure => failure is HttpRequestException or FormatException or TaskCanceledException,
				cancellationToken).ConfigureAwait(false);

			return fetched.Map(EarthOrientationTable.Parse);
		}
		catch (ResponseUnavailableException unavailable)
		{
			throw new IersException($"Could not fetch {FinalsEndpoint} and nothing is cached.", unavailable.InnerException ?? unavailable);
		}
	}

	/// <summary>Downloads the series and refuses a copy worse than the one already held.</summary>
	/// <param name="cached">The copy currently cached at any age, or null.</param>
	/// <param name="cancellationToken">Cancels the download.</param>
	/// <returns>The body to cache.</returns>
	/// <exception cref="FormatException">The body is not a usable series, or ends early.</exception>
	private async Task<string> DownloadAsync(string? cached, CancellationToken cancellationToken)
	{
		string body = await http.GetStringAsync(new Uri(FinalsEndpoint), cancellationToken).ConfigureAwait(false);

		// Parsing catches a redirect or a login page, which has no header. It does not catch a
		// truncated download: any prefix of the file that keeps the header and one whole row is a
		// valid table, just one that ends wherever the connection dropped. Written over the good
		// copy it would refuse every later instant, offline too. The series only ever grows at its
		// end, so a new copy that ends earlier than the one held is a truncated one.
		EarthOrientationTable table = EarthOrientationTable.Parse(body);

		if (TryParse(cached) is EarthOrientationTable held
			&& table.LastModifiedJulianDate < held.LastModifiedJulianDate)
		{
			throw new FormatException(FormattableString.Invariant(
				$"The downloaded series ends at MJD {table.LastModifiedJulianDate}, earlier than the cached copy's {held.LastModifiedJulianDate}; treating it as truncated."));
		}

		return body;
	}

	/// <summary>Parses a cached copy, or gives null when there is none or it no longer parses.</summary>
	/// <param name="cached">The cached body.</param>
	/// <returns>The table.</returns>
	/// <remarks>A cached copy that does not parse is no reason to refuse one that does.</remarks>
	private static EarthOrientationTable? TryParse(string? cached)
	{
		if (cached is null)
		{
			return null;
		}

		try
		{
			return EarthOrientationTable.Parse(cached);
		}
		catch (FormatException)
		{
			return null;
		}
	}
}

/// <summary>
/// The IERS series could not be obtained.
/// </summary>
public sealed class IersException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="IersException"/> class.</summary>
	public IersException()
	{
	}

	/// <summary>Initializes a new instance of the <see cref="IersException"/> class.</summary>
	/// <param name="message">What went wrong.</param>
	public IersException(string message)
		: base(message)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="IersException"/> class.</summary>
	/// <param name="message">What went wrong.</param>
	/// <param name="innerException">The underlying failure.</param>
	public IersException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
