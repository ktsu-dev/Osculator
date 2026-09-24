// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Iers;

using System;
using System.Collections.Generic;
using System.Globalization;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Time;

/// <summary>
/// The IERS daily Earth orientation series, parsed and interpolable.
/// </summary>
/// <remarks>
/// <para>
/// Reads <c>finals2000A.all.csv</c>, which the IERS publishes openly at
/// <c>datacenter.iers.org</c> — no account, unlike the laser-ranging archives. One row per day at
/// 0h UTC from 1973 to about fourteen months ahead, keyed by Modified Julian Date.
/// </para>
/// <para>
/// <strong>The file's trailing rows carry no values, and reading them as zero is the defect this
/// class exists to prevent.</strong> About fifty rows at the end have every field empty, because
/// the IERS publishes the date grid further ahead than it publishes forecasts for it. A parser
/// that splits on the delimiter and calls <c>double.Parse</c> on an empty field either throws or,
/// worse, defaults to zero — and zero is a legal-looking UT1 − UTC that silently costs up to
/// 415 m. Rows without values are dropped at parse time and an instant past the last real row is
/// refused by name.
/// </para>
/// <para>
/// Interpolation between the daily rows is linear. The quantities vary smoothly on a scale of
/// weeks, so over one day the second-order term is far below the published uncertainty; a spline
/// would be a more precise answer to a question the data cannot support.
/// </para>
/// </remarks>
public sealed class EarthOrientationTable
{
	private readonly List<Row> rows;

	private EarthOrientationTable(List<Row> parsed) => rows = parsed;

	/// <summary>Gets the Modified Julian Date of the first row carrying values.</summary>
	public double FirstModifiedJulianDate => rows[0].ModifiedJulianDate;

	/// <summary>Gets the Modified Julian Date of the last row carrying values.</summary>
	public double LastModifiedJulianDate => rows[^1].ModifiedJulianDate;

	/// <summary>Gets how many days carry values.</summary>
	public int Count => rows.Count;

	/// <summary>
	/// Parses the IERS CSV.
	/// </summary>
	/// <param name="csv">The file's contents.</param>
	/// <returns>The table.</returns>
	/// <exception cref="FormatException">The file has no usable rows, or no recognisable header.</exception>
	public static EarthOrientationTable Parse(string csv)
	{
		Ensure.NotNull(csv);

		string[] lines = csv.Split('\n');

		if (lines.Length == 0 || !lines[0].StartsWith("MJD;", StringComparison.Ordinal))
		{
			throw new FormatException("This is not an IERS finals2000A CSV; its first line should begin \"MJD;\".");
		}

		List<Row> parsed = [];

		for (int i = 1; i < lines.Length; i++)
		{
			string[] fields = lines[i].Split(';');

			// Columns, 1-based as the header numbers them: 1 MJD, 5 pole type, 6 x, 8 y,
			// 14 UT1 type, 15 UT1-UTC. A short line is a truncated download, not a data row.
			if (fields.Length < 16)
			{
				continue;
			}

			if (!TryParse(fields[0], out double mjd)
				|| !TryParse(fields[5], out double poleX)
				|| !TryParse(fields[7], out double poleY)
				|| !TryParse(fields[14], out double ut1MinusUtc))
			{
				continue;
			}

			parsed.Add(new Row(
				mjd,
				poleX,
				poleY,
				ut1MinusUtc,
				fields[4].Trim() != "final" || fields[13].Trim() != "final"));
		}

		return parsed.Count == 0
			? throw new FormatException("The IERS CSV parsed to no usable rows.")
			: new EarthOrientationTable(parsed);
	}

	/// <summary>
	/// The Earth's orientation at an instant, interpolated between the daily rows.
	/// </summary>
	/// <param name="instant">The instant, on the UTC scale.</param>
	/// <returns>The orientation.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The instant is outside the range the file carries values for.
	/// </exception>
	public EarthOrientation At(JulianDate instant)
	{
		double mjd = instant.Day + instant.DayFraction - 2400000.5;

		if (mjd < FirstModifiedJulianDate || mjd > LastModifiedJulianDate)
		{
			throw new ArgumentOutOfRangeException(
				nameof(instant),
				mjd,
				$"The table covers MJD {FirstModifiedJulianDate} to {LastModifiedJulianDate}; refusing to extrapolate.");
		}

		int after = IndexAtOrAfter(mjd);

		if (rows[after].ModifiedJulianDate == mjd || after == 0)
		{
			return rows[after].ToOrientation();
		}

		Row before = rows[after - 1];
		Row next = rows[after];
		double span = next.ModifiedJulianDate - before.ModifiedJulianDate;
		double t = (mjd - before.ModifiedJulianDate) / span;

		return new EarthOrientation(
			before.PoleXArcseconds + ((next.PoleXArcseconds - before.PoleXArcseconds) * t),
			before.PoleYArcseconds + ((next.PoleYArcseconds - before.PoleYArcseconds) * t),
			before.Ut1MinusUtcSeconds + ((next.Ut1MinusUtcSeconds - before.Ut1MinusUtcSeconds) * t),

			// Either neighbour being a forecast makes the answer one. A value blended from a
			// measurement and a forecast is not a measurement.
			before.IsPrediction || next.IsPrediction);
	}

	/// <summary>Binary search for the first row at or after a Modified Julian Date.</summary>
	/// <param name="mjd">The date.</param>
	/// <returns>Its index.</returns>
	private int IndexAtOrAfter(double mjd)
	{
		int low = 0;
		int high = rows.Count - 1;

		while (low < high)
		{
			int middle = (low + high) / 2;

			if (rows[middle].ModifiedJulianDate < mjd)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
	}

	private static bool TryParse(string field, out double value) =>
		double.TryParse(field.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

	private readonly record struct Row(
		double ModifiedJulianDate,
		double PoleXArcseconds,
		double PoleYArcseconds,
		double Ut1MinusUtcSeconds,
		bool IsPrediction)
	{
		public EarthOrientation ToOrientation() =>
			new(PoleXArcseconds, PoleYArcseconds, Ut1MinusUtcSeconds, IsPrediction);
	}
}
