// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using ktsu.Osculator.Core.Forces;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Forces;
using ktsu.Osculator.Data.Horizons;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The seam between a Horizons table and the third-body force.
/// </summary>
[TestClass]
public sealed class HorizonsBodyEphemerisTests
{
	[TestMethod]
	public void ATable_BecomesAnEphemeris_InSecondsSinceTheEpoch()
	{
		HorizonsEphemeris table = HorizonsVectorTable.Parse(HorizonsSample.MoonJson);
		JulianDate epoch = table.Vectors[0].Epoch;
		TabulatedEphemeris<double> moon = HorizonsBodyEphemeris.Create(table, epoch, HorizonsTimeScale.Tdb, DoubleStorageMath.Instance, points: 4);

		Assert.AreEqual(0.0, moon.Start);
		Assert.AreEqual((table.Vectors.Count - 1) * 3600.0, moon.End, 1e-3, "hourly rows, to a millisecond");
		for (int i = 0; i < table.Vectors.Count; i++)
		{
			HorizonsStateVector row = table.Vectors[i];
			double seconds = (row.Epoch.Day - epoch.Day + (row.Epoch.DayFraction - epoch.DayFraction)) * 86400.0;
			BodyPosition<double> p = moon.PositionAt(seconds);
			Assert.AreEqual(row.X, p.X, 1e-6);
			Assert.AreEqual(row.Y, p.Y, 1e-6);
			Assert.AreEqual(row.Z, p.Z, 1e-6);
		}

		ThirdBodyGravity<double> pull = new(ThirdBodyGravity<double>.MoonGravitationalParameter, moon, DoubleStorageMath.Instance);
		CartesianAcceleration<double> a = pull.Acceleration(1800.0, new(6778.0, 0.0, 0.0, 0.0, 7.67, 0.0));
		double magnitude = Math.Sqrt((a.X * a.X) + (a.Y * a.Y) + (a.Z * a.Z));
		Assert.IsTrue(magnitude is > 1e-10 and < 2e-9, $"the Moon's tidal pull at LEO is of order 1e-9 km/s²; read {magnitude:E2}");
	}

	[TestMethod]
	public void AnEpochInAnotherTimeScale_IsRefused()
	{
		HorizonsEphemeris table = HorizonsVectorTable.Parse(HorizonsSample.MoonJson);
		Assert.ThrowsExactly<ArgumentException>(() =>
			HorizonsBodyEphemeris.Create(table, table.Vectors[0].Epoch, HorizonsTimeScale.Ut, DoubleStorageMath.Instance, points: 4));
	}
}
