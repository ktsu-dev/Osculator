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
/// Converts between the Earth-fixed frame and geodetic coordinates on the WGS-84 ellipsoid.
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
/// The latitude solve is the standard fixed-point iteration rather than a closed form. It converges
/// in a handful of steps for anything in orbit, and <see cref="MaximumIterations"/> is a ceiling
/// that throws rather than a budget that returns whatever it reached — an unconverged latitude is
/// a wrong answer, not an imprecise one.
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
	public const int MaximumIterations = 30;

	/// <summary>
	/// Converts an Earth-fixed position to geodetic latitude, longitude and altitude.
	/// </summary>
	/// <param name="state">The Earth-fixed state. Only its position is read.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The geodetic position.</returns>
	/// <exception cref="ArithmeticException">The latitude iteration did not settle.</exception>
	public static GeodeticPosition<T> FromEarthFixed(PefState<T> state, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T equatorialDistance = math.Sqrt((state.X * state.X) + (state.Y * state.Y));
		T longitude = math.Atan2(state.Y, state.X);

		// Seeded with the geocentric latitude, which is the answer at the equator and at the poles
		// and wrong by up to 0.19 degrees in between.
		T latitude = math.Atan2(state.Z, equatorialDistance);
		T settled = T.Zero;
		bool converged = false;

		for (int i = 0; i < MaximumIterations; i++)
		{
			T sin = math.Sin(latitude);
			T radiusOfCurvature = SemiMajorAxisKm / math.Sqrt(T.One - (EccentricitySquared * sin * sin));
			T next = math.Atan2(state.Z + (radiusOfCurvature * EccentricitySquared * sin), equatorialDistance);

			if (next == latitude)
			{
				settled = radiusOfCurvature;
				converged = true;
				latitude = next;
				break;
			}

			latitude = next;
			settled = radiusOfCurvature;
		}

		if (!converged)
		{
			throw new ArithmeticException(
				$"The geodetic latitude did not settle in {MaximumIterations} iterations.");
		}

		return new GeodeticPosition<T>(latitude, longitude, Altitude(state, equatorialDistance, latitude, settled, math));
	}

	/// <summary>
	/// Converts a geodetic position to an Earth-fixed one.
	/// </summary>
	/// <param name="position">The geodetic position.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The Earth-fixed position, with zero velocity.</returns>
	/// <remarks>
	/// Closed form, unlike the reverse, and that asymmetry is the whole reason the reverse needs an
	/// iteration: going this way the latitude is given, and going back it is what is being solved
	/// for. Velocity comes back zero because a geodetic position carries none — a ground station is
	/// at rest in this frame by definition.
	/// </remarks>
	public static PefState<T> ToEarthFixed(GeodeticPosition<T> position, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T sinLatitude = math.Sin(position.LatitudeRadians);
		T cosLatitude = math.Cos(position.LatitudeRadians);
		T radiusOfCurvature = SemiMajorAxisKm / math.Sqrt(T.One - (EccentricitySquared * sinLatitude * sinLatitude));
		T equatorialDistance = (radiusOfCurvature + position.AltitudeKilometers) * cosLatitude;

		return new PefState<T>(
			equatorialDistance * math.Cos(position.LongitudeRadians),
			equatorialDistance * math.Sin(position.LongitudeRadians),
			((radiusOfCurvature * (T.One - EccentricitySquared)) + position.AltitudeKilometers) * sinLatitude,
			T.Zero,
			T.Zero,
			T.Zero);
	}

	/// <summary>
	/// Height above the ellipsoid.
	/// </summary>
	/// <param name="state">The Earth-fixed state.</param>
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
	private static T Altitude(PefState<T> state, T equatorialDistance, T latitude, T radiusOfCurvature, IStorageMath<T> math)
	{
		T sin = math.Sin(latitude);
		T cos = math.Cos(latitude);

		return T.Abs(sin) > T.CreateChecked(0.7071067811865476)
			? (state.Z / sin) - (radiusOfCurvature * (T.One - EccentricitySquared))
			: (equatorialDistance / cos) - radiusOfCurvature;
	}
}
