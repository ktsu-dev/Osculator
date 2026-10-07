// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>What kind of thing a catalogued object is, as the SATCAT classifies it.</summary>
public enum SatcatObjectType
{
	/// <summary>A payload: <c>PAY</c>.</summary>
	Payload,

	/// <summary>A spent rocket body: <c>R/B</c>.</summary>
	RocketBody,

	/// <summary>Any other debris: <c>DEB</c>.</summary>
	Debris,

	/// <summary>Not yet identified: <c>UNK</c>.</summary>
	Unknown,
}

/// <summary>
/// The size class of a radar cross section, on the thresholds Space-Track publishes its own
/// <c>RCS_SIZE</c> with.
/// </summary>
public enum RcsSize
{
	/// <summary>Under 0.1 m².</summary>
	Small,

	/// <summary>From 0.1 m² up to 1 m².</summary>
	Medium,

	/// <summary>Over 1 m².</summary>
	Large,
}

/// <summary>
/// One object's row of CelesTrak's satellite catalogue: what it is, when it went up, how big it
/// looks to radar, and when it came down.
/// </summary>
/// <param name="NoradCatalogId">The NORAD catalogue number.</param>
/// <param name="ObjectName">The object's name.</param>
/// <param name="ObjectId">The international designator, such as <c>1998-067A</c>; null when the catalogue gives none.</param>
/// <param name="ObjectType">Payload, rocket body, debris or unknown.</param>
/// <param name="LaunchDate">The launch date; null when the catalogue gives none.</param>
/// <param name="DecayDate">The decay date; null for an object still in orbit.</param>
/// <param name="RadarCrossSection">The radar cross section in square metres; null when there is no measurement.</param>
public sealed record SatcatRecord(
	int NoradCatalogId,
	string ObjectName,
	string? ObjectId,
	SatcatObjectType ObjectType,
	DateOnly? LaunchDate,
	DateOnly? DecayDate,
	double? RadarCrossSection)
{
	/// <summary>The upper bound of <see cref="RcsSize.Small"/>, in square metres.</summary>
	public const double SmallRcsLimit = 0.1;

	/// <summary>The upper bound of <see cref="RcsSize.Medium"/>, in square metres.</summary>
	public const double MediumRcsLimit = 1.0;

	private static readonly string[] RequiredColumns =
		["OBJECT_NAME", "OBJECT_ID", "NORAD_CAT_ID", "OBJECT_TYPE", "LAUNCH_DATE", "DECAY_DATE", "RCS"];

	/// <summary>Gets whether the object has decayed, which is whether the catalogue gives a decay date.</summary>
	public bool HasDecayed => DecayDate is not null;

	/// <summary>Gets the size class of <see cref="RadarCrossSection"/>, or null when there is no measurement.</summary>
	public RcsSize? RcsSize => RadarCrossSection switch
	{
		null => null,
		< SmallRcsLimit => CelesTrak.RcsSize.Small,
		<= MediumRcsLimit => CelesTrak.RcsSize.Medium,
		_ => CelesTrak.RcsSize.Large,
	};

	/// <summary>
	/// Parses CelesTrak's <c>satcat.csv</c>.
	/// </summary>
	/// <param name="csv">The file's contents.</param>
	/// <returns>One record per row, in the file's order.</returns>
	/// <exception cref="FormatException">
	/// The header is missing a column this reads, a row has the wrong number of fields, a field
	/// that is present does not parse, a required field is empty, or a catalogue number repeats.
	/// </exception>
	/// <remarks>
	/// <para>
	/// <strong>A row that does not parse fails the whole file rather than being skipped.</strong>
	/// The file is about sixty thousand rows and is cached for hours, so a skipped row is an object
	/// that silently vanishes from the catalogue for the whole window. A truncated download ends in a
	/// short row, and failing on it is what keeps that download out of the cache.
	/// </para>
	/// <para>
	/// <strong>An empty field is null, never zero or a default date.</strong> An active object has
	/// no decay date, and reading that as <c>0001-01-01</c> would file it as decayed two thousand
	/// years ago. The same lesson <c>EarthOrientationTable</c> learned about empty UT1 − UTC.
	/// Only the catalogue number, the name and the object type are required to be present.
	/// </para>
	/// <para>
	/// Columns are found by name in the header rather than by position, so a column CelesTrak adds
	/// later does not move the ones read here.
	/// </para>
	/// </remarks>
	public static IReadOnlyList<SatcatRecord> ParseCsv(string csv)
	{
		Ensure.NotNull(csv);

		string[] lines = csv.Split('\n');
		List<string> header = SplitRow(lines[0].TrimEnd('\r'), 1);
		Dictionary<string, int> columns = new(StringComparer.Ordinal);

		for (int i = 0; i < header.Count; i++)
		{
			columns[header[i].Trim()] = i;
		}

		foreach (string required in RequiredColumns)
		{
			if (!columns.ContainsKey(required))
			{
				throw new FormatException($"This is not CelesTrak's satcat.csv; its header has no {required} column.");
			}
		}

		List<SatcatRecord> records = [];
		HashSet<int> seen = [];

		for (int i = 1; i < lines.Length; i++)
		{
			string line = lines[i].TrimEnd('\r');

			if (line.Length == 0)
			{
				continue;
			}

			int lineNumber = i + 1;
			List<string> fields = SplitRow(line, lineNumber);

			if (fields.Count != header.Count)
			{
				throw new FormatException(FormattableString.Invariant(
					$"satcat.csv line {lineNumber} has {fields.Count} fields where the header has {header.Count}."));
			}

			SatcatRecord record = new(
				ParseCatalogNumber(fields[columns["NORAD_CAT_ID"]], lineNumber),
				Required(fields[columns["OBJECT_NAME"]], "OBJECT_NAME", lineNumber),
				Optional(fields[columns["OBJECT_ID"]]),
				ParseObjectType(fields[columns["OBJECT_TYPE"]], lineNumber),
				ParseDate(fields[columns["LAUNCH_DATE"]], "LAUNCH_DATE", lineNumber),
				ParseDate(fields[columns["DECAY_DATE"]], "DECAY_DATE", lineNumber),
				ParseRcs(fields[columns["RCS"]], lineNumber));

			if (!seen.Add(record.NoradCatalogId))
			{
				throw new FormatException(FormattableString.Invariant(
					$"satcat.csv line {lineNumber} repeats catalogue number {record.NoradCatalogId}."));
			}

			records.Add(record);
		}

		return records.Count == 0
			? throw new FormatException("satcat.csv has a header and no rows.")
			: records;
	}

	private static int ParseCatalogNumber(string field, int line)
	{
		string text = Required(field, "NORAD_CAT_ID", line);

		return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0
			? value
			: throw new FormatException($"satcat.csv line {line}: NORAD_CAT_ID \"{text}\" is not a catalogue number.");
	}

	private static SatcatObjectType ParseObjectType(string field, int line) =>
		Required(field, "OBJECT_TYPE", line) switch
		{
			"PAY" => SatcatObjectType.Payload,
			"R/B" => SatcatObjectType.RocketBody,
			"DEB" => SatcatObjectType.Debris,
			"UNK" => SatcatObjectType.Unknown,
			string other => throw new FormatException($"satcat.csv line {line}: OBJECT_TYPE \"{other}\" is not PAY, R/B, DEB or UNK."),
		};

	private static DateOnly? ParseDate(string field, string column, int line)
	{
		string? text = Optional(field);

		if (text is null)
		{
			return null;
		}

		return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
			? date
			: throw new FormatException($"satcat.csv line {line}: {column} \"{text}\" is not a yyyy-MM-dd date.");
	}

	private static double? ParseRcs(string field, int line)
	{
		string? text = Optional(field);

		if (text is null)
		{
			return null;
		}

		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
			&& double.IsFinite(value) && value >= 0
			? value
			: throw new FormatException($"satcat.csv line {line}: RCS \"{text}\" is not a cross section in square metres.");
	}

	private static string Required(string field, string column, int line) =>
		Optional(field) ?? throw new FormatException($"satcat.csv line {line}: {column} is empty.");

	private static string? Optional(string field)
	{
		string trimmed = field.Trim();
		return trimmed.Length == 0 ? null : trimmed;
	}

	/// <summary>
	/// Splits one CSV row, honouring double-quoted fields so a name with a comma in it stays whole.
	/// </summary>
	/// <param name="line">The row.</param>
	/// <param name="lineNumber">The row's 1-based line number, for the error message.</param>
	/// <returns>The fields, unquoted.</returns>
	private static List<string> SplitRow(string line, int lineNumber)
	{
		List<string> fields = [];
		StringBuilder current = new();
		bool quoted = false;

		for (int i = 0; i < line.Length; i++)
		{
			char c = line[i];

			if (quoted)
			{
				if (c != '"')
				{
					current.Append(c);
				}
				else if (i + 1 < line.Length && line[i + 1] == '"')
				{
					current.Append('"');
					i++;
				}
				else
				{
					quoted = false;
				}
			}
			else if (c == '"')
			{
				quoted = true;
			}
			else if (c == ',')
			{
				fields.Add(current.ToString());
				current.Clear();
			}
			else
			{
				current.Append(c);
			}
		}

		if (quoted)
		{
			throw new FormatException(FormattableString.Invariant($"satcat.csv line {lineNumber} ends inside a quoted field."));
		}

		fields.Add(current.ToString());
		return fields;
	}
}
