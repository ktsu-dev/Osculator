// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Threading;
using ktsu.ImGui.App.Testing;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.App.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the Cowell round-off panel: that it measures what <see cref="CowellRoundOffTests"/>
/// measures, and that the measurement reaches the panel through the shell's work queue.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CowellRoundOffPanelTests : IDisposable
{
	/// <summary>The panel's measurement, computed once for the class; the two PreciseNumber runs are the cost.</summary>
	private static readonly Lazy<CowellRoundOffResult> Measured = new(() => CowellRoundOffArc.Measure(new CowellRoundOffProgress(), CancellationToken.None));

	private AppShell? shell;
	private ImGuiAppHarness? harness;

	/// <inheritdoc/>
	public void Dispose()
	{
		if (shell is not null)
		{
			foreach (Panel panel in shell.Registry.All)
			{
				panel.Dismiss();
			}

			shell.Dispose();
		}

		shell = null;
		harness?.Dispose();
		harness = null;
	}

	[TestMethod]
	public void TheFiguresAtDayOne_AreTheTestsFigures()
	{
		CowellRoundOffResult panel = Measured.Value;
		CowellRoundOffTests.Measurement test = CowellRoundOffTests.Measured.Value;
		int day = CowellRoundOffArc.DayOneIndex;

		// The same integration in the same arithmetic, stopped at more checkpoints along the way. Two-body
		// gravity ignores its time argument, so the extra stops cannot move a digit: equal, not close.
		Assert.AreEqual(test.Float[0], panel.Runs[0].Distances[day], "float, day 1");
		Assert.AreEqual(test.Double[0], panel.Runs[1].Distances[day], "double, day 1");
		Assert.AreEqual(test.Decimal[0], panel.Runs[2].Distances[day], "decimal, day 1");

		Assert.AreEqual(test.Float[^1], panel.Runs[0].Distances[^1], "float, day 30");
		Assert.AreEqual(test.Double[^1], panel.Runs[1].Distances[^1], "double, day 30");
		Assert.AreEqual(test.Decimal[^1], panel.Runs[2].Distances[^1], "decimal, day 30");

		// The PreciseNumber row is forty digits against the thirty-digit reference, which is the test's
		// convergence check carried along the whole arc rather than stopped at the first day.
		Assert.AreEqual(test.ReferenceConvergence, panel.Runs[3].Distances[day], "forty digits against thirty, day 1");
	}

	[TestMethod]
	public void TheGrowthExponent_IsTheSquareOfTheArc_NotTheSpecsRoot()
	{
		CowellRoundOffResult result = Measured.Value;

		// Measured t^1.96 for float and t^1.94 for double; the spec projected √t.
		Assert.AreEqual(1.94, result.Runs[1].Exponent, 0.05, "double");
		Assert.AreEqual(1.96, result.Runs[0].Exponent, 0.05, "float");
		Assert.IsGreaterThan(CowellRoundOffArc.SpecExponent * 3.0, result.Runs[1].Exponent);
	}

	[TestMethod]
	public void TheReference_IsBelowEveryOtherRow_AtEveryCheckpoint()
	{
		CowellRoundOffResult result = Measured.Value;
		double[] reference = result.Runs[3].Distances;

		double[] days = CowellRoundOffArc.CheckpointDays;
		for (int i = 0; i < days.Length; i++)
		{
			Console.WriteLine($"{days[i],7:F3} d   {result.Runs[0].Distances[i],10:E2} {result.Runs[1].Distances[i],10:E2} {result.Runs[2].Distances[i],10:E2} {reference[i],10:E2}");
		}

		for (int i = 0; i < reference.Length; i++)
		{
			Assert.IsGreaterThan(0.0, reference[i], "Forty and thirty digits agreeing exactly would mean the precision never reached the arithmetic.");

			// The reference's own round-off accumulates too, as the square of the arc like everyone
			// else's: 6.0e-23 km after a day and 6.4e-20 km after thirty. Decimal is the row it has to
			// clear and decimal's distance wanders, so the narrowest margin is at day 7, where it was
			// measured at 490x. Two orders is what the measurement needs to mean what it says.
			for (int run = 0; run < 3; run++)
			{
				Assert.IsLessThan(result.Runs[run].Distances[i] / 1e2, reference[i], $"{result.Runs[run].StorageName} at checkpoint {i}");
			}
		}
	}

	[TestMethod]
	public void TheMeasurement_ReachesThePanel_ThroughTheWorkQueue()
	{
		shell = new AppShell(registry => registry.Register<CowellRoundOffPanel>());
		harness = ImGuiAppHarness.Start(shell.BuildConfig(), new HarnessOptions());
		harness.Step(2);

		CowellRoundOffPanel panel = (CowellRoundOffPanel)shell.Registry.All[0];
		Assert.IsTrue(panel.DrawnThisFrame || harness.StepUntil(() => panel.DrawnThisFrame, 10), "The panel never drew.");

		panel.Measure();
		Assert.IsTrue(panel.IsMeasuring, "The measurement should be in flight on the work queue.");
		Assert.IsTrue(harness.StepUntil(() => panel.Result is not null, 20000), "The measurement never reached the panel.");

		// A frame with the table and the plot drawn.
		harness.Step(2);
		Assert.AreEqual(4, panel.Result!.Runs.Count);
		Assert.IsFalse(panel.IsMeasuring);
	}
}
