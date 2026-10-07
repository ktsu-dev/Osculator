// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Horizons;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using ktsu.Osculator.Core.Time;

/// <summary>
/// Reads the vector table out of a <c>horizons.api</c> JSON response.
/// </summary>
/// <remarks>
/// <para>
/// The JSON is only an envelope. Horizons puts its whole text report into one <c>result</c> string:
/// a header describing the target, the centre, the units and the frame, then the table between the
/// <c>$$SOE</c> and <c>$$EOE</c> markers. A failed request carries an <c>error</c> member instead.
/// </para>
/// <para>
/// Two kinds of failure are kept apart, because the client does different things with them. A
/// response that is an answer but not a table — an <c>error</c> member, or a report with no
/// <c>$$SOE</c> such as the candidate list an ambiguous target produces — raises
/// <see cref="HorizonsException"/>: the service has spoken and asking the cache instead would hide
/// what it said. A response that is not a well-formed answer at all — HTML, a truncated table,
/// a number that does not parse — raises <see cref="JsonException"/> or <see cref="FormatException"/>,
/// which the client treats like the network being down.
/// </para>
/// <para>
/// The header's units are checked rather than trusted. The request pins kilometres and kilometres
/// per second, and a table in astronomical units read as kilometres would be off by a factor of
/// 150 million without anything else noticing.
/// </para>
/// </remarks>
public static class HorizonsVectorTable
{
	private const string StartOfTable = "$$SOE";

	private const string EndOfTable = "$$EOE";

	/// <summary>
	/// Parses a <c>horizons.api</c> response.
	/// </summary>
	/// <param name="json">The response body.</param>
	/// <returns>The table and what its header says about it.</returns>
	/// <exception cref="HorizonsException">Horizons answered, but not with a vector table.</exception>
	/// <exception cref="JsonException">The body is not JSON.</exception>
	/// <exception cref="FormatException">The body is JSON but not a readable vector table.</exception>
	public static HorizonsEphemeris Parse(string json)
	{
		Ensure.NotNull(json);

		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
		{
			throw new FormatException("A Horizons response is a JSON object.");
		}

		if (root.TryGetProperty("error", out JsonElement error))
		{
			throw new HorizonsException($"Horizons refused the request: {error}");
		}

		if (!root.TryGetProperty("result", out JsonElement resultElement) || resultElement.ValueKind != JsonValueKind.String)
		{
			throw new FormatException("A Horizons response carries its report in a 'result' string.");
		}

		string result = resultElement.GetString()!;
		string[] lines = result.Split('\n');

		int start = Array.FindIndex(lines, l => l.Trim() == StartOfTable);

		if (start < 0)
		{
			throw new HorizonsException($"Horizons answered without a vector table: {FirstMeaningfulLine(lines)}");
		}

		int end = Array.FindIndex(lines, start + 1, l => l.Trim() == EndOfTable);

		if (end < 0)
		{
			throw new FormatException("The vector table never ends; the response was cut short.");
		}

		ReadOnlySpan<string> header = lines.AsSpan(0, start);

		string units = HeaderValue(header, "Output units");

		if (!units.StartsWith("KM-S", StringComparison.Ordinal))
		{
			throw new FormatException($"The table is in '{units}', not kilometres and kilometres per second.");
		}

		List<HorizonsStateVector> vectors = [];

		for (int i = start + 1; i < end; i++)
		{
			if (!string.IsNullOrWhiteSpace(lines[i]))
			{
				vectors.Add(ParseRow(lines[i]));
			}
		}

		if (vectors.Count == 0)
		{
			throw new FormatException("The vector table is empty.");
		}

		return new HorizonsEphemeris(
			BodyName(HeaderValue(header, "Target body name")),
			BodyName(HeaderValue(header, "Center body name")),
			TimeScaleOf(header),
			HeaderValue(header, "Reference frame"),
			vectors);
	}

	/// <summary>
	/// Splits a Julian date written in decimal into its whole-day and fractional parts exactly.
	/// </summary>
	/// <param name="text">The Julian date as written, such as <c>2461041.541666667</c>.</param>
	/// <returns>The two-part date.</returns>
	/// <remarks>
	/// Split in <see cref="decimal"/>, which holds every digit Horizons writes, so the fraction keeps
	/// its precision instead of inheriting the 48-microsecond ulp of the whole number.
	/// </remarks>
	internal static JulianDate SplitJulianDate(string text)
	{
		decimal value = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
		decimal day = decimal.Floor(value - 0.5m) + 0.5m;

		return new JulianDate((double)day, (double)(value - day));
	}

	private static HorizonsStateVector ParseRow(string line)
	{
		string[] fields = line.Split(',');

		// Eight values, and the trailing comma Horizons ends every CSV row with.
		if (fields.Length < 8)
		{
			throw new FormatException(FormattableString.Invariant($"A vector row has {fields.Length} fields, not 8: {line.Trim()}"));
		}

		return new HorizonsStateVector(
			SplitJulianDate(fields[0].Trim()),
			fields[1].Trim(),
			Number(fields[2]),
			Number(fields[3]),
			Number(fields[4]),
			Number(fields[5]),
			Number(fields[6]),
			Number(fields[7]));
	}

	private static double Number(string field) =>
		double.Parse(field.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

	/// <summary>
	/// Reads the value of a <c>Name : value</c> line in the report's header.
	/// </summary>
	/// <param name="header">The lines before the table.</param>
	/// <param name="name">The name, without its padding or colon.</param>
	/// <returns>The value, trimmed.</returns>
	private static string HeaderValue(ReadOnlySpan<string> header, string name)
	{
		foreach (string line in header)
		{
			if (line.StartsWith(name, StringComparison.Ordinal))
			{
				int colon = line.IndexOf(':', StringComparison.Ordinal);

				if (colon > 0)
				{
					return line[(colon + 1)..].Trim();
				}
			}
		}

		throw new FormatException($"The report has no '{name}' line.");
	}

	/// <summary>Drops the ephemeris source Horizons appends to a body's name.</summary>
	/// <param name="value">For example <c>Moon (301)                {source: DE441}</c>.</param>
	/// <returns>For example <c>Moon (301)</c>.</returns>
	private static string BodyName(string value)
	{
		int source = value.IndexOf('{', StringComparison.Ordinal);

		return (source >= 0 ? value[..source] : value).Trim();
	}

	/// <summary>
	/// Reads the time scale from the table's column header, which names it in its first column.
	/// </summary>
	/// <param name="header">The lines before the table.</param>
	/// <returns>The scale.</returns>
	/// <remarks>
	/// The column header rather than the start-time line, because the column header is what labels
	/// the numbers actually being read.
	/// </remarks>
	private static HorizonsTimeScale TimeScaleOf(ReadOnlySpan<string> header)
	{
		foreach (string line in header)
		{
			string trimmed = line.Trim();

			if (trimmed.StartsWith("JD", StringComparison.Ordinal) && trimmed.Contains("Calendar Date", StringComparison.Ordinal))
			{
				string column = trimmed.Split(',')[0].Trim();

				foreach (HorizonsTimeScale scale in Enum.GetValues<HorizonsTimeScale>())
				{
					if (column == "JD" + HorizonsRequest.TimeScaleName(scale))
					{
						return scale;
					}
				}

				throw new FormatException($"The table's epochs are in '{column}', which is not a scale this reader knows.");
			}
		}

		throw new FormatException("The report has no column header naming its time scale.");
	}

	private static string FirstMeaningfulLine(string[] lines)
	{
		foreach (string line in lines)
		{
			string trimmed = line.Trim();

			if (trimmed.Length > 0 && trimmed.Trim('*').Length > 0 && !trimmed.StartsWith("API ", StringComparison.Ordinal))
			{
				return trimmed;
			}
		}

		return "(an empty report)";
	}
}
