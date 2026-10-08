// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Numerics;
using System.Threading;
using Hexa.NET.ImGui;
using ktsu.ImGui.App;
using ktsu.ImGui.App.Testing;
using ktsu.Osculator.App.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HexaWidgetManager = Hexa.NET.ImGui.Widgets.WidgetManager;

/// <summary>
/// Drives the application shell headlessly, through the same configuration <c>Program</c> starts.
/// </summary>
/// <remarks>
/// The docking cases exist because the docked window the panels are built on unregisters itself
/// whenever <c>ImGui.Begin</c> returns <see langword="false"/>, which ImGui does for a collapsed
/// window and for a tab hidden behind another, not only for one the user closed. Without the shell's
/// guard, tabbing two panels together closes one of them for good; these tests were seen to fail
/// with the guard removed.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class AppShellTests : IDisposable
{
	private AppShell? shell;
	private ImGuiAppHarness? harness;

	private AppShell Shell => shell ?? throw new InvalidOperationException("Start the shell first.");

	private ImGuiAppHarness Harness => harness ?? throw new InvalidOperationException("Start the shell first.");

	/// <inheritdoc/>
	public void Dispose()
	{
		// Hexa's window manager is process-wide, so a panel left registered would be drawn again by
		// the next test's harness.
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
	public void Config_DocksAndThrottles()
	{
		using AppShell configured = new(Panels.Register);
		ImGuiAppConfig config = configured.BuildConfig();

		Assert.IsTrue(config.EnableDocking, "DrawDeferredDocked requires docking, and ImGui only accepts it before the first frame.");
		ImGuiAppPerformanceSettings performance = config.PerformanceSettings;
		Assert.IsTrue(performance.EnableThrottledRendering);
		Assert.IsTrue(performance.EnableIdleDetection);
		Assert.IsLessThan(performance.FocusedFps, performance.UnfocusedFps);
		Assert.IsLessThan(performance.FocusedFps, performance.IdleFps);
		Assert.IsLessThanOrEqualTo(performance.UnfocusedFps, performance.NotVisibleFps);
	}

	[TestMethod]
	public void StorageComparison_IsTheFirstPanel_AndIsDrawnDocked()
	{
		Start(Panels.Register);

		Assert.IsInstanceOfType<StorageComparisonWindow>(Shell.Registry.All[0]);

		Harness.Step(3);

		Panel storage = Shell.Registry.All[0];
		Assert.IsTrue(storage.IsOpen);
		Assert.IsTrue(storage.DrawnThisFrame, "The storage comparison panel was not drawn by the docked pump.");
	}

	[TestMethod]
	public void Register_RejectsADuplicateTitle()
	{
		using BackgroundWork work = new(_ => throw new InvalidOperationException("No work runs here."));
		PanelRegistry registry = new(work);
		registry.Register<AlphaPanel>();

		Assert.ThrowsExactly<InvalidOperationException>(registry.Register<AlphaPanel>);
		Assert.ThrowsExactly<InvalidOperationException>(() => registry.Register(new AlphaImpostorPanel()));
	}

	[TestMethod]
	public void TabbedPanels_BothStayOpen()
	{
		Start(RegisterAlphaAndBeta);
		Harness.Step(3);

		DockTogether();
		Harness.Step(6);

		Panel alpha = Shell.Registry.All[0];
		Panel beta = Shell.Registry.All[1];
		Assert.IsTrue(alpha.IsOpen, "The panel behind the selected tab was closed.");
		Assert.IsTrue(beta.IsOpen, "The selected tab was closed.");
		Assert.AreNotEqual(alpha.DrawnThisFrame, beta.DrawnThisFrame, "Exactly one of two tabs in one node should be drawn.");

		// Close the visible tab: the hidden one has to still be there to take its place.
		Panel visible = alpha.DrawnThisFrame ? alpha : beta;
		Panel hidden = alpha.DrawnThisFrame ? beta : alpha;
		visible.Dismiss();
		Harness.Step(3);

		Assert.IsTrue(hidden.IsOpen);
		Assert.IsTrue(hidden.DrawnThisFrame, "The tab that had been hidden did not come back when the other closed.");
	}

	[TestMethod]
	public void CollapsingAPanel_DoesNotCloseIt()
	{
		Start(RegisterAlpha);
		Harness.Step(3);

		ClickTitleBar("Alpha", fromRight: false);

		Panel alpha = Shell.Registry.All[0];
		Assert.IsTrue(IsCollapsed("Alpha"), "The click did not collapse the window, so this case tested nothing.");
		Assert.IsTrue(alpha.IsOpen, "Collapsing the panel closed it.");

		ClickTitleBar("Alpha", fromRight: false);

		Assert.IsFalse(IsCollapsed("Alpha"));
		Assert.IsTrue(alpha.DrawnThisFrame, "The panel did not draw again once expanded.");
	}

	[TestMethod]
	public void CloseButton_ClosesThePanel_AndOpenBringsItBack()
	{
		Start(RegisterAlpha);
		Harness.Step(3);

		ClickTitleBar("Alpha", fromRight: true);

		Panel alpha = Shell.Registry.All[0];
		Assert.IsFalse(alpha.IsOpen, "The close button did not close the panel.");

		Harness.Step(3);
		Assert.IsFalse(alpha.DrawnThisFrame, "A closed panel was drawn again.");

		// What the View menu does.
		alpha.Open();
		Harness.Step(3);
		Assert.IsTrue(alpha.DrawnThisFrame);
	}

	[TestMethod]
	public void BackgroundResult_ArrivesThroughTheInvoker_WithoutBlockingFrames()
	{
		Start(Panels.Register);
		Harness.Step(1);

		int uiThread = Environment.CurrentManagedThreadId;
		int workerThread = 0;
		int resultThread = 0;
		string? result = null;
		using ManualResetEventSlim release = new();

		Shell.Work.Run(
			"sweep",
			token =>
			{
				workerThread = Environment.CurrentManagedThreadId;
				release.Wait(TimeSpan.FromSeconds(10), token);
				return "propagated";
			},
			value =>
			{
				resultThread = Environment.CurrentManagedThreadId;
				result = value;
			});

		// Frames keep coming while the work is held, which is the point of moving it off this thread.
		Harness.Step(5);
		Assert.IsNull(result);
		Assert.IsTrue(Shell.Work.IsRunning("sweep"));

		release.Set();
		Assert.IsTrue(Harness.StepUntil(() => result is not null, 600), "The result never reached the UI thread.");

		Assert.AreEqual("propagated", result);
		Assert.AreNotEqual(uiThread, workerThread, "The work ran on the UI thread.");
		Assert.AreEqual(uiThread, resultThread, "The result was delivered off the UI thread.");
	}

	private static void RegisterAlpha(PanelRegistry registry) => registry.Register<AlphaPanel>();

	private static void RegisterAlphaAndBeta(PanelRegistry registry)
	{
		registry.Register<AlphaPanel>();
		registry.Register<BetaPanel>();
	}

	private static bool IsCollapsed(string title) => ImGuiP.FindWindowByName(title).Collapsed;

	private static void DockTogether()
	{
		uint dockspace = HexaWidgetManager.DockSpaceId;
		Assert.AreNotEqual(0u, dockspace, "The docked pump has not created its dockspace yet.");
		ImGuiP.DockBuilderDockWindow("Alpha", dockspace);
		ImGuiP.DockBuilderDockWindow("Beta", dockspace);
	}

	private void Start(Action<PanelRegistry> register)
	{
		shell = new AppShell(register);
		harness = ImGuiAppHarness.Start(shell.BuildConfig(), new HarnessOptions());
	}

	/// <summary>
	/// Clicks a floating window's collapse arrow or close button, which sit a frame-padding in from
	/// either end of its title bar.
	/// </summary>
	private void ClickTitleBar(string title, bool fromRight)
	{
		ImGuiWindowPtr window = ImGuiP.FindWindowByName(title);
		Vector2 position = window.Pos;
		Vector2 size = window.Size;
		ImGuiStylePtr style = ImGui.GetStyle();
		float half = ImGui.GetFontSize() * 0.5f;

		float x = fromRight
			? position.X + size.X - style.FramePadding.X - half
			: position.X + style.FramePadding.X + half;
		float y = position.Y + style.FramePadding.Y + half;

		Harness.Mouse.Click(x, y);
		Harness.Step(3);
	}

	private sealed class AlphaPanel : Panel
	{
		protected override string Title => "Alpha";

		protected override void Draw() => ImGui.TextUnformatted("alpha");
	}

	private sealed class AlphaImpostorPanel : Panel
	{
		protected override string Title => "Alpha";

		protected override void Draw() => ImGui.TextUnformatted("impostor");
	}

	private sealed class BetaPanel : Panel
	{
		protected override string Title => "Beta";

		protected override void Draw() => ImGui.TextUnformatted("beta");
	}
}
