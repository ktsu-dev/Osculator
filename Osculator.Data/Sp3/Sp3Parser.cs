// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Sp3;

using System;
using System.Collections.Generic;
using System.Globalization;
using ktsu.Osculator.Core.Time;

/// <summary>
/// Reads the SP3-c precise orbit format: the one IGS publishes GPS orbits in and ILRS publishes the
/// laser-ranging satellites' orbits in.
/// </summary>
/// <remarks>
/// <para>
/// SP3 is fixed-column text, and every column below is the one the SP3-c specification gives,
/// counted from one as it counts them. Position, velocity and clock fields are cut by column rather
/// than split on whitespace, because the specification defines them by width and promises nothing
/// about a space between them, and the trailing accuracy and flag fields are optional.
/// </para>
/// <para>
/// Two places are read by token instead, because a real ILRS file breaks the columns there and both
/// can be read unambiguously either way: the epoch lines, whose year that file writes one column
/// early, and the four labels at the end of the first line, where it writes a six-character frame
/// name into a five-column field. See <c>ReadEpoch</c> and the test data's attribution notes.
/// </para>
/// <para>
/// <strong>A file is refused rather than half-read.</strong> The header states how many epochs
/// follow, and a body with fewer is a truncated download, which is the same failure the CelesTrak
/// cache guards against; parsing what arrived would hand an interpolator a table that silently
/// stops early. A version other than <c>c</c>, an epoch that is not a real calendar date, a record
/// for a satellite the header never declared, and epochs out of order are refused the same way, each
/// by name.
/// </para>
/// <para>
/// Two markers mean "no value" and are read as such rather than as numbers: a position of zero on
/// all three axes means the satellite has no orbit at that epoch, and a clock of
/// <c>999999.999999</c> means no clock. Laser-ranging orbits write no clock field at all.
/// </para>
/// </remarks>
public static class Sp3Parser
{
	/// <summary>The clock value SP3 writes to mean "no clock".</summary>
	private const double BadClock = 999999.0;

	/// <summary>
	/// Parses an SP3-c file.
	/// </summary>
	/// <param name="text">The file's contents.</param>
	/// <returns>The parsed file.</returns>
	/// <exception cref="FormatException">The text is not a complete, well-formed SP3-c file.</exception>
	public static Sp3File Parse(string text)
	{
		Ensure.NotNull(text);

		string[] lines = text.Split('\n');

		for (int i = 0; i < lines.Length; i++)
		{
			lines[i] = lines[i].TrimEnd('\r');
		}

		Sp3Header header = ReadFirstLines(lines);
		CalendarTime start = CalendarTime.ReadHeader(lines[0], 1);

		int index = 2;
		List<string> satellites = ReadSatellites(lines, ref index);
		string timeSystem = ReadTimeSystem(lines);
		header = header with { TimeSystem = timeSystem };

		Dictionary<string, List<Sp3Record>> records = new(StringComparer.Ordinal);

		foreach (string satellite in satellites)
		{
			records[satellite] = [];
		}

		List<JulianDate> epochs = [];
		JulianDate epoch = default;
		double secondsSinceStart = double.NaN;
		string? lastPositionSatellite = null;
		bool lastPositionKept = false;

		for (; index < lines.Length; index++)
		{
			string line = lines[index];
			int lineNumber = index + 1;

			if (line.StartsWith("EOF", StringComparison.Ordinal))
			{
				break;
			}

			if (line.Length == 0 || line.StartsWith('%') || line.StartsWith("/*", StringComparison.Ordinal)
				|| line.StartsWith('+') || line.StartsWith("EP", StringComparison.Ordinal) || line.StartsWith("EV", StringComparison.Ordinal))
			{
				continue;
			}

			switch (line[0])
			{
				case '*':
				{
					CalendarTime at = CalendarTime.ReadEpoch(line, lineNumber);
					double seconds = at.SecondsSince(start);

					if (epochs.Count > 0 && seconds <= secondsSinceStart)
					{
						throw new FormatException($"Line {lineNumber}: epoch {at} does not follow the one before it.");
					}

					epoch = at.ToJulianDate();
					secondsSinceStart = seconds;
					epochs.Add(epoch);
					lastPositionSatellite = null;
					break;
				}

				case 'P':
				{
					RequireEpoch(epochs, lineNumber);
					string satellite = Field(line, 2, 4);
					List<Sp3Record> entries = EntriesFor(records, satellite, lineNumber);

					double x = Number(line, 5, 18, lineNumber);
					double y = Number(line, 19, 32, lineNumber);
					double z = Number(line, 33, 46, lineNumber);

					lastPositionSatellite = satellite;
					lastPositionKept = false;

					// All three exactly zero is the format's "no orbit here". A real orbit passes
					// through no such point, so the comparison is exact rather than a tolerance.
					if (x == 0.0 && y == 0.0 && z == 0.0)
					{
						break;
					}

					entries.Add(new Sp3Record(epoch, secondsSinceStart, x, y, z, Clock(line, lineNumber), null));
					lastPositionKept = true;
					break;
				}

				case 'V':
				{
					RequireEpoch(epochs, lineNumber);
					string satellite = Field(line, 2, 4);
					List<Sp3Record> entries = EntriesFor(records, satellite, lineNumber);

					if (!header.HasVelocities)
					{
						throw new FormatException($"Line {lineNumber}: a velocity record in a file whose header declares positions only.");
					}

					if (lastPositionSatellite != satellite)
					{
						throw new FormatException($"Line {lineNumber}: a velocity for {satellite} does not follow that satellite's position.");
					}

					lastPositionSatellite = null;

					// The position was the missing marker, so there is nothing to attach this to.
					if (!lastPositionKept)
					{
						break;
					}

					// Decimetres per second to kilometres per second, and 1e-4 microseconds per second
					// to microseconds per second.
					const double Scale = 1e-4;
					double? rate = Clock(line, lineNumber);
					Sp3Velocity velocity = new(
						Number(line, 5, 18, lineNumber) * Scale,
						Number(line, 19, 32, lineNumber) * Scale,
						Number(line, 33, 46, lineNumber) * Scale,
						rate * Scale);

					entries[^1] = entries[^1] with { Velocity = velocity };
					break;
				}

				default:
					throw new FormatException($"Line {lineNumber}: \"{line}\" is not an SP3-c record.");
			}
		}

		if (epochs.Count != header.EpochCount)
		{
			throw new FormatException(
				$"The header promises {header.EpochCount} epochs and the file holds {epochs.Count}; it is truncated or was cut by hand.");
		}

		return new Sp3File(header, satellites, epochs, records);
	}

	private static Sp3Header ReadFirstLines(string[] lines)
	{
		if (lines.Length < 2 || lines[0].Length < 3 || lines[0][0] != '#')
		{
			throw new FormatException("This is not an SP3 file; its first line should begin with '#' and a version letter.");
		}

		char version = lines[0][1];

		if (version != 'c')
		{
			throw new FormatException($"This is SP3 version '{version}'; this parser reads SP3-c only.");
		}

		char mode = lines[0][2];

		if (mode is not ('P' or 'V'))
		{
			throw new FormatException($"Line 1: the position/velocity flag is '{mode}', which should be 'P' or 'V'.");
		}

		if (!lines[1].StartsWith("##", StringComparison.Ordinal))
		{
			throw new FormatException("Line 2 should begin \"##\".");
		}

		CalendarTime start = CalendarTime.ReadHeader(lines[0], 1);

		// Data used, coordinate system, orbit type and agency, specified as columns 41-45, 47-51,
		// 53-55 and 57-60. The ILRS LAGEOS-1 excerpt writes "ITRF14" — six characters in a
		// five-column field — which shifts the two after it, so the four are read as tokens
		// when there are four of them and by column only when there are not.
		string[] labels = Field(lines[0], 41, lines[0].Length).Split(' ', StringSplitOptions.RemoveEmptyEntries);

		if (labels.Length != 4)
		{
			labels = ["", Field(lines[0], 47, 51), Field(lines[0], 53, 55), Field(lines[0], 57, 60)];
		}

		return new Sp3Header(
			version,
			mode == 'V',
			start.ToJulianDate(),
			(int)Number(lines[0], 33, 39, 1),
			Number(lines[1], 25, 38, 2),
			labels[1],
			labels[2],
			labels[3],
			string.Empty);
	}

	private static List<string> ReadSatellites(string[] lines, ref int index)
	{
		if (index >= lines.Length || !lines[index].StartsWith("+ ", StringComparison.Ordinal))
		{
			throw new FormatException($"Line {index + 1} should begin the satellite list with \"+ \".");
		}

		int count = (int)Number(lines[index], 4, 6, index + 1);
		List<string> satellites = [];

		for (; index < lines.Length && lines[index].StartsWith("+ ", StringComparison.Ordinal); index++)
		{
			// Seventeen three-character identifiers per line, from column 10.
			for (int column = 10; column < 61 && satellites.Count < count; column += 3)
			{
				string id = Field(lines[index], column, column + 2);

				if (id.Length == 0 || id == "0")
				{
					throw new FormatException($"Line {index + 1}: the header declares {count} satellites and lists {satellites.Count}.");
				}

				satellites.Add(id);
			}
		}

		if (satellites.Count != count)
		{
			throw new FormatException($"The header declares {count} satellites and lists {satellites.Count}.");
		}

		return satellites;
	}

	private static string ReadTimeSystem(string[] lines)
	{
		foreach (string line in lines)
		{
			if (line.StartsWith("%c", StringComparison.Ordinal))
			{
				return Field(line, 10, 12);
			}

			if (line.StartsWith('*'))
			{
				break;
			}
		}

		throw new FormatException("The header has no \"%c\" line, so the epochs' time system is unknown.");
	}

	private static void RequireEpoch(List<JulianDate> epochs, int lineNumber)
	{
		if (epochs.Count == 0)
		{
			throw new FormatException($"Line {lineNumber}: a record before the first epoch line.");
		}
	}

	private static List<Sp3Record> EntriesFor(Dictionary<string, List<Sp3Record>> records, string satellite, int lineNumber) =>
		records.TryGetValue(satellite, out List<Sp3Record>? entries)
			? entries
			: throw new FormatException($"Line {lineNumber}: a record for {satellite}, which the header does not declare.");

	private static double? Clock(string line, int lineNumber)
	{
		if (Field(line, 47, 60).Length == 0)
		{
			return null;
		}

		double clock = Number(line, 47, 60, lineNumber);
		return Math.Abs(clock) >= BadClock ? null : clock;
	}

	/// <summary>Cuts a field by its one-based, inclusive columns, trimmed; columns past the line's end are empty.</summary>
	private static string Field(string line, int first, int last)
	{
		int start = first - 1;

		if (start >= line.Length)
		{
			return string.Empty;
		}

		int length = Math.Min(last, line.Length) - start;
		return line.Substring(start, length).Trim();
	}

	private static double Number(string line, int first, int last, int lineNumber)
	{
		string field = Field(line, first, last);

		return double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
			? value
			: throw new FormatException($"Line {lineNumber}, columns {first}-{last}: \"{field}\" is not a number.");
	}

	/// <summary>An epoch as SP3 writes it: calendar fields with fractional seconds.</summary>
	private readonly record struct CalendarTime(DateOnly Date, int Hour, int Minute, double Second)
	{
		/// <summary>Reads the calendar fields of the first header line, which sit in the specification's columns.</summary>
		public static CalendarTime ReadHeader(string line, int lineNumber) => Validate(
			(int)Number(line, 4, 7, lineNumber),
			(int)Number(line, 9, 10, lineNumber),
			(int)Number(line, 12, 13, lineNumber),
			(int)Number(line, 15, 16, lineNumber),
			(int)Number(line, 18, 19, lineNumber),
			Number(line, 21, 31, lineNumber),
			lineNumber);

		/// <summary>
		/// Reads an epoch line by its six whitespace-separated fields rather than by column.
		/// </summary>
		/// <remarks>
		/// The specification puts the year in columns 4-7, and at least one ILRS analysis centre
		/// writes it one column early — the LAGEOS-1 excerpt in the tests is such a file. The
		/// calendar fields are integers and a fractional second with spaces between every one, so
		/// unlike the position fields they cannot run together, and reading them by token accepts
		/// both without guessing at anything.
		/// </remarks>
		public static CalendarTime ReadEpoch(string line, int lineNumber)
		{
			string[] fields = line[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

			if (fields.Length != 6)
			{
				throw new FormatException($"Line {lineNumber}: an epoch line should carry six fields and has {fields.Length}.");
			}

			int[] whole = new int[5];

			for (int i = 0; i < 5; i++)
			{
				if (!int.TryParse(fields[i], NumberStyles.None, CultureInfo.InvariantCulture, out whole[i]))
				{
					throw new FormatException($"Line {lineNumber}: \"{fields[i]}\" is not a whole number.");
				}
			}

			return double.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double second)
				? Validate(whole[0], whole[1], whole[2], whole[3], whole[4], second, lineNumber)
				: throw new FormatException($"Line {lineNumber}: \"{fields[5]}\" is not a number of seconds.");
		}

		private static CalendarTime Validate(int year, int month, int day, int hour, int minute, double second, int lineNumber)
		{
			// Some producers have written "day 0" or "minute 60" across midnight rather than rolling
			// the date over. Normalising those would be guessing at what was meant, so they are
			// refused, by name.
			if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
				|| hour is < 0 or > 23 || minute is < 0 or > 59 || second < 0.0 || second >= 60.0)
			{
				throw new FormatException(
					$"Line {lineNumber}: {year:D4}-{month:D2}-{day:D2} {hour:D2}:{minute:D2}:{second} is not a calendar instant.");
			}

			return new CalendarTime(new DateOnly(year, month, day), hour, minute, second);
		}

		/// <summary>
		/// Seconds after another instant, from whole days, hours and minutes and only then the
		/// fractional seconds, so a whole-second epoch comes out as an exact whole number.
		/// </summary>
		public double SecondsSince(CalendarTime other)
		{
			double whole = ((Date.DayNumber - other.Date.DayNumber) * 86400.0) + ((Hour - other.Hour) * 3600.0) + ((Minute - other.Minute) * 60.0);
			return whole + (Second - other.Second);
		}

		public JulianDate ToJulianDate() => JulianDate.FromCalendar(Date.Year, Date.Month, Date.Day, Hour, Minute, Second);

		public override string ToString() =>
			$"{Date.Year:D4}-{Date.Month:D2}-{Date.Day:D2} {Hour:D2}:{Minute:D2}:{Second.ToString(CultureInfo.InvariantCulture)}";
	}
}
