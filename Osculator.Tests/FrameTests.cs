// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System.Collections.Generic;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the TEME → Earth-fixed rotation and the geodetic conversion.
/// </summary>
/// <remarks>
/// <para>
/// There are no IERS test vectors in this repository, so the transform is not checked against a
/// published number. It is checked against physics instead, which for this particular transform is
/// stronger than it sounds: a geostationary satellite has to stay over one longitude, and there is
/// essentially no way to get the rotation sense, the sidereal rate or the rotating-frame velocity
/// wrong and still have that come out. A sign error sends it round the planet once a day.
/// </para>
/// <para>
/// Gate 3 in the spec — frame transforms against IERS vectors — is what these do not replace.
/// </para>
/// </remarks>
[TestClass]
public sealed class FrameTests
{
	/// <summary>Catalogue 25954: mean motion 1.00271 rev/day, inclination 0.0004°.</summary>
	private const int GeostationaryCatalogId = 25954;

	private static readonly DoubleStorageMath Math = DoubleStorageMath.Instance;

	[TestMethod]
	public void TheRotationIsAboutThePoleAndChangesNothingElse()
	{
		TemeState<double> teme = new(4821.7, -3112.4, 5908.2, 3.114, 6.402, -1.877);
		JulianDate epoch = JulianDate.FromCalendar(2026, 9, 23, 6, 0, 0.0);

		PefState<double> pef = EarthFixedFrame<double>.ToPef(teme, epoch, EarthOrientation.Ignored, Math);

		Assert.AreEqual(teme.Z, pef.Z, 1e-12, "A rotation about z leaves z alone.");
		Assert.AreEqual(
			System.Math.Sqrt((teme.X * teme.X) + (teme.Y * teme.Y)),
			System.Math.Sqrt((pef.X * pef.X) + (pef.Y * pef.Y)),
			1e-9,
			"And leaves the distance from the axis alone.");
	}

	[TestMethod]
	public void TheRoundTripReturnsWhatWentIn()
	{
		TemeState<double> teme = new(4821.7, -3112.4, 5908.2, 3.114, 6.402, -1.877);
		JulianDate epoch = JulianDate.FromCalendar(2026, 9, 23, 6, 0, 0.0);

		EarthOrientation orientation = new(0.0, 0.0, 0.3, IsPrediction: false);
		PefState<double> pef = EarthFixedFrame<double>.ToPef(teme, epoch, orientation, Math);
		TemeState<double> back = EarthFixedFrame<double>.PefToTeme(pef, epoch, orientation, Math);

		Assert.AreEqual(teme.X, back.X, 1e-9);
		Assert.AreEqual(teme.Y, back.Y, 1e-9);
		Assert.AreEqual(teme.Z, back.Z, 1e-12);
		Assert.AreEqual(teme.VelocityX, back.VelocityX, 1e-12);
		Assert.AreEqual(teme.VelocityY, back.VelocityY, 1e-12);
		Assert.AreEqual(teme.VelocityZ, back.VelocityZ, 1e-12);
	}

	[TestMethod]
	public void AGeostationarySatelliteStaysOverOneLongitude()
	{
		// The test the whole transform exists to pass, and the one a sign error cannot survive:
		// get the rotation sense backwards and this satellite appears to circle the Earth twice a
		// day; drop the sidereal rate to a solar one and it drifts a degree a day.
		Sgp4Satellite<double> satellite = Load(GeostationaryCatalogId, out ElementSet elements);
		List<double> longitudes = [];

		for (double minutes = 0.0; minutes <= 1440.0; minutes += 60.0)
		{
			longitudes.Add(LongitudeDegrees(satellite, elements, minutes));
		}

		double first = longitudes[0];
		double worst = 0.0;

		foreach (double longitude in longitudes)
		{
			worst = System.Math.Max(worst, System.Math.Abs(Wrap(longitude - first)));
		}

		Assert.IsLessThan(1.0, worst,
			$"A geostationary satellite wandered {worst:F3}° of longitude in a day.");
	}

	[TestMethod]
	public void AGeostationarySatelliteIsNearlyAtRestInTheRotatingFrame()
	{
		// What pins the omega-cross-r term. Without it the rotating-frame velocity is just the
		// inertial one re-expressed, which for this satellite is about 3.07 km/s — a factor of a
		// thousand out, and invisible to any test that only looks at position.
		Sgp4Satellite<double> satellite = Load(GeostationaryCatalogId, out ElementSet elements);
		TemeState<double> teme = StateAt(satellite, 120.0);
		PefState<double> pef = EarthFixedFrame<double>.ToPef(
			teme, At(elements, 120.0), EarthOrientation.Ignored, Math);

		double inertialSpeed = Speed(teme.VelocityX, teme.VelocityY, teme.VelocityZ);
		double rotatingSpeed = Speed(pef.VelocityX, pef.VelocityY, pef.VelocityZ);

		Assert.AreEqual(3.07, inertialSpeed, 0.05, "Geostationary inertial speed.");
		Assert.IsLessThan(0.01, rotatingSpeed,
			$"In the rotating frame it should be near rest; it measured {rotatingSpeed:F6} km/s.");
	}

	[TestMethod]
	public void ItsAltitudeIsGeostationaryToo()
	{
		Sgp4Satellite<double> satellite = Load(GeostationaryCatalogId, out ElementSet elements);
		GeodeticPosition<double> position = Geodetic<double>.FromEarthFixed(
			EarthFixedFrame<double>.ToItrf(StateAt(satellite, 0.0), At(elements, 0.0), EarthOrientation.Ignored, Math), Math);

		// 42164 km radius minus the equatorial radius.
		Assert.AreEqual(35786.0, position.AltitudeKilometers, 60.0);
		Assert.AreEqual(0.0, Degrees(position.LatitudeRadians), 0.1, "And sits over the equator.");
	}

	[TestMethod]
	public void GeodeticLatitudeIsNotGeocentricLatitude()
	{
		// Documented on GeodeticPosition as the largest error available in this layer, so it is
		// measured rather than asserted. The geocentric form is what a one-line asin(z / |r|)
		// gives, and it is the single most common defect in amateur ground tracks.
		GeodeticPosition<double> at45 = new(System.Math.PI / 4.0, 0.0, 0.0);
		ItrfState<double> earthFixed = Geodetic<double>.ToEarthFixed(at45, Math);

		double radius = System.Math.Sqrt(
			(earthFixed.X * earthFixed.X) + (earthFixed.Y * earthFixed.Y) + (earthFixed.Z * earthFixed.Z));
		double geocentricDegrees = Degrees(System.Math.Asin(earthFixed.Z / radius));
		double difference = 45.0 - geocentricDegrees;

		Assert.AreEqual(0.192, difference, 0.01,
			$"Geodetic and geocentric latitude differ by {difference:F4}° at 45°.");

		double groundKm = difference * System.Math.PI / 180.0 * 6371.0;

		Assert.IsGreaterThan(20.0, groundKm, $"Which is {groundKm:F1} km on the ground.");
	}

	[TestMethod]
	public void TheGeodeticRoundTripReturnsWhatWentIn()
	{
		foreach ((double latitude, double longitude, double altitude) in Cases())
		{
			GeodeticPosition<double> original = new(Radians(latitude), Radians(longitude), altitude);
			GeodeticPosition<double> back = Geodetic<double>.FromEarthFixed(
				Geodetic<double>.ToEarthFixed(original, Math), Math);

			Assert.AreEqual(latitude, Degrees(back.LatitudeRadians), 1e-9, $"latitude at {latitude}");
			Assert.AreEqual(longitude, Degrees(back.LongitudeRadians), 1e-9, $"longitude at {longitude}");
			Assert.AreEqual(altitude, back.AltitudeKilometers, 1e-7, $"altitude at {latitude}");
		}
	}

	[TestMethod]
	public void IgnoringUt1MinusUtcCostsHundredsOfMetres()
	{
		// Documented on EarthFixedFrame as the larger of the two omitted corrections — forty times
		// the polar motion this layer deliberately leaves out — so it is measured, and the number
		// is what justifies making the offset a required argument rather than an optional one.
		Sgp4Satellite<double> satellite = Load(GeostationaryCatalogId, out ElementSet elements);
		TemeState<double> teme = StateAt(satellite, 0.0);
		JulianDate epoch = At(elements, 0.0);

		PefState<double> withoutOffset = EarthFixedFrame<double>.ToPef(teme, epoch, EarthOrientation.Ignored, Math);
		PefState<double> withOffset = EarthFixedFrame<double>.ToPef(
			teme, epoch, new EarthOrientation(0.0, 0.0, 0.9, IsPrediction: false), Math);

		double shiftKm = System.Math.Sqrt(
			System.Math.Pow(withOffset.X - withoutOffset.X, 2)
			+ System.Math.Pow(withOffset.Y - withoutOffset.Y, 2));

		// At geostationary radius rather than at the equator, so the arc is 6.6 times longer than
		// the 415 m quoted for a point on the ground. Both follow from the same 0.9 seconds.
		double atEquator = shiftKm * 6378.137 / 42164.0;

		Assert.AreEqual(0.415, atEquator, 0.02,
			$"0.9 s of rotation is {atEquator * 1000.0:F0} m at the equator.");
	}

	private static IEnumerable<(double Latitude, double Longitude, double Altitude)> Cases()
	{
		yield return (0.0, 0.0, 0.0);
		yield return (45.0, 120.0, 400.0);
		yield return (-45.0, -170.0, 35786.0);
		yield return (89.9, 30.0, 2.0);
		yield return (-89.9, -30.0, 1000.0);
		yield return (60.0, 179.5, -0.4);
	}

	private static double Speed(double x, double y, double z) => System.Math.Sqrt((x * x) + (y * y) + (z * z));

	private static double Degrees(double radians) => radians * 180.0 / System.Math.PI;

	private static double Radians(double degrees) => degrees * System.Math.PI / 180.0;

	private static double Wrap(double degrees)
	{
		double wrapped = degrees % 360.0;

		if (wrapped > 180.0)
		{
			wrapped -= 360.0;
		}

		return wrapped < -180.0 ? wrapped + 360.0 : wrapped;
	}

	private static Sgp4Satellite<double> Load(int catalogId, out ElementSet elements)
	{
		foreach (VerificationSet.Case one in VerificationSet.ReadCases())
		{
			if (one.Elements.NoradCatalogId == catalogId)
			{
				elements = one.Elements;
				return Sgp4<double>.Initialize(elements, Math);
			}
		}

		throw new AssertFailedException($"Catalogue {catalogId} is not in the verification set.");
	}

	private static JulianDate At(ElementSet elements, double minutes)
	{
		JulianDate epoch = elements.EpochJulianDate;
		return new JulianDate(epoch.Day, epoch.DayFraction + (minutes / 1440.0));
	}

	private static TemeState<double> StateAt(Sgp4Satellite<double> satellite, double minutes)
	{
		Sgp4Result<double> result = Sgp4<double>.Propagate(satellite, minutes, Math);
		Assert.IsTrue(result.IsSuccess, $"Propagation failed at {minutes} min: {result.Error}");
		return result.State;
	}

	private static double LongitudeDegrees(Sgp4Satellite<double> satellite, ElementSet elements, double minutes) =>
		Degrees(Geodetic<double>.FromEarthFixed(
			EarthFixedFrame<double>.ToItrf(StateAt(satellite, minutes), At(elements, minutes), EarthOrientation.Ignored, Math),
			Math).LongitudeRadians);
}
