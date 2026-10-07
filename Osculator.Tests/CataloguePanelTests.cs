// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// The catalogue panel's model: the orbit classifier, the filtered and ranked rows, and the shared
/// selection. Everything here runs without an ImGui context.
/// </summary>
[TestClass]
public sealed class CataloguePanelTests
{
	private static readonly int[] LeoAndGeoIds = [25544, 41866];

	private static readonly DateTime Epoch = new(2026, 9, 15, 8, 51, 14, DateTimeKind.Utc);

	[TestMethod]
	[DataRow(15.49122235, 0.00049264, OrbitClass.Leo, DisplayName = "ISS")]
	[DataRow(2.00563, 0.0102, OrbitClass.Meo, DisplayName = "GPS")]
	[DataRow(1.00271, 0.0002, OrbitClass.Geo, DisplayName = "Geostationary")]
	[DataRow(1.00271, 0.075, OrbitClass.Geo, DisplayName = "Inclined, eccentric geosynchronous (QZSS)")]
	[DataRow(2.00610, 0.7400, OrbitClass.Heo, DisplayName = "Molniya")]
	[DataRow(2.2700, 0.7300, OrbitClass.Heo, DisplayName = "Geostationary transfer")]
	[DataRow(14.2, 0.30, OrbitClass.Heo, DisplayName = "Eccentric LEO-period orbit is still HEO")]
	[DataRow(0.9852, 0.0010, OrbitClass.BeyondGeo, DisplayName = "Graveyard belt")]
	public void Classify_PutsKnownOrbitsInTheirConventionalClass(double meanMotion, double eccentricity, OrbitClass expected) =>
		Assert.AreEqual(expected, OrbitGeometry.Classify(meanMotion, eccentricity));

	[TestMethod]
	[DataRow(0.0, 0.001)]
	[DataRow(-1.0, 0.001)]
	[DataRow(double.NaN, 0.001)]
	[DataRow(double.PositiveInfinity, 0.001)]
	[DataRow(15.0, 1.0)]
	[DataRow(15.0, 1.5)]
	[DataRow(15.0, -0.1)]
	[DataRow(15.0, double.NaN)]
	public void Of_ReturnsUnclassifiedRatherThanThrowing_ForAnythingThatIsNotAClosedOrbit(double meanMotion, double eccentricity)
	{
		OrbitGeometry geometry = OrbitGeometry.Of(meanMotion, eccentricity);

		Assert.AreEqual(OrbitClass.Unclassified, geometry.Class);
		Assert.IsTrue(double.IsNaN(geometry.SemiMajorAxisKm));
	}

	[TestMethod]
	public void Of_PutsTheLeoCeilingAtTwoThousandKilometresOfApogee()
	{
		// Circular orbits a kilometre either side of the ceiling, so apogee is the altitude itself.
		double below = MeanMotionAtAltitude(OrbitGeometry.LeoCeilingKm - 1.0);
		double above = MeanMotionAtAltitude(OrbitGeometry.LeoCeilingKm + 1.0);

		Assert.AreEqual(OrbitClass.Leo, OrbitGeometry.Classify(below, 0.0));
		Assert.AreEqual(OrbitClass.Meo, OrbitGeometry.Classify(above, 0.0));
		Assert.AreEqual(OrbitGeometry.LeoCeilingKm - 1.0, OrbitGeometry.Of(below, 0.0).ApogeeAltitudeKm, 1e-6);
	}

	[TestMethod]
	public void Of_ClassifiesByApogee_SoAnOrbitThatLeavesLeoIsNotLeo()
	{
		// Perigee at 500 km, apogee well above the ceiling, and nowhere near eccentric enough for HEO.
		double semiMajorAxis = Wgs72<double>.RadiusEarthKm + 1700.0;
		double eccentricity = 1200.0 / semiMajorAxis;
		OrbitGeometry geometry = OrbitGeometry.Of(MeanMotionAtSemiMajorAxis(semiMajorAxis), eccentricity);

		Assert.AreEqual(500.0, geometry.PerigeeAltitudeKm, 1e-6);
		Assert.AreEqual(2900.0, geometry.ApogeeAltitudeKm, 1e-6);
		Assert.AreEqual(OrbitClass.Meo, geometry.Class);
	}

	[TestMethod]
	public void Of_GivesTheGeostationaryRadiusForOneRevolutionPerSiderealDay()
	{
		// One sidereal day is 1436.0682 minutes. The geostationary radius is 42,164 km; WGS-72's mu
		// differs from the modern value in the sixth digit, which is well inside a kilometre here.
		OrbitGeometry geometry = OrbitGeometry.Of(1440.0 / 1436.0682, 0.0);

		Assert.AreEqual(42_164.0, geometry.SemiMajorAxisKm, 1.0);
		Assert.AreEqual(1436.0682, geometry.PeriodMinutes, 1e-9);
		Assert.AreEqual(OrbitClass.Geo, geometry.Class);
	}

	[TestMethod]
	public void Of_PlacesTheIssWhereItFlies()
	{
		OrbitGeometry geometry = OrbitGeometry.Of(Elements(25544, "ISS (ZARYA)", 15.49122235, 0.00049264));

		Assert.IsTrue(geometry.PerigeeAltitudeKm is > 400 and < 430, geometry.PerigeeAltitudeKm.ToString(CultureInfo.InvariantCulture));
		Assert.IsTrue(geometry.ApogeeAltitudeKm is > 400 and < 430, geometry.ApogeeAltitudeKm.ToString(CultureInfo.InvariantCulture));
		Assert.AreEqual(1440.0 / 15.49122235, geometry.PeriodMinutes, 1e-12);
	}

	[TestMethod]
	public void View_WithNoQueryAndNoFilter_ShowsTheWholeCatalogueInItsOwnOrder()
	{
		CatalogueView view = new(SmallCatalogue());

		IReadOnlyList<CatalogueEntry> rows = view.Update(string.Empty, CatalogueView.AllClasses);

		CollectionAssert.AreEqual(view.All.ToList(), rows.ToList());
	}

	[TestMethod]
	public void View_RanksTheObjectWhoseNumberWasTypedFirst()
	{
		CatalogueView view = new(SmallCatalogue());

		IReadOnlyList<CatalogueEntry> rows = view.Update("25544", CatalogueView.AllClasses);

		Assert.AreEqual(25544, rows[0].Elements.NoradCatalogId);
	}

	[TestMethod]
	public void View_RanksTheObjectWhoseNameWasTypedFirst()
	{
		CatalogueView view = new(SmallCatalogue());

		IReadOnlyList<CatalogueEntry> rows = view.Update("molniya", CatalogueView.AllClasses);

		Assert.AreEqual("MOLNIYA 1-93", rows[0].Elements.ObjectName);
	}

	[TestMethod]
	public void View_ToleratesNamesThatRepeat()
	{
		// Fragments share one name by the hundred; the search key must still tell them apart.
		CatalogueEntry[] entries = [.. Enumerable.Range(1, 50).Select(i => new CatalogueEntry(Elements(30000 + i, "FENGYUN 1C DEB", 14.5, 0.01)))];
		CatalogueView view = new(entries);

		IReadOnlyList<CatalogueEntry> rows = view.Update("fengyun", CatalogueView.AllClasses);

		Assert.AreEqual(50, rows.Select(row => row.Elements.NoradCatalogId).Distinct().Count());
	}

	[TestMethod]
	public void View_ShowsOnlyTheClassesTheFilterAllows()
	{
		CatalogueView view = new(SmallCatalogue());
		int leoAndGeo = CatalogueView.MaskOf(OrbitClass.Leo) | CatalogueView.MaskOf(OrbitClass.Geo);

		IReadOnlyList<CatalogueEntry> rows = view.Update(string.Empty, leoAndGeo);

		CollectionAssert.AreEquivalent(LeoAndGeoIds, rows.Select(row => row.Elements.NoradCatalogId).ToArray());
	}

	[TestMethod]
	public void View_AppliesTheFilterAndTheSearchTogether()
	{
		CatalogueView view = new(SmallCatalogue());

		IReadOnlyList<CatalogueEntry> rows = view.Update("25544", CatalogueView.MaskOf(OrbitClass.Geo));

		Assert.IsFalse(rows.Any(row => row.Elements.NoradCatalogId == 25544));
	}

	[TestMethod]
	public void View_CountsEachClassOnce_WhenTheCatalogueLoads()
	{
		CatalogueView view = new(SmallCatalogue());

		Assert.AreEqual(1, view.ClassCounts[(int)OrbitClass.Leo]);
		Assert.AreEqual(1, view.ClassCounts[(int)OrbitClass.Meo]);
		Assert.AreEqual(1, view.ClassCounts[(int)OrbitClass.Geo]);
		Assert.AreEqual(1, view.ClassCounts[(int)OrbitClass.Heo]);
		Assert.AreEqual(Epoch.AddHours(4), view.NewestEpoch);
	}

	[TestMethod]
	public void View_DoesNoWorkOnAFrameWhereNothingChanged_HoweverLargeTheCatalogue()
	{
		// Thirty thousand rows, the size the spec plans for. The frame cost must not depend on it,
		// so a frame that repeats the last query and filter must not rebuild anything.
		CatalogueEntry[] entries = [.. Enumerable.Range(1, 30_000).Select(i => new CatalogueEntry(
			Elements(i, string.Create(CultureInfo.InvariantCulture, $"OBJECT {i}"), 15.0 - (i % 14), 0.001)))];
		CatalogueView view = new(entries);

		IReadOnlyList<CatalogueEntry> first = view.Update("object 2999", CatalogueView.AllClasses);
		int rebuilds = view.Rebuilds;

		for (int frame = 0; frame < 100; frame++)
		{
			Assert.AreSame(first, view.Update("object 2999", CatalogueView.AllClasses));
		}

		Assert.AreEqual(rebuilds, view.Rebuilds);

		view.Update("object 29999", CatalogueView.AllClasses);
		Assert.AreEqual(rebuilds + 1, view.Rebuilds);
	}

	[TestMethod]
	public void Selection_ChangesVersionOnlyWhenTheSelectionChanges()
	{
		ElementSet iss = Elements(25544, "ISS (ZARYA)", 15.49122235, 0.00049264);
		CatalogueSelection.Select(null);
		long before = CatalogueSelection.Version;

		CatalogueSelection.Select(iss);
		CatalogueSelection.Select(iss);

		Assert.AreSame(iss, CatalogueSelection.Selected);
		Assert.AreEqual(before + 1, CatalogueSelection.Version);

		CatalogueSelection.Select(null);
		Assert.IsNull(CatalogueSelection.Selected);
		Assert.AreEqual(before + 2, CatalogueSelection.Version);
	}

	private static CatalogueEntry[] SmallCatalogue() =>
	[
		new(Elements(24792, "GPS BIIR-2 (PRN 13)", 2.00563, 0.0102)),
		new(Elements(25544, "ISS (ZARYA)", 15.49122235, 0.00049264, hours: 4)),
		new(Elements(40296, "MOLNIYA 1-93", 2.00610, 0.7400)),
		new(Elements(41866, "GOES 16", 1.00271, 0.0002)),
	];

	private static ElementSet Elements(int norad, string name, double meanMotion, double eccentricity, int hours = 0) => new()
	{
		ObjectName = name,
		ObjectId = string.Create(CultureInfo.InvariantCulture, $"2000-{norad % 1000:D3}A"),
		NoradCatalogId = norad,
		Epoch = Epoch.AddHours(hours),
		EpochJulianDate = JulianDate.FromUtc(Epoch.AddHours(hours)),
		MeanMotion = meanMotion,
		Eccentricity = eccentricity,
		Inclination = 51.6,
		RightAscensionOfAscendingNode = 0.0,
		ArgumentOfPericenter = 0.0,
		MeanAnomaly = 0.0,
		BStar = 0.0,
	};

	private static double MeanMotionAtAltitude(double altitudeKm) =>
		MeanMotionAtSemiMajorAxis(Wgs72<double>.RadiusEarthKm + altitudeKm);

	private static double MeanMotionAtSemiMajorAxis(double semiMajorAxisKm) =>
		Math.Sqrt(Wgs72<double>.Mu / (semiMajorAxisKm * semiMajorAxisKm * semiMajorAxisKm)) * 86_400.0 / (2.0 * Math.PI);
}
