// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Time;

using System;
using System.Numerics;
using ktsu.Osculator.Core.Frames;

/// <summary>
/// An instant held as one Julian date in the storage type, on a stated time scale.
/// </summary>
/// <typeparam name="T">The storage type.</typeparam>
/// <param name="JulianDay">The Julian date, whole days and fraction together in one value.</param>
/// <param name="Scale">The time scale <paramref name="JulianDay"/> is read on.</param>
/// <remarks>
/// <para>
/// This is the other side of <see cref="JulianDate"/>. A Julian date is about 2.46 million, so in a
/// single <see langword="double"/> one unit in the last place is about 48 microseconds, and that is
/// why the two-part form exists. In an arbitrary-precision type the single value is simply wide
/// enough: adding a sub-microsecond offset to it and taking it away again gives the offset back, and
/// in <see langword="double"/> it does not. This type deliberately does <em>not</em> split the value,
/// so that the same code shows the staircase in one storage type and the line in another.
/// </para>
/// <para>
/// "Exact" has one qualification worth stating plainly. A second is 1/86400 of a day, which does
/// not terminate in decimal, so turning a number of seconds into days rounds — in
/// <c>PreciseNumber</c>, to its division precision, which is dozens of digits past anything physical.
/// Everything else is exact there: addition does not round, so an offset added and then subtracted
/// again returns the original value to every digit, and a whole-day count or a terminating day
/// fraction is held as written.
/// </para>
/// <para>
/// The calendar here is the proleptic Gregorian one, with the full leap-year rule. That differs
/// from <see cref="JulianDate"/>, which keeps the published SGP4 algorithm's divisible-by-four rule;
/// the two agree from 1901 to 2099.
/// </para>
/// </remarks>
public readonly record struct PreciseInstant<T>(T JulianDay, TimeScale Scale)
	where T : struct, INumber<T>
{
	/// <summary>Gets the modified Julian date, which is the Julian date less 2400000.5.</summary>
	public T ModifiedJulianDay => JulianDay - Half(4800001);

	/// <summary>
	/// Builds an instant from a calendar date and a time of day.
	/// </summary>
	/// <param name="year">The year, proleptic Gregorian.</param>
	/// <param name="month">The month, 1 through 12.</param>
	/// <param name="day">The day of the month.</param>
	/// <param name="hour">The hour, 0 through 23.</param>
	/// <param name="minute">The minute, 0 through 59.</param>
	/// <param name="second">The second, including any fraction, in [0, 60).</param>
	/// <param name="scale">The time scale the clock reading is on.</param>
	/// <returns>The instant.</returns>
	/// <exception cref="ArgumentOutOfRangeException">A field is out of range.</exception>
	/// <remarks>
	/// The second must be below 60, so 23:59:60 — an inserted leap second — cannot be written in UTC.
	/// That is not an oversight: a UTC Julian date has no fraction left to name it with. See
	/// <see cref="LeapSeconds.TaiMinusUtcAtTai{TValue}"/>.
	/// </remarks>
	public static PreciseInstant<T> FromCalendar(int year, int month, int day, int hour, int minute, T second, TimeScale scale)
	{
		if (year is < -4712 or > 99999)
		{
			throw new ArgumentOutOfRangeException(nameof(year));
		}

		if (month is < 1 or > 12)
		{
			throw new ArgumentOutOfRangeException(nameof(month));
		}

		if (day < 1 || day > DaysInMonth(year, month))
		{
			throw new ArgumentOutOfRangeException(nameof(day));
		}

		if (hour is < 0 or > 23)
		{
			throw new ArgumentOutOfRangeException(nameof(hour));
		}

		if (minute is < 0 or > 59)
		{
			throw new ArgumentOutOfRangeException(nameof(minute));
		}

		if (second < T.Zero || second >= T.CreateChecked(60))
		{
			throw new ArgumentOutOfRangeException(nameof(second));
		}

		long dayNumber = DayNumberAtNoon(year, month, day);
		T midnight = Half((2L * dayNumber) - 1);
		T seconds = T.CreateChecked((hour * 3600) + (minute * 60)) + second;

		return new PreciseInstant<T>(midnight + TimeScales.SecondsToDays(seconds), scale);
	}

	/// <summary>
	/// Builds an instant from a two-part Julian date.
	/// </summary>
	/// <param name="julianDate">The Julian date.</param>
	/// <param name="scale">Its time scale.</param>
	/// <returns>
	/// The instant, holding the sum of the two parts exactly, which no single <see langword="double"/>
	/// could.
	/// </returns>
	public static PreciseInstant<T> FromJulianDate(JulianDate julianDate, TimeScale scale)
		=> new(T.CreateChecked(julianDate.Day) + T.CreateChecked(julianDate.DayFraction), scale);

	/// <summary>Builds an instant from a modified Julian date.</summary>
	/// <param name="modifiedJulianDay">The modified Julian date.</param>
	/// <param name="scale">Its time scale.</param>
	/// <returns>The instant.</returns>
	public static PreciseInstant<T> FromModifiedJulianDay(T modifiedJulianDay, TimeScale scale)
		=> new(modifiedJulianDay + Half(4800001), scale);

	/// <summary>Moves the instant along its own scale.</summary>
	/// <param name="seconds">How far, in seconds; negative moves it earlier.</param>
	/// <returns>The moved instant.</returns>
	public PreciseInstant<T> AddSeconds(T seconds) => this with { JulianDay = JulianDay + TimeScales.SecondsToDays(seconds) };

	/// <summary>Measures from this instant to another on the same scale.</summary>
	/// <param name="other">The other instant.</param>
	/// <returns>The seconds from this instant to <paramref name="other"/>.</returns>
	/// <exception cref="ArgumentException">The instants are on different scales.</exception>
	/// <remarks>
	/// Across a leap second, the elapsed SI seconds between two UTC instants are not their difference
	/// in UTC Julian date. Convert both to TAI first when that matters.
	/// </remarks>
	public T SecondsUntil(PreciseInstant<T> other)
	{
		if (other.Scale != Scale)
		{
			throw new ArgumentException($"Cannot difference a {Scale} instant with a {other.Scale} one.", nameof(other));
		}

		return (other.JulianDay - JulianDay) * T.CreateChecked(TimeScales.SecondsPerDay);
	}

	/// <summary>
	/// Converts to another atomic-derived scale: UTC, TAI or TT.
	/// </summary>
	/// <param name="target">The scale to convert to.</param>
	/// <param name="leapSeconds">The leap-second table.</param>
	/// <returns>The same instant on <paramref name="target"/>.</returns>
	/// <exception cref="InvalidOperationException">
	/// UT1 is the source or the target. It is the Earth's rotation, not a clock, and has to be given
	/// UT1 − UTC; use the overload that takes it.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">The conversion passes through UTC before 1972.</exception>
	public PreciseInstant<T> To(TimeScale target, LeapSeconds leapSeconds)
	{
		if (Scale == TimeScale.Ut1 || target == TimeScale.Ut1)
		{
			throw new InvalidOperationException(
				"UT1 is measured, not derived: pass UT1 − UTC explicitly. Passing zero is a choice worth up to 0.9 s.");
		}

		return Convert(target, leapSeconds, T.Zero);
	}

	/// <summary>
	/// Converts to any scale, UT1 included.
	/// </summary>
	/// <param name="target">The scale to convert to.</param>
	/// <param name="leapSeconds">The leap-second table.</param>
	/// <param name="ut1MinusUtcSeconds">UT1 − UTC at this instant, in seconds.</param>
	/// <returns>The same instant on <paramref name="target"/>.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The conversion passes through UTC before 1972.</exception>
	public PreciseInstant<T> To(TimeScale target, LeapSeconds leapSeconds, T ut1MinusUtcSeconds)
		=> Convert(target, leapSeconds, ut1MinusUtcSeconds);

	/// <summary>
	/// Converts to any scale, UT1 included, taking UT1 − UTC from an Earth orientation.
	/// </summary>
	/// <param name="target">The scale to convert to.</param>
	/// <param name="leapSeconds">The leap-second table.</param>
	/// <param name="orientation">The Earth orientation at this instant.</param>
	/// <returns>The same instant on <paramref name="target"/>.</returns>
	public PreciseInstant<T> To(TimeScale target, LeapSeconds leapSeconds, EarthOrientation orientation)
		=> Convert(target, leapSeconds, T.CreateChecked(orientation.Ut1MinusUtcSeconds));

	/// <summary>
	/// Reads the instant back as a calendar date and time of day on its own scale.
	/// </summary>
	/// <returns>The calendar fields, with the second carrying the whole fraction.</returns>
	public (int Year, int Month, int Day, int Hour, int Minute, T Second) ToCalendar()
	{
		T shifted = JulianDay + Half(1);
		long dayNumber = Floor(shifted);
		T secondsOfDay = (shifted - T.CreateChecked(dayNumber)) * T.CreateChecked(TimeScales.SecondsPerDay);
		long wholeSeconds = Floor(secondsOfDay);
		int hour = (int)(wholeSeconds / 3600);
		int minute = (int)(wholeSeconds % 3600 / 60);
		T second = secondsOfDay - T.CreateChecked((hour * 3600L) + (minute * 60L));

		// Richards' inverse of the Gregorian day number, valid for every day number from 0.
		long f = dayNumber + 1401 + ((((4 * dayNumber) + 274277) / 146097 * 3 / 4) - 38);
		long e = (4 * f) + 3;
		long g = e % 1461 / 4;
		long h = (5 * g) + 2;
		int day = (int)((h % 153 / 5) + 1);
		int month = (int)(((h / 153) + 2) % 12) + 1;
		int year = (int)((e / 1461) - 4716 + ((12 + 2 - month) / 12));

		return (year, month, day, hour, minute, second);
	}

	/// <summary>
	/// Reads the instant back as a two-part Julian date, which is the form the propagator takes.
	/// </summary>
	/// <returns>The two-part Julian date. The scale is dropped, since that type has none.</returns>
	public JulianDate ToJulianDate()
	{
		long dayNumber = Floor(JulianDay + Half(1));
		T midnight = Half((2L * dayNumber) - 1);

		return new JulianDate(double.CreateChecked(midnight), double.CreateChecked(JulianDay - midnight));
	}

	private static T Half(long numerator) => T.CreateChecked(numerator) / T.CreateChecked(2);

	private static long Floor(T value)
	{
		long truncated = long.CreateTruncating(value);

		return T.CreateChecked(truncated) > value ? truncated - 1 : truncated;
	}

	private static long DayNumberAtNoon(int year, int month, int day)
	{
		// Fliegel and Van Flandern's day number, in its proleptic Gregorian form.
		long a = (14 - month) / 12;
		long y = year + 4800 - a;
		long m = month + (12 * a) - 3;

		return day + (((153 * m) + 2) / 5) + (365 * y) + (y / 4) - (y / 100) + (y / 400) - 32045;
	}

	private static int DaysInMonth(int year, int month)
	{
		bool leap = (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;

		return month switch
		{
			2 => leap ? 29 : 28,
			4 or 6 or 9 or 11 => 30,
			_ => 31,
		};
	}

	private PreciseInstant<T> Convert(TimeScale target, LeapSeconds leapSeconds, T ut1MinusUtcSeconds)
	{
		Ensure.NotNull(leapSeconds);

		if (target == Scale)
		{
			return this;
		}

		// Every route goes through UTC, so each conversion is one step out and one step in, and each
		// step subtracts exactly what its inverse adds.
		T utc = Scale switch
		{
			TimeScale.Utc => JulianDay,
			TimeScale.Tai => FromTai(JulianDay, leapSeconds),
			TimeScale.Tt => FromTai(JulianDay - TimeScales.SecondsToDays(TimeScales.TtMinusTaiSeconds<T>()), leapSeconds),
			TimeScale.Ut1 => JulianDay - TimeScales.SecondsToDays(ut1MinusUtcSeconds),
			_ => throw new InvalidOperationException($"Unknown time scale {Scale}."),
		};

		T result = target switch
		{
			TimeScale.Utc => utc,
			TimeScale.Tai => ToTai(utc, leapSeconds),
			TimeScale.Tt => ToTai(utc, leapSeconds) + TimeScales.SecondsToDays(TimeScales.TtMinusTaiSeconds<T>()),
			TimeScale.Ut1 => utc + TimeScales.SecondsToDays(ut1MinusUtcSeconds),
			_ => throw new ArgumentOutOfRangeException(nameof(target)),
		};

		return new PreciseInstant<T>(result, target);
	}

	private static T ToTai(T utcJulianDay, LeapSeconds leapSeconds)
	{
		int offset = leapSeconds.TaiMinusUtcAtUtc(utcJulianDay - Half(4800001));

		return utcJulianDay + TimeScales.SecondsToDays(T.CreateChecked(offset));
	}

	private static T FromTai(T taiJulianDay, LeapSeconds leapSeconds)
	{
		int offset = leapSeconds.TaiMinusUtcAtTai(taiJulianDay - Half(4800001));

		return taiJulianDay - TimeScales.SecondsToDays(T.CreateChecked(offset));
	}
}
