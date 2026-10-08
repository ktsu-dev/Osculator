// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Residuals;

using System;
using System.Collections.Generic;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// How far apart two predictions can be purely because the element set is written down in a
/// fixed number of digits.
/// </summary>
/// <remarks>
/// <para>
/// This is <strong>Δ_data</strong>, the term everything else in this repository is compared
/// against. A TLE carries mean motion to eight decimals, eccentricity to seven and the angles to
/// four, so the element set naming an orbit is one of a band of element sets that would have been
/// written identically. Every member of that band is an equally valid reading of the same
/// observation, and they diverge as they are propagated.
/// </para>
/// <para>
/// <strong>Half a step, not a whole one.</strong> A value written as <c>15.49234213</c> came from a
/// true value somewhere in <c>[15.492342125, 15.492342135)</c>, so the furthest the truth can be
/// from what is written is half of the last digit. Perturbing by a whole step measures the distance
/// between two element sets that were written differently, which is a different and larger quantity.
/// </para>
/// <para>
/// <strong>One field at a time, then combined in quadrature.</strong> The alternative — perturbing
/// every field at once — measures one particular corner of the band and calls it the size of the
/// band. The corners differ from each other by a factor of several, and which corner a
/// simultaneous perturbation lands on depends on sign choices nobody made deliberately. Each
/// field's contribution is measured alone, which is reproducible, and they are summed in
/// quadrature, which is what the band's radius is if the roundings are independent — and they are,
/// being separate digits of separate numbers.
/// </para>
/// </remarks>
public static class DataTerm
{
	/// <summary>
	/// One field's contribution to the data term.
	/// </summary>
	/// <param name="Field">The element field that was perturbed.</param>
	/// <param name="StepSize">The quantization step, in the field's own units.</param>
	/// <param name="PositionKilometers">
	/// How far the propagated position moved when the field was perturbed by half a step.
	/// </param>
	public readonly record struct Contribution(string Field, double StepSize, double PositionKilometers);

	/// <summary>
	/// Measures the data term for one element set at one arc length.
	/// </summary>
	/// <param name="elements">The element set as written.</param>
	/// <param name="minutesSinceEpoch">The arc to propagate over.</param>
	/// <param name="math">The storage type's transcendentals.</param>
	/// <returns>
	/// Each field's contribution, largest first. The combined term is
	/// <see cref="CombineInQuadrature"/> over them.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// The element set does not propagate at this arc, so there is nothing to compare against.
	/// </exception>
	/// <remarks>
	/// <see langword="double"/> rather than the generic storage type, deliberately. The point of
	/// this measurement is to be the yardstick that Δ_arith is held against, and a yardstick whose
	/// own length depends on which storage type was used to measure it is not one. It is also
	/// sound: the term being measured is around 0.06 km, eight orders of magnitude above
	/// <see langword="double"/>'s own arithmetic error, which `StorageComparisonTests` measures.
	/// </remarks>
	public static IReadOnlyList<Contribution> Measure(ElementSet elements, double minutesSinceEpoch, IStorageMath<double> math)
	{
		Ensure.NotNull(elements);
		Ensure.NotNull(math);

		TemeState<double> asWritten = PropagateOrThrow(elements, minutesSinceEpoch, math, "as written");
		ElementFieldSteps steps = ElementFieldQuantization.For(elements);
		List<Contribution> contributions = [];

		foreach ((string field, Func<ElementSet, ElementFieldSteps, double> stepOf, Func<ElementSet, double, ElementSet> nudge) in Perturbations)
		{
			double step = stepOf(elements, steps);
			ElementSet nudged = nudge(elements, step / 2.0);

			// Both predictions are of the same instant, so the arc is measured from each set's own
			// epoch. For every field but the epoch the two epochs agree and this is zero; for the
			// epoch it is the whole perturbation, because SGP4 sees only minutes since epoch outside
			// the deep-space terms, and a shifted epoch at an unshifted arc is the same computation.
			double minutesFromNudgedEpoch = minutesSinceEpoch - MinutesBetween(elements.EpochJulianDate, nudged.EpochJulianDate);
			TemeState<double> perturbed = PropagateOrThrow(nudged, minutesFromNudgedEpoch, math, field);

			contributions.Add(new Contribution(field, step, Separation(asWritten, perturbed)));
		}

		contributions.Sort((a, b) => b.PositionKilometers.CompareTo(a.PositionKilometers));

		return contributions;
	}

	/// <summary>
	/// Combines per-field contributions into one number.
	/// </summary>
	/// <param name="contributions">The contributions, from <see cref="Measure"/>.</param>
	/// <returns>The data term, in kilometres.</returns>
	/// <remarks>
	/// Quadrature rather than a sum, because the fields are separate digits of separate numbers and
	/// their roundings are independent. A plain sum would be the worst case of every rounding
	/// conspiring, which is not what "how well is this orbit known" means.
	/// </remarks>
	public static double CombineInQuadrature(IReadOnlyList<Contribution> contributions)
	{
		Ensure.NotNull(contributions);

		double sumOfSquares = 0.0;

		for (int i = 0; i < contributions.Count; i++)
		{
			sumOfSquares += contributions[i].PositionKilometers * contributions[i].PositionKilometers;
		}

		return System.Math.Sqrt(sumOfSquares);
	}

	/// <summary>
	/// The nine fields SGP4 is handed, each with the step it is written to and how to nudge it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The step is a function of the element set and its format's steps rather than a constant for
	/// two reasons. B* is not written as a fixed decimal: it carries a fixed number of significant
	/// digits and an exponent, so its step is relative, and one absolute number would be meaningless
	/// across the six orders of magnitude the field spans. And the two distributed formats write
	/// eccentricity and B* to different precisions, so which steps apply is a property of where the
	/// element set came from.
	/// </para>
	/// <para>
	/// The epoch is nudged in both of the forms <see cref="ElementSet"/> carries, so the deep-space
	/// solar and lunar terms see the shift as well as the arc does. Half of its step is 432 µs, a
	/// whole number of <see cref="DateTime"/> ticks, so neither form is rounded.
	/// <see cref="ElementSet.MeanMotionDdot"/> is the one written field that is absent, because SGP4
	/// never reads it and there is nothing to nudge.
	/// </para>
	/// </remarks>
	private static readonly (string Field, Func<ElementSet, ElementFieldSteps, double> StepOf, Func<ElementSet, double, ElementSet> Nudge)[] Perturbations =
	[
		(nameof(ElementSet.Epoch), static (_, s) => s.EpochDays,
			static (e, d) => e with
			{
				Epoch = e.Epoch.AddTicks((long)System.Math.Round(d * TimeSpan.TicksPerDay)),
				EpochJulianDate = e.EpochJulianDate with { DayFraction = e.EpochJulianDate.DayFraction + d },
			}),
		(nameof(ElementSet.MeanMotion), static (_, s) => s.MeanMotion,
			static (e, d) => e with { MeanMotion = e.MeanMotion + d }),
		(nameof(ElementSet.Eccentricity), static (_, s) => s.Eccentricity,
			static (e, d) => e with { Eccentricity = e.Eccentricity + d }),
		(nameof(ElementSet.Inclination), static (_, s) => s.Inclination,
			static (e, d) => e with { Inclination = e.Inclination + d }),
		(nameof(ElementSet.RightAscensionOfAscendingNode), static (_, s) => s.RightAscensionOfAscendingNode,
			static (e, d) => e with { RightAscensionOfAscendingNode = e.RightAscensionOfAscendingNode + d }),
		(nameof(ElementSet.ArgumentOfPericenter), static (_, s) => s.ArgumentOfPericenter,
			static (e, d) => e with { ArgumentOfPericenter = e.ArgumentOfPericenter + d }),
		(nameof(ElementSet.MeanAnomaly), static (_, s) => s.MeanAnomaly,
			static (e, d) => e with { MeanAnomaly = e.MeanAnomaly + d }),
		(nameof(ElementSet.MeanMotionDot), static (_, s) => s.MeanMotionDot,
			static (e, d) => e with { MeanMotionDot = e.MeanMotionDot + d }),
		(nameof(ElementSet.BStar), static (e, s) => s.StepForExponentialField(e.BStar),
			static (e, d) => e with { BStar = e.BStar + d }),
	];

	/// <summary>Gets the field names <see cref="Measure"/> perturbs, in table order.</summary>
	/// <remarks>So a test can hold this table against the quantization model and catch a field going missing.</remarks>
	public static IReadOnlyList<string> PerturbedFields { get; } = Array.ConvertAll(Perturbations, static p => p.Field);

	private static double MinutesBetween(JulianDate from, JulianDate to) =>
		(to.Day - from.Day + (to.DayFraction - from.DayFraction)) * 1440.0;

	private static TemeState<double> PropagateOrThrow(ElementSet elements, double minutes, IStorageMath<double> math, string what)
	{
		Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(elements, math);
		Sgp4Result<double> result = Sgp4<double>.Propagate(satellite, minutes, math);

		return result.IsSuccess
			? result.State
			: throw new ArgumentException(
				$"The element set does not propagate to {minutes} minutes ({what}): {result.Error}.", nameof(elements));
	}

	private static double Separation(TemeState<double> a, TemeState<double> b)
	{
		double dx = b.X - a.X;
		double dy = b.Y - a.Y;
		double dz = b.Z - a.Z;

		return System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
	}
}
