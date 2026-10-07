// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Cddis;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// The name of one ILRS precise orbit file in the CDDIS archive, and the rules for finding it.
/// </summary>
/// <remarks>
/// <para>
/// The archive lays the files out as
/// <c>slr/products/orbits/{satellite}/{yymmdd}/{centre}.orb.{satellite}.{yymmdd}.v{nn}.sp3.gz</c>,
/// one file per satellite per week. The date is the <em>end</em> of the seven-day solution, which
/// is a Saturday: the published example, <c>ilrsa.orb.lageos1.160220.v01.sp3.gz</c>, is dated
/// Saturday 20 February 2016. So the file holding a given day is the one dated the first Saturday on
/// or after it, which is <see cref="WeekEnding"/>.
/// </para>
/// <para>
/// The version cannot be predicted, because a centre reissues a week when it reprocesses, so the
/// directory is listed and the highest version for the requested centre is taken
/// (<see cref="Latest"/>). Files older than CDDIS's move to gzip end in <c>.Z</c>, Unix
/// <c>compress</c>, which .NET has no decoder for; those are skipped, and a week that has nothing
/// else is refused by name rather than fetched and then failed on.
/// </para>
/// </remarks>
/// <param name="Centre">The producing centre: <c>ilrsa</c> is the primary combination, <c>ilrsb</c> the backup.</param>
/// <param name="Satellite">The satellite as the archive names it, such as <c>lageos1</c>.</param>
/// <param name="WeekEnd">The Saturday the solution ends on.</param>
/// <param name="Version">The version number.</param>
/// <param name="Name">The file name as listed.</param>
public sealed partial record IlrsOrbitFile(string Centre, string Satellite, DateOnly WeekEnd, int Version, string Name)
{
	/// <summary>The root of the archive's laser-ranging orbit products.</summary>
	public const string ArchiveRoot = "https://cddis.nasa.gov/archive/slr/products/orbits/";

	/// <summary>The geodetic satellites the archive carries orbits for, by the names it uses.</summary>
	public static IReadOnlyList<string> Satellites { get; } =
		["lageos1", "lageos2", "etalon1", "etalon2", "ajisai", "lares", "larets", "starlette", "stella"];

	/// <summary>Gets the file's address.</summary>
	public Uri Address => new(DirectoryAddress(Satellite, WeekEnd), Name);

	/// <summary>Gets a value indicating whether the file is gzip-compressed rather than plain text.</summary>
	public bool IsGzip => Name.EndsWith(".gz", StringComparison.Ordinal);

	/// <summary>
	/// Gets the Saturday ending the solution week that contains a day.
	/// </summary>
	/// <param name="day">A UTC day.</param>
	/// <returns>That day if it is a Saturday, otherwise the next Saturday.</returns>
	public static DateOnly WeekEnding(DateOnly day) =>
		day.AddDays(((int)DayOfWeek.Saturday - (int)day.DayOfWeek + 7) % 7);

	/// <summary>Gets the directory a week's files are in.</summary>
	/// <param name="satellite">The satellite, as the archive names it.</param>
	/// <param name="weekEnd">The Saturday the week ends on.</param>
	/// <returns>The directory's address, ending in a slash.</returns>
	/// <exception cref="ArgumentException">The satellite is not one the archive carries, or the date is not a Saturday.</exception>
	public static Uri DirectoryAddress(string satellite, DateOnly weekEnd)
	{
		RequireSatellite(satellite);

		if (weekEnd.DayOfWeek != DayOfWeek.Saturday)
		{
			throw new ArgumentException($"{weekEnd:yyyy-MM-dd} is a {weekEnd.DayOfWeek}; ILRS weeks end on a Saturday.", nameof(weekEnd));
		}

		return new Uri($"{ArchiveRoot}{satellite}/{Stamp(weekEnd)}/");
	}

	/// <summary>
	/// Picks the newest readable file for a centre out of a directory listing.
	/// </summary>
	/// <param name="listing">The listing, in any format that names each file as a whitespace-separated word.</param>
	/// <param name="centre">The centre wanted.</param>
	/// <param name="satellite">The satellite wanted.</param>
	/// <param name="weekEnd">The week wanted.</param>
	/// <returns>The file, or <see langword="null"/> when the listing has no readable file for that centre.</returns>
	public static IlrsOrbitFile? Latest(string listing, string centre, string satellite, DateOnly weekEnd)
	{
		Ensure.NotNull(listing);

		return Parse(listing)
			.Where(f => f.Centre == centre && f.Satellite == satellite && f.WeekEnd == weekEnd && !f.Name.EndsWith(".Z", StringComparison.Ordinal))
			.OrderByDescending(f => f.Version)
			.ThenByDescending(f => f.IsGzip)
			.FirstOrDefault();
	}

	/// <summary>Reads every orbit file name out of a listing, whichever centre or week it belongs to.</summary>
	/// <param name="listing">The listing.</param>
	/// <returns>The files named in it.</returns>
	public static IEnumerable<IlrsOrbitFile> Parse(string listing)
	{
		Ensure.NotNull(listing);

		// The name can be preceded by a path or surrounded by markup in an HTML listing, so the
		// pattern is searched for inside each word rather than matched against the whole of it.
		IEnumerable<Match> matches = listing
			.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
			.Select(word => NamePattern().Match(word));

		foreach (Match match in matches)
		{
			if (match.Success
				&& DateOnly.TryParseExact(match.Groups["date"].Value, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
			{
				yield return new IlrsOrbitFile(
					match.Groups["centre"].Value,
					match.Groups["sat"].Value,
					date,
					int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture),
					match.Value);
			}
		}
	}

	internal static void RequireSatellite(string satellite)
	{
		Ensure.NotNull(satellite);

		if (!Satellites.Contains(satellite, StringComparer.Ordinal))
		{
			throw new ArgumentException(
				$"'{satellite}' is not a satellite the ILRS archive carries. Expected one of: {string.Join(", ", Satellites)}.",
				nameof(satellite));
		}
	}

	private static string Stamp(DateOnly day) => day.ToString("yyMMdd", CultureInfo.InvariantCulture);

	[GeneratedRegex(@"(?<centre>[a-z0-9]+)\.orb\.(?<sat>[a-z0-9]+)\.(?<date>\d{6})\.v(?<version>\d+)\.sp3(\.gz|\.Z)?", RegexOptions.CultureInvariant)]
	private static partial Regex NamePattern();
}
