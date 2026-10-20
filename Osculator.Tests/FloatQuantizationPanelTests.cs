// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Linq;
using ktsu.ImGui.App.Testing;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.App.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the float quantization panel: registered with the shell, computing through the work queue,
/// and drawing once its result arrives.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FloatQuantizationPanelTests : IDisposable
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
	public void Panel_IsRegistered()
	{
		using BackgroundWork work = new(_ => throw new InvalidOperationException("No work runs here."));
		PanelRegistry registry = new(work);
		Panels.Register(registry);

		Assert.IsTrue(registry.All.OfType<FloatQuantizationPanel>().Any(), "The panel is missing from Panels.Register.");
	}

	[TestMethod]
	public void Panel_ComputesThroughTheWorkQueue_AndKeepsDrawingItsResult()
	{
		shell = new AppShell(registry => registry.Register<FloatQuantizationPanel>());
		harness = ImGuiAppHarness.Start(shell.BuildConfig(), new HarnessOptions());
		harness.Step(2);

		FloatQuantizationPanel panel = (FloatQuantizationPanel)shell.Registry.All[0];
		Assert.IsTrue(panel.DrawnThisFrame);

		Assert.IsTrue(harness.StepUntil(() => panel.Result is not null, 2000), "The computation never reached the panel.");
		Assert.HasCount(3, panel.Result!.Components);
		Assert.AreEqual(1000.0 / 1024.0, panel.Result.MagnitudeStepMeters);

		harness.Step(2);
		Assert.IsTrue(panel.DrawnThisFrame, "The panel stopped drawing once it had a result to show.");

		// The closest spacing offered is the most the plots ever draw; ImGui aborts the process if
		// one draw list outgrows its 16-bit indices, so drawing it at all is the assertion.
		FloatQuantizationResult first = panel.Result;
		panel.Recompute(1.0);
		Assert.IsTrue(harness.StepUntil(() => !ReferenceEquals(panel.Result, first), 2000), "The recomputation never reached the panel.");
		Assert.HasCount(361, panel.Result.Minutes);
		harness.Step(3);
		Assert.IsTrue(panel.DrawnThisFrame);
	}
}
