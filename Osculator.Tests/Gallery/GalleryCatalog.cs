// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests.Gallery;

using System;
using System.Collections.Generic;
using ktsu.Osculator.App;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.App.Shell;
using ktsu.Osculator.Core.Conjunction;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Time;

/// <summary>Every picture in the application gallery, in the order the index shows them.</summary>
/// <remarks>
/// <para>
/// Each stage starts from the shell <c>Program</c> runs, with every panel the application registers
/// open and its first frames drawn. A new panel earns a picture by adding an entry here; nothing else
/// needs to change, because the runner, the index and the workflow all read this list.
/// </para>
/// <para>
/// Nothing here reaches the network or the wall clock. The storage comparison and the element
/// inspector draw the element sets compiled into the application, at their own epochs, and the
/// conjunction screen runs over the constructed objects <see cref="ConjunctionScreeningPanelTests"/>
/// reasons about, because the panel's own Screen button fetches today's catalogue from CelesTrak and
/// starts its window at the current instant. The panels the shell does not register yet - the
/// catalogue, the globe, the time inspector and the residual plots - have no picture, since the
/// application does not show them.
/// </para>
/// </remarks>
internal static class GalleryCatalog
{
	/// <summary>The minutes in a day.</summary>
	private const double MinutesPerDay = 1440.0;

	/// <summary>The height of a picture of the storage comparison alone, which is as tall as what it draws.</summary>
	private const int StorageComparisonDisplayHeight = 440;

	/// <summary>The height the storage comparison takes in the overview, the rest of its column going to the conjunction screen.</summary>
	private const float StorageComparisonHeight = 440f;

	/// <summary>The instant the constructed conjunction objects are fitted at, and the screen starts.</summary>
	private static readonly DateTime ScreenStart = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

	/// <summary>Gets the entries.</summary>
	internal static IReadOnlyList<GalleryEntry> Entries { get; } =
	[
		new(
			"The application",
			"Every panel the shell registers, laid out side by side: the storage comparison above the conjunction screen on the left, and the element inspector down the right. The View menu reopens any panel that is closed.",
			stage =>
			{
				stage.ShowOverview<StorageComparisonWindow, ConjunctionScreeningPanel, ElementInspectorPanel>(StorageComparisonHeight);
				AwaitStorageComparison(stage, "ISS (ZARYA)", MinutesPerDay);
				_ = Screen(stage);
			})
		{
			Display = (1600, 1000),
		},
		new(
			"Storage comparison: the ISS a day out",
			"One element set propagated a day past its epoch in float, double, decimal and 50-digit PreciseNumber. Each run's arithmetic error is its distance from the PreciseNumber run, so the model and the inputs are held fixed and only the arithmetic differs. Double's error is far below anything the element set itself can resolve; float's is not.",
			stage =>
			{
				stage.ShowAlone<StorageComparisonWindow>();
				AwaitStorageComparison(stage, "ISS (ZARYA)", MinutesPerDay);
			})
		{
			Display = (1280, StorageComparisonDisplayHeight),
		},
		new(
			"Storage comparison: a Molniya orbit a week out",
			"The same measurement on a highly eccentric deep-space orbit from the published verification set, seven days past its epoch. The resolution table above it is fixed at a 7,000 km radius, so it says how coarse each storage type is at a low orbit's scale rather than at this one's apogee.",
			stage =>
			{
				stage.ShowAlone<StorageComparisonWindow>();
				AwaitStorageComparison(stage, "09880 (Molniya)", 7.0 * MinutesPerDay);
			})
		{
			Display = (1280, StorageComparisonDisplayHeight),
		},
		new(
			"Element inspector",
			"Every orbit field of the ISS element set with the step its format writes it to, and how far one step moves the propagated position a day out. Mean motion's two derivatives move it not at all, because SGP4 never reads them.",
			stage => stage.ShowAlone<ElementInspectorPanel>())
		{
			Display = (1280, 1080),
		},
		new(
			"Conjunction screening",
			"A station-like orbit screened against two constructed objects over a day: one whose plane crosses the station's with the phase tuned to a 13 m pass, and a geostationary one the perigee-apogee filter rejects before anything is propagated.",
			stage =>
			{
				stage.ShowAlone<ConjunctionScreeningPanel>();
				_ = Screen(stage);
			}),
		new(
			"A close approach in every storage type",
			"Selecting an approach finds it again in float, double, decimal and PreciseNumber over a narrow window around it. The miss distance is the difference of two nearly equal positions, so float is about two metres out on a 13 m pass while double agrees with the reference to about ten nanometres.",
			stage =>
			{
				stage.ShowAlone<ConjunctionScreeningPanel>();
				ConjunctionScreeningPanel panel = Screen(stage);
				panel.SelectRow(0);
				stage.Settle(() => panel.Comparison is not null, 5000, "the approach to be measured in every storage type");
			}),
	];

	private static ElementSet Station => Constructed(90001, "STATION");

	private static ElementSet Crossing => Constructed(90003, "CROSSING DEBRIS", meanAnomaly: 347.526, raan: 120.0);

	private static ElementSet Geostationary => Constructed(90004, "GEOSTATIONARY", inclination: 0.05, meanMotion: 1.0027);

	/// <summary>
	/// Chooses an object and an instant in the storage comparison, and waits for that measurement to
	/// land with nothing further in flight.
	/// </summary>
	private static void AwaitStorageComparison(GalleryStage stage, string objectName, double minutes)
	{
		StorageComparisonPanel.Select(objectName, minutes);
		stage.Settle(
			() => StorageComparisonPanel.Latest is { } latest
				&& string.Equals(latest.ObjectName, objectName, StringComparison.Ordinal)
				&& Math.Abs(latest.Minutes - minutes) < 1e-9
				&& !StorageComparisonPanel.IsMeasuring,
			5000,
			$"the storage comparison of {objectName}");
	}

	/// <summary>
	/// Selects the constructed station and screens it against the constructed objects, over the
	/// window and threshold the panel's own controls start at, and waits for the result.
	/// </summary>
	private static ConjunctionScreeningPanel Screen(GalleryStage stage)
	{
		ElementSet station = Station;
		CatalogueSelection.Select(station);

		ConjunctionScreeningPanel panel = stage.Panel<ConjunctionScreeningPanel>();
		ConjunctionScreenOptions options = new()
		{
			WindowStart = JulianDate.FromUtc(ScreenStart),
			WindowMinutes = MinutesPerDay,
			ThresholdKm = 10.0,
		};

		panel.StartScreen(station, [station, Crossing, Geostationary], options, ScreenStart);
		stage.Settle(() => panel.Result is not null, 2000, "the conjunction screen");
		return panel;
	}

	private static ElementSet Constructed(int catalogId, string name, double inclination = 51.6, double meanMotion = 15.5, double meanAnomaly = 0.0, double raan = 100.0) => new()
	{
		ObjectName = name,
		ObjectId = "2026-001A",
		NoradCatalogId = catalogId,
		Epoch = ScreenStart,
		EpochJulianDate = JulianDate.FromUtc(ScreenStart),
		MeanMotion = meanMotion,
		Eccentricity = 0.0005,
		Inclination = inclination,
		RightAscensionOfAscendingNode = raan,
		ArgumentOfPericenter = 90.0,
		MeanAnomaly = meanAnomaly,
		BStar = 0.0001,
	};
}
