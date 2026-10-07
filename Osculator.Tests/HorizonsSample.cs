// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System.Text.Json;

/// <summary>
/// A <c>horizons.api</c> response for the parser and client tests, which must not reach the network.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Written to Horizons' layout, not recorded from it.</strong> The CI container this was
/// written in cannot reach <c>ssd.jpl.nasa.gov</c>, so the envelope, the header lines, the column
/// header and the row format follow what the service writes for
/// <c>EPHEM_TYPE=VECTORS, VEC_TABLE=2, CSV_FORMAT=YES, OUT_UNITS=KM-S</c>, and the numbers are a
/// circular orbit of the Moon's radius and period rather than the Moon. Swapping in a recorded
/// response is worth doing; nothing in the tests depends on the numbers being the real Moon's,
/// only on them being self-consistent, which is what lets a test catch a position column read as
/// a velocity.
/// </para>
/// <para>
/// The report is built as text and wrapped by the serializer, as Horizons wraps it, so the escaping
/// of the <c>result</c> string is the serializer's rather than hand-written.
/// </para>
/// </remarks>
internal static class HorizonsSample
{
	/// <summary>The second row's Julian date, as Horizons writes it.</summary>
	internal const string SecondRowJulianDate = "2461041.541666667";

	/// <summary>Gets the column header Horizons writes for a TDB table.</summary>
	internal const string TdbColumns =
		"            JDTDB,            Calendar Date (TDB),                      X,                      Y,                      Z,                     VX,                     VY,                     VZ,";

	/// <summary>Gets four hourly geocentric rows.</summary>
	internal const string Rows =
		"""
		2461041.500000000, A.D. 2026-Jan-01 00:00:00.0000,  1.743623490759919E+05,  3.144044798926128E+05,  1.360549677452374E+05, -9.118453147111976E-01,  4.259300889027561E-01,  1.843164083641041E-01,
		2461041.541666667, A.D. 2026-Jan-01 01:00:00.0000,  1.710717515246595E+05,  3.159233710219755E+05,  1.367122506302794E+05, -9.162504483795819E-01,  4.178918598067980E-01,  1.808379560190454E-01,
		2461041.583333333, A.D. 2026-Jan-01 02:00:00.0000,  1.677654468000265E+05,  3.174132552399453E+05,  1.373569811038696E+05, -9.205714553324151E-01,  4.098152614312580E-01,  1.773428998040365E-01,
		2461041.625000000, A.D. 2026-Jan-01 03:00:00.0000,  1.644437384747217E+05,  3.188739957508829E+05,  1.379890999692485E+05, -9.248079388308761E-01,  4.017010353394228E-01,  1.738315606221671E-01,
		""";

	/// <summary>Gets a complete Moon response.</summary>
	internal static string MoonJson { get; } = Wrap(Report());

	/// <summary>
	/// Builds a report, with each part replaceable so a test can break exactly one of them.
	/// </summary>
	/// <param name="target">The target line's value.</param>
	/// <param name="units">The output units.</param>
	/// <param name="columns">The column header.</param>
	/// <param name="rows">The table rows.</param>
	/// <param name="endMarker">The end-of-table marker, or empty to leave it out.</param>
	/// <returns>The report text.</returns>
	internal static string Report(
		string target = "Moon (301)                      {source: DE441}",
		string units = "KM-S",
		string columns = TdbColumns,
		string rows = Rows,
		string endMarker = "$$EOE") =>
		$"""
		API VERSION: 1.2
		API SOURCE: NASA/JPL Horizons API

		*******************************************************************************
		Ephemeris / API_USER Wed Oct  7 12:00:00 2026 Pasadena, USA      / Horizons
		*******************************************************************************
		Target body name: {target}
		Center body name: Earth (399)                     {"{source: DE441}"}
		Center-site name: BODY CENTER
		*******************************************************************************
		Start time      : A.D. 2026-Jan-01 00:00:00.0000 TDB
		Stop  time      : A.D. 2026-Jan-01 03:00:00.0000 TDB
		Step-size       : 60 minutes
		*******************************************************************************
		Center geodetic : 0.0, 0.0, 0.0                   {"{E-lon(deg),Lat(deg),Alt(km)}"}
		Center radii    : 6378.137, 6378.137, 6356.752 km {"{Equator_a, b, pole_c}"}
		Output units    : {units}
		Calendar mode   : Mixed Julian/Gregorian
		Output type     : GEOMETRIC cartesian states
		Output format   : 2 (position and velocity)
		Reference frame : ICRF
		*******************************************************************************
		{columns}
		*******************************************************************************
		$$SOE
		{rows}
		{endMarker}
		*******************************************************************************
		""";

	/// <summary>Wraps a report in the API's JSON envelope.</summary>
	/// <param name="report">The report text.</param>
	/// <returns>The response body.</returns>
	internal static string Wrap(string report) =>
		JsonSerializer.Serialize(new { signature = new { source = "NASA/JPL Horizons API", version = "1.2" }, result = report });
}
