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

			// Columns, 1-based as the header numbers them: 1 MJD, 5 pole type, 6 x, 7 sigma x, 8 y,
			// 9 sigma y, 14 UT1 type, 15 UT1-UTC, 16 sigma UT1-UTC. A short line is a truncated
			// download, not a data row.
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
				fields[4].Trim() != "final" || fields[13].Trim() != "final",
				SigmaOrUnknown(fields[6]),
				SigmaOrUnknown(fields[8]),
				SigmaOrUnknown(fields[15])));
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

		// The first row has no predecessor to interpolate from, and the range check above means
		// reaching it at all requires the instant to be exactly on it.
		if (after == 0)
		{
			return rows[0].ToOrientation();
		}

		// There is deliberately no "landed exactly on a row" fast path. It would be an equality
		// comparison between doubles, and it would buy nothing: an instant on a row interpolates
		// at t = 1, which returns that row's values bit for bit, and an instant a single ulp off a
		// row interpolates at t within an ulp of 1, which returns them to within an ulp — about
		// 1e-17 arcseconds, or a picometre at the Earth's surface. The branch could only ever
		// change the answer by less than the arithmetic it was skipping.
		Row before = rows[after - 1];
		Row next = rows[after];
		double span = next.ModifiedJulianDate - before.ModifiedJulianDate;
		double t = (mjd - before.ModifiedJulianDate) / span;

		// UT1 − UTC is not continuous: it steps by a whole second at every leap second, which lands
		// at 0h UTC on the later row's date. Interpolating straight through that step drags the
		// whole preceding day toward the post-leap value — up to ~465 m of rotation at the equator
		// just before midnight. The real day-to-day change is a few milliseconds, so any whole
		// second in the difference is the step, and taking it out keeps the value continuous within
		// the day and puts the jump exactly on the later row. At t = 1 the instant is on that row,
		// after the step, so the step goes back in and the row's own value comes out.
		double ut1Delta = next.Ut1MinusUtcSeconds - before.Ut1MinusUtcSeconds;
		double leapStep = Math.Round(ut1Delta);
		double ut1Drift = ut1Delta - leapStep;
		double ut1MinusUtc = t < 1.0
			? before.Ut1MinusUtcSeconds + (ut1Drift * t)
			: next.Ut1MinusUtcSeconds;

		return new EarthOrientation(
			before.PoleXArcseconds + ((next.PoleXArcseconds - before.PoleXArcseconds) * t),
			before.PoleYArcseconds + ((next.PoleYArcseconds - before.PoleYArcseconds) * t),
			ut1MinusUtc,

			// Either neighbour being a forecast makes the answer one. A value blended from a
			// measurement and a forecast is not a measurement.
			before.IsPrediction || next.IsPrediction,
			SigmaBetween(before.PoleXSigmaArcseconds, next.PoleXSigmaArcseconds, t),
			SigmaBetween(before.PoleYSigmaArcseconds, next.PoleYSigmaArcseconds, t),
			SigmaBetween(before.Ut1MinusUtcSigmaSeconds, next.Ut1MinusUtcSigmaSeconds, t));
	}

	/// <summary>
	/// The uncertainty of a value interpolated between two rows.
	/// </summary>
	/// <param name="before">The earlier row's sigma.</param>
	/// <param name="next">The later row's sigma.</param>
	/// <param name="t">How far between them, 0 to 1.</param>
	/// <returns>The sigma to report.</returns>
	/// <remarks>
	/// <para>
	/// <strong>The larger neighbour, not a blend.</strong> A value interpolated from two rows is
	/// no better known than the worse of them, and a linear blend would claim it is: midway between
	/// a final row and a forecast four times as uncertain it would report two and a half times the
	/// final sigma for a value that is half forecast. The same reasoning makes
	/// <see cref="EarthOrientation.IsPrediction"/> true when either neighbour is a forecast.
	/// </para>
	/// <para>
	/// On a row exactly (<c>t = 1</c>) the row's own sigma is reported, because the row's own
	/// value is what is reported and nothing has been blended. A neighbour with no stated sigma
	/// makes the answer unknown too: <see cref="Math.Max(double, double)"/> carries the NaN
	/// through, which is what it should do.
	/// </para>
	/// </remarks>
	private static double SigmaBetween(double before, double next, double t) =>
		t < 1.0 ? Math.Max(before, next) : next;

	/// <summary>
	/// Reads a sigma column, where an empty field means "not stated" rather than zero.
	/// </summary>
	/// <param name="field">The field.</param>
	/// <returns>The sigma, or <see cref="double.NaN"/> when the field is empty or unreadable.</returns>
	/// <remarks>
	/// The row is kept either way: a value without its uncertainty is still the value, and the
	/// frame transform needs it. What it must not become is a sigma of zero, which is the empty-row
	/// trap again in a smaller font — a legal-looking number claiming the orientation is known
	/// exactly.
	/// </remarks>
	private static double SigmaOrUnknown(string field) =>
		TryParse(field, out double sigma) ? sigma : double.NaN;

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
		bool IsPrediction,
		double PoleXSigmaArcseconds,
		double PoleYSigmaArcseconds,
		double Ut1MinusUtcSigmaSeconds)
	{
		public EarthOrientation ToOrientation() =>
			new(
				PoleXArcseconds,
				PoleYArcseconds,
				Ut1MinusUtcSeconds,
				IsPrediction,
				PoleXSigmaArcseconds,
				PoleYSigmaArcseconds,
				Ut1MinusUtcSigmaSeconds);
	}
}
