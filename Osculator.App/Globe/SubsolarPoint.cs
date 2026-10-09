// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Globe;

using System;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// Where the Sun is overhead, for shading the night side of the globe.
/// </summary>
/// <remarks>
/// <para>
/// The Astronomical Almanac's low-precision solar position, good to about a hundredth of a degree
/// between 1950 and 2050, which is a kilometre on the ground and far below the width of the
/// terminator's own twilight band. <strong>It is for drawing and nothing else</strong>: the
/// propagator has its own solar ephemeris in the deep-space model, and this is deliberately not
/// that one, so that nothing measured can come to depend on a picture.
/// </para>
/// <para>
/// The longitude is the Sun's right ascension less Greenwich sidereal time, taken from
/// <see cref="EarthFixedFrame{T}.SiderealAngle"/> so the shading turns with exactly the Earth the
/// ground tracks are drawn on.
/// </para>
/// </remarks>
internal static class SubsolarPoint
{
	private const double DegreesToRadians = Math.PI / 180.0;

	/// <summary>
	/// The point on the Earth with the Sun at the zenith.
	/// </summary>
	/// <param name="instantUtc">The instant, on the UTC scale.</param>
	/// <returns>Latitude and longitude, in degrees.</returns>
	internal static (double LatitudeDegrees, double LongitudeDegrees) At(DateTime instantUtc)
	{
		JulianDate date = JulianDate.FromUtc(instantUtc);
		double n = date.Day - 2451545.0 + date.DayFraction;

		double meanLongitude = 280.460 + (0.9856474 * n);
		double meanAnomaly = (357.528 + (0.9856003 * n)) * DegreesToRadians;
		double eclipticLongitude = (meanLongitude + (1.915 * Math.Sin(meanAnomaly)) + (0.020 * Math.Sin(2.0 * meanAnomaly))) * DegreesToRadians;
		double obliquity = (23.439 - (0.0000004 * n)) * DegreesToRadians;

		double rightAscension = Math.Atan2(Math.Cos(obliquity) * Math.Sin(eclipticLongitude), Math.Cos(eclipticLongitude));
		double declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(eclipticLongitude));

		double sidereal = EarthFixedFrame<double>.SiderealAngle(date, 0.0, DoubleStorageMath.Instance);

		return (
			declination / DegreesToRadians,
			MapView.WrapDegrees((rightAscension - sidereal) / DegreesToRadians));
	}

	/// <summary>
	/// The cosine of the Sun's zenith angle at a point: positive by day, negative by night.
	/// </summary>
	/// <param name="latitudeDegrees">The point's latitude, in degrees.</param>
	/// <param name="longitudeDegrees">The point's longitude, in degrees east.</param>
	/// <param name="subsolar">The subsolar point, from <see cref="At"/>.</param>
	/// <returns>The cosine, in [−1, 1].</returns>
	internal static double CosineOfZenith(double latitudeDegrees, double longitudeDegrees, (double LatitudeDegrees, double LongitudeDegrees) subsolar)
	{
		double latitude = latitudeDegrees * DegreesToRadians;
		double sunLatitude = subsolar.LatitudeDegrees * DegreesToRadians;
		double deltaLongitude = (longitudeDegrees - subsolar.LongitudeDegrees) * DegreesToRadians;

		return (Math.Sin(latitude) * Math.Sin(sunLatitude)) + (Math.Cos(latitude) * Math.Cos(sunLatitude) * Math.Cos(deltaLongitude));
	}
}
