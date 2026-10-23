// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Slr;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Sp3;

/// <summary>
/// Turns a laser-ranging orbit product into the truth samples <see cref="TruthComparison{T}"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// The comparison needs a velocity at every sample, because the truth's velocity is what defines
/// along-track and cross-track. An ILRS SP3 orbit publishes one and it is used as written. A file
/// that publishes positions only — a CPF prediction, or a position-only SP3 — gets one from the
/// tenth-order Lagrange polynomial the positions are interpolated with, differenced a second either
/// side of the epoch. The polynomial is accurate to millimetres inside the table, so the derived
/// velocity is good to a few micrometres per second, and its only use is orienting a basis.
/// </para>
/// <para>
/// A derived velocity needs a point either side, so the first and last positions of such a file are
/// not samples. Nothing is extrapolated to make up for them.
/// </para>
/// </remarks>
public static class SlrTruth
{
	/// <summary>Half the span, in seconds, a derived velocity is differenced over.</summary>
	private const double DifferenceSeconds = 1.0;

	/// <summary>
	/// The truth samples of one satellite in an SP3 orbit.
	/// </summary>
	/// <typeparam name="T">The numeric storage type.</typeparam>
	/// <param name="orbit">The orbit file.</param>
	/// <param name="satellite">The identifier the header declares, such as <c>L51</c>.</param>
	/// <param name="math">The storage type's arithmetic, for the interpolator.</param>
	/// <returns>One sample per epoch the satellite has a position at.</returns>
	/// <exception cref="ArgumentException">The header does not declare that satellite.</exception>
	/// <exception cref="FormatException">The orbit is not on the UTC scale.</exception>
	public static IReadOnlyList<TruthSample<T>> FromSp3<T>(Sp3File orbit, string satellite, IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		Ensure.NotNull(orbit);
		Ensure.NotNull(math);

		// GPS time is 18 s ahead of UTC today: 140 m of along-track error on LAGEOS, and nowhere
		// near what a careless reading of an IGS file would notice in a kilometre-scale residual.
		if (!string.Equals(orbit.TimeSystem, "UTC", StringComparison.OrdinalIgnoreCase))
		{
			throw new FormatException(
				$"The orbit is on the {orbit.TimeSystem} time scale; the comparison reads UTC, and converting is not done here.");
		}

		IReadOnlyList<Sp3Record> records = orbit.RecordsOf(satellite);

		bool everyVelocity = true;

		foreach (Sp3Record record in records)
		{
			everyVelocity &= record.Velocity is not null;
		}

		if (everyVelocity)
		{
			TruthSample<T>[] samples = new TruthSample<T>[records.Count];

			for (int i = 0; i < records.Count; i++)
			{
				Sp3Record r = records[i];
				Sp3Velocity v = r.Velocity!.Value;
				samples[i] = new TruthSample<T>(r.Instant, State<T>(r.X, r.Y, r.Z, v.X, v.Y, v.Z));
			}

			return samples;
		}

		List<(JulianDate Instant, double Seconds, double X, double Y, double Z)> positions = new(records.Count);

		foreach (Sp3Record r in records)
		{
			positions.Add((r.Instant, r.SecondsSinceStart, r.X, r.Y, r.Z));
		}

		return Differenced(positions, math);
	}

	/// <summary>
	/// The truth samples of a CPF prediction.
	/// </summary>
	/// <typeparam name="T">The numeric storage type.</typeparam>
	/// <param name="prediction">The prediction.</param>
	/// <param name="math">The storage type's arithmetic, for the interpolator.</param>
	/// <returns>One sample per position, less the first and the last.</returns>
	public static IReadOnlyList<TruthSample<T>> FromCpf<T>(CpfFile prediction, IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		Ensure.NotNull(prediction);

		List<(JulianDate Instant, double Seconds, double X, double Y, double Z)> positions = new(prediction.Records.Count);

		foreach (CpfRecord r in prediction.Records)
		{
			positions.Add((r.Instant, r.SecondsSinceStart, r.X, r.Y, r.Z));
		}

		return Differenced(positions, math);
	}

	internal static TruthSample<T>[] Differenced<T>(
		List<(JulianDate Instant, double Seconds, double X, double Y, double Z)> positions,
		IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		Ensure.NotNull(math);

		if (positions.Count < Sp3Interpolator<T>.StandardOrder + 1)
		{
			throw new ArgumentException(
				$"{positions.Count} positions are too few to derive velocities from; the interpolator needs {Sp3Interpolator<T>.StandardOrder + 1}.",
				nameof(positions));
		}

		TabulatedPosition<T>[] table = new TabulatedPosition<T>[positions.Count];

		for (int i = 0; i < positions.Count; i++)
		{
			(JulianDate _, double seconds, double x, double y, double z) = positions[i];
			table[i] = new TabulatedPosition<T>(T.CreateChecked(seconds), T.CreateChecked(x), T.CreateChecked(y), T.CreateChecked(z));
		}

		Sp3Interpolator<T> interpolator = new(table, math);
		T h = T.CreateChecked(DifferenceSeconds);
		T span = h + h;
		TruthSample<T>[] samples = new TruthSample<T>[positions.Count - 2];

		for (int i = 1; i < positions.Count - 1; i++)
		{
			TabulatedPosition<T> before = interpolator.At(table[i].Seconds - h);
			TabulatedPosition<T> after = interpolator.At(table[i].Seconds + h);

			samples[i - 1] = new TruthSample<T>(
				positions[i].Instant,
				new ItrfState<T>(
					table[i].X,
					table[i].Y,
					table[i].Z,
					math.ToWorkingPrecision((after.X - before.X) / span),
					math.ToWorkingPrecision((after.Y - before.Y) / span),
					math.ToWorkingPrecision((after.Z - before.Z) / span)));
		}

		return samples;
	}

	private static ItrfState<T> State<T>(double x, double y, double z, double vx, double vy, double vz)
		where T : struct, INumber<T> =>
		new(T.CreateChecked(x), T.CreateChecked(y), T.CreateChecked(z), T.CreateChecked(vx), T.CreateChecked(vy), T.CreateChecked(vz));
}
