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
/// One position and velocity of an independently determined orbit, in the frame it was published in.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="Instant">The instant, on the UTC scale.</param>
/// <param name="State">The state in the ITRF: kilometres and km/s, velocity relative to the rotating frame.</param>
/// <remarks>
/// The ITRF because that is where every precise orbit this repository reads is tabulated: ILRS and
/// IGS SP3 files, and ILRS predictions. A truth source in an inertial frame would be rotated into the
/// ITRF before it reached here, so the comparison only ever has one way to get to TEME.
/// </remarks>
public readonly record struct TruthSample<T>(JulianDate Instant, ItrfState<T> State)
	where T : struct, INumber<T>;

/// <summary>
/// The difference between an SGP4 prediction and the truth at one instant.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="Instant">The instant compared, on the UTC scale.</param>
/// <param name="MinutesSinceEpoch">
/// How far the instant is from the element set's epoch, in minutes; negative before it.
/// </param>
/// <param name="Residual">SGP4 minus truth, resolved in the truth's own RSW frame.</param>
public readonly record struct TruthResidual<T>(JulianDate Instant, double MinutesSinceEpoch, RswResidual<T> Residual)
	where T : struct, INumber<T>;

/// <summary>
/// Compares an SGP4 prediction against an independently determined orbit: the end-to-end measurement
/// of how wrong a public element set is, which is what the M4 gate reports.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// <strong>The truth is taken to TEME, not the prediction to the ITRF.</strong> SGP4's output is in
/// TEME and <see cref="RswResidual{T}"/> is defined there, so rotating each truth state back through
/// polar motion and sidereal time is the one transform the comparison needs, and every residual is
/// built from states in the same frame. Rotating the other way would give the same position
/// difference, since a rotation preserves it, but the RSW basis would have to come from a velocity
/// in a rotating frame, which is not the orbit's velocity.
/// </para>
/// <para>
/// <strong>The residual is SGP4 minus truth, in the truth's frame.</strong> The truth is the
/// reference, so its position and velocity define radial, along-track and cross-track. A positive
/// along-track residual means the prediction is ahead of the satellite.
/// </para>
/// <para>
/// <strong>The Earth's orientation is a required argument</strong>, for the reason
/// <see cref="EarthFixedFrame{T}"/> gives: UT1 − UTC is worth up to 415 m along-track at the surface,
/// and a gate measuring kilometres should not quietly spend a tenth of its answer on a default. A
/// caller with no bulletin passes <c>_ => EarthOrientation.Ignored</c>, which says so.
/// </para>
/// <para>
/// A propagation that fails is refused rather than skipped. An element set that cannot reach an
/// instant of its own truth arc is not a result to average over.
/// </para>
/// </remarks>
public static class TruthComparison<T>
	where T : struct, INumber<T>
{
	/// <summary>
	/// Propagates an element set to every truth instant and resolves the difference.
	/// </summary>
	/// <param name="elements">The element set to test.</param>
	/// <param name="truth">The independently determined states, in any order.</param>
	/// <param name="orientation">The Earth's orientation at an instant on the UTC scale.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>One residual per truth sample, in the order given.</returns>
	/// <exception cref="InvalidOperationException">SGP4 reported an error at one of the instants.</exception>
	public static IReadOnlyList<TruthResidual<T>> Compare(
		ElementSet elements,
		IReadOnlyList<TruthSample<T>> truth,
		Func<JulianDate, EarthOrientation> orientation,
		IStorageMath<T> math)
	{
		Ensure.NotNull(elements);
		Ensure.NotNull(truth);
		Ensure.NotNull(orientation);
		Ensure.NotNull(math);

		Sgp4Satellite<T> satellite = Sgp4<T>.Initialize(elements, math);
		TruthResidual<T>[] residuals = new TruthResidual<T>[truth.Count];

		for (int i = 0; i < truth.Count; i++)
		{
			TruthSample<T> sample = truth[i];
			double minutes = MinutesBetween(elements.EpochJulianDate, sample.Instant);

			Sgp4Result<T> predicted = Sgp4<T>.Propagate(satellite, T.CreateChecked(minutes), math);

			if (!predicted.IsSuccess)
			{
				throw new InvalidOperationException(
					$"SGP4 reported {predicted.Error} for object {elements.NoradCatalogId} at {minutes:F3} minutes from its epoch.");
			}

			EarthOrientation at = orientation(sample.Instant);
			PefState<T> pef = EarthFixedFrame<T>.ItrfToPef(sample.State, at, math);
			TemeState<T> reference = EarthFixedFrame<T>.PefToTeme(pef, sample.Instant, at, math);

			residuals[i] = new TruthResidual<T>(
				sample.Instant,
				minutes,
				RswResidual<T>.Between(reference, predicted.State, math));
		}

		return residuals;
	}

	/// <summary>
	/// Minutes from one instant to another, keeping the two-part dates apart until the difference.
	/// </summary>
	/// <param name="from">The earlier instant.</param>
	/// <param name="to">The later instant.</param>
	/// <returns>The minutes elapsed; negative when <paramref name="to"/> is the earlier.</returns>
	/// <remarks>
	/// Summing either date first would round it to the 40 µs a single <see langword="double"/> holds a
	/// Julian date to (domain trap 4); differencing the parts first loses nothing a satellite moving at
	/// 5 km/s would notice.
	/// </remarks>
	public static double MinutesBetween(JulianDate from, JulianDate to) =>
		(to.Day - from.Day + (to.DayFraction - from.DayFraction)) * 1440.0;
}
