// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Frames;

using System;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// A point on or above the Earth's surface, in geodetic coordinates.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="LatitudeRadians">Geodetic latitude, positive north, in [−π/2, π/2].</param>
/// <param name="LongitudeRadians">Longitude, positive east of Greenwich, in (−π, π].</param>
/// <param name="AltitudeKilometers">Height above the reference ellipsoid, in kilometres.</param>
/// <remarks>
/// <strong>Geodetic latitude, not geocentric.</strong> The two differ by up to about 0.19 degrees
/// at mid-latitudes, which is <strong>21 kilometres</strong> on the ground — the largest error
/// available anywhere in this layer, and the one most often shipped, because the geocentric form
/// is the one that falls out of <c>asin(z / |r|)</c> in a single line.
/// </remarks>
public readonly record struct GeodeticPosition<T>(T LatitudeRadians, T LongitudeRadians, T AltitudeKilometers)
	where T : struct, INumber<T>;

/// <summary>
/// Converts between the ITRF and geodetic coordinates on the WGS-84 ellipsoid.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// <strong>WGS-84 here, while the propagator runs on WGS-72, and both are right.</strong> The
/// WGS-72 constants in <see cref="Wgs72{T}"/> are part of SGP4's curve fit and changing them makes
/// the orbit worse. The ellipsoid is a different question — it is the shape a latitude is measured
/// against, nothing to do with the fit — and WGS-84 is what every map, every GPS receiver and
/// every ground station uses. Using the WGS-72 ellipsoid instead would shift altitudes by about
/// <strong>2 metres</strong> and report latitudes against a surface nobody else is using.
/// </para>
/// <para>
/// <strong>The ITRF and not the PEF</strong>, because that is the frame a geodetic latitude is
/// defined against — the crust, not the instantaneous rotation axis. Feeding a
/// <see cref="PefState{T}"/> here would be wrong by the polar-motion term, about 12 m, and the
/// type system now refuses it.
/// </para>
/// <para>
/// The latitude solve is Newton's method rather than the textbook fixed-point iteration. The
/// fixed point gains about 2.5 digits a step, so at a working precision of 80 digits it needed
/// more steps than the ceiling allowed and threw on a latitude that was converging, just slowly —
/// at exactly the precisions used to show the reference computation has converged. Newton doubles
/// its digits each step, so a tenfold rise in precision costs about three more steps.
/// <see cref="MaximumIterations"/> is still a ceiling that throws rather than a budget that
/// returns whatever it reached — an unconverged latitude is a wrong answer, not an imprecise one.
/// </para>
/// </remarks>
public static class Geodetic<T>
	where T : struct, INumber<T>
{
	/// <summary>Gets the WGS-84 semi-major axis, in kilometres.</summary>
	public static T SemiMajorAxisKm { get; } = T.CreateChecked(6378.137);

	/// <summary>Gets the WGS-84 flattening.</summary>
	public static T Flattening { get; } = T.CreateChecked(1.0 / 298.257223563);

	/// <summary>Gets the WGS-84 first eccentricity squared.</summary>
	public static T EccentricitySquared { get; } = Flattening * (T.CreateChecked(2) - Flattening);

	/// <summary>How many latitude iterations are allowed before the answer is called wrong.</summary>
	/// <remarks>
	/// Newton's method doubles the correct digits each step from a seed good to about three, so
	/// this is enough for a working precision of hundreds of millions of digits. It is a ceiling
	/// that throws, not a budget.
	/// </remarks>
	public const int MaximumIterations = 30;

	/// <summary>
	/// Below this size a step that fails to shrink is rounding noise rather than divergence: about
	/// 0.2 arcseconds, far above any storage type's resolution of a latitude and far below the
	/// seed's 0.19° error.
	/// </summary>
	private static T StallThresholdRadians { get; } = T.CreateChecked(1e-6);

	/// <summary>
	/// Converts an Earth-fixed position to geodetic latitude, longitude and altitude.
	/// </summary>
	/// <param name="state">The ITRF state. Only its position is read.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The geodetic position.</returns>
	/// <exception cref="ArithmeticException">The latitude iteration did not settle.</exception>
	public static GeodeticPosition<T> FromEarthFixed(ItrfState<T> state, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T equatorialDistance = math.Sqrt((state.X * state.X) + (state.Y * state.Y));
		T longitude = math.Atan2(state.Y, state.X);

		// Seeded with the geocentric latitude, which is the answer at the equator and at the poles
		// and wrong by up to 0.19 degrees in between.
		T latitude = math.Atan2(state.Z, equatorialDistance);
		T previousStep = T.Zero;
		bool converged = false;

		for (int i = 0; i < MaximumIterations; i++)
		{
			T step = math.ToWorkingPrecision(NewtonStep(equatorialDistance, state.Z, latitude, math));
			T next = math.ToWorkingPrecision(latitude - step);

			if (next == latitude)
			{
				converged = true;
				break;
			}

			// Newton's step shrinks quadratically until it reaches the storage type's rounding, and
			// then it stops shrinking and wanders. A step no smaller than the last one, once both are
			// already tiny, is that floor rather than a divergence, and the latitude before it is
			// the answer. Requiring next == latitude instead never terminates for a type whose last
			// digit flips back and forth.
			T size = T.Abs(step);

			if (i > 0 && size >= previousStep && size < StallThresholdRadians)
			{
				converged = true;
				break;
			}

			previousStep = size;
			latitude = next;
		}

		if (!converged)
		{
			throw new ArithmeticException(
				$"The geodetic latitude did not settle in {MaximumIterations} iterations.");
		}

		T sinLatitude = math.Sin(latitude);
		T radiusOfCurvature = SemiMajorAxisKm / math.Sqrt(T.One - (EccentricitySquared * sinLatitude * sinLatitude));

		return new GeodeticPosition<T>(latitude, longitude, Altitude(state, equatorialDistance, latitude, radiusOfCurvature, math));
	}

	/// <summary>
	/// Converts a geodetic position to an Earth-fixed one.
	/// </summary>
	/// <param name="position">The geodetic position.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The ITRF position, with zero velocity.</returns>
	/// <remarks>
	/// Closed form, unlike the reverse, and that asymmetry is the whole reason the reverse needs an
	/// iteration: going this way the latitude is given, and going back it is what is being solved
	/// for. Velocity comes back zero because a geodetic position carries none — a ground station is
	/// at rest in this frame by definition.
	/// </remarks>
	public static ItrfState<T> ToEarthFixed(GeodeticPosition<T> position, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T sinLatitude = math.Sin(position.LatitudeRadians);
		T cosLatitude = math.Cos(position.LatitudeRadians);
		T radiusOfCurvature = SemiMajorAxisKm / math.Sqrt(T.One - (EccentricitySquared * sinLatitude * sinLatitude));
		T equatorialDistance = (radiusOfCurvature + position.AltitudeKilometers) * cosLatitude;

		return new ItrfState<T>(
			equatorialDistance * math.Cos(position.LongitudeRadians),
			equatorialDistance * math.Sin(position.LongitudeRadians),
			((radiusOfCurvature * (T.One - EccentricitySquared)) + position.AltitudeKilometers) * sinLatitude,
			T.Zero,
			T.Zero,
			T.Zero);
	}

	/// <summary>
	/// One Newton step on geodetic latitude.
	/// </summary>
	/// <param name="p">Distance from the spin axis, in kilometres.</param>
	/// <param name="z">Height above the equatorial plane, in kilometres.</param>
	/// <param name="latitude">The current estimate.</param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>The amount to subtract from the estimate.</returns>
	/// <remarks>
	/// A point at geodetic latitude φ and height h sits at <c>p = (N + h)·cos φ</c>,
	/// <c>z = (N(1 − e²) + h)·sin φ</c>. Eliminating h leaves
	/// <c>f(φ) = p·sin φ − z·cos φ − e²·N·sin φ·cos φ = 0</c>, with
	/// <c>N′ = N·e²·sin φ·cos φ / (1 − e²·sin²φ)</c>. The derivative is written out rather
	/// than differenced, because a difference quotient would cap the convergence at half the
	/// working precision — the problem this replaced, in a different form.
	/// </remarks>
	private static T NewtonStep(T p, T z, T latitude, IStorageMath<T> math)
	{
		T sin = math.Sin(latitude);
		T cos = math.Cos(latitude);
		T sinCos = sin * cos;
		T weight = T.One - (EccentricitySquared * sin * sin);
		T radiusOfCurvature = SemiMajorAxisKm / math.Sqrt(weight);

		T value = (p * sin) - (z * cos) - (EccentricitySquared * radiusOfCurvature * sinCos);
		T slope = (p * cos) + (z * sin)
			- (EccentricitySquared * radiusOfCurvature * ((cos * cos) - (sin * sin) + (EccentricitySquared * sinCos * sinCos / weight)));

		return value / slope;
	}

	/// <summary>
	/// Height above the ellipsoid.
	/// </summary>
	/// <param name="state">The ITRF state.</param>
	/// <param name="equatorialDistance">Distance from the spin axis, in kilometres.</param>
	/// <param name="latitude">The settled geodetic latitude.</param>
	/// <param name="radiusOfCurvature">The prime vertical radius of curvature at that latitude.</param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>The altitude, in kilometres.</returns>
	/// <remarks>
	/// Two expressions rather than one, because the obvious
	/// <c>equatorialDistance / cos(latitude) − radiusOfCurvature</c> divides by a cosine that goes
	/// to zero over the poles, where it turns a perfectly ordinary overflight into infinity. Near
	/// the poles the sine form is the well-conditioned one, and the crossover at 45 degrees is
	/// where both are equally comfortable.
	/// </remarks>
	private static T Altitude(ItrfState<T> state, T equatorialDistance, T latitude, T radiusOfCurvature, IStorageMath<T> math)
	{
		T sin = math.Sin(latitude);
		T cos = math.Cos(latitude);

		return T.Abs(sin) > T.CreateChecked(0.7071067811865476)
			? (state.Z / sin) - (radiusOfCurvature * (T.One - EccentricitySquared))
			: (equatorialDistance / cos) - radiusOfCurvature;
	}
}
