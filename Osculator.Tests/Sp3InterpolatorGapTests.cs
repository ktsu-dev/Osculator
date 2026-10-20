// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Data.Sp3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// An SP3 table with epochs missing from its middle: the interpolator refuses inside the gap and
/// reads each side of it as a table of its own.
/// </summary>
/// <remarks>
/// The parser drops every epoch a file marks missing, so a gap reaches the interpolator as nothing
/// more than one long step. These tests make gaps by removing blocks of epochs from the real IGS and
/// ILRS excerpts, which is exactly what such a file looks like after parsing.
/// </remarks>
[TestClass]
public sealed class Sp3InterpolatorGapTests
{
	/// <summary>
	/// GPS within half a window of an end, and so of a gap: the bound gate 4 uses at the table's ends,
	/// where it measured 135 mm. Beside a gap this suite measured 225 mm, because holding an epoch out
	/// there doubles a step at the very place the window is already off-centre.
	/// </summary>
	private const double GpsNearEndBoundKm = 3e-4;

	[TestMethod]
	public void AnInstantInsideAGapIsRefused_NotInterpolated()
	{
		// Six GPS epochs, ninety minutes, removed from the middle of G02. The instants they were
		// tabulated at are inside the gap, and the right answer to each is known: it is a refusal.
		IReadOnlyList<TabulatedPosition<double>> table = Gps("G02");
		List<TabulatedPosition<double>> gapped = Without(table, 40, 6);
		Sp3Interpolator<double> interpolator = new(gapped, DoubleStorageMath.Instance);

		for (int k = 40; k < 46; k++)
		{
			double seconds = table[k].Seconds;
			Assert.IsFalse(interpolator.Covers(seconds), $"epoch {k}");
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => interpolator.At(seconds), $"epoch {k}");
		}

		// Just inside either edge is inside the gap too.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => interpolator.At(table[39].Seconds + 1.0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => interpolator.At(table[46].Seconds - 1.0));
	}

	[TestMethod]
	public void TheGapsEdgesAreReadFromTheirOwnSides()
	{
		IReadOnlyList<TabulatedPosition<double>> table = Gps("G02");
		Sp3Interpolator<double> interpolator = new(Without(table, 40, 6), DoubleStorageMath.Instance);

		Assert.IsTrue(interpolator.Covers(table[39].Seconds));
		Assert.IsTrue(interpolator.Covers(table[46].Seconds));
		Assert.AreEqual(table[39], interpolator.At(table[39].Seconds));
		Assert.AreEqual(table[46], interpolator.At(table[46].Seconds));
	}

	[TestMethod]
	public void BesideAGap_TheWindowStaysInItsOwnRun_AsAtTheTablesEnds()
	{
		// The decided edge behaviour: within a normal step of a gap the instant still interpolates,
		// and it reads exactly what an interpolator over that run alone reads. The window never
		// reaches across the gap for a node, which would put an hour-wide step inside the polynomial.
		IReadOnlyList<TabulatedPosition<double>> table = Gps("G02");
		Sp3Interpolator<double> gapped = new(Without(table, 40, 6), DoubleStorageMath.Instance);
		Sp3Interpolator<double> before = new([.. table.Take(40)], DoubleStorageMath.Instance);
		Sp3Interpolator<double> after = new([.. table.Skip(46)], DoubleStorageMath.Instance);

		foreach (double fraction in new[] { 0.25, 0.5, 0.75 })
		{
			double lastStepBefore = table[38].Seconds + (fraction * (table[39].Seconds - table[38].Seconds));
			double firstStepAfter = table[46].Seconds + (fraction * (table[47].Seconds - table[46].Seconds));

			Assert.AreEqual(before.At(lastStepBefore), gapped.At(lastStepBefore));
			Assert.AreEqual(after.At(firstStepAfter), gapped.At(firstStepAfter));
		}
	}

	[TestMethod]
	public void BesideAGap_AHeldOutEpochComesBackWithinTheNearEndBound()
	{
		// How good the reading beside a gap is, measured the gate's way: hold out the epoch next to
		// the gap's edge as well and interpolate it back. It stays inside the bound gate 4 states for
		// the table's own ends: centimetres, against the kilometres a window across the gap can cost.
		IReadOnlyList<TabulatedPosition<double>> table = Gps("G02");
		double worst = 0.0;

		foreach (int heldOut in new[] { 38, 47 })
		{
			List<TabulatedPosition<double>> gapped = Without(table, 40, 6);
			gapped.Remove(table[heldOut]);

			TabulatedPosition<double> truth = table[heldOut];
			TabulatedPosition<double> back = new Sp3Interpolator<double>(gapped, DoubleStorageMath.Instance).At(truth.Seconds);
			double dx = back.X - truth.X;
			double dy = back.Y - truth.Y;
			double dz = back.Z - truth.Z;
			worst = Math.Max(worst, Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
		}

		Console.WriteLine($"G02 held out beside a gap: worst {worst * 1e6:F3} mm");
		Assert.IsLessThan(GpsNearEndBoundKm, worst);
	}

	[TestMethod]
	public void OneMissingEpochIsBridged_TwoAreAGap()
	{
		// One missing epoch doubles a step, which is what every held-out-epoch measurement in gate 4
		// interpolates across at millimetres. Two triple it, and that is refused.
		IReadOnlyList<TabulatedPosition<double>> table = Gps("G05");

		Sp3Interpolator<double> one = new(Without(table, 50, 1), DoubleStorageMath.Instance);
		Assert.IsTrue(one.Covers(table[50].Seconds));
		_ = one.At(table[50].Seconds);

		Sp3Interpolator<double> two = new(Without(table, 50, 2), DoubleStorageMath.Instance);
		Assert.IsFalse(two.Covers(table[50].Seconds));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => two.At(table[50].Seconds));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => two.At(table[51].Seconds));
	}

	[TestMethod]
	public void TheNominalSpacingIsTheFilesInterval_GapsOrNot()
	{
		IReadOnlyList<TabulatedPosition<double>> table = Gps("G02");

		Assert.AreEqual(900.0, new Sp3Interpolator<double>(table, DoubleStorageMath.Instance).NominalSpacing);
		Assert.AreEqual(900.0, new Sp3Interpolator<double>(Without(table, 40, 6), DoubleStorageMath.Instance).NominalSpacing);
		Assert.AreEqual(900.0, new Sp3Interpolator<double>(Without(Without(table, 70, 3), 20, 4), DoubleStorageMath.Instance).NominalSpacing);
	}

	[TestMethod]
	public void ALageosGapIsRefused_AndBothSidesStillInterpolate()
	{
		// The laser-ranging orbit at two-minute spacing, with six minutes removed from its middle:
		// fourteen epochs remain on each side, enough for the polynomial in each run.
		Sp3File file = Sp3Parser.Parse(Sp3ParserTests.Lageos);
		IReadOnlyList<TabulatedPosition<double>> table = file.Tabulate<double>("L51");
		Sp3Interpolator<double> interpolator = new(Without(table, 14, 3), DoubleStorageMath.Instance);

		Assert.AreEqual(120.0, interpolator.NominalSpacing);
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => interpolator.At(table[15].Seconds));
		Assert.AreEqual(table[13], interpolator.At(table[13].Seconds));
		Assert.AreEqual(table[17], interpolator.At(table[17].Seconds));
		Assert.IsTrue(interpolator.Covers(table[13].Seconds - 60.0));
		Assert.IsTrue(interpolator.Covers(table[17].Seconds + 60.0));
		Assert.IsFalse(interpolator.Covers(table[13].Seconds + 60.0));
	}

	[TestMethod]
	public void ARunBetweenGapsTooShortForThePolynomialIsRefusedThroughout()
	{
		// Three points survive between two gaps. A tenth-order polynomial cannot be fitted to three
		// points, and borrowing nodes across either gap is the extrapolation this exists to refuse.
		TabulatedPosition<double>[] uniform = new TabulatedPosition<double>[40];

		for (int i = 0; i < uniform.Length; i++)
		{
			uniform[i] = new(i, i, 2.0 * i, 3.0 * i);
		}

		List<TabulatedPosition<double>> table = Without(Without(uniform, 18, 3), 12, 3);
		Sp3Interpolator<double> interpolator = new(table, DoubleStorageMath.Instance);

		foreach (double seconds in new[] { 15.0, 16.0, 16.5, 17.0 })
		{
			Assert.IsFalse(interpolator.Covers(seconds), $"{seconds} s");
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => interpolator.At(seconds), $"{seconds} s");
		}

		// The runs either side are long enough, and a straight line comes back.
		Assert.AreEqual(4.5, interpolator.At(4.5).X, 1e-9);
		Assert.AreEqual(91.5, interpolator.At(30.5).Z, 1e-9);
	}

	private static IReadOnlyList<TabulatedPosition<double>> Gps(string satellite) =>
		Sp3Parser.Parse(Sp3ParserTests.Igs).Tabulate<double>(satellite);

	/// <summary>Removes a block of consecutive epochs, as a file marking them missing would.</summary>
	private static List<TabulatedPosition<double>> Without(IReadOnlyList<TabulatedPosition<double>> table, int start, int count)
	{
		List<TabulatedPosition<double>> result = [.. table];
		result.RemoveRange(start, count);
		return result;
	}
}
