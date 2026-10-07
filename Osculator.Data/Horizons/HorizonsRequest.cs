// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Horizons;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// One request for a table of state vectors from JPL Horizons.
/// </summary>
/// <param name="Command">
/// The target, as Horizons' <c>COMMAND</c> parameter takes it: a body number such as <c>301</c>, or a
/// spacecraft's negative identifier such as <c>-48</c>.
/// </param>
/// <param name="Center">The origin of the vectors, as Horizons' <c>CENTER</c> parameter takes it.</param>
/// <param name="Start">The first instant of the table, read in <paramref name="TimeScale"/>.</param>
/// <param name="Stop">The last instant of the table, read in <paramref name="TimeScale"/>.</param>
/// <param name="Step">The spacing of the table, in whole minutes.</param>
/// <param name="TimeScale">The scale <paramref name="Start"/>, <paramref name="Stop"/> and the table's epochs are in.</param>
/// <remarks>
/// <para>
/// Everything else Horizons could be asked is pinned rather than offered, because each of it changes
/// what the numbers mean and none of it is a choice this repository wants made by accident: geometric
/// states with no light-time correction, position and velocity only, kilometres and kilometres per
/// second, and the ICRF with its equator as the reference plane — the frame a geocentric force model
/// works in, rather than the ecliptic Horizons otherwise defaults to.
/// </para>
/// <para>
/// <see cref="DateTime.Kind"/> is ignored. The instants are read in <paramref name="TimeScale"/>,
/// which by default is TDB; a <see cref="DateTime"/> has no way to say that, so the request does.
/// </para>
/// </remarks>
public sealed record HorizonsRequest(
	string Command,
	string Center,
	DateTime Start,
	DateTime Stop,
	TimeSpan Step,
	HorizonsTimeScale TimeScale = HorizonsTimeScale.Tdb)
{
	/// <summary>Horizons' body number for the Sun.</summary>
	public const string SunCommand = "10";

	/// <summary>Horizons' body number for the Moon.</summary>
	public const string MoonCommand = "301";

	/// <summary>The centre of the Earth, as an origin: site 500 on body 399.</summary>
	public const string GeocentreCenter = "500@399";

	/// <summary>Gets a request for geocentric Sun vectors.</summary>
	/// <param name="start">The first instant.</param>
	/// <param name="stop">The last instant.</param>
	/// <param name="step">The spacing, in whole minutes.</param>
	/// <returns>The request.</returns>
	public static HorizonsRequest Sun(DateTime start, DateTime stop, TimeSpan step) =>
		new(SunCommand, GeocentreCenter, start, stop, step);

	/// <summary>Gets a request for geocentric Moon vectors.</summary>
	/// <param name="start">The first instant.</param>
	/// <param name="stop">The last instant.</param>
	/// <param name="step">The spacing, in whole minutes.</param>
	/// <returns>The request.</returns>
	public static HorizonsRequest Moon(DateTime start, DateTime stop, TimeSpan step) =>
		new(MoonCommand, GeocentreCenter, start, stop, step);

	/// <summary>Gets a request for a spacecraft's geocentric vectors.</summary>
	/// <param name="spacecraftId">Horizons' identifier for the spacecraft, such as <c>-48</c>.</param>
	/// <param name="start">The first instant.</param>
	/// <param name="stop">The last instant.</param>
	/// <param name="step">The spacing, in whole minutes.</param>
	/// <returns>The request.</returns>
	/// <remarks>
	/// By identifier rather than by name, because a name can match several records and Horizons then
	/// answers with a list of candidates instead of a table.
	/// </remarks>
	public static HorizonsRequest Spacecraft(int spacecraftId, DateTime start, DateTime stop, TimeSpan step) =>
		new(spacecraftId.ToString(CultureInfo.InvariantCulture), GeocentreCenter, start, stop, step);

	/// <summary>
	/// Writes the request as a query string for the <c>horizons.api</c> endpoint.
	/// </summary>
	/// <returns>The query string, without a leading question mark.</returns>
	/// <exception cref="ArgumentException">The request cannot be asked of Horizons as it stands.</exception>
	/// <remarks>The parameters are in a fixed order, because the query string is also the cache key.</remarks>
	public string ToQuery()
	{
		RequireQuotable(Command, nameof(Command));
		RequireQuotable(Center, nameof(Center));

		if (Stop <= Start)
		{
			throw new ArgumentException("The table must stop after it starts.");
		}

		if (Step < TimeSpan.FromMinutes(1) || Step.Ticks % TimeSpan.TicksPerMinute != 0)
		{
			throw new ArgumentException("The step must be a positive whole number of minutes.");
		}

		long minutes = Step.Ticks / TimeSpan.TicksPerMinute;

		KeyValuePair<string, string>[] parameters =
		[
			new("format", "json"),
			new("COMMAND", Quote(Command)),
			new("OBJ_DATA", "'NO'"),
			new("MAKE_EPHEM", "'YES'"),
			new("EPHEM_TYPE", "'VECTORS'"),
			new("CENTER", Quote(Center)),
			new("START_TIME", Quote(FormatInstant(Start))),
			new("STOP_TIME", Quote(FormatInstant(Stop))),
			new("STEP_SIZE", Quote(FormattableString.Invariant($"{minutes} m"))),
			new("TIME_TYPE", Quote(TimeScaleName(TimeScale))),
			new("VEC_TABLE", "'2'"),
			new("VEC_CORR", "'NONE'"),
			new("REF_SYSTEM", "'ICRF'"),
			new("REF_PLANE", "'FRAME'"),
			new("OUT_UNITS", "'KM-S'"),
			new("CSV_FORMAT", "'YES'"),
			new("VEC_LABELS", "'NO'"),
		];

		return string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
	}

	/// <summary>Gets the name Horizons uses for a time scale, in requests and in its column headers.</summary>
	/// <param name="scale">The scale.</param>
	/// <returns>The name.</returns>
	internal static string TimeScaleName(HorizonsTimeScale scale) => scale switch
	{
		HorizonsTimeScale.Tdb => "TDB",
		HorizonsTimeScale.Tt => "TT",
		HorizonsTimeScale.Ut => "UT",
		_ => throw new ArgumentOutOfRangeException(nameof(scale), scale, "Not a time scale Horizons offers."),
	};

	private static string Quote(string value) => $"'{value}'";

	private static string FormatInstant(DateTime instant) =>
		instant.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

	/// <summary>
	/// Refuses a value that cannot sit inside Horizons' single quotes.
	/// </summary>
	/// <param name="value">The value.</param>
	/// <param name="name">The parameter it came from.</param>
	private static void RequireQuotable(string value, string name)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Contains('\'', StringComparison.Ordinal))
		{
			throw new ArgumentException($"Horizons takes {name} in single quotes, so it must be non-empty and contain none.");
		}
	}
}
