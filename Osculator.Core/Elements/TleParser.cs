// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

using System;
using System.Globalization;
using ktsu.Osculator.Core.Time;

/// <summary>
/// Reads element sets from the fixed-column two-line element format.
/// </summary>
/// <remarks>
/// <para>
/// The format is positional, not delimited: every field is a fixed column range, and two fields —
/// the second derivative of mean motion and the drag term — are written with an assumed leading
/// decimal point and a single-digit exponent, so <c>" 11249-3"</c> means 0.11249 × 10⁻³.
/// </para>
/// <para>
/// This is where the data error term comes from. Every field is truncated to the width of its
/// column, and nothing finer survives the round trip. See <see cref="ElementFieldQuantization"/>.
/// </para>
/// </remarks>
public static class TleParser
{
	/// <summary>The column layout requires each line to be at least this long.</summary>
	private const int MinimumLineLength = 69;

	/// <summary>Two-digit years at or above this belong to the twentieth century.</summary>
	/// <remarks>The format's own pivot, fixed by the epoch of the catalogue rather than chosen here.</remarks>
	private const int CenturyPivot = 57;

	/// <summary>
	/// Reads one element set from its two lines, with an optional object name.
	/// </summary>
	/// <param name="line1">The first line, beginning with <c>1</c>.</param>
	/// <param name="line2">The second line, beginning with <c>2</c>.</param>
	/// <param name="objectName">The object's name, where a third line carried one.</param>
	/// <param name="checksum">Whether each line's checksum digit is verified. Verified by default.</param>
	/// <returns>The element set, in the units the format uses: degrees and revolutions per day.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="line1"/> or <paramref name="line2"/> is null.</exception>
	/// <exception cref="FormatException">
	/// Either line is too short, does not begin with its line number, fails its checksum, or carries
	/// a field that will not parse.
	/// </exception>
	public static ElementSet Parse(string line1, string line2, string? objectName = null, TleChecksum checksum = TleChecksum.Verify)
	{
		Ensure.NotNull(line1);
		Ensure.NotNull(line2);

		if (line1.Length < MinimumLineLength || line2.Length < MinimumLineLength)
		{
			throw new FormatException($"A two-line element set needs {MinimumLineLength} columns per line; got {line1.Length} and {line2.Length}.");
		}

		// Both lines are long enough to parse from here on, so anything still wrong with them
		// produces an element set rather than an error. Column 1 catches the two lines arriving
		// swapped, and the checksum catches a line that was re-wrapped, re-typed or truncated
		// somewhere in transit — the corruption that is otherwise indistinguishable from an orbit.
		VerifyLineNumber(line1, '1');
		VerifyLineNumber(line2, '2');

		if (checksum == TleChecksum.Verify)
		{
			VerifyChecksum(line1, 1);
			VerifyChecksum(line2, 2);
		}

		int epochYear = Integer(line1, 18, 2);
		double epochDays = Decimal(line1, 20, 12);
		int fullYear = epochYear < CenturyPivot ? 2000 + epochYear : 1900 + epochYear;

		return new ElementSet
		{
			ObjectName = objectName?.Trim() ?? string.Empty,
			ObjectId = line1.Substring(9, 8).Trim(),
			NoradCatalogId = Integer(line1, 2, 5),
			Epoch = EpochOf(fullYear, epochDays),
			EpochJulianDate = JulianDate.FromDayOfYear(fullYear, epochDays),
			MeanMotionDot = Decimal(line1, 33, 10),
			MeanMotionDdot = AssumedDecimal(line1, 44, 8),
			BStar = AssumedDecimal(line1, 53, 8),
			ElementSetNumber = Integer(line1, 64, 4),
			Inclination = Decimal(line2, 8, 8),
			RightAscensionOfAscendingNode = Decimal(line2, 17, 8),
			Eccentricity = Decimal(line2, 26, 7) / 1e7,
			ArgumentOfPericenter = Decimal(line2, 34, 8),
			MeanAnomaly = Decimal(line2, 43, 8),
			MeanMotion = Decimal(line2, 52, 11),
			RevolutionAtEpoch = Integer(line2, 63, 5),
		};
	}

	/// <summary>
	/// Converts a year and a fractional day of that year into an instant.
	/// </summary>
	/// <param name="year">The four-digit year.</param>
	/// <param name="dayOfYear">The day of the year, where 1.0 is the start of 1 January.</param>
	/// <returns>The instant, in UTC.</returns>
	/// <remarks>
	/// The fractional part carries eight decimals, about 864 microseconds, which is finer than
	/// <see cref="DateTime"/>'s hundred-nanosecond tick and so survives intact.
	/// </remarks>
	public static DateTime EpochOf(int year, double dayOfYear)
	{
		DateTime startOfYear = new(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		return startOfYear.AddDays(dayOfYear - 1.0);
	}

	/// <summary>Confirms a line carries its own line number in column 1.</summary>
	/// <param name="line">The line.</param>
	/// <param name="expected">The digit that column holds, <c>1</c> or <c>2</c>.</param>
	/// <exception cref="FormatException">The column holds something else.</exception>
	private static void VerifyLineNumber(string line, char expected)
	{
		if (line[0] != expected)
		{
			throw new FormatException($"A two-line element set carries '{expected}' in column 1 of line {expected}; got '{line[0]}'. The two lines may be swapped.");
		}
	}

	/// <summary>Confirms a line's modulo-10 checksum digit agrees with the rest of the line.</summary>
	/// <param name="line">The line.</param>
	/// <param name="lineNumber">The line's number, for the message.</param>
	/// <exception cref="FormatException">The digit disagrees, or column 69 is not a digit at all.</exception>
	private static void VerifyChecksum(string line, int lineNumber)
	{
		char stated = line[MinimumLineLength - 1];

		if (stated is < '0' or > '9')
		{
			throw new FormatException($"Line {lineNumber} of a two-line element set ends with a checksum digit; column {MinimumLineLength} holds '{stated}'.");
		}

		int computed = ChecksumOf(line);

		if (computed != stated - '0')
		{
			throw new FormatException($"Line {lineNumber} of a two-line element set fails its checksum: the line sums to {computed}, and states {stated - '0'}.");
		}
	}

	/// <summary>Sums a line's first 68 columns the way the format's checksum does.</summary>
	/// <param name="line">The line.</param>
	/// <returns>The sum, modulo ten.</returns>
	/// <remarks>
	/// Digits count as themselves and a minus sign counts as one. Everything else — spaces, the
	/// plus signs in the exponent fields, the classification letter and the international
	/// designator — counts as nothing.
	/// </remarks>
	private static int ChecksumOf(string line)
	{
		int sum = 0;

		for (int column = 0; column < MinimumLineLength - 1; column++)
		{
			char c = line[column];

			if (c is >= '0' and <= '9')
			{
				sum += c - '0';
			}
			else if (c == '-')
			{
				sum++;
			}
		}

		return sum % 10;
	}

	/// <summary>Reads a plain decimal field.</summary>
	/// <param name="line">The line.</param>
	/// <param name="start">The zero-based column the field starts at.</param>
	/// <param name="length">The field width.</param>
	/// <returns>The value.</returns>
	private static double Decimal(string line, int start, int length)
	{
		string text = line.Substring(start, length).Trim();

		return text.Length == 0
			? 0.0
			: double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
	}

	/// <summary>Reads an integer field, treating blanks as zero.</summary>
	/// <param name="line">The line.</param>
	/// <param name="start">The zero-based column the field starts at.</param>
	/// <param name="length">The field width.</param>
	/// <returns>The value.</returns>
	private static int Integer(string line, int start, int length)
	{
		string text = line.Substring(start, length).Trim();

		return text.Length == 0
			? 0
			: int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Reads a field written with an assumed leading decimal point and a trailing exponent.
	/// </summary>
	/// <param name="line">The line.</param>
	/// <param name="start">The zero-based column the field starts at.</param>
	/// <param name="length">The field width, eight in every current use.</param>
	/// <returns>The value.</returns>
	/// <remarks>
	/// <c>" 11249-3"</c> is 0.11249 × 10⁻³. The leading character is the mantissa's sign and is a
	/// space when positive; the final two are the exponent's sign and one digit. Five mantissa
	/// digits is the whole of the precision the format keeps for these fields.
	/// </remarks>
	private static double AssumedDecimal(string line, int start, int length)
	{
		string field = line.Substring(start, length);
		string trimmed = field.Trim();

		if (trimmed.Length == 0 || trimmed.TrimStart('+', '-').TrimStart('0').Length == 0)
		{
			return 0.0;
		}

		double sign = field[0] == '-' ? -1.0 : 1.0;
		string mantissa = field.Substring(1, length - 3).Trim();
		string exponent = field[(length - 2)..].Trim();

		double value = double.Parse(mantissa, NumberStyles.Integer, CultureInfo.InvariantCulture)
			/ System.Math.Pow(10.0, mantissa.Length);
		int power = int.Parse(exponent, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

		return sign * value * System.Math.Pow(10.0, power);
	}
}
