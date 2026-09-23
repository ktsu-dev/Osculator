// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Measures Δ_data — how far apart two predictions can be purely because the element set is
/// written down in a fixed number of digits.
/// </summary>
/// <remarks>
/// This is the yardstick. The repository's central claim is that <see langword="double"/>'s
/// arithmetic error is negligible, and "negligible" is only a claim once there is a number to be
/// negligible against. `StorageComparisonTests` supplies the other side.
/// </remarks>
[TestClass]
public sealed class DataTermTests
{
	[TestMethod]
	public void TheDataTermIsMeasuredAcrossTheVerificationSet()
	{
		List<VerificationSet.Case> cases = VerificationSet.ReadCases();
		StringBuilder report = new();
		report.AppendLine("catalog     1 day      3 days     7 days   dominant field at 7 days");

		List<double> atOneDay = [];
		List<double> atSevenDays = [];
		Dictionary<string, int> dominant = [];

		foreach (VerificationSet.Case one in cases)
		{
			double[] terms = new double[3];
			string leader = "-";
			bool usable = true;

			for (int i = 0; i < 3; i++)
			{
				double minutes = new[] { 1440.0, 4320.0, 10080.0 }[i];

				try
				{
					IReadOnlyList<DataTerm.Contribution> contributions =
						DataTerm.Measure(one.Elements, minutes, DoubleStorageMath.Instance);

					terms[i] = DataTerm.CombineInQuadrature(contributions);
					leader = contributions[0].Field;
				}
				catch (ArgumentException)
				{
					// Decayed, or outside the model's range at this arc. Not every case in the
					// verification set survives a week, and that is not a defect in the measurement.
					usable = false;
					break;
				}
			}

			if (!usable)
			{
				continue;
			}

			atOneDay.Add(terms[0]);
			atSevenDays.Add(terms[2]);
			dominant[leader] = dominant.GetValueOrDefault(leader) + 1;

			report.AppendLine(string.Create(CultureInfo.InvariantCulture,
				$"{one.Elements.NoradCatalogId,7}  {terms[0],9:F3}  {terms[1],9:F3}  {terms[2],9:F3}   {leader}"));
		}

		atOneDay.Sort();
		atSevenDays.Sort();

		report.AppendLine(string.Create(CultureInfo.InvariantCulture,
			$"\nmedian over {atOneDay.Count} usable cases: {atOneDay[atOneDay.Count / 2]:F3} km at 1 day, {atSevenDays[atSevenDays.Count / 2]:F3} km at 7 days"));
		report.AppendLine($"dominant field: {string.Join(", ", dominant)}");
		Console.WriteLine(report);

		Assert.IsGreaterThan(5, atOneDay.Count, "Too few cases survived to say anything.");
	}

	[TestMethod]
	public void TheDataTermGrowsWithTheArcBecauseAMeanMotionErrorIsATimingError()
	{
		// The shape of the growth is the point, not its value. A half-step of mean motion is a
		// slightly wrong orbital period, and a wrong period integrates into a growing along-track
		// offset — so the term grows roughly linearly in the arc rather than staying put.
		ElementSet elements = VerificationSet.ReadCases()[0].Elements;

		double atOneDay = Term(elements, 1440.0);
		double atTwoDays = Term(elements, 2880.0);
		double atFourDays = Term(elements, 5760.0);

		Assert.IsGreaterThan(atOneDay, atTwoDays);
		Assert.IsGreaterThan(atTwoDays, atFourDays);

		double firstDoubling = atTwoDays / atOneDay;
		double secondDoubling = atFourDays / atTwoDays;

		Assert.AreEqual(firstDoubling, secondDoubling, 0.5,
			$"Doubling the arc scales the term by {firstDoubling:F2} then {secondDoubling:F2}; a consistent ratio is what linear growth looks like.");
	}

	[TestMethod]
	public void MeanMotionIsNotTheFieldThatMatters_WhichIsTheOppositeOfTheObviousGuess()
	{
		// Written expecting mean motion to dominate at a week, on the reasoning that it is the one
		// field whose error is a rate and therefore integrates. It does integrate, and it still
		// loses, because the step is so much finer: half a step is 5e-9 rev/day, which over seven
		// days is 3.5e-8 of a revolution — about 1.5 metres of along-track phase on a LEO orbit.
		// Half a step of an angle is 5e-5 degrees, which is already 3 metres of phase at t=0 and
		// stays there. The rate has seven days to catch up and does not manage it.
		//
		// Across the whole usable set, mean motion is the leading field in zero cases out of 27.
		List<VerificationSet.Case> cases = VerificationSet.ReadCases();
		int meanMotionLeads = 0;
		int counted = 0;

		foreach (VerificationSet.Case one in cases)
		{
			IReadOnlyList<DataTerm.Contribution> contributions;

			try
			{
				contributions = DataTerm.Measure(one.Elements, 10080.0, DoubleStorageMath.Instance);
			}
			catch (ArgumentException)
			{
				continue;
			}

			counted++;

			if (contributions[0].Field == "MeanMotion")
			{
				meanMotionLeads++;
			}
		}

		Assert.IsGreaterThan(20, counted);
		Assert.AreEqual(0, meanMotionLeads,
			"Mean motion leading anywhere would be worth understanding; it led nowhere when this was written.");
	}

	[TestMethod]
	public void TwoOfTheTlesFieldsAreDecorativeAsFarAsSgp4IsConcerned()
	{
		// MeanMotionDot and MeanMotionDdot are in every TLE and SGP4 reads neither: all of the drag
		// is carried by B*. They belong to SGP, the older model this one replaced. The perturbation
		// is kept in the table rather than dropped, because a contribution of exactly zero is the
		// evidence — dropping it would leave the fact undocumented and make the table look like a
		// list of fields that matter, which it is not.
		IReadOnlyList<DataTerm.Contribution> contributions =
			DataTerm.Measure(VerificationSet.ReadCases()[0].Elements, 10080.0, DoubleStorageMath.Instance);

		DataTerm.Contribution meanMotionDot = contributions.Single(c => c.Field == "MeanMotionDot");

		Assert.AreEqual(0.0, meanMotionDot.PositionKilometers,
			"Exactly zero, not merely small: the field is never read, so the two propagations are the same computation.");
	}

	[TestMethod]
	public void QuadratureIsNotASumAndIsNotTheLargestTerm()
	{
		IReadOnlyList<DataTerm.Contribution> contributions =
			DataTerm.Measure(VerificationSet.ReadCases()[0].Elements, 1440.0, DoubleStorageMath.Instance);

		double combined = DataTerm.CombineInQuadrature(contributions);
		double plainSum = 0.0;

		foreach (DataTerm.Contribution contribution in contributions)
		{
			plainSum += contribution.PositionKilometers;
		}

		Assert.IsGreaterThan(contributions[0].PositionKilometers, combined, "At least the largest term.");
		Assert.IsLessThan(plainSum, combined, "But less than every rounding conspiring.");
	}

	private static double Term(ElementSet elements, double minutes) =>
		DataTerm.CombineInQuadrature(DataTerm.Measure(elements, minutes, DoubleStorageMath.Instance));
}
