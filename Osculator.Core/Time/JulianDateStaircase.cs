// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Time;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// How a sweep carries the instant it hands the propagator.
/// </summary>
public enum JulianDateMode
{
	/// <summary>
	/// One Julian date, held as a single value of the storage type. In <see langword="double"/> this
	/// is the form that cannot resolve time finer than one unit in the last place of about 2.46 million.
	/// </summary>
	SingleValue,

	/// <summary>
	/// The two-part form: a whole day and a fraction of a day, each a value of the storage type. The
	/// fraction keeps the precision a single value spends on the day count.
	/// </summary>
	TwoPart,
}

/// <summary>
/// One sample of a time sweep.
/// </summary>
/// <param name="RequestedSeconds">The instant asked for, in seconds since the element set's epoch.</param>
/// <param name="PropagatedSeconds">
/// The instant the propagator was actually handed, in seconds since the epoch, after the time had been
/// carried as a Julian date in the storage type. Where the two differ, the Julian date could not hold
/// the request.
/// </param>
/// <param name="AlongTrackMeters">
/// The propagated position's along-track displacement from the sweep's reference state, in metres, or
/// <see cref="double.NaN"/> where the propagator returned an error.
/// </param>
public readonly record struct JulianDateStaircaseSample(double RequestedSeconds, double PropagatedSeconds, double AlongTrackMeters);

/// <summary>
/// Sweeps requested time continuously and reports along-track position, with the time carried as a
/// Julian date in the storage type being measured.
/// </summary>
/// <remarks>
/// <para>
/// This is the computation behind the time inspector. Carried as one <see langword="double"/>, a
/// present-day Julian date is around 2.46 million, which lies between 2^21 and 2^22, so one unit in
/// the last place is 2^-31 of a day: <b>40.2 microseconds</b>. Every request inside one of those
/// treads reaches the propagator as the same instant, and along-track position comes out as a
/// staircase with 31-centimetre risers at low Earth orbital speed. The two-part form, and any type
/// with enough digits to hold the whole date, turn the staircase back into a line.
/// </para>
/// <para>
/// The figure usually quoted, and the one this repository's own documentation used, is 48
/// microseconds. That is the machine epsilon multiplied by the date — a bound on the spacing, not
/// the spacing — and it overstates the tread by the ratio of 2.46 million to 2^21, about 1.17. The
/// tests assert 40.2.
/// </para>
/// <para>
/// Along-track displacement is measured from a reference state propagated at the sweep's first
/// requested instant through the two-part form, in the same storage type, and projected on that
/// state's velocity direction. Using the two-part form for the reference keeps every mode's curve on
/// the same line, so a staircase is seen straddling it rather than offset from it by however far
/// the first request happened to sit inside a tread. The difference is taken in the storage type
/// before anything is converted to <see langword="double"/>, so a type's own precision is what the
/// curve shows.
/// </para>
/// </remarks>
public static class JulianDateStaircase
{
	/// <summary>Seconds in a day.</summary>
	private const double SecondsPerDay = 86400.0;

	/// <summary>Minutes in a day, the propagator's time unit against the Julian date's.</summary>
	private const double MinutesPerDay = 1440.0;

	/// <summary>
	/// Gets the tread of the staircase at an instant: the spacing between adjacent Julian dates a single
	/// <see langword="double"/> can hold there, in seconds.
	/// </summary>
	/// <param name="instant">The instant.</param>
	/// <returns>One unit in the last place of the instant's Julian date as a single <see langword="double"/>, in seconds.</returns>
	public static double TreadSeconds(JulianDate instant)
	{
		double single = instant.Day + instant.DayFraction;
		return (Math.BitIncrement(single) - single) * SecondsPerDay;
	}

	/// <summary>
	/// Propagates an element set across a span of requested time and reports along-track position.
	/// </summary>
	/// <typeparam name="T">The storage type the propagation and the Julian date are carried in.</typeparam>
	/// <param name="elements">The element set.</param>
	/// <param name="startSeconds">The first requested instant, in seconds since the element set's epoch.</param>
	/// <param name="spanSeconds">The length of the sweep, in seconds; must be positive.</param>
	/// <param name="samples">The number of evenly spaced samples, including both ends; at least two.</param>
	/// <param name="mode">How the instant is carried.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>One sample per requested instant, in order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="elements"/> or <paramref name="math"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="spanSeconds"/> is not positive and finite, or <paramref name="samples"/> is below two.
	/// </exception>
	public static IReadOnlyList<JulianDateStaircaseSample> Sweep<T>(
		ElementSet elements,
		double startSeconds,
		double spanSeconds,
		int samples,
		JulianDateMode mode,
		IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		Ensure.NotNull(elements);
		Ensure.NotNull(math);

		if (!double.IsFinite(spanSeconds) || spanSeconds <= 0.0)
		{
			throw new ArgumentOutOfRangeException(nameof(spanSeconds), spanSeconds, "The span must be positive and finite.");
		}

		if (samples < 2)
		{
			throw new ArgumentOutOfRangeException(nameof(samples), samples, "A sweep needs at least two samples.");
		}

		Sgp4Satellite<T> satellite = Sgp4<T>.Initialize(elements, math);
		JulianDate epoch = elements.EpochJulianDate;

		Sgp4Result<T> reference = Sgp4<T>.Propagate(satellite, MinutesSinceEpoch(epoch, startSeconds, JulianDateMode.TwoPart, math), math);
		(double ux, double uy, double uz) = UnitVelocity(reference);

		JulianDateStaircaseSample[] result = new JulianDateStaircaseSample[samples];

		for (int i = 0; i < samples; i++)
		{
			double requested = startSeconds + (spanSeconds * i / (samples - 1));
			T minutes = MinutesSinceEpoch(epoch, requested, mode, math);
			Sgp4Result<T> state = Sgp4<T>.Propagate(satellite, minutes, math);

			double alongTrack = double.NaN;

			if (reference.IsSuccess && state.IsSuccess)
			{
				// Differenced in T first: converting two 7,000 km positions to double and then
				// subtracting would put double's floor under every storage type's curve.
				double dx = ToDouble(math.ToWorkingPrecision(state.State.X - reference.State.X));
				double dy = ToDouble(math.ToWorkingPrecision(state.State.Y - reference.State.Y));
				double dz = ToDouble(math.ToWorkingPrecision(state.State.Z - reference.State.Z));
				alongTrack = ((dx * ux) + (dy * uy) + (dz * uz)) * 1000.0;
			}

			result[i] = new JulianDateStaircaseSample(requested, ToDouble(minutes) * 60.0, alongTrack);
		}

		return result;
	}

	/// <summary>
	/// Carries a requested instant as a Julian date in the storage type and turns it back into the
	/// minutes since epoch the propagator takes.
	/// </summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <param name="epoch">The element set's epoch.</param>
	/// <param name="secondsSinceEpoch">The requested instant.</param>
	/// <param name="mode">How the instant is carried.</param>
	/// <param name="math">The storage type's functions, for its working precision.</param>
	/// <returns>The minutes since epoch, as far as the Julian date could hold them.</returns>
	internal static T MinutesSinceEpoch<T>(JulianDate epoch, double secondsSinceEpoch, JulianDateMode mode, IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		T epochDay = T.CreateChecked(epoch.Day);
		T epochFraction = T.CreateChecked(epoch.DayFraction);
		T offsetDays = math.ToWorkingPrecision(T.CreateChecked(secondsSinceEpoch) / T.CreateChecked(SecondsPerDay));

		T elapsedDays;

		if (mode == JulianDateMode.SingleValue)
		{
			// The whole date in one value, as a caller who never heard of the two-part form would
			// write it. The rounding happens here, on the sum, and the subtraction that follows is
			// exact; so whatever the type cannot hold is lost before the propagator sees it.
			T epochSingle = epochDay + epochFraction;
			T requested = epochSingle + offsetDays;
			elapsedDays = requested - epochSingle;
		}
		else
		{
			T fraction = epochFraction + offsetDays;
			T day = epochDay;
			T carry = T.CreateChecked(Math.Floor(double.CreateChecked(fraction)));

			if (carry != T.Zero)
			{
				day += carry;
				fraction -= carry;
			}

			elapsedDays = day - epochDay + (fraction - epochFraction);
		}

		return math.ToWorkingPrecision(elapsedDays * T.CreateChecked(MinutesPerDay));
	}

	/// <summary>Gets the direction of a state's velocity, in <see langword="double"/>.</summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <param name="state">The state.</param>
	/// <returns>The unit vector, or zero if the propagation failed.</returns>
	private static (double X, double Y, double Z) UnitVelocity<T>(Sgp4Result<T> state)
		where T : struct, INumber<T>
	{
		if (!state.IsSuccess)
		{
			return (0.0, 0.0, 0.0);
		}

		double vx = ToDouble(state.State.VelocityX);
		double vy = ToDouble(state.State.VelocityY);
		double vz = ToDouble(state.State.VelocityZ);
		double speed = Math.Sqrt((vx * vx) + (vy * vy) + (vz * vz));
		return (vx / speed, vy / speed, vz / speed);
	}

	private static double ToDouble<T>(T value)
		where T : struct, INumber<T> => double.CreateChecked(value);
}
