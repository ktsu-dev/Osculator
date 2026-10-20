// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ktsu.ImGui.App.Testing;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.App.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the panel for spec demonstration 3: the computation behind it, and the panel delivering
/// it through the shell's work queue for the Molniya defaults.
/// </summary>
/// <remarks>
/// The numbers asserted are the ones <see cref="KeplerEquationDigitLossTests"/> measured for the same
/// arrangement, so the panel and the suite cannot quietly disagree.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class KeplerDemoPanelTests : IDisposable
{
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
	public void Compute_AtMolniya_FloatLosesUnderADigit_AndDecimalLosesSeveral()
	{
		KeplerDemoResult result = KeplerDemo.Compute(KeplerDemo.MolniyaEccentricity, 1e-6, CancellationToken.None);
		AssertMolniyaShape(result);

		// Measured by the digit-loss suite at M = 1e-6: decimal's naive E loses 5.26 of 28 digits,
		// because E there is about 4e-6 and decimal's precision is absolute.
		KeplerRun decimals = result.Runs.Single(r => r.StorageName == "decimal");
		Assert.IsGreaterThan(4.5, decimals.NaiveAnomalyLost);
		Assert.IsLessThan(6.0, decimals.NaiveAnomalyLost);
	}

	[TestMethod]
	public void Compute_BothSolversAgreeOnTheEccentricAnomaly()
	{
		KeplerDemoResult result = KeplerDemo.Compute(KeplerDemo.MolniyaEccentricity, 0.5, CancellationToken.None);

		foreach (KeplerRun run in result.Runs)
		{
			Assert.AreEqual(run.NaiveAnomaly, run.UniversalAnomaly, 1e-5 * run.NaiveAnomaly, run.StorageName);
		}
	}

	[TestMethod]
	public void Compute_AtHighEccentricity_ReportsTheNaiveFloatSolveDiverging()
	{
		KeplerDemoResult result = KeplerDemo.Compute(0.99, 0.1, CancellationToken.None);

		KeplerRun floats = result.Runs[0];
		Assert.IsFalse(floats.NaiveConverged, "E₀ = M at e = 0.99, M = 0.1 is expected to send float's Newton iteration away.");
		Assert.IsTrue(double.IsNaN(floats.NaivePositionLost));
		Assert.IsTrue(result.Runs.All(r => r.UniversalConverged), "The universal solver converges everywhere.");
	}

	[TestMethod]
	public void Compute_RefusesInputsOutOfRange()
	{
		Assert.IsFalse(KeplerDemo.IsValid(1.0, 0.1));
		Assert.IsFalse(KeplerDemo.IsValid(-0.1, 0.1));
		Assert.IsFalse(KeplerDemo.IsValid(0.5, 0.0));
		Assert.IsFalse(KeplerDemo.IsValid(0.5, double.NaN));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeplerDemo.Compute(1.0, 0.1, CancellationToken.None));
	}

	[TestMethod]
	public void Panel_IsRegistered_AndReportsTheMolniyaDigitLossThroughTheWorkQueue()
	{
		Assert.IsTrue(RegisteredPanels().OfType<KeplerDemoPanel>().Any(), "The panel is missing from Panels.Register.");

		shell = new AppShell(registry => registry.Register<KeplerDemoPanel>());
		harness = ImGuiAppHarness.Start(shell.BuildConfig(), new HarnessOptions());
		harness.Step(2);

		KeplerDemoPanel panel = (KeplerDemoPanel)shell.Registry.All[0];
		Assert.IsTrue(panel.DrawnThisFrame);

		// Drawing the panel starts a run at its own defaults; nothing has to be clicked.
		Assert.IsTrue(harness.StepUntil(() => panel.Result is not null, 5000), "The Molniya run never reached the panel.");
		Assert.AreEqual(KeplerDemo.MolniyaEccentricity, panel.Result!.Eccentricity);
		Assert.AreEqual(KeplerDemo.DefaultMeanAnomaly, panel.Result.MeanAnomaly);
		AssertMolniyaShape(panel.Result);

		panel.Select(0.99, 0.1);
		Assert.IsTrue(harness.StepUntil(() => Math.Abs(panel.Result!.Eccentricity - 0.99) < 1e-12, 5000), "A changed input never produced a new run.");
		Assert.IsFalse(panel.Result!.Runs[0].NaiveConverged);

		harness.Step(2);
		Assert.IsTrue(panel.DrawnThisFrame, "The panel stopped drawing once it had results to show.");
	}

	/// <summary>
	/// What the digit-loss suite measured at e = 0.74: float, double and thirty digits lose under a
	/// digit by either solver (worst 0.73 naive and 0.57 universal), and every solve converges.
	/// </summary>
	private static void AssertMolniyaShape(KeplerDemoResult result)
	{
		Assert.HasCount(4, result.Runs);
		Assert.AreEqual("float", result.Runs[0].StorageName);
		Assert.AreEqual("double", result.Runs[1].StorageName);
		Assert.AreEqual("decimal", result.Runs[2].StorageName);
		StringAssert.StartsWith(result.Runs[3].StorageName, "PreciseNumber");

		foreach (KeplerRun run in result.Runs)
		{
			Assert.IsTrue(run.NaiveConverged && run.UniversalConverged, run.StorageName);
		}

		foreach (KeplerRun run in result.Runs.Where(r => r.StorageName != "decimal"))
		{
			Assert.IsLessThan(1.0, run.NaiveAnomalyLost, $"{run.StorageName} naive E");
			Assert.IsLessThan(1.0, run.NaivePositionLost, $"{run.StorageName} naive r");
			Assert.IsLessThan(1.0, run.UniversalPositionLost, $"{run.StorageName} universal r");
		}
	}

	private static List<Panel> RegisteredPanels()
	{
		using BackgroundWork work = new(_ => throw new InvalidOperationException("No work runs here."));
		PanelRegistry registry = new(work);
		Panels.Register(registry);
		return [.. registry.All];
	}
}
