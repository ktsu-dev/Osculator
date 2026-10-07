// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ktsu.Osculator.App.Globe;
using ktsu.Osculator.Core.Elements;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers what the globe panel draws, everywhere it can be checked without a window: the land
/// mask, the projections, the ground tracks, the Sun, the colour scale and the residuals.
/// </summary>
/// <remarks>
/// The ground-track checks are physics rather than snapshots. An orbit inclined at 51.6° cannot
/// take its ground track further from the equator than that, and the Earth turns under it by a
/// fixed amount per revolution; neither holds if the frame chain feeding the map is wrong.
/// </remarks>
[TestClass]
public sealed class GlobeTests
{
	private const string IssLine1 = "1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999";
	private const string IssLine2 = "2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812";

	private static ElementSet Iss => TleParser.Parse(IssLine1, IssLine2, "ISS (ZARYA)");

	/// <summary>
	/// The compiled-in outlines put land and sea where an atlas does.
	/// </summary>
	[TestMethod]
	public void LandMask_KnowsLandFromSea()
	{
		LandMask mask = LandMask.Default;

		Assert.AreEqual(LandOutlines.RingCount, LandMask.ParseRings(LandOutlines.Rings).Count);

		(string Place, double Latitude, double Longitude)[] land =
		[
			("Paris", 48.85, 2.35),
			("Denver", 39.74, -104.99),
			("the Sahara", 23.0, 10.0),
			("central Australia", -25.0, 134.0),
			("Siberia", 62.0, 100.0),
			("the Amazon", -5.0, -62.0),
			("Antarctica near the pole", -88.0, 45.0),
		];

		(string Place, double Latitude, double Longitude)[] sea =
		[
			("the central Pacific", 0.0, -140.0),
			("the North Atlantic", 30.0, -40.0),
			("the Indian Ocean", -20.0, 80.0),
			("the Southern Ocean", -55.0, 0.0),
			("the Pacific across the antimeridian", 10.0, 180.0),
		];

		foreach ((string place, double latitude, double longitude) in land)
		{
			Assert.IsTrue(mask.IsLand(latitude, longitude), $"{place} should be land.");
		}

		foreach ((string place, double latitude, double longitude) in sea)
		{
			Assert.IsFalse(mask.IsLand(latitude, longitude), $"{place} should be sea.");
		}
	}

	/// <summary>
	/// A ring inside another is a hole, without the data having to say so.
	/// </summary>
	[TestMethod]
	public void LandMask_FillsByTheEvenOddRule()
	{
		LandMask mask = new(2, "-10,-10 10,-10 10,10 -10,10 | -5,-5 5,-5 5,5 -5,5 |");

		Assert.IsTrue(mask.IsLand(7.0, 7.0), "Between the rings is land.");
		Assert.IsFalse(mask.IsLand(0.0, 0.0), "Inside the inner ring is a hole.");
		Assert.IsFalse(mask.IsLand(20.0, 20.0), "Outside both is sea.");
		Assert.IsTrue(mask.IsLand(7.0, 367.0), "Longitude wraps.");
	}

	/// <summary>
	/// Projecting and unprojecting returns the same point, in both projections.
	/// </summary>
	[TestMethod]
	public void MapView_RoundTrips()
	{
		MapView[] views =
		[
			new(MapProjection.Equirectangular, 0.0, 0.0),
			new(MapProjection.Equirectangular, 0.0, 120.0),
			new(MapProjection.Orthographic, 0.0, 0.0),
			new(MapProjection.Orthographic, 51.0, -100.0),
			new(MapProjection.Orthographic, -80.0, 170.0),
		];

		foreach (MapView view in views)
		{
			for (double latitude = -85.0; latitude <= 85.0; latitude += 17.0)
			{
				for (double longitude = -175.0; longitude < 180.0; longitude += 25.0)
				{
					if (!view.TryProject(latitude, longitude, out Vector2 at))
					{
						continue;
					}

					Assert.IsTrue(view.TryUnproject(at, out double backLatitude, out double backLongitude), $"{view} lost ({latitude}, {longitude}).");
					Assert.AreEqual(latitude, backLatitude, 1e-3, $"{view} latitude at ({latitude}, {longitude}).");

					// Scaled by the cosine of the latitude: near a pole a degree of longitude is a short
					// way, and the single-precision image coordinate cannot pin it any closer.
					double eastward = MapView.WrapDegrees(backLongitude - longitude) * Math.Cos(latitude * Math.PI / 180.0);
					Assert.AreEqual(0.0, eastward, 1e-3, $"{view} longitude at ({latitude}, {longitude}).");
				}
			}
		}
	}

	/// <summary>
	/// An orthographic globe shows its centre in the middle and hides the far side.
	/// </summary>
	[TestMethod]
	public void MapView_OrthographicHidesTheFarSide()
	{
		MapView view = new(MapProjection.Orthographic, 30.0, 40.0);

		Assert.IsTrue(view.TryProject(30.0, 40.0, out Vector2 centre));
		Assert.AreEqual(0.5f, centre.X, 1e-6f);
		Assert.AreEqual(0.5f, centre.Y, 1e-6f);

		Assert.IsFalse(view.TryProject(-30.0, -140.0, out _), "The antipode is behind the globe.");
		Assert.IsTrue(view.TryProject(60.0, 40.0, out Vector2 north));
		Assert.IsTrue(north.Y < 0.5f, "North is up.");
		Assert.IsTrue(view.TryProject(30.0, 70.0, out Vector2 east));
		Assert.IsTrue(east.X > 0.5f, "East is right.");
		Assert.IsFalse(view.TryUnproject(new Vector2(0.01f, 0.01f), out _, out _), "The corner is space.");
	}

	/// <summary>
	/// A track crossing the antimeridian on a map is two lines, not one drawn across the world.
	/// </summary>
	[TestMethod]
	public void GroundTrack_BreaksAtTheAntimeridianAndBehindTheGlobe()
	{
		List<TrackPoint> crossing =
		[
			new(default, 0.0, 170.0, 400.0),
			new(default, 1.0, 175.0, 400.0),
			new(default, 2.0, -180.0, 400.0),
			new(default, 3.0, -175.0, 400.0),
			new(default, 4.0, -170.0, 400.0),
		];

		List<List<Vector2>> map = GroundTrack.Project(new MapView(MapProjection.Equirectangular, 0.0, 0.0), [crossing]);
		Assert.AreEqual(2, map.Count);
		Assert.IsTrue(map.SelectMany(line => line.Zip(line.Skip(1))).All(pair => MathF.Abs(pair.First.X - pair.Second.X) < 0.5f));

		List<TrackPoint> passingBehind =
		[
			new(default, 0.0, -60.0, 400.0),
			new(default, 0.0, -30.0, 400.0),
			new(default, 0.0, 120.0, 400.0),
			new(default, 0.0, 30.0, 400.0),
			new(default, 0.0, 60.0, 400.0),
		];

		List<List<Vector2>> globe = GroundTrack.Project(new MapView(MapProjection.Orthographic, 0.0, 0.0), [passingBehind]);
		Assert.AreEqual(2, globe.Count, "The hidden sample splits the line.");
	}

	/// <summary>
	/// The ISS's ground track reaches its inclination and no further.
	/// </summary>
	/// <remarks>
	/// Geodetic latitude runs a little past the inclination, because the inclination is a
	/// geocentric angle and the geodetic latitude under a point at 51.6° geocentric is about a fifth
	/// of a degree higher. A track drawn from geocentric latitude would peak at the inclination
	/// itself; one drawn in the wrong frame would not peak anywhere near it.
	/// </remarks>
	[TestMethod]
	public void GroundTrack_PeaksAtTheInclination()
	{
		GlobeObject iss = new(Iss);
		List<List<TrackPoint>> runs = GroundTrack.Sample(
			iss.Elements, iss.AsDouble, iss.Elements.Epoch, TimeSpan.Zero, TimeSpan.FromMinutes(190), TimeSpan.FromSeconds(20));

		Assert.AreEqual(1, runs.Count);
		double highest = runs[0].Max(p => Math.Abs(p.LatitudeDegrees));

		Assert.IsTrue(highest is > 51.6 and < 52.0, $"Highest latitude {highest:F3}°.");
		Assert.IsTrue(runs[0].All(p => p.AltitudeKilometers is > 380.0 and < 450.0), "The ISS stays at its altitude.");
	}

	/// <summary>
	/// Each ascending node falls about 23.6° west of the one before.
	/// </summary>
	/// <remarks>
	/// The Earth turns 0.2507° per minute under a 92.9-minute orbit, and J2 drags the node another
	/// third of a degree west each revolution. A sign error in the sidereal rotation moves the
	/// track east instead; omitting it leaves every node on top of the last.
	/// </remarks>
	[TestMethod]
	public void GroundTrack_FallsWestEachRevolution()
	{
		GlobeObject iss = new(Iss);
		List<TrackPoint> track = GroundTrack.Sample(
			iss.Elements, iss.AsDouble, iss.Elements.Epoch, TimeSpan.Zero, TimeSpan.FromHours(6), TimeSpan.FromSeconds(10))[0];

		List<double> nodes = [];
		for (int i = 1; i < track.Count; i++)
		{
			if (track[i - 1].LatitudeDegrees < 0.0 && track[i].LatitudeDegrees >= 0.0)
			{
				double t = -track[i - 1].LatitudeDegrees / (track[i].LatitudeDegrees - track[i - 1].LatitudeDegrees);
				double step = MapView.WrapDegrees(track[i].LongitudeDegrees - track[i - 1].LongitudeDegrees);
				nodes.Add(track[i - 1].LongitudeDegrees + (t * step));
			}
		}

		Assert.IsTrue(nodes.Count >= 3, $"Only {nodes.Count} ascending nodes in six hours.");

		for (int i = 1; i < nodes.Count; i++)
		{
			double shift = MapView.WrapDegrees(nodes[i] - nodes[i - 1]);
			Assert.AreEqual(-23.6, shift, 0.3, $"Node {i} moved {shift:F3}°.");
		}
	}

	/// <summary>
	/// The Sun is overhead at the Tropic of Cancer at the June solstice and on the equator at the
	/// March equinox, and east of Greenwich at noon UTC while the equation of time is negative.
	/// </summary>
	[TestMethod]
	public void SubsolarPoint_FollowsTheSeasons()
	{
		(double solsticeLatitude, _) = SubsolarPoint.At(new DateTime(2026, 6, 21, 8, 24, 0, DateTimeKind.Utc));
		Assert.AreEqual(23.436, solsticeLatitude, 0.02);

		(double equinoxLatitude, _) = SubsolarPoint.At(new DateTime(2026, 3, 20, 14, 46, 0, DateTimeKind.Utc));
		Assert.AreEqual(0.0, equinoxLatitude, 0.02);

		// The equation of time is about −7.5 minutes on 20 March, so the Sun crosses Greenwich at
		// 12:07:30 and is still 1.9° east of it at noon.
		(_, double noonLongitude) = SubsolarPoint.At(new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc));
		Assert.AreEqual(1.9, noonLongitude, 0.3);
	}

	/// <summary>
	/// The rasterizer lights the day side, darkens the night side and leaves space beyond the disc.
	/// </summary>
	[TestMethod]
	public void EarthRasterizer_ShadesDayAndNight()
	{
		Assert.AreEqual(1f, EarthRasterizer.Daylight(1.0), 1e-6f);
		Assert.AreEqual(EarthRasterizer.NightBrightness, EarthRasterizer.Daylight(-1.0), 1e-6f);
		Assert.IsTrue(EarthRasterizer.Daylight(-0.05) > EarthRasterizer.Daylight(-0.08), "Twilight ramps.");

		const int width = 72;
		const int height = 36;
		byte[] map = EarthRasterizer.Render(new MapView(MapProjection.Equirectangular, 0.0, 0.0), width, height, LandMask.Default, (0.0, 0.0));
		Assert.AreEqual(width * height * 4, map.Length);

		// Sea at the subsolar point (0°, 0°, the Gulf of Guinea) and at its antipode (0°, 180°).
		static int Offset(int column, int row) => ((row * width) + column) * 4;
		int day = Offset(width / 2, height / 2);
		int night = Offset(0, height / 2);
		Assert.AreEqual(EarthRasterizer.Sea.B, map[day + 2]);
		Assert.AreEqual((byte)(EarthRasterizer.Sea.B * EarthRasterizer.NightBrightness), map[night + 2]);
		Assert.AreEqual(255, map[day + 3]);

		byte[] globe = EarthRasterizer.Render(new MapView(MapProjection.Orthographic, 0.0, 0.0), 32, 32, LandMask.Default, null);
		Assert.AreEqual(EarthRasterizer.Space.R, globe[0]);
		Assert.AreEqual(EarthRasterizer.Space.B, globe[2]);
	}

	/// <summary>
	/// The colour scale is logarithmic, ordered and clamped.
	/// </summary>
	[TestMethod]
	public void ResidualColorScale_IsLogarithmicAndClamped()
	{
		Assert.AreEqual(0.0, ResidualColorScale.Position(0.0));
		Assert.AreEqual(0.0, ResidualColorScale.Position(1e-20));
		Assert.AreEqual(1.0, ResidualColorScale.Position(1e6));
		Assert.AreEqual(ResidualColorScale.Position(1e-6) - ResidualColorScale.Position(1e-7), ResidualColorScale.Position(10.0) - ResidualColorScale.Position(1.0), 1e-12);
		Assert.AreEqual(ResidualColorScale.Unavailable, ResidualColorScale.ColorFor(null));
		Assert.AreEqual(((byte)68, (byte)1, (byte)84), ResidualColorScale.Sample(0.0));
		Assert.AreEqual(((byte)253, (byte)231, (byte)37), ResidualColorScale.Sample(1.0));
	}

	/// <summary>
	/// The residuals the globe colours by are the ones the storage comparison measures:
	/// <see langword="float"/> far off, <see langword="decimal"/> barely distinguishable.
	/// </summary>
	[TestMethod]
	public void GlobeObject_ResidualsSeparateTheStorageTypes()
	{
		GlobeObject iss = new(Iss);
		DateTime dayLater = iss.Elements.Epoch.AddDays(1);

		double? asFloat = iss.ResidualKilometers(GlobeResidual.FloatVersusDouble, dayLater);
		double? asDecimal = iss.ResidualKilometers(GlobeResidual.DecimalVersusDouble, dayLater);

		Assert.IsNotNull(asFloat);
		Assert.IsNotNull(asDecimal);
		Console.WriteLine($"ISS one day out: float {asFloat:E3} km, decimal {asDecimal:E3} km");
		Assert.IsTrue(asFloat > 1e-3, $"float residual {asFloat} km.");
		Assert.IsTrue(asDecimal < 1e-6, $"decimal residual {asDecimal} km.");
	}
}
