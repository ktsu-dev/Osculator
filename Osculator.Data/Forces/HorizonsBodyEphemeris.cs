// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Forces;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Forces;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Horizons;

/// <summary>
/// Turns a Horizons vector table into the ephemeris a third-body or radiation-pressure force reads.
/// </summary>
/// <remarks>
/// <para>
/// The force model lives in <c>Osculator.Core</c>, which does no I/O and so cannot know Horizons
/// exists; the client lives here and knows nothing about forces. This is the one place the two
/// meet.
/// </para>
/// <para>
/// The integration's epoch has to be stated in the table's own time scale. A Horizons table is in
/// TDB unless asked otherwise, and reading TDB epochs against a UTC epoch puts the Moon 69 seconds
/// — about seventy kilometres — out of place. So the scale is an argument, checked against the
/// table, rather than an assumption.
/// </para>
/// <para>
/// Horizons vectors are ICRF and an SGP4 state is TEME. <see cref="Create{T}(HorizonsEphemeris, JulianDate, HorizonsTimeScale, IStorageMath{T}, int)"/>
/// copies the rows as they are, for an integration in the ICRF; <see cref="CreateInTeme{T}(HorizonsEphemeris, JulianDate, HorizonsTimeScale, CelestialPoleOffsets, IStorageMath{T}, int)"/>
/// rotates each row into TEME at its own epoch, for an integration started from an SGP4 state.
/// Mixing the two frames puts the Sun and the Moon about 0.36° out of place in 2026 — the
/// precession accumulated since J2000 — and the error grows by 50″ a year.
/// </para>
/// </remarks>
public static class HorizonsBodyEphemeris
{
	/// <summary>Builds an interpolating ephemeris from a Horizons table, in the table's own ICRF axes.</summary>
	/// <typeparam name="T">The numeric storage type.</typeparam>
	/// <param name="table">The geocentric vectors.</param>
	/// <param name="epoch">The integration's epoch, in <paramref name="epochScale"/>.</param>
	/// <param name="epochScale">The time scale <paramref name="epoch"/> is stated in.</param>
	/// <param name="math">The working precision for <typeparamref name="T"/>.</param>
	/// <param name="points">How many samples each interpolation passes through.</param>
	/// <returns>The ephemeris, with its times in seconds since <paramref name="epoch"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="math"/> is null.</exception>
	/// <exception cref="ArgumentException">The table is in a different time scale from the epoch.</exception>
	public static TabulatedEphemeris<T> Create<T>(HorizonsEphemeris table, JulianDate epoch, HorizonsTimeScale epochScale, IStorageMath<T> math, int points = 9)
		where T : struct, INumber<T>
	{
		Ensure.NotNull(table);
		Ensure.NotNull(math);
		CheckScale(table, epochScale);
		return Build(table, epoch, math, points, row => new BodyPosition<T>(T.CreateChecked(row.X), T.CreateChecked(row.Y), T.CreateChecked(row.Z)));
	}

	/// <summary>
	/// Builds an interpolating ephemeris from a Horizons table, with every row rotated into TEME,
	/// ignoring the celestial pole offsets.
	/// </summary>
	/// <typeparam name="T">The numeric storage type.</typeparam>
	/// <param name="table">The geocentric ICRF vectors, in TDB or TT.</param>
	/// <param name="epoch">The integration's epoch, in <paramref name="epochScale"/>.</param>
	/// <param name="epochScale">The time scale <paramref name="epoch"/> is stated in.</param>
	/// <param name="math">The working precision for <typeparamref name="T"/>.</param>
	/// <param name="points">How many samples each interpolation passes through.</param>
	/// <returns>The ephemeris in TEME, with its times in seconds since <paramref name="epoch"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="math"/> is null.</exception>
	/// <exception cref="ArgumentException">
	/// The table is in a different time scale from the epoch, in UT, or not in the ICRF.
	/// </exception>
	public static TabulatedEphemeris<T> CreateInTeme<T>(HorizonsEphemeris table, JulianDate epoch, HorizonsTimeScale epochScale, IStorageMath<T> math, int points = 9)
		where T : struct, INumber<T> =>
		CreateInTeme(table, epoch, epochScale, CelestialPoleOffsets.Ignored, math, points);

	/// <summary>
	/// Builds an interpolating ephemeris from a Horizons table, with every row rotated into TEME at
	/// its own epoch, for an integration started from an SGP4 state.
	/// </summary>
	/// <typeparam name="T">The numeric storage type.</typeparam>
	/// <param name="table">The geocentric ICRF vectors, in TDB or TT.</param>
	/// <param name="epoch">The integration's epoch, in <paramref name="epochScale"/>.</param>
	/// <param name="epochScale">The time scale <paramref name="epoch"/> is stated in.</param>
	/// <param name="offsets">
	/// The IERS celestial pole offsets. One pair serves the whole table: they move by milliarcseconds
	/// over days, which at the Moon's distance is metres, against a pull that changes by one part in
	/// a thousand per hundred kilometres.
	/// </param>
	/// <param name="math">The working precision for <typeparamref name="T"/>.</param>
	/// <param name="points">How many samples each interpolation passes through.</param>
	/// <returns>The ephemeris in TEME, with its times in seconds since <paramref name="epoch"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="math"/> is null.</exception>
	/// <exception cref="ArgumentException">
	/// The table is in a different time scale from the epoch, in UT, or not in the ICRF.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Each row is rotated with <see cref="GcrfFrame{T}.ToTeme(GcrfState{T}, JulianDate, CelestialPoleOffsets, IStorageMath{T})"/>
	/// at its own epoch, so the rotation follows precession and nutation across the table rather
	/// than freezing them at the start. The ICRF and the GCRF share their axes to well under a
	/// milliarcsecond, so a Horizons row is read as a GCRF position.
	/// </para>
	/// <para>
	/// The rotation wants TT. A TDB epoch is used as one directly: the two scales differ by under
	/// 2 ms, which is a few picoradians of precession. A UT table is refused rather than converted,
	/// because the 69 s between UT and TT is a leap-second table this method does not have.
	/// </para>
	/// </remarks>
	public static TabulatedEphemeris<T> CreateInTeme<T>(HorizonsEphemeris table, JulianDate epoch, HorizonsTimeScale epochScale, CelestialPoleOffsets offsets, IStorageMath<T> math, int points = 9)
		where T : struct, INumber<T>
	{
		Ensure.NotNull(table);
		Ensure.NotNull(math);
		CheckScale(table, epochScale);
		if (table.TimeScale == HorizonsTimeScale.Ut)
		{
			throw new ArgumentException("Rotating into TEME needs dynamical time; ask Horizons for a TDB or TT table.", nameof(table));
		}

		if (!string.Equals(table.ReferenceFrame, "ICRF", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException($"The table is in {table.ReferenceFrame}, and only an ICRF table can be rotated into TEME.", nameof(table));
		}

		return Build(table, epoch, math, points, row =>
		{
			GcrfState<T> icrf = new(T.CreateChecked(row.X), T.CreateChecked(row.Y), T.CreateChecked(row.Z), T.Zero, T.Zero, T.Zero);
			TemeState<T> teme = GcrfFrame<T>.ToTeme(icrf, row.Epoch, offsets, math);
			return new BodyPosition<T>(teme.X, teme.Y, teme.Z);
		});
	}

	private static void CheckScale(HorizonsEphemeris table, HorizonsTimeScale epochScale)
	{
		if (table.TimeScale != epochScale)
		{
			throw new ArgumentException(
				$"The table's epochs are {table.TimeScale} and the integration's epoch is {epochScale}.",
				nameof(epochScale));
		}
	}

	private static TabulatedEphemeris<T> Build<T>(HorizonsEphemeris table, JulianDate epoch, IStorageMath<T> math, int points, Func<HorizonsStateVector, BodyPosition<T>> position)
		where T : struct, INumber<T>
	{
		List<T> times = new(table.Vectors.Count);
		List<BodyPosition<T>> positions = new(table.Vectors.Count);
		foreach (HorizonsStateVector row in table.Vectors)
		{
			// Whole days and fractions differenced separately, so the 2.46e6 never meets the fraction.
			double days = row.Epoch.Day - epoch.Day + (row.Epoch.DayFraction - epoch.DayFraction);
			times.Add(T.CreateChecked(days * 86400.0));
			positions.Add(position(row));
		}

		return new TabulatedEphemeris<T>(times, positions, math, points);
	}
}
