// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

using System;
using System.Globalization;
using System.Text;

/// <summary>
/// Writes element sets in the fixed-column two-line element format that <see cref="TleParser"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// Writing is the inverse of the parser's quantization, not of the orbit: each field is rounded to
/// the width of its column, so an element set read from two lines writes those lines back, while one
/// that came from OMM JSON loses the extra digits that format carries. See
/// <see cref="ElementFieldQuantization"/> for what each column is worth.
/// </para>
/// <para>
/// A value the columns cannot hold is refused rather than written. A field that overflows its width
/// shifts every field after it, and the result still parses — into a different orbit — unless the
/// checksum happens to catch it, so a corrupt line is the worst available answer.
/// </para>
/// </remarks>
public static class TleWriter
{
	/// <summary>The width of each line, including the checksum digit.</summary>
	private const int LineLength = 69;

	/// <summary>The largest catalogue number plain digits can write in five columns.</summary>
	private const int LargestNumericCatalogId = 99999;

	/// <summary>The largest catalogue number the Alpha-5 scheme can write.</summary>
	private const int LargestAlpha5CatalogId = 339999;

	/// <summary>
	/// The Alpha-5 leading characters for 10 through 33, which are the letters with I and O left out
	/// so that neither can be misread as a digit.
	/// </summary>
	private const string Alpha5Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";

	/// <summary>
	/// Writes an element set's two lines, with options matching what CelesTrak serves.
	/// </summary>
	/// <param name="elements">The element set.</param>
	/// <returns>The two lines, each 69 columns long with a correct checksum digit.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A field cannot be written in its columns.</exception>
	public static TleLines Write(ElementSet elements) => Write(elements, TleWriterOptions.CelesTrak);

	/// <summary>
	/// Writes an element set's two lines.
	/// </summary>
	/// <param name="elements">The element set.</param>
	/// <param name="options">The spellings the element set does not record.</param>
	/// <returns>The two lines, each 69 columns long with a correct checksum digit.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="elements"/> or <paramref name="options"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A field cannot be written in its columns.</exception>
	public static TleLines Write(ElementSet elements, TleWriterOptions options)
	{
		Ensure.NotNull(elements);
		Ensure.NotNull(options);

		string catalog = CatalogNumber(elements.NoradCatalogId);
		(int year, double dayOfYear) = EpochFields(elements.Epoch);

		StringBuilder line1 = new(LineLength);
		line1.Append("1 ")
			.Append(catalog)
			.Append(options.Classification)
			.Append(' ')
			.Append(Designator(elements.ObjectId))
			.Append(' ')
			.Append((year % 100).ToString("00", CultureInfo.InvariantCulture))
			.Append(dayOfYear.ToString("000.00000000", CultureInfo.InvariantCulture))
			.Append(' ')
			.Append(MeanMotionDot(elements.MeanMotionDot))
			.Append(' ')
			.Append(AssumedDecimal(elements.MeanMotionDdot, options.ExponentZeroSign, nameof(ElementSet.MeanMotionDdot)))
			.Append(' ')
			.Append(AssumedDecimal(elements.BStar, options.ExponentZeroSign, nameof(ElementSet.BStar)))
			.Append(" 0 ")
			.Append(Integer(elements.ElementSetNumber, 4, nameof(ElementSet.ElementSetNumber)));

		StringBuilder line2 = new(LineLength);
		line2.Append("2 ")
			.Append(catalog)
			.Append(' ')
			.Append(Angle(elements.Inclination, nameof(ElementSet.Inclination)))
			.Append(' ')
			.Append(Angle(elements.RightAscensionOfAscendingNode, nameof(ElementSet.RightAscensionOfAscendingNode)))
			.Append(' ')
			.Append(Eccentricity(elements.Eccentricity))
			.Append(' ')
			.Append(Angle(elements.ArgumentOfPericenter, nameof(ElementSet.ArgumentOfPericenter)))
			.Append(' ')
			.Append(Angle(elements.MeanAnomaly, nameof(ElementSet.MeanAnomaly)))
			.Append(' ')
			.Append(MeanMotion(elements.MeanMotion))
			.Append(Integer(elements.RevolutionAtEpoch, 5, nameof(ElementSet.RevolutionAtEpoch)));

		return new TleLines(WithChecksum(line1), WithChecksum(line2));
	}

	/// <summary>
	/// Computes the modulo-10 checksum digit of a line's first 68 columns.
	/// </summary>
	/// <param name="line">The line, at least 68 columns long.</param>
	/// <returns>The digit, 0 to 9.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="line"/> is null.</exception>
	/// <exception cref="ArgumentException"><paramref name="line"/> is shorter than 68 columns.</exception>
	/// <remarks>
	/// Digits count as themselves and a minus sign counts as one; everything else counts as nothing.
	/// The same rule <see cref="TleParser"/> verifies against.
	/// </remarks>
	public static int Checksum(string line)
	{
		Ensure.NotNull(line);

		if (line.Length < LineLength - 1)
		{
			throw new ArgumentException($"A checksum covers {LineLength - 1} columns; the line has {line.Length}.", nameof(line));
		}

		int sum = 0;

		for (int column = 0; column < LineLength - 1; column++)
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

	/// <summary>Writes a catalogue number in five columns, using Alpha-5 above 99999.</summary>
	/// <param name="id">The catalogue number.</param>
	/// <returns>The five characters.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The number is negative or beyond what Alpha-5 can write.</exception>
	/// <remarks>
	/// Alpha-5 replaces the leading digit pair with one letter: <c>A</c> is 10, <c>Z</c> is 33, and
	/// I and O are skipped. So 100000 is <c>A0000</c> and 339999 is <c>Z9999</c>, the last number
	/// the scheme reaches.
	/// </remarks>
	internal static string CatalogNumber(int id)
	{
		if (id is < 0 or > LargestAlpha5CatalogId)
		{
			throw new ArgumentOutOfRangeException(nameof(id), id, $"A two-line element set writes catalogue numbers 0 to {LargestAlpha5CatalogId}, the last of them in Alpha-5.");
		}

		if (id <= LargestNumericCatalogId)
		{
			return id.ToString("00000", CultureInfo.InvariantCulture);
		}

		int lead = id / 10000;
		return Alpha5Letters[lead - 10] + (id % 10000).ToString("0000", CultureInfo.InvariantCulture);
	}

	/// <summary>Converts an epoch to the two-digit year and fractional day of year the format writes.</summary>
	/// <param name="epoch">The epoch, in UTC.</param>
	/// <returns>The four-digit year and the day of year rounded to the format's eight decimals.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The year is outside 1957 to 2056, which two digits cannot name.</exception>
	/// <remarks>
	/// Read from the epoch's ticks rather than from the Julian date. Eight decimals of a day is
	/// 864 microseconds and a tick is a ten-thousandth of that, so rounding recovers the written
	/// digits exactly.
	/// </remarks>
	private static (int Year, double DayOfYear) EpochFields(DateTime epoch)
	{
		int year = epoch.Year;
		long ticksIntoYear = epoch.Ticks - new DateTime(year, 1, 1).Ticks;
		double dayOfYear = System.Math.Round(1.0 + (ticksIntoYear / (double)TimeSpan.TicksPerDay), 8, MidpointRounding.AwayFromZero);

		// Rounding the last instant of a year up lands on the first day after it, which the format
		// writes as day 1 of the next year rather than as a day the year does not have.
		if (dayOfYear >= (DateTime.IsLeapYear(year) ? 367.0 : 366.0))
		{
			year++;
			dayOfYear = 1.0;
		}

		if (year is < 1957 or > 2056)
		{
			throw new ArgumentOutOfRangeException(nameof(epoch), epoch, "A two-line element set's two-digit year names 1957 to 2056.");
		}

		return (year, dayOfYear);
	}

	/// <summary>Writes the international designator left-aligned in eight columns.</summary>
	/// <param name="designator">The designator in the compact form the parser returns, for example <c>98067A</c>.</param>
	/// <returns>The eight characters.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The designator is longer than eight characters.</exception>
	/// <remarks>
	/// OMM writes the designator as <c>1998-067A</c>, which is nine characters and the same object.
	/// That form is compacted to the two-line one rather than refused.
	/// </remarks>
	private static string Designator(string designator)
	{
		string text = designator ?? string.Empty;

		if (text.Length == 9 && text[4] == '-')
		{
			text = string.Concat(text.AsSpan(2, 2), text.AsSpan(5));
		}

		if (text.Length > 8)
		{
			throw new ArgumentOutOfRangeException(nameof(designator), designator, "A two-line element set writes the international designator in eight columns.");
		}

		return text.PadRight(8);
	}

	/// <summary>Writes the first derivative of mean motion: a sign and eight decimals with no leading zero.</summary>
	/// <param name="value">The value, in revolutions per day squared.</param>
	/// <returns>The ten characters, for example <c>" .00000023"</c> or <c>"-.00000084"</c>.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The value rounds to a magnitude of one or more.</exception>
	private static string MeanMotionDot(double value)
	{
		string digits = System.Math.Abs(value).ToString("0.00000000", CultureInfo.InvariantCulture);

		if (digits[0] != '0' || double.IsNaN(value))
		{
			throw new ArgumentOutOfRangeException(nameof(value), value, "A two-line element set writes the first derivative of mean motion below one in magnitude.");
		}

		bool negative = value < 0 && digits != "0.00000000";
		return (negative ? "-" : " ") + digits[1..];
	}

	/// <summary>
	/// Writes a value with an assumed leading decimal point and a single-digit exponent, the inverse
	/// of the parser's reading of the same eight columns.
	/// </summary>
	/// <param name="value">The value.</param>
	/// <param name="zeroSign">The sign written before an exponent of zero.</param>
	/// <param name="field">The field's name, for the message.</param>
	/// <returns>The eight characters, for example <c>" 28098-4"</c> for 0.28098 × 10⁻⁴.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The value needs an exponent outside −9 to +9.</exception>
	private static string AssumedDecimal(double value, char zeroSign, string field)
	{
		if (!double.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(field, value, $"{field} must be finite to be written.");
		}

		double magnitude = System.Math.Abs(value);
		long mantissa = 0;
		int exponent = 0;

		if (magnitude > 0.0)
		{
			// Normalize to 0.1 <= m < 1. The logarithm can land a hair either side of an integer for
			// an exact power of ten, which shows up as a mantissa of 100000 and is corrected below.
			exponent = (int)System.Math.Floor(System.Math.Log10(magnitude)) + 1;
			mantissa = (long)System.Math.Round(magnitude / System.Math.Pow(10.0, exponent) * 1e5, MidpointRounding.AwayFromZero);

			if (mantissa >= 100000)
			{
				mantissa /= 10;
				exponent++;
			}

			if (mantissa == 0)
			{
				exponent = 0;
			}
		}

		if (exponent is < -9 or > 9)
		{
			// Too small to write is zero at this quantization, and the format has a spelling for it.
			// Too large has none.
			if (exponent < -9)
			{
				mantissa = 0;
				exponent = 0;
			}
			else
			{
				throw new ArgumentOutOfRangeException(field, value, $"A two-line element set writes {field} with a single-digit exponent.");
			}
		}

		char sign = value < 0 && mantissa != 0 ? '-' : ' ';
		char exponentSign = exponent > 0 ? '+' : exponent < 0 ? '-' : zeroSign;

		return string.Create(CultureInfo.InvariantCulture, $"{sign}{mantissa:00000}{exponentSign}{System.Math.Abs(exponent)}");
	}

	/// <summary>Writes an angle in degrees as eight columns with four decimals.</summary>
	/// <param name="degrees">The angle.</param>
	/// <param name="field">The field's name, for the message.</param>
	/// <returns>The eight characters.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The angle is negative, not finite, or needs more than three integer digits.</exception>
	private static string Angle(double degrees, string field)
	{
		string text = degrees.ToString("0.0000", CultureInfo.InvariantCulture);

		if (!double.IsFinite(degrees) || degrees < 0 || text.Length > 8)
		{
			throw new ArgumentOutOfRangeException(field, degrees, $"A two-line element set writes {field} as a non-negative angle below 1000 degrees.");
		}

		return text.PadLeft(8);
	}

	/// <summary>Writes eccentricity as seven digits with an assumed leading decimal point.</summary>
	/// <param name="eccentricity">The eccentricity.</param>
	/// <returns>The seven characters.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The eccentricity is negative or rounds to one or more.</exception>
	private static string Eccentricity(double eccentricity)
	{
		double scaled = System.Math.Round(eccentricity * 1e7, MidpointRounding.AwayFromZero);

		if (double.IsNaN(scaled) || scaled < 0 || scaled >= 1e7)
		{
			throw new ArgumentOutOfRangeException(nameof(eccentricity), eccentricity, "A two-line element set writes an eccentricity from 0 to below 1.");
		}

		return ((long)scaled).ToString("0000000", CultureInfo.InvariantCulture);
	}

	/// <summary>Writes mean motion as eleven columns with eight decimals.</summary>
	/// <param name="revolutionsPerDay">The mean motion.</param>
	/// <returns>The eleven characters.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The mean motion is negative, not finite, or 100 or more.</exception>
	private static string MeanMotion(double revolutionsPerDay)
	{
		string text = revolutionsPerDay.ToString("0.00000000", CultureInfo.InvariantCulture);

		if (!double.IsFinite(revolutionsPerDay) || revolutionsPerDay < 0 || text.Length > 11)
		{
			throw new ArgumentOutOfRangeException(nameof(revolutionsPerDay), revolutionsPerDay, "A two-line element set writes mean motion from 0 to below 100 revolutions per day.");
		}

		return text.PadLeft(11);
	}

	/// <summary>Writes a non-negative integer right-aligned in a fixed width.</summary>
	/// <param name="value">The value.</param>
	/// <param name="width">The width.</param>
	/// <param name="field">The field's name, for the message.</param>
	/// <returns>The characters.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or has more digits than the width.</exception>
	private static string Integer(int value, int width, string field)
	{
		string text = value.ToString(CultureInfo.InvariantCulture);

		if (value < 0 || text.Length > width)
		{
			throw new ArgumentOutOfRangeException(field, value, $"A two-line element set writes {field} in {width} digits.");
		}

		return text.PadLeft(width);
	}

	/// <summary>Appends a line's checksum digit and returns the finished line.</summary>
	/// <param name="line">The first 68 columns.</param>
	/// <returns>The 69-column line.</returns>
	private static string WithChecksum(StringBuilder line)
	{
		string body = line.ToString();
		return body + Checksum(body).ToString(CultureInfo.InvariantCulture);
	}
}
