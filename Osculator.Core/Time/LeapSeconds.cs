// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Time;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;

/// <summary>
/// One step in TAI − UTC: from the start of <see cref="ModifiedJulianDay"/> (UTC) onwards, TAI is
/// ahead of UTC by <see cref="TaiMinusUtcSeconds"/>.
/// </summary>
/// <param name="ModifiedJulianDay">The UTC day the new offset takes effect, at its midnight.</param>
/// <param name="TaiMinusUtcSeconds">TAI − UTC from that midnight, in whole seconds.</param>
public readonly record struct LeapSecond(int ModifiedJulianDay, int TaiMinusUtcSeconds)
{
	private static readonly int ModifiedJulianDayZero = new DateOnly(1858, 11, 17).DayNumber;

	/// <summary>Gets the calendar date the offset takes effect.</summary>
	public DateOnly Date => DateOnly.FromDayNumber(ModifiedJulianDay + ModifiedJulianDayZero);

	/// <summary>Gets the modified Julian date of a calendar date.</summary>
	/// <param name="date">The date.</param>
	/// <returns>The modified Julian date of its midnight.</returns>
	public static int ModifiedJulianDayOf(DateOnly date) => date.DayNumber - ModifiedJulianDayZero;
}

/// <summary>
/// The leap-second table: TAI − UTC as a step function of the date.
/// </summary>
/// <remarks>
/// <para>
/// UTC before 1972 was not stepped by whole seconds but steered by a drifting rate offset, so it is
/// not a step function and is refused rather than approximated. Every element set this application
/// reads is later than that.
/// </para>
/// <para>
/// <see cref="BuiltIn"/> carries every leap second up to the last one inserted, at the end of 2016.
/// The IERS announces a leap second about six months ahead, so a table is only known to be complete
/// up to the expiry date its source file states; <see cref="ExpiresOn"/> carries that date for a
/// table read from the IERS file and is <see langword="null"/> for the built-in one, which makes no
/// claim past the moment it was written. Past its last entry a table answers with its last offset,
/// which is the right answer until the IERS says otherwise.
/// </para>
/// </remarks>
public sealed class LeapSeconds
{
	private LeapSeconds(IReadOnlyList<LeapSecond> entries, DateOnly? expiresOn)
	{
		Entries = entries;
		ExpiresOn = expiresOn;
	}

	/// <summary>Gets the table compiled into the application, complete through the 2017-01-01 step.</summary>
	public static LeapSeconds BuiltIn { get; } = new(
		new ReadOnlyCollection<LeapSecond>(
		[
			new(41317, 10), // 1972-01-01
			new(41499, 11), // 1972-07-01
			new(41683, 12), // 1973-01-01
			new(42048, 13), // 1974-01-01
			new(42413, 14), // 1975-01-01
			new(42778, 15), // 1976-01-01
			new(43144, 16), // 1977-01-01
			new(43509, 17), // 1978-01-01
			new(43874, 18), // 1979-01-01
			new(44239, 19), // 1980-01-01
			new(44786, 20), // 1981-07-01
			new(45151, 21), // 1982-07-01
			new(45516, 22), // 1983-07-01
			new(46247, 23), // 1985-07-01
			new(47161, 24), // 1988-01-01
			new(47892, 25), // 1990-01-01
			new(48257, 26), // 1991-01-01
			new(48804, 27), // 1992-07-01
			new(49169, 28), // 1993-07-01
			new(49534, 29), // 1994-07-01
			new(50083, 30), // 1996-01-01
			new(50630, 31), // 1997-07-01
			new(51179, 32), // 1999-01-01
			new(53736, 33), // 2006-01-01
			new(54832, 34), // 2009-01-01
			new(56109, 35), // 2012-07-01
			new(57204, 36), // 2015-07-01
			new(57754, 37), // 2017-01-01
		]),
		expiresOn: null);

	/// <summary>Gets the steps, in date order.</summary>
	public IReadOnlyList<LeapSecond> Entries { get; }

	/// <summary>
	/// Gets the date up to which the source declared the table complete, or <see langword="null"/>
	/// when it declared none.
	/// </summary>
	public DateOnly? ExpiresOn { get; }

	/// <summary>
	/// Parses the IERS <c>Leap_Second.dat</c> file.
	/// </summary>
	/// <param name="text">The file's contents.</param>
	/// <returns>The table.</returns>
	/// <exception cref="FormatException">
	/// The text is not that file: no data rows, a row that does not read, a modified Julian date that
	/// does not match the calendar date beside it, dates out of order, or a step other than one
	/// second.
	/// </exception>
	/// <remarks>
	/// Each row carries its date twice, as a modified Julian date and as a calendar date, and the two
	/// are checked against each other. That is what turns a truncated or mangled download into a
	/// refusal rather than a table that is quietly wrong by a second from some date onwards.
	/// </remarks>
	public static LeapSeconds Parse(string text)
	{
		Ensure.NotNull(text);

		List<LeapSecond> entries = [];
		DateOnly? expiresOn = null;
		const string expiryMarker = "File expires on";

		foreach (string line in text.Split('\n').Select(static rawLine => rawLine.Trim()))
		{
			if (line.Length == 0)
			{
				continue;
			}

			if (line[0] == '#')
			{
				int marker = line.IndexOf(expiryMarker, StringComparison.OrdinalIgnoreCase);

				if (marker >= 0)
				{
					string date = line[(marker + expiryMarker.Length)..].Trim();
					expiresOn = DateOnly.TryParseExact(date, "d MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
						? parsed
						: throw new FormatException($"Unreadable expiry date '{date}'.");
				}

				continue;
			}

			entries.Add(ParseRow(line, entries.Count > 0 ? entries[^1] : null));
		}

		return entries.Count == 0
			? throw new FormatException("No leap-second rows were found.")
			: new LeapSeconds(new ReadOnlyCollection<LeapSecond>(entries), expiresOn);
	}

	/// <summary>
	/// Gets TAI − UTC at a UTC instant.
	/// </summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <param name="utcModifiedJulianDay">The instant, as a modified Julian date on the UTC scale.</param>
	/// <returns>TAI − UTC, in seconds.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The instant is before 1972.</exception>
	public int TaiMinusUtcAtUtc<T>(T utcModifiedJulianDay)
		where T : struct, INumber<T>
	{
		for (int i = Entries.Count - 1; i >= 0; i--)
		{
			if (utcModifiedJulianDay >= T.CreateChecked(Entries[i].ModifiedJulianDay))
			{
				return Entries[i].TaiMinusUtcSeconds;
			}
		}

		throw BeforeTable();
	}

	/// <summary>
	/// Gets TAI − UTC at an instant given on the TAI scale.
	/// </summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <param name="taiModifiedJulianDay">The instant, as a modified Julian date on the TAI scale.</param>
	/// <returns>TAI − UTC, in seconds.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The instant is before 1972.</exception>
	/// <remarks>
	/// <para>
	/// A step takes effect at the TAI instant its UTC midnight falls on, which is the midnight plus
	/// the <em>new</em> offset. The inserted second itself — 23:59:60 UTC — is the TAI second just
	/// before that, and it is answered with the old offset.
	/// </para>
	/// <para>
	/// That makes two TAI seconds land on the first UTC second of the new day, because a UTC Julian
	/// date has no way to name 23:59:60: the day it would extend has only 86400 seconds of fraction.
	/// Every conversion that <em>starts</em> in UTC is unaffected and round-trips exactly. A TAI
	/// instant inside an inserted second comes back one second later than it went in, which is the
	/// cost of naming UTC instants by Julian date at all, and is pinned by a test rather than hidden.
	/// </para>
	/// </remarks>
	public int TaiMinusUtcAtTai<T>(T taiModifiedJulianDay)
		where T : struct, INumber<T>
	{
		for (int i = Entries.Count - 1; i >= 0; i--)
		{
			T takesEffect = T.CreateChecked(Entries[i].ModifiedJulianDay)
				+ TimeScales.SecondsToDays(T.CreateChecked(Entries[i].TaiMinusUtcSeconds));

			if (taiModifiedJulianDay >= takesEffect)
			{
				return Entries[i].TaiMinusUtcSeconds;
			}
		}

		throw BeforeTable();
	}

	private static ArgumentOutOfRangeException BeforeTable() => new(
		"modifiedJulianDay",
		"UTC before 1972 was steered by a rate offset rather than stepped by leap seconds, and is not supported.");

	private static LeapSecond ParseRow(string line, LeapSecond? previous)
	{
		string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

		if (fields.Length != 5
			|| !decimal.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal mjdValue)
			|| !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)
			|| !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int month)
			|| !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int year)
			|| !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset)
			|| mjdValue != decimal.Truncate(mjdValue)
			|| mjdValue is < 0 or > int.MaxValue
			|| year is < 1 or > 9999
			|| month is < 1 or > 12
			|| day < 1
			|| day > DateTime.DaysInMonth(year, month))
		{
			throw new FormatException($"Unreadable leap-second row '{line}'.");
		}

		int mjd = (int)mjdValue;

		if (mjd != LeapSecond.ModifiedJulianDayOf(new DateOnly(year, month, day)))
		{
			throw new FormatException($"Row '{line}' gives a modified Julian date that is not its calendar date.");
		}

		if (previous is LeapSecond before)
		{
			if (mjd <= before.ModifiedJulianDay)
			{
				throw new FormatException($"Row '{line}' is out of date order.");
			}

			if (System.Math.Abs(offset - before.TaiMinusUtcSeconds) != 1)
			{
				throw new FormatException($"Row '{line}' steps TAI − UTC by something other than one second.");
			}
		}

		return new LeapSecond(mjd, offset);
	}
}
