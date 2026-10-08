// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Conjunction;

using System;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// The band of geocentric radius an orbit sweeps, from perigee to apogee: the pre-filter's view of
/// an element set.
/// </summary>
/// <param name="PerigeeRadiusKm">The perigee radius, in kilometres from the Earth's centre.</param>
/// <param name="ApogeeRadiusKm">The apogee radius, in kilometres from the Earth's centre.</param>
/// <remarks>
/// <para>
/// Two objects whose shells are further apart than the screening distance cannot come within it,
/// whatever their planes and phases, because the distance between two points is at least the
/// difference of their distances from the origin. That is the whole of the filter, and it is why it
/// is safe: it can only reject a pair that genuinely cannot meet.
/// </para>
/// <para>
/// Computed in <see langword="double"/> whatever the storage type being screened. The shell decides
/// which pairs are worth propagating, not what a miss distance is, and it is padded by a margin far
/// larger than any rounding — so it is not a place the storage types could disagree in a way that
/// matters.
/// </para>
/// <para>
/// The radii are the mean elements' Keplerian shell. SGP4 adds short-period oscillations of the
/// order of ten kilometres on top of that and drag lowers it over time, so the caller pads the
/// comparison by <see cref="ConjunctionScreenOptions.ShellMarginKm"/> rather than trusting the band
/// to the metre.
/// </para>
/// </remarks>
public readonly record struct OrbitShell(double PerigeeRadiusKm, double ApogeeRadiusKm)
{
	private const double MinutesPerDay = 1440.0;

	/// <summary>Computes the shell of an element set from its mean motion and eccentricity.</summary>
	/// <param name="elements">The element set.</param>
	/// <returns>The shell.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
	/// <remarks>
	/// The semi-major axis comes from the mean motion through the WGS-72 constants the element set
	/// was fitted with, as SGP4 itself recovers it. A mean motion that is not positive yields an
	/// infinite shell that every other shell overlaps, so a pair containing it is never rejected
	/// here — the propagator is the one to refuse it, and to say why.
	/// </remarks>
	public static OrbitShell Of(ElementSet elements)
	{
		Ensure.NotNull(elements);

		double meanMotionRadiansPerMinute = elements.MeanMotion * 2.0 * Math.PI / MinutesPerDay;

		// NaN is spelled out because "<= 0" alone lets it through, and a NaN shell would compare
		// false against every other shell and quietly reject every pair it is in.
		if (double.IsNaN(meanMotionRadiansPerMinute) || meanMotionRadiansPerMinute <= 0.0)
		{
			return new OrbitShell(0.0, double.PositiveInfinity);
		}

		double xke = Wgs72<double>.Xke(DoubleStorageMath.Instance);
		double semiMajorAxisKm = Math.Pow(xke / meanMotionRadiansPerMinute, 2.0 / 3.0) * Wgs72<double>.RadiusEarthKm;

		return new OrbitShell(
			semiMajorAxisKm * (1.0 - elements.Eccentricity),
			semiMajorAxisKm * (1.0 + elements.Eccentricity));
	}

	/// <summary>
	/// Gets the radial gap between two shells: zero when they overlap, otherwise how far the lower
	/// one's apogee falls short of the higher one's perigee.
	/// </summary>
	/// <param name="other">The other shell.</param>
	/// <returns>The gap, in kilometres. Never negative.</returns>
	public double GapTo(OrbitShell other) =>
		Math.Max(0.0, Math.Max(PerigeeRadiusKm, other.PerigeeRadiusKm) - Math.Min(ApogeeRadiusKm, other.ApogeeRadiusKm));
}
