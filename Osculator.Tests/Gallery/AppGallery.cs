// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests.Gallery;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ktsu.ImGui.App.Testing;
using ktsu.Osculator.App;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.App.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Photographs the application's panels for <c>docs/gallery</c>: one picture per
/// <see cref="GalleryCatalog"/> entry, and an index that captions them.
/// </summary>
/// <remarks>
/// <para>
/// These run as ordinary tests, so every pull request proves each picture can still be staged: a
/// panel that stops drawing, or a measurement that never lands, fails here rather than at the next
/// regeneration on main. Pictures are written to a temporary directory unless
/// <c>OSCULATOR_GALLERY_OUT</c> names one, which is how the gallery workflow, and anyone
/// regenerating by hand, sends them to <c>docs/gallery</c>.
/// </para>
/// <para>
/// Each entry starts its own shell through <see cref="AppShell.BuildConfig"/>, the configuration
/// <c>Program</c> starts, so a picture never depends on the one taken before it. Two things are
/// process-wide and put back around every entry: the catalogue selection and the storage
/// comparison's last measurement. The stopwatch columns are hidden for the whole class, through
/// <see cref="MeasuredDurations"/>, because a timing is a fact about the machine and would make every
/// regeneration a new picture.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class AppGallery : IDisposable
{
	/// <summary>The environment variable naming the directory the gallery is written to.</summary>
	internal const string OutputVariable = "OSCULATOR_GALLERY_OUT";

	private static readonly Lazy<string> TemporaryOutput = new(() =>
		Path.Combine(Path.GetTempPath(), $"osculator-gallery-{Guid.NewGuid():N}"));

	private AppShell? shell;
	private ImGuiAppHarness? harness;

	/// <summary>Gets or sets the test context, for reporting where each picture went.</summary>
	public TestContext TestContext { get; set; } = null!;

	/// <summary>Gets every entry's name, one test case each.</summary>
	public static IEnumerable<object[]> EntryNames => GalleryCatalog.Entries.Select(entry => new object[] { entry.Name });

	/// <summary>Gets the directory pictures are written to.</summary>
	internal static string OutputDirectory =>
		Environment.GetEnvironmentVariable(OutputVariable) is string output && output.Length > 0
			? Path.GetFullPath(output)
			: TemporaryOutput.Value;

	/// <summary>Hides the stopwatch columns for every picture.</summary>
	/// <param name="context">Unused.</param>
	[ClassInitialize]
	public static void HideMeasuredDurations(TestContext context) => MeasuredDurations.Hidden = true;

	/// <summary>
	/// Shows the stopwatch columns again, and removes the temporary directory when pictures went
	/// there rather than to a named one.
	/// </summary>
	[ClassCleanup]
	public static void CleanUp()
	{
		MeasuredDurations.Hidden = false;
		ResetSharedState();

		if (TemporaryOutput.IsValueCreated && Directory.Exists(TemporaryOutput.Value))
		{
			Directory.Delete(TemporaryOutput.Value, recursive: true);
		}
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
		ResetSharedState();
	}

	[TestMethod]
	[DynamicData(nameof(EntryNames))]
	public void Photograph(string name)
	{
		GalleryEntry entry = GalleryCatalog.Entries.Single(candidate => candidate.Name == name);
		ResetSharedState();

		shell = new AppShell(Panels.Register);
		harness = ImGuiAppHarness.Start(shell.BuildConfig(), new HarnessOptions { Width = entry.Display.Width, Height = entry.Display.Height });
		Assert.IsTrue(GalleryFonts.Load(), "ImGuiApp's own font could not be found, so the pictures would not look like the application.");
		harness.Mouse.MoveTo(-100f, -100f);
		harness.Step(3);

		entry.Stage(new GalleryStage(harness, shell));
		harness.Step(3);

		Bitmap32 picture = harness.Target;
		Directory.CreateDirectory(OutputDirectory);
		string path = Path.Combine(OutputDirectory, entry.Slug + ".png");
		picture.SavePng(path);
		TestContext.WriteLine($"Wrote {path} ({picture.Width}x{picture.Height}).");
	}

	[TestMethod]
	public void WriteTheIndex()
	{
		string[] slugs = [.. GalleryCatalog.Entries.Select(entry => entry.Slug)];
		Assert.HasCount(slugs.Length, slugs.Distinct(StringComparer.Ordinal), "Two gallery entries would write the same file.");

		Directory.CreateDirectory(OutputDirectory);
		File.WriteAllText(Path.Combine(OutputDirectory, "README.md"), GalleryIndex.Render(GalleryCatalog.Entries));
	}

	private static void ResetSharedState()
	{
		CatalogueSelection.Select(null);
		StorageComparisonPanel.ResetState();
	}
}
