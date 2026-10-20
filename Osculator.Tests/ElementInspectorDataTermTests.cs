// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Data.CelesTrak;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Holds the element inspector against <see cref="DataTerm"/>: every row's step is the one the set's
/// own format writes the field to, which is the step the data term perturbs by, and every field the
/// data term perturbs has a measured row, the epoch included.
/// </summary>
/// <remarks>
/// The OMM set is a committed ISS snapshot read through <see cref="SnapshotStore"/>, as the
/// application reads it; the two-line set is the one the panel shows before anything is selected.
/// </remarks>
[TestClass]
public sealed class ElementInspectorDataTermTests
{
	private const int Iss = 25544;

	private const float HorizonDays = 1.0f;

	private static ElementSet OmmSet()
	{
		ElementSet set = new SnapshotStore(Path.Join(VerificationSet.DataDirectory, "iss-snapshots")).History(Iss)[0];
		Assert.AreEqual(ElementSetFormat.Omm, set.Format, "The committed snapshots are OMM JSON.");
		return set;
	}

	private static ElementSet TleSet() => TleParser.Parse(
		"1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999",
		"2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812",
		"ISS (ZARYA)");

	[TestMethod]
	public void AnOmmSet_ShowsTheJsonsFinerSteps()
	{
		ElementSet omm = OmmSet();

		Assert.AreEqual(1e-8, ElementInspectorPanel.StepOf(omm, nameof(ElementSet.Eccentricity)));
		Assert.AreEqual(ElementFieldQuantization.Omm.StepForExponentialField(omm.BStar), ElementInspectorPanel.StepOf(omm, nameof(ElementSet.BStar)));
		Assert.IsTrue(
			ElementInspectorPanel.StepOf(omm, nameof(ElementSet.BStar)) < ElementFieldQuantization.Tle.StepForExponentialField(omm.BStar) / 100.0,
			"OMM carries three more significant digits of B* than the text.");
	}

	[TestMethod]
	public void ATwoLineSet_ShowsTheTextsSteps()
	{
		ElementSet tle = TleSet();

		Assert.AreEqual(1e-7, ElementInspectorPanel.StepOf(tle, nameof(ElementSet.Eccentricity)));
		Assert.AreEqual(ElementFieldQuantization.Tle.StepForExponentialField(tle.BStar), ElementInspectorPanel.StepOf(tle, nameof(ElementSet.BStar)));
	}

	[TestMethod]
	public void EveryShownStep_IsTheStepTheDataTermPerturbedBy()
	{
		foreach (ElementSet elements in new[] { OmmSet(), TleSet() })
		{
			IReadOnlyList<DataTerm.Contribution> contributions = DataTerm.Measure(elements, HorizonDays * 1440.0, DoubleStorageMath.Instance);

			foreach (DataTerm.Contribution contribution in contributions)
			{
				Assert.AreEqual(
					contribution.StepSize,
					ElementInspectorPanel.StepOf(elements, contribution.Field),
					$"{elements.Format} {contribution.Field}: the row shows a different step from the one its sensitivity was measured with.");
			}
		}
	}

	[TestMethod]
	public void TheMeasuredRows_AreExactlyTheFieldsTheDataTermPerturbs()
	{
		CollectionAssert.AreEquivalent(DataTerm.PerturbedFields.ToList(), ElementInspectorPanel.MeasuredFields.ToList());
	}

	[TestMethod]
	public void TheEpochRow_IsMeasured()
	{
		foreach (ElementSet elements in new[] { OmmSet(), TleSet() })
		{
			ElementInspectorPanel.Measurement measurement = ElementInspectorPanel.Measure(elements, HorizonDays);
			Assert.IsNull(measurement.Error);

			string text = ElementInspectorPanel.Sensitivity(measurement, nameof(ElementSet.Epoch));
			Assert.IsTrue(text.EndsWith(" per step", StringComparison.Ordinal), $"{elements.Format}: epoch row read \"{text}\".");
			Assert.IsTrue(measurement.PerField[nameof(ElementSet.Epoch)] > 0.0);
		}
	}

	[TestMethod]
	public void TheRows_AddUpToTheHeadline()
	{
		foreach (ElementSet elements in new[] { OmmSet(), TleSet() })
		{
			ElementInspectorPanel.Measurement measurement = ElementInspectorPanel.Measure(elements, HorizonDays);

			double sumOfSquares = ElementInspectorPanel.MeasuredFields.Sum(field => measurement.PerField[field] * measurement.PerField[field]);
			double fromRowsMeters = Math.Sqrt(sumOfSquares) * 1000.0;

			Assert.AreEqual(measurement.CombinedMeters, fromRowsMeters, measurement.CombinedMeters * 1e-12, $"{elements.Format}: the rows do not combine to the headline.");
		}
	}
}
