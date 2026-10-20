// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using ktsu.ImGui.App.Testing;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.App.Shell;
using ktsu.Osculator.Core.Conjunction;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.CelesTrak;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the conjunction panel asking for its catalogue on every screen (#134): inside the refetch
/// window that is the cached copy, past it a fresh fetch, and when the source is down a stale copy
/// that the result and the summary say is stale.
/// </summary>
/// <remarks>
/// Requests are counted through a stub transport against a clock the test moves by hand, through a
/// real <see cref="CelesTrakClient"/> and <see cref="ResponseCache"/>, so what is asserted is what
/// the panel does to the cache rather than what a fake of the cache would allow.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ConjunctionCatalogueRefreshTests : IDisposable
{
	private const int IssId = 25544;

	private const string IssResponse = """
		[{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-10-19T20:26:37.026816",
		"MEAN_MOTION":15.49234213,"ECCENTRICITY":0.00047339,"INCLINATION":51.6316,
		"RA_OF_ASC_NODE":176.7315,"ARG_OF_PERICENTER":169.8211,"MEAN_ANOMALY":190.2874,
		"NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,"REV_AT_EPOCH":58689,
		"BSTAR":0.00014639046,"MEAN_MOTION_DOT":7.689e-5,"MEAN_MOTION_DDOT":0}]
		""";

	private const string IssLaterResponse = """
		[{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-10-20T04:11:02.500000",
		"MEAN_MOTION":15.49236000,"ECCENTRICITY":0.00047400,"INCLINATION":51.6317,
		"RA_OF_ASC_NODE":172.1100,"ARG_OF_PERICENTER":171.0000,"MEAN_ANOMALY":189.0000,
		"NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,"REV_AT_EPOCH":58694,
		"BSTAR":0.00014700000,"MEAN_MOTION_DOT":7.700e-5,"MEAN_MOTION_DDOT":0}]
		""";

	private static readonly TimeSpan Window = TimeSpan.FromHours(2);

	private static readonly DateTimeOffset Start = new(2026, 10, 20, 6, 0, 0, TimeSpan.Zero);

	private static readonly DateTime Epoch = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

	private readonly List<IDisposable> owned = [];

	private string root = string.Empty;
	private AppShell? shell;
	private ImGuiAppHarness? harness;

	/// <summary>A screened object that is not in the catalogue, so every screen has exactly one pair.</summary>
	private static ElementSet Primary => new()
	{
		ObjectName = "TEST 90001",
		ObjectId = "2026-001A",
		NoradCatalogId = 90001,
		Epoch = Epoch,
		EpochJulianDate = JulianDate.FromUtc(Epoch),
		MeanMotion = 15.5,
		Eccentricity = 0.0005,
		Inclination = 51.6,
		RightAscensionOfAscendingNode = 100.0,
		ArgumentOfPericenter = 90.0,
		MeanAnomaly = 0.0,
		BStar = 0.0001,
	};

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-conjunction-refresh-").FullName;

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

		foreach (IDisposable disposable in owned)
		{
			disposable.Dispose();
		}

		owned.Clear();

		if (root.Length > 0 && Directory.Exists(root))
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void ASecondScreenInsideTheWindow_ReadsTheCachedCopy_WithoutAskingCelesTrak()
	{
		(ConjunctionScreeningPanel panel, CelesTrakClientTests.CountingHandler handler, _) = StartPanel();

		ConjunctionSweepResult first = Screen(panel);
		ConjunctionSweepResult second = Screen(panel);

		Assert.AreEqual(1, handler.Requests, "Inside the refetch window the cache should answer.");
		Assert.AreEqual(Start, first.CatalogueFetchedAt);
		Assert.AreEqual(Start, second.CatalogueFetchedAt);
		Assert.IsFalse(second.CatalogueIsStale);
	}

	[TestMethod]
	public void ASecondScreenPastTheWindow_FetchesAgain_AndScreensTheNewElementSets()
	{
		(ConjunctionScreeningPanel panel, CelesTrakClientTests.CountingHandler handler, CelesTrakClientTests.FakeClock clock) = StartPanel();

		ConjunctionSweepResult first = Screen(panel);
		Assert.AreEqual(15.49234213, first.Objects[IssId].MeanMotion);

		handler.Body = IssLaterResponse;
		clock.Advance(TimeSpan.FromHours(3));

		ConjunctionSweepResult second = Screen(panel);

		Assert.AreEqual(2, handler.Requests, "Past the refetch window the screen should have asked CelesTrak again.");
		Assert.AreEqual(15.49236000, second.Objects[IssId].MeanMotion, "The second screen used the first catalogue.");
		Assert.AreEqual(Start + TimeSpan.FromHours(3), second.CatalogueFetchedAt);
		Assert.IsFalse(second.CatalogueIsStale);
	}

	[TestMethod]
	public void AScreenWhileCelesTrakIsDown_UsesTheLastGoodCopy_AndSaysItIsStale()
	{
		(ConjunctionScreeningPanel panel, CelesTrakClientTests.CountingHandler handler, CelesTrakClientTests.FakeClock clock) = StartPanel();

		_ = Screen(panel);

		handler.FailWith = new HttpRequestException("no route to host");
		clock.Advance(TimeSpan.FromHours(3));

		ConjunctionSweepResult offline = Screen(panel);

		Assert.AreEqual(2, handler.Requests, "The expired copy should have been refetched before falling back.");
		Assert.IsTrue(offline.CatalogueIsStale);
		Assert.AreEqual(Start, offline.CatalogueFetchedAt, "The fallback should report when its own copy was fetched.");

		string? line = ConjunctionScreeningPanel.DescribeCatalogue(offline, (Start + TimeSpan.FromHours(3)).UtcDateTime);
		Assert.IsNotNull(line);
		StringAssert.Contains(line, "2026-10-20 06:00 UTC");
		StringAssert.Contains(line, "3.0 h before the window");
		StringAssert.Contains(line, "STALE");
	}

	[TestMethod]
	public void AnInjectedCatalogue_IsNotReusedByTheNextScreen_AndCarriesNoFetchTime()
	{
		(ConjunctionScreeningPanel panel, CelesTrakClientTests.CountingHandler handler, _) = StartPanel();

		panel.StartScreen(Primary, [Primary with { NoradCatalogId = 90002 }], Options(), Epoch);
		Assert.IsTrue(harness!.StepUntil(() => panel.Result is not null, 2000), "The injected screen never reached the panel.");
		Assert.IsNull(panel.Result!.CatalogueFetchedAt);
		Assert.IsNull(ConjunctionScreeningPanel.DescribeCatalogue(panel.Result, Epoch));
		Assert.AreEqual(0, handler.Requests);

		ConjunctionSweepResult loaded = Screen(panel);

		Assert.AreEqual(1, handler.Requests, "The screen reused the injected catalogue instead of loading one.");
		Assert.IsTrue(loaded.Objects.ContainsKey(IssId));
		Assert.IsFalse(loaded.Objects.ContainsKey(90002));
	}

	[TestMethod]
	public void DescribeCatalogue_GivesOldCopiesInDays_AndOmitsTheFlagWhenFresh()
	{
		ConjunctionSweepResult result = new(Options(), [], 0, 0, [], new Dictionary<int, ElementSet>())
		{
			CatalogueFetchedAt = Start,
		};

		string? line = ConjunctionScreeningPanel.DescribeCatalogue(result, (Start + TimeSpan.FromDays(3)).UtcDateTime);

		Assert.AreEqual("Catalogue fetched 2026-10-20 06:00 UTC, 3.0 days before the window", line);
	}

	private static ConjunctionScreenOptions Options() => new()
	{
		WindowStart = JulianDate.FromUtc(Epoch),
		WindowMinutes = 60.0,
		ThresholdKm = 5.0,
	};

	private (ConjunctionScreeningPanel Panel, CelesTrakClientTests.CountingHandler Handler, CelesTrakClientTests.FakeClock Clock) StartPanel()
	{
		CelesTrakClientTests.CountingHandler handler = new(IssResponse);
		CelesTrakClientTests.FakeClock clock = new(Start);
		owned.Add(handler);
		HttpClient http = new(handler, disposeHandler: false);
		owned.Add(http);
		CelesTrakClient client = new(http, new ResponseCache(Path.Join(root, "cache"), Window, clock));

		ConjunctionScreeningPanel panel = new() { LoadCatalogue = token => client.GetGroupAsync(CataloguePanel.Group, token) };
		shell = new AppShell(registry => registry.Register(panel));
		harness = ImGuiAppHarness.Start(shell.BuildConfig(), new HarnessOptions());
		harness.Step(2);

		return (panel, handler, clock);
	}

	private ConjunctionSweepResult Screen(ConjunctionScreeningPanel panel)
	{
		ConjunctionSweepResult? before = panel.Result;
		panel.ScreenCatalogue(Primary, group: false, term: string.Empty);
		Assert.IsTrue(harness!.StepUntil(() => !ReferenceEquals(panel.Result, before), 10000), "The screen never reached the panel.");
		return panel.Result!;
	}
}
