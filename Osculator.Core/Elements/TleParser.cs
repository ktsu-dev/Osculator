// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

using System;
using System.Globalization;

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
	/// <returns>The element set, in the units the format uses: degrees and revolutions per day.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="line1"/> or <paramref name="line2"/> is null.</exception>
	/// <exception cref="FormatException">Either line is too short or carries a field that will not parse.</exception>
	public static ElementSet Parse(string line1, string line2, string? objectName = null)
	{
		Ensure.NotNull(line1);
		Ensure.NotNull(line2);

		if (line1.Length < MinimumLineLength || line2.Length < MinimumLineLength)
		{
			throw new FormatException($"A two-line element set needs {MinimumLineLength} columns per line; got {line1.Length} and {line2.Length}.");
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
