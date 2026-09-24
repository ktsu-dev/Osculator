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
	/// <returns>The parsed table.</returns>
	/// <exception cref="IersException">The series could not be fetched and nothing is cached.</exception>
	public async Task<EarthOrientationTable> GetTableAsync(CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(cache);

		string? fresh = cache.Read(CacheKey);

		if (fresh is not null)
		{
			return EarthOrientationTable.Parse(fresh);
		}

		try
		{
			string body = await http.GetStringAsync(new Uri(FinalsEndpoint), cancellationToken).ConfigureAwait(false);

			// Parsed before it is written, so a truncated or redirected download never displaces a
			// good cached copy with something that will fail to parse on every later run.
			EarthOrientationTable table = EarthOrientationTable.Parse(body);
			cache.Write(CacheKey, body);

			return table;
		}
		catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or FormatException)
		{
			string? stale = cache.ReadAtAnyAge(CacheKey);

			return stale is not null
				? EarthOrientationTable.Parse(stale)
				: throw new IersException($"Could not fetch {FinalsEndpoint} and nothing is cached.", failure);
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
