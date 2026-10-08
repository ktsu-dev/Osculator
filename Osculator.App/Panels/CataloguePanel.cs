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
using System.Threading.Tasks;
using Hexa.NET.ImGui;
using ktsu.ImGui.Widgets;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Data.CelesTrak;

/// <summary>
/// The catalogue: every active object CelesTrak publishes, searchable, filterable, and selectable.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue loads once, on a background thread, through <see cref="CelesTrakClient"/> and so
/// through its <see cref="ResponseCache"/>: the panel cannot ask CelesTrak more often than the cache
/// allows, and without a network it shows the last catalogue the cache holds, however old. The
/// newest epoch in the catalogue is shown beside the count, which is the honest answer to "how old
/// is this", whichever way it arrived.
/// </para>
/// <para>
/// Only the visible rows are drawn, through <see cref="ImGuiListClipper"/>, and the filtered list is
/// rebuilt only when the search or the filter changes (<see cref="CatalogueView"/>), so frame time
/// does not grow with the catalogue.
/// </para>
/// <para>
/// Clicking a row sets <see cref="CatalogueSelection"/>, which is what every other panel reads.
/// There is no object-type filter yet: the element sets do not say what an object is, and the
/// satellite catalogue that does is a separate source (#71).
/// </para>
/// </remarks>
internal static class CataloguePanel
{
	/// <summary>The CelesTrak group the catalogue is loaded from.</summary>
	internal const string Group = "active";

	/// <summary>
	/// How long a fetched catalogue is kept before CelesTrak may be asked again. Its guidelines ask
	/// for no more than one fetch per dataset every few hours.
	/// </summary>
	private static readonly TimeSpan RefetchInterval = TimeSpan.FromHours(2);

	private static readonly OrbitClass[] Classes = Enum.GetValues<OrbitClass>();

	private static readonly string[] ClassNames = [.. Classes.Select(Describe)];

	private static readonly bool[] ClassShown = [.. Classes.Select(_ => true)];

	/// <summary>The transport, which lives as long as the process does.</summary>
	private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

	private static readonly Lazy<CelesTrakClient> Client = new(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);

	private static SearchBoxOptions searchOptions = new SearchBoxRankedOptions(
		Label: "##catalogue-search",
		Hint: "Search by name, NORAD number or designator",
		FullWidth: true);

	private static string query = string.Empty;

	private static Task<CatalogueView>? loading;

	/// <summary>
	/// Draws the panel for one frame.
	/// </summary>
	internal static void Draw()
	{
		loading ??= LoadAsync();

		if (!loading.IsCompleted)
		{
			ImGui.TextUnformatted("Loading the catalogue from CelesTrak…");
			return;
		}

		if (!loading.IsCompletedSuccessfully)
		{
			Exception? failure = loading.Exception?.GetBaseException();
			ImGui.TextWrapped(failure?.Message ?? "The catalogue could not be loaded.");

			if (ImGui.Button("Retry"))
			{
				loading = LoadAsync();
			}

			return;
		}

		CatalogueView view = loading.Result;

		ImGuiWidgets.SearchBox(ref searchOptions, ref query);
		DrawClassFilter(view);

		IReadOnlyList<CatalogueEntry> rows = view.Update(query, ShownMask());

		ImGui.TextUnformatted(string.Create(
			CultureInfo.InvariantCulture,
			$"{rows.Count:N0} of {view.All.Count:N0} objects · newest epoch {DescribeAge(view.NewestEpoch)}"));

		DrawTable(rows);
	}

	/// <summary>
	/// Renders an orbit class as the abbreviation everyone uses for it.
	/// </summary>
	/// <param name="orbitClass">The class.</param>
	/// <returns>Its short name.</returns>
	internal static string Describe(OrbitClass orbitClass) => orbitClass switch
	{
		OrbitClass.Leo => "LEO",
		OrbitClass.Meo => "MEO",
		OrbitClass.Geo => "GEO",
		OrbitClass.Heo => "HEO",
		OrbitClass.BeyondGeo => "Beyond GEO",
		_ => "Unclassified",
	};

	/// <summary>
	/// Builds the class mask the checkboxes describe.
	/// </summary>
	/// <returns>The mask.</returns>
	private static int ShownMask()
	{
		int mask = 0;

		for (int i = 0; i < Classes.Length; i++)
		{
			if (ClassShown[i])
			{
				mask |= CatalogueView.MaskOf(Classes[i]);
			}
		}

		return Array.TrueForAll(ClassShown, shown => shown) ? CatalogueView.AllClasses : mask;
	}

	/// <summary>
	/// Draws one checkbox per orbit class that has anything in it, labelled with its count.
	/// </summary>
	/// <param name="view">The catalogue.</param>
	private static void DrawClassFilter(CatalogueView view)
	{
		bool first = true;

		for (int i = 0; i < Classes.Length; i++)
		{
			int count = view.ClassCounts[(int)Classes[i]];

			if (count == 0)
			{
				continue;
			}

			if (!first)
			{
				ImGui.SameLine();
			}

			first = false;
			ImGui.Checkbox(string.Create(CultureInfo.InvariantCulture, $"{ClassNames[i]} ({count:N0})"), ref ClassShown[i]);
		}
	}

	/// <summary>
	/// Draws the visible rows of the table.
	/// </summary>
	/// <param name="rows">Every row that passes the filter.</param>
	private static void DrawTable(IReadOnlyList<CatalogueEntry> rows)
	{
		const ImGuiTableFlags flags = ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter
			| ImGuiTableFlags.BordersV | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit;

		if (!ImGui.BeginTable("catalogue", 9, flags, new Vector2(0, ImGui.GetContentRegionAvail().Y)))
		{
			return;
		}

		ImGui.TableSetupScrollFreeze(0, 1);
		ImGui.TableSetupColumn("NORAD");
		ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
		ImGui.TableSetupColumn("Designator");
		ImGui.TableSetupColumn("Class");
		ImGui.TableSetupColumn("Perigee km");
		ImGui.TableSetupColumn("Apogee km");
		ImGui.TableSetupColumn("Incl °");
		ImGui.TableSetupColumn("Period min");
		ImGui.TableSetupColumn("Epoch age");
		ImGui.TableHeadersRow();

		ElementSet? selected = CatalogueSelection.Selected;
		CultureInfo culture = CultureInfo.InvariantCulture;

		ImGuiListClipper clipper = default;
		clipper.Begin(rows.Count);

		while (clipper.Step())
		{
			for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
			{
				CatalogueEntry entry = rows[i];
				ElementSet elements = entry.Elements;
				OrbitGeometry geometry = entry.Geometry;

				ImGui.TableNextRow();
				ImGui.TableNextColumn();

				// The catalogue number is unique, so it serves as the row's ID as well as its label.
				if (ImGui.Selectable(entry.NoradText, ReferenceEquals(elements, selected), ImGuiSelectableFlags.SpanAllColumns))
				{
					CatalogueSelection.Select(elements);
				}

				ImGui.TableNextColumn();
				ImGui.TextUnformatted(elements.ObjectName);
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(elements.ObjectId);
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(ClassNames[(int)geometry.Class]);
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(geometry.PerigeeAltitudeKm.ToString("F0", culture));
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(geometry.ApogeeAltitudeKm.ToString("F0", culture));
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(elements.Inclination.ToString("F2", culture));
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(geometry.PeriodMinutes.ToString("F1", culture));
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(DescribeAge(elements.Epoch));
			}
		}

		clipper.End();
		ImGui.EndTable();
	}

	/// <summary>
	/// Renders how long ago an epoch was.
	/// </summary>
	/// <param name="epoch">The epoch, in UTC.</param>
	/// <returns>A short description, in hours below two days and in days above.</returns>
	private static string DescribeAge(DateTime? epoch)
	{
		if (epoch is null)
		{
			return "none";
		}

		TimeSpan age = DateTime.UtcNow - epoch.Value;

		return age.TotalDays < 2
			? string.Create(CultureInfo.InvariantCulture, $"{age.TotalHours:F1} h")
			: string.Create(CultureInfo.InvariantCulture, $"{age.TotalDays:F1} d");
	}

	/// <summary>
	/// Loads the catalogue on a background thread.
	/// </summary>
	/// <returns>The catalogue, in catalogue-number order.</returns>
	private static Task<CatalogueView> LoadAsync() => Task.Run(async () =>
	{
		IReadOnlyList<ElementSet> elements = (await Client.Value.GetGroupAsync(Group).ConfigureAwait(false)).Value;

		return new CatalogueView([.. elements.OrderBy(e => e.NoradCatalogId).Select(e => new CatalogueEntry(e))]);
	});

	/// <summary>
	/// Creates the client the panel loads through, caching under the user's local application data.
	/// </summary>
	/// <returns>The client.</returns>
	private static CelesTrakClient CreateClient()
	{
		Http.DefaultRequestHeaders.UserAgent.ParseAdd("Osculator (+https://github.com/ktsu-dev/Osculator)");

		string directory = Path.Join(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"Osculator",
			"celestrak");

		return new CelesTrakClient(Http, new ResponseCache(directory, RefetchInterval, TimeProvider.System));
	}
}
