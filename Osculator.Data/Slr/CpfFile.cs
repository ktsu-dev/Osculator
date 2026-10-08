// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Slr;

using System;
using System.Collections.Generic;
using System.Globalization;
using ktsu.Osculator.Core.Time;

/// <summary>
/// One position of a Consolidated Prediction Format file.
/// </summary>
/// <param name="Instant">The instant, on the UTC scale.</param>
/// <param name="SecondsSinceStart">Seconds after the file's first position.</param>
/// <param name="X">ITRF x, in kilometres.</param>
/// <param name="Y">ITRF y, in kilometres.</param>
/// <param name="Z">ITRF z, in kilometres.</param>
public sealed record CpfRecord(JulianDate Instant, double SecondsSinceStart, double X, double Y, double Z);

/// <summary>
/// An ILRS Consolidated Prediction Format file: the predicted Earth-fixed positions laser-ranging
/// stations point their telescopes with.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A prediction, not a precise orbit.</strong> A CPF is an analysis centre's orbit
/// determination propagated forward, issued daily so a station can find the satellite. For a
/// geodetic satellite such as LAGEOS it is good to metres over its first day, three orders of
/// magnitude below the kilometres SGP4 is wrong by, which is what makes it usable as truth for that
/// comparison and no tighter one. An SP3 orbit is the after-the-fact product and is preferred
/// whenever one exists for the day.
/// </para>
/// <para>
/// Only what the comparison reads is parsed: the header's version, the NORAD number, and the
/// instantaneous position records (record type 10, direction flag 0), in metres in the ITRF. A file
/// is refused rather than half-read: a version other than 1 or 2, a missing end-of-file record
/// (<c>99</c>), which is how a truncated download looks, and positions out of time order are each
/// refused by name.
/// </para>
/// </remarks>
public sealed class CpfFile
{
	private CpfFile(int version, int noradCatalogId, IReadOnlyList<CpfRecord> records)
	{
		Version = version;
		NoradCatalogId = noradCatalogId;
		Records = records;
	}

	/// <summary>Gets the format version from the first header line.</summary>
	public int Version { get; }

	/// <summary>Gets the satellite's NORAD catalogue number from the second header line.</summary>
	public int NoradCatalogId { get; }

	/// <summary>Gets the positions, in time order.</summary>
	public IReadOnlyList<CpfRecord> Records { get; }

	/// <summary>
	/// Parses a CPF file.
	/// </summary>
	/// <param name="text">The file's contents.</param>
	/// <returns>The file.</returns>
	/// <exception cref="FormatException">The text is not a complete CPF file.</exception>
	public static CpfFile Parse(string text)
	{
		Ensure.NotNull(text);

		int? version = null;
		int? norad = null;
		bool ended = false;
		List<CpfRecord> records = [];
		JulianDate? first = null;

		foreach (string raw in text.Split('\n'))
		{
			string line = raw.TrimEnd('\r');
			string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

			if (fields.Length == 0)
			{
				continue;
			}

			if (ended)
			{
				throw new FormatException("The CPF file has content after its end-of-file record.");
			}

			switch (fields[0].ToUpperInvariant())
			{
				case "H1":
					version = ReadVersion(fields);
					break;

				case "H2":
					norad = fields.Length > 3
						? Integer(fields[3], "the NORAD number")
						: throw new FormatException("The CPF H2 line has no NORAD number.");
					break;

				case "10":
					CpfRecord? record = ReadPosition(fields, ref first);

					if (record is not null)
					{
						if (records.Count > 0 && record.SecondsSinceStart <= records[^1].SecondsSinceStart)
						{
							throw new FormatException($"The CPF positions are out of time order at {line.Trim()}.");
						}

						records.Add(record);
					}

					break;

				case "99":
					ended = true;
					break;

				default:
					break;
			}
		}

		if (version is null)
		{
			throw new FormatException("This is not a CPF file; it has no H1 line.");
		}

		if (norad is null)
		{
			throw new FormatException("The CPF file has no H2 line.");
		}

		if (!ended)
		{
			throw new FormatException("The CPF file has no end-of-file record (99); it is truncated.");
		}

		return records.Count == 0
			? throw new FormatException("The CPF file has no position records.")
			: new CpfFile(version.Value, norad.Value, records);
	}

	private static int ReadVersion(string[] fields)
	{
		if (fields.Length < 3 || !string.Equals(fields[1], "CPF", StringComparison.OrdinalIgnoreCase))
		{
			throw new FormatException("This is not a CPF file; its H1 line does not name the format.");
		}

		int version = Integer(fields[2], "the format version");

		return version is 1 or 2
			? version
			: throw new FormatException($"CPF version {version} is not supported; expected 1 or 2.");
	}

	private static CpfRecord? ReadPosition(string[] fields, ref JulianDate? first)
	{
		// 10 <direction> <MJD> <seconds of day> <leap second flag> <x> <y> <z>
		if (fields.Length < 8)
		{
			throw new FormatException($"A CPF position record has {fields.Length} fields; expected 8.");
		}

		// Direction 1 and 2 are the transmit and receive legs of a two-way prediction for a distant
		// target; only the instantaneous position is a position of the satellite.
		if (Integer(fields[1], "the direction flag") != 0)
		{
			return null;
		}

		int mjd = Integer(fields[2], "the MJD");
		double secondsOfDay = Real(fields[3], "the seconds of day");
		JulianDate instant = new(mjd + 2400000.5, secondsOfDay / 86400.0);

		first ??= instant;

		double seconds = (instant.Day - first.Value.Day + (instant.DayFraction - first.Value.DayFraction)) * 86400.0;

		return new CpfRecord(
			instant,
			System.Math.Round(seconds, 6),
			Real(fields[5], "x") / 1000.0,
			Real(fields[6], "y") / 1000.0,
			Real(fields[7], "z") / 1000.0);
	}

	private static int Integer(string field, string what) =>
		int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
			? value
			: throw new FormatException($"The CPF file's {what} \"{field}\" is not an integer.");

	private static double Real(string field, string what) =>
		double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
			? value
			: throw new FormatException($"The CPF file's {what} \"{field}\" is not a number.");
}
