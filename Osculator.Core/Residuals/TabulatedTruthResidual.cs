// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Residuals;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// One sample of an SGP4 prediction minus a tabulated precise orbit, every step taken in one
/// storage type.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="SecondsSinceTableStart">The instant, in seconds after the table's first epoch.</param>
/// <param name="MinutesSinceEpoch">The instant, in minutes from the element set's epoch; negative before it.</param>
/// <param name="Predicted">SGP4's state at the instant, in TEME.</param>
/// <param name="Truth">The tabulated orbit at the instant, interpolated and rotated into TEME.</param>
/// <param name="Residual">Prediction minus truth, resolved in the truth's RSW frame.</param>
/// <remarks>
/// Both states are kept, not only their difference, because the point of a series in
/// <see langword="float"/> is that the difference is made of the two states' own rounding. The
/// TEME difference of each component is <c>Predicted − Truth</c>, which <see cref="DeltaX"/> and
/// its siblings give in <typeparamref name="T"/>.
/// </remarks>
public readonly record struct TabulatedTruthSample<T>(
	double SecondsSinceTableStart,
	double MinutesSinceEpoch,
	TemeState<T> Predicted,
	TemeState<T> Truth,
	RswResidual<T> Residual)
	where T : struct, INumber<T>
{
	/// <summary>Gets the TEME x component of prediction minus truth, in kilometres.</summary>
	public T DeltaX => Predicted.X - Truth.X;

	/// <summary>Gets the TEME y component of prediction minus truth, in kilometres.</summary>
	public T DeltaY => Predicted.Y - Truth.Y;

	/// <summary>Gets the TEME z component of prediction minus truth, in kilometres.</summary>
	public T DeltaZ => Predicted.Z - Truth.Z;
}

/// <summary>
/// An SGP4 prediction against a tabulated precise orbit, such as an ILRS laser-ranging SP3 file,
/// sampled as densely as the caller asks and computed end to end in one storage type.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// <strong>This is the vehicle for spec demonstration 2: <see langword="float"/> cannot represent
/// the residual.</strong> A laser-ranging orbit is good to centimetres, but in
/// <see langword="float"/> a position 12,000 km from the geocentre is held to the nearest
/// 2⁻¹⁰ km, about 0.98 m. The prediction and the truth are each rounded to that lattice, so their
/// difference — exact, since the two are close — is a whole number of lattice steps, and a
/// residual sampled every few seconds moves in metre-high stairs where the same computation in
/// <see langword="double"/> moves smoothly. No modelling improvement changes that; it is the
/// storage type.
/// </para>
/// <para>
/// <strong>Every step runs in <typeparamref name="T"/></strong>: SGP4, the Lagrange interpolation
/// of the table, the rotation from the ITRF through polar motion and sidereal time into TEME, the
/// difference and its resolution into RSW. That is the comparison a program storing its states in
/// <typeparamref name="T"/> would make. The two angles of the rotation are evaluated in
/// <see langword="double"/> and converted, as everywhere in the frame layer, and so is the time
/// since epoch: the instant is a shared input, not part of the arithmetic being compared.
/// </para>
/// <para>
/// <strong>The truth is taken to TEME, not the prediction to the ITRF</strong>, and the residual is
/// prediction minus truth in the truth's RSW frame, so a positive along-track component means the
/// prediction is ahead. The table's velocity is interpolated the same way as its position, because
/// the RSW frame needs one and an SP3 file publishing velocities has already supplied it.
/// </para>
/// </remarks>
public static class TabulatedTruthResidual<T>
	where T : struct, INumber<T>
{
	private const double SecondsPerDay = 86400.0;
	private const double MinutesPerDay = 1440.0;

	/// <summary>
	/// Propagates an element set to each sample instant and differences it against the table.
	/// </summary>
	/// <param name="elements">The element set to test.</param>
	/// <param name="positions">The table's positions in the ITRF, in kilometres, timed in seconds after <paramref name="tableStart"/>.</param>
	/// <param name="velocities">
	/// The table's velocities in the ITRF, relative to the rotating frame, in km/s, on the same epochs
	/// as <paramref name="positions"/>; carried in <see cref="TabulatedPosition{T}"/>'s three components.
	/// </param>
	/// <param name="tableStart">The instant the table's seconds are counted from, on the UTC scale.</param>
	/// <param name="sampleSeconds">The instants to sample, in seconds after <paramref name="tableStart"/>, inside the table.</param>
	/// <param name="orientation">The Earth's orientation at an instant on the UTC scale.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>One sample per instant, in the order given.</returns>
	/// <exception cref="ArgumentException">The two tables do not share their epochs.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A sample instant is outside the table.</exception>
	/// <exception cref="InvalidOperationException">SGP4 reported an error at one of the instants.</exception>
	public static IReadOnlyList<TabulatedTruthSample<T>> Compute(
		ElementSet elements,
		IReadOnlyList<TabulatedPosition<T>> positions,
		IReadOnlyList<TabulatedPosition<T>> velocities,
		JulianDate tableStart,
		IReadOnlyList<double> sampleSeconds,
		Func<JulianDate, EarthOrientation> orientation,
		IStorageMath<T> math)
	{
		Ensure.NotNull(elements);
		Ensure.NotNull(positions);
		Ensure.NotNull(velocities);
		Ensure.NotNull(sampleSeconds);
		Ensure.NotNull(orientation);
		Ensure.NotNull(math);

		if (positions.Count != velocities.Count)
		{
			throw new ArgumentException($"{positions.Count} positions against {velocities.Count} velocities.", nameof(velocities));
		}

		for (int i = 0; i < positions.Count; i++)
		{
			if (positions[i].Seconds != velocities[i].Seconds)
			{
				throw new ArgumentException($"Epoch {i} is at {positions[i].Seconds} s for positions and {velocities[i].Seconds} s for velocities.", nameof(velocities));
			}
		}

		Sp3Interpolator<T> position = new(positions, math);
		Sp3Interpolator<T> velocity = new(velocities, math);
		Sgp4Satellite<T> satellite = Sgp4<T>.Initialize(elements, math);
		TabulatedTruthSample<T>[] samples = new TabulatedTruthSample<T>[sampleSeconds.Count];

		for (int i = 0; i < sampleSeconds.Count; i++)
		{
			double seconds = sampleSeconds[i];
			JulianDate instant = After(tableStart, seconds);
			double minutes = MinutesBetween(elements.EpochJulianDate, instant);

			Sgp4Result<T> predicted = Sgp4<T>.Propagate(satellite, T.CreateChecked(minutes), math);

			if (!predicted.IsSuccess)
			{
				throw new InvalidOperationException(
					$"SGP4 reported {predicted.Error} for object {elements.NoradCatalogId} at {minutes:F3} minutes from its epoch.");
			}

			T at = T.CreateChecked(seconds);
			TabulatedPosition<T> r = position.At(at);
			TabulatedPosition<T> v = velocity.At(at);
			EarthOrientation eop = orientation(instant);
			PefState<T> pef = EarthFixedFrame<T>.ItrfToPef(new ItrfState<T>(r.X, r.Y, r.Z, v.X, v.Y, v.Z), eop, math);
			TemeState<T> truth = EarthFixedFrame<T>.PefToTeme(pef, instant, eop, math);

			samples[i] = new TabulatedTruthSample<T>(
				seconds,
				minutes,
				predicted.State,
				truth,
				RswResidual<T>.Between(truth, predicted.State, math));
		}

		return samples;
	}

	/// <summary>Adds seconds to a two-part Julian date without summing its parts.</summary>
	/// <param name="start">The start.</param>
	/// <param name="seconds">The seconds to add.</param>
	/// <returns>The later instant.</returns>
	private static JulianDate After(JulianDate start, double seconds)
	{
		double fraction = start.DayFraction + (seconds / SecondsPerDay);
		double whole = Math.Floor(fraction);

		return new JulianDate(start.Day + whole, fraction - whole);
	}

	/// <summary>Minutes from one instant to another, differencing the two parts separately.</summary>
	/// <param name="from">The earlier reference.</param>
	/// <param name="to">The instant.</param>
	/// <returns>The minutes; negative when <paramref name="to"/> is before <paramref name="from"/>.</returns>
	private static double MinutesBetween(JulianDate from, JulianDate to) =>
		(to.Day - from.Day + (to.DayFraction - from.DayFraction)) * MinutesPerDay;
}
