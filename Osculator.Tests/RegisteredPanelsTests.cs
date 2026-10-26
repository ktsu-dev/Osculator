// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using ktsu.ImGui.App.Testing;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.App.Shell;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Data.CelesTrak;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Checks that every panel the application has is one the shell shows.
/// </summary>
/// <remarks>
/// <para>
/// Four panels were once built and never registered, so nothing ever drew them, and because the
/// catalogue was one of them nothing could ever be selected either (#120). Each of these tests was
/// seen to fail with a registration removed.
/// </para>
/// <para>
/// Drawing the shell draws the catalogue, and the catalogue loads from CelesTrak. A test run must not
/// be a request to a free service run by one person, so before any test runs, the user's application
/// data folder is pointed at a temporary one holding a fresh cached copy of the group the catalogue
/// reads, and the catalogue loads from that without touching the network. That redirection works
/// where .NET reads the folder from <c>XDG_DATA_HOME</c>, which is Linux, where CI runs. Elsewhere the
/// folder cannot be moved, and seeding the real one would plant a one-object catalogue in the
/// application's own cache, so nothing is seeded and the catalogue fetches as the application would.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class RegisteredPanelsTests : IDisposable
{
	private static readonly Type[] PreviouslyUndrawn =
	[
		typeof(CatalogueWindow),
		typeof(GlobeWindow),
		typeof(ResidualPlotsWindow),
		typeof(TimeInspectorWindow),
	];

	private static bool seeded;

	private AppShell? shell;
	private ImGuiAppHarness? harness;

	/// <summary>
	/// Points the application data folder at a temporary one with the catalogue already cached.
	/// </summary>
	/// <param name="context">Unused.</param>
	[AssemblyInitialize]
	public static void KeepTheCatalogueOffline(TestContext context)
	{
		_ = context;

		string root = Path.Join(Path.GetTempPath(), "osculator-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		Environment.SetEnvironmentVariable("XDG_DATA_HOME", root);

		if (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) != root)
		{
			return;
		}

		string snapshot = Directory.EnumerateFiles(Path.Join(AppContext.BaseDirectory, "Data", "iss-snapshots", "25544"), "*.json").Order(StringComparer.Ordinal).Last();
		ResponseCache cache = new(Path.Join(root, "Osculator", "celestrak"), TimeSpan.FromHours(2), TimeProvider.System);
		cache.Write($"GROUP={CataloguePanel.Group}&FORMAT=json", "[" + File.ReadAllText(snapshot).Trim() + "]");

		// The catalogue merges several groups, and one left uncached would be fetched from CelesTrak.
		foreach (string group in CatalogueLoader.CelesTrakGroups.Where(group => group != CataloguePanel.Group))
		{
			cache.Write($"GROUP={group}&FORMAT=json", "[]");
		}
		seeded = true;
	}

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
	public void EveryPanelInTheApplication_IsRegistered()
	{
		HashSet<Type> registered = [.. RegisteredPanels().Select(p => p.GetType())];

		Type[] panels = [.. typeof(Panel).Assembly.GetTypes()
			.Where(t => t.IsSubclassOf(typeof(Panel)) && !t.IsAbstract)
			.OrderBy(t => t.Name, StringComparer.Ordinal)];

		Assert.IsNotEmpty(panels);

		foreach (Type panel in panels)
		{
			Assert.Contains(panel, registered, $"{panel.Name} is a panel nothing registers, so the application never draws it.");
		}
	}

	[TestMethod]
	public void TheFourPanelsThatPredateTheShell_AreDrawnDocked()
	{
		shell = new AppShell(Panels.Register);
		harness = ImGuiAppHarness.Start(shell.BuildConfig(), new HarnessOptions());

		List<Panel> panels = [.. PreviouslyUndrawn.Select(type => shell.Registry.All.Single(p => p.GetType() == type))];

		Assert.IsTrue(
			harness.StepUntil(() => panels.TrueForAll(p => p.DrawnThisFrame), 60),
			"Not drawn: " + string.Join(", ", panels.Where(p => !p.DrawnThisFrame).Select(p => p.Caption)));

		Assert.IsTrue(panels.TrueForAll(p => p.IsOpen));
	}

	[TestMethod]
	public void ClickingACatalogueRow_SelectsTheObject()
	{
		if (!seeded)
		{
			Assert.Inconclusive("The catalogue could not be kept offline on this platform, so this would be a request to CelesTrak.");
		}

		CatalogueSelection.Select(null);
		shell = new AppShell(registry => registry.Register<CatalogueWindow>());
		harness = ImGuiAppHarness.Start(shell.BuildConfig(), new HarnessOptions());
		harness.Step(3);

		// A panel opens floating at ImGui's small default size, which is too short to show a row.
		ImGui.SetWindowPos("Catalogue", new Vector2(40, 40));
		ImGui.SetWindowSize("Catalogue", new Vector2(900, 400));
		harness.Step(3);

		// The rows are not probe-marked, so walk up the window from its bottom edge until a click
		// lands on one. Below the rows is empty table, where a click does nothing; above them are the
		// search box and filters, which the walk stops short of once a row is selected. A row's
		// selectable spans every column.
		ImGuiWindowPtr window = ImGuiP.FindWindowByName("Catalogue");
		float x = window.Pos.X + (window.Size.X * 0.5f);
		float step = ImGui.GetFrameHeight() * 0.5f;

		for (float y = window.Pos.Y + window.Size.Y - step; y > window.Pos.Y && CatalogueSelection.Selected is null; y -= step)
		{
			harness.Mouse.Click(x, y);
			harness.Step(2);
		}

		ElementSet? selected = CatalogueSelection.Selected;
		Assert.IsNotNull(selected, "No click on the catalogue selected anything.");
		Assert.AreEqual(25544, selected.NoradCatalogId);
	}

	private static List<Panel> RegisteredPanels()
	{
		using BackgroundWork work = new(_ => throw new InvalidOperationException("No work runs here."));
		PanelRegistry registry = new(work);
		Panels.Register(registry);
		return [.. registry.All];
	}
}
