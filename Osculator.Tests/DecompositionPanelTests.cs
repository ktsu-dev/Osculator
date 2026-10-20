// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ktsu.ImGui.App.Testing;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.App.Shell;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the decomposition panel and the feed that archives what the application fetches: the M5
/// routine reached from the application, over the committed ISS snapshots.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DecompositionPanelTests : IDisposable
{
	private const int Iss = 25544;

	private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

	private readonly List<string> temporaryDirectories = [];

	private AppShell? shell;
	private ImGuiAppHarness? harness;

	private static string IssSnapshots => Path.Join(VerificationSet.DataDirectory, "iss-snapshots");

	/// <inheritdoc/>
	public void Dispose()
	{
		CatalogueSelection.Select(null);

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

		foreach (string directory in temporaryDirectories)
		{
			Directory.Delete(directory, recursive: true);
		}

		temporaryDirectories.Clear();
	}

	[TestMethod]
	public void Feed_ArchivesEachCachedResponseOnce_AndSkipsOneThatIsNotElementSets()
	{
		string root = TemporaryDirectory();
		ResponseCache cache = new(Path.Join(root, "celestrak"), ResponseCache.MinimumAllowedAge, TimeProvider.System);
		SnapshotStore store = new(Path.Join(root, "snapshots"));
		SnapshotFeed feed = new(cache.Directory, store);
		string[] bodies = [.. Directory.GetFiles(Path.Join(IssSnapshots, "25544")).Order(StringComparer.Ordinal).Select(File.ReadAllText)];

		Assert.AreEqual(0, feed.Sweep(), "Nothing is cached yet, and a missing directory is not an error.");

		cache.Write("CATNR=25544&FORMAT=json", bodies[0]);
		Assert.AreEqual(1, feed.Sweep());
		Assert.AreEqual(0, feed.Sweep(), "An unchanged response is not offered again.");

		// Another client's response in the same directory, which is not OMM.
		cache.Write("SATCAT", "not element sets");
		Assert.AreEqual(0, feed.Sweep());
		Assert.AreEqual(1, feed.UnreadableResponses);

		// The refetch replaces the cached body. The earlier set is already archived, so both survive.
		cache.Write("CATNR=25544&FORMAT=json", bodies[1]);
		Assert.AreEqual(1, feed.Sweep());
		Assert.AreEqual(0, feed.UnreadableResponses);

		IReadOnlyList<ElementSet> history = store.History(Iss);
		Assert.HasCount(2, history);
		Assert.IsLessThan(history[1].Epoch, history[0].Epoch);
	}

	[TestMethod]
	public void Panel_IsRegistered_AndDecomposesTheIssWeek_WithTheReferenceExactlyZero()
	{
		Assert.IsTrue(RegisteredPanels().OfType<DecompositionPanel>().Any(), "The panel is missing from Panels.Register.");

		SnapshotStore store = new(IssSnapshots);
		IReadOnlyList<ElementSet> history = store.History(Iss);
		DecompositionPanel panel = Start(store);

		CatalogueSelection.Select(history[2]);
		StepUntil(() => panel.Result is not null, "The decomposition never reached the panel.");

		Assert.HasCount(3, panel.History!.ElementSets);
		Assert.AreEqual(1, panel.PredictionIndex, "The default pair is the two newest sets.");
		Assert.AreEqual(2, panel.TruthIndex);

		DecompositionResult<PreciseNumber> result = panel.Result!;
		PreciseStorageMath math = new(DecompositionPanel.ReferenceDigits);
		ElementSetDivergence<PreciseNumber> divergence = Divergence<PreciseNumber>.Between(history[1], history[2], math);

		Assert.IsTrue(result.IsSuccess);
		Assert.AreEqual(divergence.Residual, result.Model, "Δ_model is the library's figure, unchanged.");
		Assert.AreEqual(10.915, DecompositionPanel.Magnitude(result.Model, math), 1e-3);
		Assert.AreEqual(panel.Samples, result.Data.Samples);
		Assert.IsGreaterThan(0.0, double.CreateChecked(result.Data.RmsMagnitude));

		Assert.HasCount(4, result.Arithmetic);
		Assert.IsTrue(result.ReferenceAgainstItself.IsExactlyZero, "The reference against itself must be zero to the last digit.");
		Assert.AreEqual("0, every digit", DecompositionPanel.DescribeArithmetic(result.ReferenceAgainstItself, math));
		Assert.IsTrue(result.Arithmetic.Take(3).All(term => term.IsSuccess && !term.IsExactlyZero), "Only the reference is exactly zero.");

		harness!.Step(2);
		Assert.IsTrue(panel.DrawnThisFrame, "The panel stopped drawing once it had a result to show.");

		// The half-day pair, chosen by hand.
		panel.Choose(0, 1);
		StepUntil(() => panel.Result is not null, "The second decomposition never reached the panel.");
		Assert.AreEqual(0.283, DecompositionPanel.Magnitude(panel.Result!.Model, math), 1e-3);
	}

	[TestMethod]
	public void Panel_WithOneSnapshot_SaysTwoAreNeeded_AndComputesNothing()
	{
		string root = TemporaryDirectory();
		string folder = Path.Join(root, "25544");
		Directory.CreateDirectory(folder);
		string first = Directory.GetFiles(Path.Join(IssSnapshots, "25544")).Order(StringComparer.Ordinal).First();
		File.Copy(first, Path.Join(folder, Path.GetFileName(first)));

		SnapshotStore store = new(root);
		DecompositionPanel panel = Start(store);

		CatalogueSelection.Select(store.History(Iss)[0]);
		StepUntil(() => panel.History is not null, "The history never reached the panel.");
		harness!.Step(2);

		Assert.HasCount(1, panel.History!.ElementSets);
		Assert.IsNull(panel.Result);
		Assert.IsFalse(panel.IsComputing);
		Assert.IsTrue(panel.DrawnThisFrame);
		StringAssert.Contains(DecompositionPanel.DescribeShortHistory(1), "needs two");
		StringAssert.Contains(DecompositionPanel.DescribeShortHistory(0), "No element set");
	}

	[TestMethod]
	public void FormatKilometres_SignsResidualsAndSwitchesToExponentsAtTheExtremes()
	{
		Assert.AreEqual("+10.9150", DecompositionPanel.FormatKilometres(10.915));
		Assert.AreEqual("-0.2640", DecompositionPanel.FormatKilometres(-0.264));
		Assert.AreEqual("1.700e-9", DecompositionPanel.FormatKilometres(1.7e-9, signed: false));
		Assert.AreEqual("0", DecompositionPanel.FormatKilometres(0.0));
	}

	private static List<Panel> RegisteredPanels()
	{
		using BackgroundWork work = new(_ => throw new InvalidOperationException("No work runs here."));
		PanelRegistry registry = new(work);
		Panels.Register(registry);
		return [.. registry.All];
	}

	private DecompositionPanel Start(SnapshotStore store)
	{
		CatalogueSelection.Select(null);
		shell = new AppShell(registry => registry.Register(new DecompositionPanel(store, feed: null)));
		harness = ImGuiAppHarness.Start(shell.BuildConfig(), new HarnessOptions());
		harness.Step(2);

		DecompositionPanel panel = (DecompositionPanel)shell.Registry.All[0];
		Assert.IsTrue(panel.DrawnThisFrame);
		Assert.IsNull(panel.Subject, "Nothing is selected yet.");
		return panel;
	}

	/// <summary>Steps frames until a condition holds, bounded by wall-clock time rather than frames.</summary>
	/// <remarks>
	/// The decomposition is seconds of thirty-digit arithmetic on a worker, and an idle frame is far
	/// shorter than that, so a frame budget would be a guess about the machine.
	/// </remarks>
	private void StepUntil(Func<bool> condition, string message)
	{
		Stopwatch clock = Stopwatch.StartNew();

		while (!condition())
		{
			Assert.IsLessThan(Patience, clock.Elapsed, message);
			harness!.Step(1);
		}
	}

	private string TemporaryDirectory()
	{
		string directory = Path.Join(Path.GetTempPath(), "osculator-decomposition-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		temporaryDirectories.Add(directory);
		return directory;
	}
}
