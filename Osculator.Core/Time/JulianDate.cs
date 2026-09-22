// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Time;

using System;

/// <summary>
/// An instant as a two-part Julian date: a whole-day part and a fraction of a day.
/// </summary>
/// <param name="Day">The whole-day part, always a half-integer, since a Julian day begins at noon.</param>
/// <param name="DayFraction">The fraction of a day past <paramref name="Day"/>, in [0, 1).</param>
/// <remarks>
/// <para>
/// Two parts rather than one because a single <see cref="double"/> cannot hold a Julian date at
/// useful resolution: the value is around 2.46 million, so one unit in the last place is about
/// 48 microseconds. Splitting it keeps the fraction's full precision, which is what carries the
/// sub-second part of an epoch.
/// </para>
/// <para>
/// The arithmetic here is the published algorithm's, deliberately — including its leap-year rule of
/// <c>year % 4 == 0</c>, which is wrong in 1900 and 2100 and right across the whole range a
/// two-digit element-set year can express (1957–2056). Changing it would move results away from the
/// vectors SGP4 is verified against, for no gain within that range.
/// </para>
/// </remarks>
public readonly record struct JulianDate(double Day, double DayFraction)
{
	/// <summary>
	/// The Julian date of 1949 December 31 at 00:00 UT, which is the day SGP4 counts from.
	/// </summary>
	public const double Sgp4DayZero = 2433281.5;

	/// <summary>Gets the days elapsed since <see cref="Sgp4DayZero"/>.</summary>
	/// <remarks>
	/// The deep-space model's solar and lunar geometry is a function of this, so it is the one form
	/// of the epoch the propagator actually consumes.
	/// </remarks>
	public double DaysSinceSgp4DayZero => Day + DayFraction - Sgp4DayZero;

	/// <summary>
	/// Builds a Julian date from a calendar date and a time of day.
	/// </summary>
	/// <param name="year">The four-digit year.</param>
	/// <param name="month">The month, 1 through 12.</param>
	/// <param name="day">The day of the month, 1 through 31.</param>
	/// <param name="hour">The hour, 0 through 23.</param>
	/// <param name="minute">The minute, 0 through 59.</param>
	/// <param name="second">The second, including any fraction.</param>
	/// <returns>The two-part Julian date.</returns>
	public static JulianDate FromCalendar(int year, int month, int day, int hour, int minute, double second)
	{
		// Every multiplication is written with a floating-point literal so none of them is an integer
		// multiply that could overflow before the division sees it. For a month in 1 to 12 it cannot,
		// but the parameters are a public surface and this costs nothing: the products are small
		// integers either way, so the result is bit-for-bit what the integer form gives.
		double whole = (367.0 * year)
			- System.Math.Floor(7.0 * (year + System.Math.Floor((month + 9.0) / 12.0)) * 0.25)
			+ System.Math.Floor(275.0 * month / 9.0)
			+ day
			+ 1721013.5;

		double fraction = (second + (minute * 60.0) + (hour * 3600.0)) / 86400.0;

		if (System.Math.Abs(fraction) > 1.0)
		{
			double carry = System.Math.Floor(fraction);
			whole += carry;
			fraction -= carry;
		}

		return new JulianDate(whole, fraction);
	}

	/// <summary>
	/// Builds a Julian date from the year and fractional day of year an element set carries.
	/// </summary>
	/// <param name="year">The four-digit year.</param>
	/// <param name="dayOfYear">The day of the year, where 1.0 is the start of 1 January.</param>
	/// <returns>The two-part Julian date.</returns>
	/// <remarks>
	/// <para>
	/// This is the path an element set takes, and it is exact: the fractional day goes straight to
	/// hours, minutes and seconds without passing through <see cref="DateTime"/>, which truncates to
	/// its 100-nanosecond tick and so loses up to that much of the epoch. Measured over real element
	/// sets, the loss is 0 to 99 nanoseconds.
	/// </para>
	/// <para>
	/// That is a smaller error than it first looks, and worth being accurate about rather than
	/// alarmed by: 99 nanoseconds is 7e-12 radians of the Earth's rotation, which at geostationary
	/// altitude is 3e-7 km — thirty times the tolerance the verification suite asserts. It does not
	/// show up there because the sidereal angle cancels out of the resonance terms, entering once
	/// when the integrator's starting longitude is set and once again, with the same error, when the
	/// longitude is compared against it. So the reason to convert exactly is not that the rounding
	/// is known to be harmful; it is that establishing it is harmless is more work than not
	/// incurring it, and has to be redone every time the model is extended.
	/// </para>
	/// </remarks>
	public static JulianDate FromDayOfYear(int year, double dayOfYear)
	{
		int wholeDays = (int)System.Math.Floor(dayOfYear);
		int[] monthLengths = [31, (year % 4) == 0 ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

		int month = 1;
		int elapsed = 0;

		while (month < 12 && wholeDays > elapsed + monthLengths[month - 1])
		{
			elapsed += monthLengths[month - 1];
			month++;
		}

		double hours = (dayOfYear - wholeDays) * 24.0;
		int hour = (int)System.Math.Floor(hours);
		double minutes = (hours - hour) * 60.0;
		int minute = (int)System.Math.Floor(minutes);
		double second = (minutes - minute) * 60.0;

		return FromCalendar(year, month, wholeDays - elapsed, hour, minute, second);
	}

	/// <summary>
	/// Builds a Julian date from an instant, reading its calendar fields rather than its ticks.
	/// </summary>
	/// <param name="instant">The instant, interpreted as UTC whatever its <see cref="DateTime.Kind"/>.</param>
	/// <returns>The two-part Julian date.</returns>
	public static JulianDate FromUtc(DateTime instant) => FromCalendar(
		instant.Year,
		instant.Month,
		instant.Day,
		instant.Hour,
		instant.Minute,
		instant.Second + (instant.Ticks % TimeSpan.TicksPerSecond / (double)TimeSpan.TicksPerSecond));
}
