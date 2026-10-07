// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ktsu.Osculator.Core.Elements;

/// <summary>
/// Writes element sets as the Orbit Mean-Elements Message JSON that <see cref="OmmJson"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// The output is shaped like CelesTrak's: an array of flat objects with the fields in the order
/// CelesTrak sends them, and each number written the way CelesTrak's PHP writes it. That is not
/// cosmetic. A file this writes and a file the service served are then the same bytes for the same
/// element set, so the snapshot archive can compare them as text.
/// </para>
/// <para>
/// Every number is the shortest decimal that reads back as the same <see cref="double"/>, so an
/// element set read from OMM writes and re-reads to an equal one. The epoch is written to the
/// microsecond, as the service writes it; an <see cref="ElementSet.Epoch"/> holding a finer instant
/// is rounded to that.
/// </para>
/// </remarks>
public static class OmmJsonWriter
{
	/// <summary>
	/// Above this many digits before the decimal point PHP switches a number to exponent form. It
	/// is the digit count of its shortest round-trip mode.
	/// </summary>
	private const int LargestFixedDecimalPoint = 17;

	/// <summary>
	/// Below this decimal-point position PHP switches a number to exponent form: 0.0001 is written
	/// out, 0.00001 is <c>1.0e-5</c>.
	/// </summary>
	private const int SmallestFixedDecimalPoint = -3;

	private static readonly JsonWriterOptions WriterOptions = new()
	{
		// The default encoder escapes '+' and the HTML-significant characters, all of which occur
		// in real object names ("ARIANE 44L+ R/B"). The escapes are valid JSON but are not what
		// the service sends, so they would make the text differ for no reason.
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	/// <summary>
	/// Writes element sets as an OMM JSON array.
	/// </summary>
	/// <param name="elementSets">The element sets, in the order to write them.</param>
	/// <returns>The document, on one line with no whitespace between tokens.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="elementSets"/> or one of its entries is null.</exception>
	public static string Write(IEnumerable<ElementSet> elementSets) => Write(elementSets, OmmWriterOptions.Default);

	/// <summary>
	/// Writes element sets as an OMM JSON array.
	/// </summary>
	/// <param name="elementSets">The element sets, in the order to write them.</param>
	/// <param name="options">The fields the element set does not record.</param>
	/// <returns>The document, on one line with no whitespace between tokens.</returns>
	/// <exception cref="ArgumentNullException">An argument or one of the element sets is null.</exception>
	public static string Write(IEnumerable<ElementSet> elementSets, OmmWriterOptions options)
	{
		Ensure.NotNull(elementSets);
		Ensure.NotNull(options);

		using MemoryStream buffer = new();

		using (Utf8JsonWriter writer = new(buffer, WriterOptions))
		{
			writer.WriteStartArray();

			foreach (ElementSet elements in elementSets)
			{
				WriteObject(writer, elements, options);
			}

			writer.WriteEndArray();
		}

		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	/// <summary>
	/// Writes a number the way PHP's <c>json_encode</c> does with its default shortest round-trip
	/// precision, which is what CelesTrak's responses are made with.
	/// </summary>
	/// <param name="value">The value.</param>
	/// <returns>The JSON number text.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The value is not finite, which JSON cannot write.</exception>
	/// <remarks>
	/// Three rules, each visible in a real response: a whole number has no decimal point
	/// (<c>"MEAN_MOTION_DDOT":0</c>); a number from 0.0001 upwards is written out
	/// (<c>0.00011249608</c>); and below that it takes an exponent with no padding and no plus sign
	/// lost (<c>5.779e-5</c>), with <c>.0</c> added when only one digit is significant.
	/// </remarks>
	internal static string FormatNumber(double value)
	{
		if (!double.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(nameof(value), value, "JSON cannot write a number that is not finite.");
		}

		if (value == 0.0)
		{
			return "0";
		}

		(string digits, int decimalPoint) = ShortestDigits(value);
		StringBuilder text = new();

		if (value < 0)
		{
			text.Append('-');
		}

		if (decimalPoint is < SmallestFixedDecimalPoint or > LargestFixedDecimalPoint)
		{
			int exponent = decimalPoint - 1;
			text.Append(digits[0])
				.Append('.')
				.Append(digits.Length > 1 ? digits[1..] : "0")
				.Append('e')
				.Append(exponent < 0 ? '-' : '+')
				.Append(System.Math.Abs(exponent).ToString(CultureInfo.InvariantCulture));
		}
		else if (decimalPoint <= 0)
		{
			text.Append("0.").Append('0', -decimalPoint).Append(digits);
		}
		else if (decimalPoint >= digits.Length)
		{
			text.Append(digits).Append('0', decimalPoint - digits.Length);
		}
		else
		{
			text.Append(digits.AsSpan(0, decimalPoint)).Append('.').Append(digits.AsSpan(decimalPoint));
		}

		return text.ToString();
	}

	/// <summary>
	/// Finds the shortest significant digits that read back as the same value, and where the decimal
	/// point falls among them.
	/// </summary>
	/// <param name="value">A finite, non-zero value.</param>
	/// <returns>
	/// The digits with no leading or trailing zeros, and the position of the decimal point counted
	/// from the left of them, so that 0.0005779 is <c>("5779", -3)</c> and 15.49 is <c>("1549", 2)</c>.
	/// </returns>
	private static (string Digits, int DecimalPoint) ShortestDigits(double value)
	{
		// "R" is the shortest round-trip form on every framework this targets. It may come back in
		// either fixed or exponent form, and only the digits and the point's position are kept.
		string roundTrip = System.Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);

		int exponentAt = roundTrip.IndexOfAny(['E', 'e']);
		string mantissa = exponentAt < 0 ? roundTrip : roundTrip[..exponentAt];
		int exponent = exponentAt < 0 ? 0 : int.Parse(roundTrip[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

		int pointAt = mantissa.IndexOf('.', StringComparison.Ordinal);
		int integerDigits = pointAt < 0 ? mantissa.Length : pointAt;
		string allDigits = mantissa.Replace(".", string.Empty, StringComparison.Ordinal);

		string trimmedLeading = allDigits.TrimStart('0');
		int leadingZeros = allDigits.Length - trimmedLeading.Length;
		string digits = trimmedLeading.TrimEnd('0');

		return (digits, integerDigits - leadingZeros + exponent);
	}

	/// <summary>Writes one element set as an object.</summary>
	/// <param name="writer">The writer.</param>
	/// <param name="elements">The element set.</param>
	/// <param name="options">The fields the element set does not record.</param>
	private static void WriteObject(Utf8JsonWriter writer, ElementSet elements, OmmWriterOptions options)
	{
		Ensure.NotNull(elements);

		writer.WriteStartObject();
		writer.WriteString("OBJECT_NAME", elements.ObjectName);
		writer.WriteString("OBJECT_ID", elements.ObjectId);
		writer.WriteString("EPOCH", Epoch(elements.Epoch));
		WriteNumber(writer, "MEAN_MOTION", elements.MeanMotion);
		WriteNumber(writer, "ECCENTRICITY", elements.Eccentricity);
		WriteNumber(writer, "INCLINATION", elements.Inclination);
		WriteNumber(writer, "RA_OF_ASC_NODE", elements.RightAscensionOfAscendingNode);
		WriteNumber(writer, "ARG_OF_PERICENTER", elements.ArgumentOfPericenter);
		WriteNumber(writer, "MEAN_ANOMALY", elements.MeanAnomaly);
		writer.WriteNumber("EPHEMERIS_TYPE", options.EphemerisType);
		writer.WriteString("CLASSIFICATION_TYPE", options.Classification);
		writer.WriteNumber("NORAD_CAT_ID", elements.NoradCatalogId);
		writer.WriteNumber("ELEMENT_SET_NO", elements.ElementSetNumber);
		writer.WriteNumber("REV_AT_EPOCH", elements.RevolutionAtEpoch);
		WriteNumber(writer, "BSTAR", elements.BStar);
		WriteNumber(writer, "MEAN_MOTION_DOT", elements.MeanMotionDot);
		WriteNumber(writer, "MEAN_MOTION_DDOT", elements.MeanMotionDdot);
		writer.WriteEndObject();
	}

	/// <summary>Writes a named number in the service's spelling.</summary>
	/// <param name="writer">The writer.</param>
	/// <param name="name">The property name.</param>
	/// <param name="value">The value.</param>
	private static void WriteNumber(Utf8JsonWriter writer, string name, double value)
	{
		writer.WritePropertyName(name);
		writer.WriteRawValue(FormatNumber(value));
	}

	/// <summary>Writes the epoch as the service does: ISO-8601, six fractional digits, no zone suffix.</summary>
	/// <param name="epoch">The epoch, in UTC.</param>
	/// <returns>The text.</returns>
	private static string Epoch(DateTime epoch)
	{
		long ticks = epoch.Ticks;
		long remainder = ticks % 10;
		long rounded = ticks - remainder + (remainder >= 5 ? 10 : 0);

		return new DateTime(rounded, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
	}
}
