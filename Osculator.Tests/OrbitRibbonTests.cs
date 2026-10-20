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
/// Covers the globe's orbit ribbon: one revolution placed with the Earth of a single instant.
/// </summary>
/// <remarks>
/// Physics rather than snapshots, like <see cref="GlobeTests"/>. The ribbon has to close where the
/// ground track over the same revolution does not, and the gap it is left with is the node's J2
/// regression and nothing else; a ribbon that rotated each sample by its own instant would be the
/// ground track again, and fail by two thousand kilometres.
/// </remarks>
[TestClass]
public sealed class OrbitRibbonTests
{
	private const string IssLine1 = "1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999";
	private const string IssLine2 = "2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812";

	private static ElementSet Iss => TleParser.Parse(IssLine1, IssLine2, "ISS (ZARYA)");

	/// <summary>
	/// After one period the ribbon is back where it started, to within the node's regression, while
	/// the ground track over the same revolution has been left 23.6° behind by the turning Earth.
	/// </summary>
	/// <remarks>
	/// J2 turns the ISS's node about 0.33° west per revolution, which at 6,800 km from the centre and
	/// 51.6° inclination opens the loop by 28 km, measured. The Earth turns 23.3° in the 92.9 minutes, which
	/// opens the ground track by 2,640.
	/// </remarks>
	[TestMethod]
	public void Ribbon_ClosesAfterOnePeriod()
	{
		GlobeObject iss = new(Iss);
		DateTime start = iss.Elements.Epoch.AddHours(3);
		TimeSpan period = OrbitRibbon.Period(iss.Elements);

		Assert.AreEqual(92.95, period.TotalMinutes, 0.05, "The ISS period.");

		List<TrackPoint> ribbon = OrbitRibbon.Sample(iss.Elements, iss.AsDouble, start);
		Assert.AreEqual(OrbitRibbon.SamplesPerRevolution + 1, ribbon.Count);
		Assert.AreEqual(start + period, ribbon[^1].InstantUtc);

		double ribbonGap = Vector3.Distance(OrbitRibbon.ToCartesian(ribbon[0]), OrbitRibbon.ToCartesian(ribbon[^1]));

		Assert.IsTrue(GroundTrack.TryLocate(iss.Elements, iss.AsDouble, start, out TrackPoint groundStart));
		Assert.IsTrue(GroundTrack.TryLocate(iss.Elements, iss.AsDouble, start + period, out TrackPoint groundEnd));
		double groundGap = Vector3.Distance(OrbitRibbon.ToCartesian(groundStart), OrbitRibbon.ToCartesian(groundEnd));

		Assert.IsTrue(ribbonGap < 40.0, $"The ribbon opens by {ribbonGap:F1} km after one revolution.");
		Assert.IsTrue(groundGap > 2000.0, $"The ground track opens by only {groundGap:F1} km.");

		// Both start at the same place: the ribbon and the track agree at the instant shown.
		Assert.AreEqual(groundStart.LatitudeDegrees, ribbon[0].LatitudeDegrees, 1e-9);
		Assert.AreEqual(groundStart.LongitudeDegrees, ribbon[0].LongitudeDegrees, 1e-9);
	}

	/// <summary>
	/// The ribbon is an orbit: it reaches the inclination on both sides of the equator, and stays at
	/// the station's height all the way round.
	/// </summary>
	[TestMethod]
	public void Ribbon_SpansTheInclinationAtAltitude()
	{
		GlobeObject iss = new(Iss);
		List<TrackPoint> ribbon = OrbitRibbon.Sample(iss.Elements, iss.AsDouble, iss.Elements.Epoch);

		double highest = ribbon.Max(p => p.LatitudeDegrees);
		double lowest = ribbon.Min(p => p.LatitudeDegrees);

		Assert.IsTrue(highest is > 51.0 and < 52.0, $"Highest latitude {highest:F3}°.");
		Assert.IsTrue(lowest is < -51.0 and > -52.0, $"Lowest latitude {lowest:F3}°.");
		Assert.IsTrue(ribbon.All(p => p.AltitudeKilometers is > 380.0 and < 450.0), "The ribbon stays at the station's altitude.");
	}

	/// <summary>
	/// On the globe a point above the surface is drawn outside the disc and stays visible past the
	/// limb, and is hidden only where the Earth stands in front of it.
	/// </summary>
	[TestMethod]
	public void MapView_ShowsHeightOnTheGlobe()
	{
		MapView globe = new(MapProjection.Orthographic, 0.0, 0.0);

		// On the limb, at 1.1 Earth radii: off the disc, to the right, and visible.
		Assert.IsTrue(globe.TryProject(0.0, 90.0, 1.1, out Vector2 limb));
		Assert.AreEqual(0.5 + (0.5 * MapView.DiscFill * 1.1), limb.X, 1e-6);

		// Just past the limb on the far side, but outside the disc's silhouette: still in view.
		Assert.IsTrue(globe.TryProject(0.0, 100.0, 1.1, out _), "A point beyond the limb at height shows.");
		Assert.IsFalse(globe.TryProject(0.0, 100.0, out _), "The same point on the surface is hidden.");

		// Directly behind the Earth: hidden whatever its height.
		Assert.IsFalse(globe.TryProject(0.0, 180.0, 1.1, out _));

		// The map has no height, so a ribbon there lies on the ground track's line.
		MapView map = new(MapProjection.Equirectangular, 0.0, 0.0);
		Assert.IsTrue(map.TryProject(10.0, 20.0, 1.1, out Vector2 high));
		Assert.IsTrue(map.TryProject(10.0, 20.0, out Vector2 low));
		Assert.AreEqual(low, high);
	}

	/// <summary>
	/// On the globe the ribbon stands off the limb, which a ground track never does.
	/// </summary>
	[TestMethod]
	public void Ribbon_ProjectsBeyondTheDiscOnTheGlobe()
	{
		GlobeObject iss = new(Iss);
		List<TrackPoint> ribbon = OrbitRibbon.Sample(iss.Elements, iss.AsDouble, iss.Elements.Epoch);
		MapView globe = new(MapProjection.Orthographic, ribbon[0].LatitudeDegrees, ribbon[0].LongitudeDegrees);

		List<List<Vector2>> lines = OrbitRibbon.Project(globe, ribbon);
		float farthest = lines.SelectMany(l => l).Max(p => Vector2.Distance(p, new Vector2(0.5f, 0.5f)));

		Assert.IsTrue(lines.Count > 0, "Some of the ribbon is visible.");
		Assert.IsTrue(farthest > 0.5f * MapView.DiscFill, $"The ribbon reaches only {farthest:F4} from the centre.");
	}
}
