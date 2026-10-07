// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Forces;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Forces;
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
/// </remarks>
public static class HorizonsBodyEphemeris
{
	/// <summary>Builds an interpolating ephemeris from a Horizons table.</summary>
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
		if (table.TimeScale != epochScale)
		{
			throw new ArgumentException(
				$"The table's epochs are {table.TimeScale} and the integration's epoch is {epochScale}.",
				nameof(epochScale));
		}

		List<T> times = new(table.Vectors.Count);
		List<BodyPosition<T>> positions = new(table.Vectors.Count);
		foreach (HorizonsStateVector row in table.Vectors)
		{
			// Whole days and fractions differenced separately, so the 2.46e6 never meets the fraction.
			double days = row.Epoch.Day - epoch.Day + (row.Epoch.DayFraction - epoch.DayFraction);
			times.Add(T.CreateChecked(days * 86400.0));
			positions.Add(new(T.CreateChecked(row.X), T.CreateChecked(row.Y), T.CreateChecked(row.Z)));
		}

		return new TabulatedEphemeris<T>(times, positions, math, points);
	}
}
