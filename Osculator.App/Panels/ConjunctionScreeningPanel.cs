// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using Hexa.NET.ImGui;
using ktsu.Osculator.App.Shell;
using ktsu.Osculator.Core.Conjunction;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.CelesTrak;

/// <summary>
/// Conjunction screening: which catalogued objects pass close, when, and how close in each storage type.
/// </summary>
/// <remarks>
/// <para>
/// Two scopes. <strong>Selected against catalogue</strong> screens the object picked in the
/// catalogue against every other active object, which is the question an operator asks.
/// <strong>All against all</strong> screens every pair among the objects whose name contains a
/// term, capped at <see cref="ConjunctionSweep.MaximumGroupSize"/> objects; a whole catalogue all
/// against all is tens of millions of pairs and most of the low-earth ones survive the shell filter.
/// </para>
/// <para>
/// The screen runs in <see langword="double"/>. Selecting a row measures that approach again in all
/// four storage types, each finding its own time of closest approach and its own miss distance from
/// its own states, and shows how far each lands from the thirty-digit reference. That disagreement is
/// the spec's cancellation case: two vectors seven thousand kilometres long differenced down to metres.
/// </para>
/// <para>
/// The catalogue comes through a <see cref="CelesTrakClient"/> on the same cache directory and refetch
/// interval as the catalogue panel, and is only asked for when a screen starts, so in practice it is
/// read from the cache the catalogue panel already filled rather than fetched again.
/// </para>
/// </remarks>
internal sealed class ConjunctionScreeningPanel : Panel
{
	/// <summary>The panel's title.</summary>
	internal const string PanelTitle = "Conjunction screening";

	/// <summary>The background job key for a screen.</summary>
	internal const string ScreenKey = "conjunction-screen";

	/// <summary>The background job key for a per-type comparison.</summary>
	internal const string CompareKey = "conjunction-compare";

	private const double MinutesPerHour = 60.0;

	private static readonly TimeSpan RefetchInterval = TimeSpan.FromHours(2);

	private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

	private static readonly Lazy<CelesTrakClient> Client = new(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);

	private readonly ConjunctionProgress progress = new();

	private IReadOnlyList<ElementSet>? catalogue;
	private bool allAgainstAll;
	private string groupTerm = "ISS";
	private double thresholdKm = 10.0;
	private double windowHours = 24.0;
	private string? status;

	private DateTime windowStartUtc;
	private int selectedRow = -1;

	/// <summary>Gets the latest finished screen.</summary>
	internal ConjunctionSweepResult? Result { get; private set; }

	/// <summary>Gets the per-type measurement of the selected row, once it has finished.</summary>
	internal PairComparison? Comparison { get; private set; }

	/// <inheritdoc/>
	protected override string Title => PanelTitle;

	/// <summary>
	/// Starts a screen of the selected object against a catalogue, without loading one.
	/// </summary>
	/// <param name="primary">The object to screen.</param>
	/// <param name="objects">The catalogue.</param>
	/// <param name="options">The window and thresholds.</param>
	/// <param name="startUtc">The window's start, for display.</param>
	internal void StartScreen(ElementSet primary, IReadOnlyList<ElementSet> objects, ConjunctionScreenOptions options, DateTime startUtc)
	{
		catalogue = objects;
		Begin(token => ConjunctionSweep.SelectedAgainst(primary, objects, options, progress, token), startUtc);
	}

	/// <summary>
	/// Starts the per-type measurement of one row of the latest screen.
	/// </summary>
	/// <param name="row">The row's index in <see cref="ConjunctionSweepResult.Conjunctions"/>.</param>
	internal void SelectRow(int row)
	{
		if (Result is null || row < 0 || row >= Result.Conjunctions.Count || row == selectedRow)
		{
			return;
		}

		selectedRow = row;
		Comparison = null;

		ConjunctionSweepResult screened = Result;
		ConjunctionEvent conjunction = screened.Conjunctions[row];
		ElementSet primary = screened.Objects[conjunction.PrimaryCatalogId];
		ElementSet secondary = screened.Objects[conjunction.SecondaryCatalogId];

		Work.Run(
			CompareKey,
			token => ConjunctionSweep.CompareAcrossStorageTypes(primary, secondary, screened.Options, conjunction, token),
			measured => Comparison = measured,
			error => status = error.GetBaseException().Message);
	}

	/// <inheritdoc/>
	protected override void Draw()
	{
		DrawControls();

		if (status is not null)
		{
			ImGui.TextWrapped(status);
		}

		if (Result is not null)
		{
			ImGui.Separator();
			DrawSummary(Result);
			DrawConjunctions(Result);
			DrawComparison();
			DrawFailures(Result);
		}
	}

	/// <inheritdoc/>
	protected override void OnDismissed()
	{
		Work.Cancel(ScreenKey);
		Work.Cancel(CompareKey);
	}

	private static CelesTrakClient CreateClient()
	{
		Http.DefaultRequestHeaders.UserAgent.ParseAdd("Osculator (+https://github.com/ktsu-dev/Osculator)");

		string directory = Path.Join(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"Osculator",
			"celestrak");

		return new CelesTrakClient(Http, new ResponseCache(directory, RefetchInterval, TimeProvider.System));
	}

	private static string Describe(ElementSet? elements, int catalogId) =>
		elements?.ObjectName is { Length: > 0 } name
			? string.Create(CultureInfo.InvariantCulture, $"{catalogId} {name}")
			: catalogId.ToString(CultureInfo.InvariantCulture);

	private static string FormatDifference(double? kilometres) => kilometres switch
	{
		null => "—",
		0.0 => "0",
		double km => string.Create(CultureInfo.InvariantCulture, $"{km * 1000.0:+0.000e+00;-0.000e+00} m"),
	};

	private bool Running => Work.IsRunning(ScreenKey);

	private void DrawControls()
	{
		if (ImGui.RadioButton("Selected object against the catalogue", !allAgainstAll))
		{
			allAgainstAll = false;
		}

		ImGui.SameLine();

		if (ImGui.RadioButton("All against all, by name", allAgainstAll))
		{
			allAgainstAll = true;
		}

		ElementSet? selected = CatalogueSelection.Selected;

		if (allAgainstAll)
		{
			ImGui.InputText("Name contains", ref groupTerm, 64);
		}
		else
		{
			ImGui.TextUnformatted(selected is null
				? "Select an object in the catalogue to screen it."
				: string.Create(CultureInfo.InvariantCulture, $"Screening {Describe(selected, selected.NoradCatalogId)}"));
		}

		ImGui.InputDouble("Threshold (km)", ref thresholdKm, 1.0, 10.0, "%.3f");
		ImGui.InputDouble("Window (hours)", ref windowHours, 1.0, 24.0, "%.1f");

		bool valid = double.IsFinite(thresholdKm) && thresholdKm >= 0.0
			&& double.IsFinite(windowHours) && windowHours > 0.0
			&& (allAgainstAll ? groupTerm.Trim().Length > 0 : selected is not null);

		if (Running)
		{
			int total = progress.Total;
			int done = progress.Done;
			ImGui.ProgressBar(
				total == 0 ? 0.0f : (float)done / total,
				new Vector2(-1.0f, 0.0f),
				total == 0 ? "Loading the catalogue…" : string.Create(CultureInfo.InvariantCulture, $"{done:N0} of {total:N0} pairs propagated"));

			if (ImGui.Button("Cancel"))
			{
				Work.Cancel(ScreenKey);
				status = "Screen cancelled.";
			}

			return;
		}

		ImGui.BeginDisabled(!valid);

		if (ImGui.Button("Screen"))
		{
			StartFromControls(selected);
		}

		ImGui.EndDisabled();
	}

	private void StartFromControls(ElementSet? selected)
	{
		DateTime startUtc = DateTime.UtcNow;
		ConjunctionScreenOptions options = new()
		{
			WindowStart = JulianDate.FromUtc(startUtc),
			WindowMinutes = windowHours * MinutesPerHour,
			ThresholdKm = thresholdKm,
		};

		IReadOnlyList<ElementSet>? loaded = catalogue;
		bool group = allAgainstAll;
		string term = groupTerm;

		Begin(
			token =>
			{
				IReadOnlyList<ElementSet> objects = loaded ?? Client.Value.GetGroupAsync(CataloguePanel.Group, token).GetAwaiter().GetResult().Value;
				Volatile.Write(ref catalogue, objects);

				if (!group)
				{
					return ConjunctionSweep.SelectedAgainst(selected!, objects, options, progress, token);
				}

				IReadOnlyList<ElementSet> members = ConjunctionSweep.Group(objects, term);

				if (members.Count > ConjunctionSweep.MaximumGroupSize)
				{
					throw new InvalidOperationException(string.Create(
						CultureInfo.InvariantCulture,
						$"{members.Count:N0} objects match \"{term}\". All against all is limited to {ConjunctionSweep.MaximumGroupSize}; narrow the name."));
				}

				return ConjunctionSweep.AllAgainstAll(members, options, progress, token);
			},
			startUtc);
	}

	private void Begin(Func<CancellationToken, ConjunctionSweepResult> work, DateTime startUtc)
	{
		progress.Start(0);
		status = null;
		Work.Cancel(CompareKey);

		Work.Run(
			ScreenKey,
			work,
			finished =>
			{
				Result = finished;
				windowStartUtc = startUtc;
				selectedRow = -1;
				Comparison = null;
			},
			error => status = error.GetBaseException().Message);
	}

	private void DrawSummary(ConjunctionSweepResult screened)
	{
		CultureInfo culture = CultureInfo.InvariantCulture;
		double rejected = screened.PairCount == 0 ? 0.0 : 100.0 * screened.PairsRejectedByFilter / screened.PairCount;

		ImGui.TextUnformatted(string.Create(
			culture,
			$"{screened.Conjunctions.Count:N0} approaches within {screened.Options.ThresholdKm:G4} km over {screened.Options.WindowMinutes / MinutesPerHour:G4} h from {windowStartUtc:yyyy-MM-dd HH:mm} UTC"));
		ImGui.TextUnformatted(string.Create(
			culture,
			$"{screened.PairCount:N0} pairs · {screened.PairsRejectedByFilter:N0} rejected by the shell filter ({rejected:F1}%) · {screened.PairsPropagated:N0} propagated in double"));
	}

	private void DrawConjunctions(ConjunctionSweepResult screened)
	{
		const ImGuiTableFlags flags = ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter
			| ImGuiTableFlags.BordersV | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit;

		if (screened.Conjunctions.Count == 0)
		{
			ImGui.TextUnformatted("Nothing passes within the threshold in this window.");
			return;
		}

		float height = Math.Min(ImGui.GetTextLineHeightWithSpacing() * (screened.Conjunctions.Count + 2), ImGui.GetContentRegionAvail().Y * 0.5f);

		if (!ImGui.BeginTable("conjunctions", 5, flags, new Vector2(0, height)))
		{
			return;
		}

		ImGui.TableSetupScrollFreeze(0, 1);
		ImGui.TableSetupColumn("Primary", ImGuiTableColumnFlags.WidthStretch);
		ImGui.TableSetupColumn("Secondary", ImGuiTableColumnFlags.WidthStretch);
		ImGui.TableSetupColumn("Closest (UTC)");
		ImGui.TableSetupColumn("Miss km");
		ImGui.TableSetupColumn("Rel km/s");
		ImGui.TableHeadersRow();

		CultureInfo culture = CultureInfo.InvariantCulture;
		ImGuiListClipper clipper = default;
		clipper.Begin(screened.Conjunctions.Count);

		while (clipper.Step())
		{
			for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
			{
				ConjunctionEvent conjunction = screened.Conjunctions[i];
				screened.Objects.TryGetValue(conjunction.PrimaryCatalogId, out ElementSet? primary);
				screened.Objects.TryGetValue(conjunction.SecondaryCatalogId, out ElementSet? secondary);

				ImGui.TableNextRow();
				ImGui.TableNextColumn();

				if (ImGui.Selectable(string.Create(culture, $"{Describe(primary, conjunction.PrimaryCatalogId)}##row{i}"), i == selectedRow, ImGuiSelectableFlags.SpanAllColumns))
				{
					SelectRow(i);
				}

				ImGui.TableNextColumn();
				ImGui.TextUnformatted(Describe(secondary, conjunction.SecondaryCatalogId));
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(windowStartUtc.AddMinutes(conjunction.MinutesFromWindowStart).ToString("yyyy-MM-dd HH:mm:ss.fff", culture));
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(conjunction.MissDistanceKm.ToString("F4", culture));
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(conjunction.RelativeSpeedKmPerSecond.ToString("F3", culture));
			}
		}

		clipper.End();
		ImGui.EndTable();
	}

	private void DrawComparison()
	{
		if (selectedRow < 0)
		{
			ImGui.TextDisabled("Select an approach to measure it in all four storage types.");
			return;
		}

		if (Comparison is null)
		{
			ImGui.TextUnformatted("Measuring in float, double, decimal and PreciseNumber…");
			return;
		}

		ImGui.TextUnformatted("The same approach, found and measured in each storage type:");

		const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV | ImGuiTableFlags.SizingFixedFit;

		if (!ImGui.BeginTable("per-type", 5, flags))
		{
			return;
		}

		ImGui.TableSetupColumn("Storage type");
		ImGui.TableSetupColumn("Miss km");
		ImGui.TableSetupColumn("Miss vs reference");
		ImGui.TableSetupColumn("Time vs reference");
		ImGui.TableSetupColumn("Search time");
		ImGui.TableHeadersRow();

		CultureInfo culture = CultureInfo.InvariantCulture;

		foreach (StorageMiss run in Comparison.Runs)
		{
			ImGui.TableNextRow();
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(run.StorageName);
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(run.Approach is ConjunctionEvent found ? found.MissDistanceKm.ToString("F9", culture) : "not found");
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(ReferenceEquals(run, Comparison.Reference) ? "reference" : FormatDifference(Comparison.MissDifferenceKm(run)));
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(ReferenceEquals(run, Comparison.Reference)
				? "reference"
				: Comparison.TimeDifferenceSeconds(run) is double seconds ? string.Create(culture, $"{seconds * 1e6:+0.000;-0.000} µs") : "—");
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(string.Create(culture, $"{run.Seconds * 1000.0:F1} ms"));
		}

		ImGui.EndTable();
	}

	private static void DrawFailures(ConjunctionSweepResult screened)
	{
		if (screened.Failures.Count == 0)
		{
			return;
		}

		if (!ImGui.CollapsingHeader(string.Create(CultureInfo.InvariantCulture, $"{screened.Failures.Count:N0} objects the model could not follow")))
		{
			return;
		}

		foreach (ScreeningFailure failure in screened.Failures.Take(200))
		{
			screened.Objects.TryGetValue(failure.CatalogId, out ElementSet? elements);
			ImGui.TextUnformatted(string.Create(
				CultureInfo.InvariantCulture,
				$"{Describe(elements, failure.CatalogId)}: {failure.Error} at {failure.MinutesFromWindowStart:F1} min"));
		}
	}
}
