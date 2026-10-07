// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

using System;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// The size and shape of an orbit read off an element set, and the class that puts it in.
/// </summary>
/// <remarks>
/// <para>
/// This is a description for sorting and filtering, not an input to anything that propagates. The
/// semi-major axis comes from Kepler's third law applied to the element set's mean motion, which
/// is the Kozai mean motion SGP4 then corrects for J₂. The two differ by a few kilometres in low
/// Earth orbit: nothing for deciding which region an object is in, and far too much for any figure
/// this repository measures.
/// </para>
/// <para>
/// The boundaries are the conventional ones. An orbit is <see cref="OrbitClass.Heo"/> once its
/// eccentricity reaches 0.25, wherever it sits, because a Molniya or a transfer orbit crosses every
/// other region twice a revolution. Otherwise it is <see cref="OrbitClass.Geo"/> between 0.99 and
/// 1.01 revolutions a day, <see cref="OrbitClass.Leo"/> when its apogee is below 2,000 km,
/// <see cref="OrbitClass.Meo"/> when it is above that and faster than geosynchronous, and
/// <see cref="OrbitClass.BeyondGeo"/> when it is slower.
/// </para>
/// </remarks>
/// <param name="SemiMajorAxisKm">The semi-major axis, in kilometres.</param>
/// <param name="PerigeeAltitudeKm">The perigee height above the WGS-72 equatorial radius, in kilometres.</param>
/// <param name="ApogeeAltitudeKm">The apogee height above the WGS-72 equatorial radius, in kilometres.</param>
/// <param name="PeriodMinutes">The orbital period, in minutes.</param>
/// <param name="Class">The region the orbit belongs to.</param>
public readonly record struct OrbitGeometry(
	double SemiMajorAxisKm,
	double PerigeeAltitudeKm,
	double ApogeeAltitudeKm,
	double PeriodMinutes,
	OrbitClass Class)
{
	/// <summary>The altitude below which the whole orbit must lie to be low Earth orbit, in kilometres.</summary>
	public const double LeoCeilingKm = 2000.0;

	/// <summary>The eccentricity at which an orbit counts as highly elliptical.</summary>
	public const double HeoMinimumEccentricity = 0.25;

	/// <summary>The slowest mean motion counted as geosynchronous, in revolutions per day.</summary>
	public const double GeoMinimumMeanMotion = 0.99;

	/// <summary>The fastest mean motion counted as geosynchronous, in revolutions per day.</summary>
	public const double GeoMaximumMeanMotion = 1.01;

	/// <summary>Minutes in a day, which is what mean motion is measured against.</summary>
	private const double MinutesPerDay = 1440.0;

	/// <summary>
	/// Describes the orbit an element set is on.
	/// </summary>
	/// <param name="elements">The element set.</param>
	/// <returns>Its geometry and class.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
	public static OrbitGeometry Of(ElementSet elements)
	{
		Ensure.NotNull(elements);

		return Of(elements.MeanMotion, elements.Eccentricity);
	}

	/// <summary>
	/// Describes the orbit with a given mean motion and eccentricity.
	/// </summary>
	/// <param name="meanMotionRevPerDay">The mean motion, in revolutions per day.</param>
	/// <param name="eccentricity">The eccentricity.</param>
	/// <returns>
	/// Its geometry and class. An element set that is not a closed orbit — a mean motion that is
	/// not positive, or an eccentricity outside [0, 1) — comes back <see cref="OrbitClass.Unclassified"/>
	/// with every length <see cref="double.NaN"/>, rather than throwing, because one malformed row
	/// in a catalogue of thirty thousand should not take the catalogue down with it.
	/// </returns>
	public static OrbitGeometry Of(double meanMotionRevPerDay, double eccentricity)
	{
		if (!(meanMotionRevPerDay > 0.0) || double.IsInfinity(meanMotionRevPerDay) || !(eccentricity >= 0.0) || !(eccentricity < 1.0))
		{
			return new(double.NaN, double.NaN, double.NaN, double.NaN, OrbitClass.Unclassified);
		}

		double radiansPerSecond = meanMotionRevPerDay * 2.0 * Math.PI / 86_400.0;
		double semiMajorAxis = Math.Cbrt(Wgs72<double>.Mu / (radiansPerSecond * radiansPerSecond));
		double radius = Wgs72<double>.RadiusEarthKm;
		double perigee = (semiMajorAxis * (1.0 - eccentricity)) - radius;
		double apogee = (semiMajorAxis * (1.0 + eccentricity)) - radius;

		return new(
			semiMajorAxis,
			perigee,
			apogee,
			MinutesPerDay / meanMotionRevPerDay,
			Classify(meanMotionRevPerDay, eccentricity, apogee));
	}

	/// <summary>
	/// Classifies an orbit by its mean motion and eccentricity.
	/// </summary>
	/// <param name="meanMotionRevPerDay">The mean motion, in revolutions per day.</param>
	/// <param name="eccentricity">The eccentricity.</param>
	/// <returns>The region the orbit belongs to.</returns>
	public static OrbitClass Classify(double meanMotionRevPerDay, double eccentricity) =>
		Of(meanMotionRevPerDay, eccentricity).Class;

	/// <summary>
	/// Applies the boundaries, in the order the remarks give them.
	/// </summary>
	/// <param name="meanMotionRevPerDay">The mean motion, in revolutions per day.</param>
	/// <param name="eccentricity">The eccentricity.</param>
	/// <param name="apogeeAltitudeKm">The apogee altitude, in kilometres.</param>
	/// <returns>The region the orbit belongs to.</returns>
	private static OrbitClass Classify(double meanMotionRevPerDay, double eccentricity, double apogeeAltitudeKm)
	{
		if (eccentricity >= HeoMinimumEccentricity)
		{
			return OrbitClass.Heo;
		}

		if (meanMotionRevPerDay is >= GeoMinimumMeanMotion and <= GeoMaximumMeanMotion)
		{
			return OrbitClass.Geo;
		}

		if (apogeeAltitudeKm < LeoCeilingKm)
		{
			return OrbitClass.Leo;
		}

		return meanMotionRevPerDay > GeoMaximumMeanMotion ? OrbitClass.Meo : OrbitClass.BeyondGeo;
	}
}
