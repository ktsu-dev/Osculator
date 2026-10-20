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
using ktsu.Osculator.Data.SpaceTrack;

/// <summary>
/// The catalogue: payloads, rocket bodies and debris, searchable, filterable by orbit class and
/// object type, and selectable.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue loads once, on a background thread, through <see cref="CatalogueLoader"/>: from
/// Space-Track's full <c>gp</c> catalogue when an account is configured, and otherwise from
/// CelesTrak's active, recent-launch and debris groups, which the panel labels as partial. Object
/// types come from the satellite catalogue, joined on catalogue number. Every request goes through
/// a <see cref="ResponseCache"/> (and, for Space-Track, its rate limiter), so the panel cannot ask
/// either service more often than they allow, and without a network it shows the last catalogue the
/// cache holds, however old. The newest epoch in the catalogue is shown beside the count, which is
/// the honest answer to "how old is this", whichever way it arrived.
/// </para>
/// <para>
/// A Space-Track account is named by the <c>OSCULATOR_SPACETRACK_USER</c> environment variable, and
/// its password is read from the operating system's credential store by
/// <see cref="OsCredentialStore"/>, never from a file. With one configured, the selected object's
/// element-set history can be seeded from Space-Track into the <see cref="SnapshotStore"/>.
/// </para>
/// <para>
/// Only the visible rows are drawn, through <see cref="ImGuiListClipper"/>, and the filtered list is
/// rebuilt only when the search or the filter changes (<see cref="CatalogueView"/>), so frame time
/// does not grow with the catalogue.
/// </para>
/// <para>
/// Clicking a row sets <see cref="CatalogueSelection"/>, which is what every other panel reads.
/// </para>
/// </remarks>
internal static class CataloguePanel
{
	/// <summary>The CelesTrak group of active satellites, which every catalogue source falls back to.</summary>
	internal const string Group = CatalogueLoader.RequiredGroup;

	/// <summary>The environment variable naming the Space-Track account, whose password is in the OS credential store.</summary>
	internal const string SpaceTrackUserVariable = "OSCULATOR_SPACETRACK_USER";

	/// <summary>
	/// How long a fetched catalogue is kept before CelesTrak may be asked again. Its guidelines ask
	/// for no more than one fetch per dataset every few hours.
	/// </summary>
	private static readonly TimeSpan RefetchInterval = TimeSpan.FromHours(2);

	private static readonly OrbitClass[] Classes = Enum.GetValues<OrbitClass>();

	private static readonly string[] ClassNames = [.. Classes.Select(Describe)];

	private static readonly bool[] ClassShown = [.. Classes.Select(_ => true)];

	private static readonly SatcatObjectType[] Types = Enum.GetValues<SatcatObjectType>();

	private static readonly string[] TypeNames = [.. Types.Select(Describe)];

	private static readonly bool[] TypeShown = [.. Types.Select(_ => true)];

	/// <summary>The transport, which lives as long as the process does.</summary>
	private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

	private static readonly Lazy<CatalogueLoader> Loader = new(CreateLoader, LazyThreadSafetyMode.ExecutionAndPublication);

	private static SearchBoxOptions searchOptions = new SearchBoxRankedOptions(
		Label: "##catalogue-search",
		Hint: "Search by name, NORAD number or designator",
		FullWidth: true);

	private static string query = string.Empty;

	private static Task<CatalogueLoad>? loading;

	private static Task<string>? seeding;

	private static int seedingFor;

	/// <summary>
	/// Draws the panel for one frame.
	/// </summary>
	internal static void Draw()
	{
		loading ??= LoadAsync();

		if (!loading.IsCompleted)
		{
			ImGui.TextUnformatted("Loading the catalogue…");
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

		CatalogueLoad load = loading.Result;
		CatalogueView view = load.View;

		DrawSource(load);
		ImGuiWidgets.SearchBox(ref searchOptions, ref query);
		DrawClassFilter(view);
		DrawTypeFilter(load);

		IReadOnlyList<CatalogueEntry> rows = view.Update(query, ShownClassMask(), ShownTypeMask());

		ImGui.TextUnformatted(string.Create(
			CultureInfo.InvariantCulture,
			$"{rows.Count:N0} of {view.All.Count:N0} objects · newest epoch {DescribeAge(view.NewestEpoch)}"));

		DrawHistorySeeding();
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
	/// Renders an object type the way the satellite catalogue's own readers say it.
	/// </summary>
	/// <param name="objectType">The type.</param>
	/// <returns>Its short name.</returns>
	internal static string Describe(SatcatObjectType objectType) => objectType switch
	{
		SatcatObjectType.Payload => "Payload",
		SatcatObjectType.RocketBody => "Rocket body",
		SatcatObjectType.Debris => "Debris",
		_ => "Unknown",
	};

	/// <summary>
	/// Builds the type mask the checkboxes describe.
	/// </summary>
	/// <returns>The mask.</returns>
	private static int ShownTypeMask()
	{
		if (Array.TrueForAll(TypeShown, shown => shown))
		{
			return CatalogueView.AllTypes;
		}

		int mask = 0;

		for (int i = 0; i < Types.Length; i++)
		{
			if (TypeShown[i])
			{
				mask |= CatalogueView.MaskOf(Types[i]);
			}
		}

		return mask;
	}

	/// <summary>
	/// Builds the class mask the checkboxes describe.
	/// </summary>
	/// <returns>The mask.</returns>
	private static int ShownClassMask()
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
	/// Draws one checkbox per object type that has anything in it, labelled with its count.
	/// </summary>
	/// <param name="load">The catalogue.</param>
	private static void DrawTypeFilter(CatalogueLoad load)
	{
		if (!load.HasObjectTypes)
		{
			return;
		}

		bool first = true;

		for (int i = 0; i < Types.Length; i++)
		{
			int count = load.View.TypeCounts[(int)Types[i]];

			if (count == 0)
			{
				continue;
			}

			if (!first)
			{
				ImGui.SameLine();
			}

			first = false;
			ImGui.Checkbox(string.Create(CultureInfo.InvariantCulture, $"{TypeNames[i]} ({count:N0})##type"), ref TypeShown[i]);
		}
	}

	/// <summary>
	/// Says where the catalogue came from, whether it is all of it, and anything that did not load.
	/// </summary>
	/// <param name="load">The catalogue.</param>
	private static void DrawSource(CatalogueLoad load)
	{
		ImGui.TextWrapped(load.Source == CatalogueSource.SpaceTrack
			? "Source: Space-Track gp, the full on-orbit catalogue."
			: string.Create(
				CultureInfo.InvariantCulture,
				$"Source: CelesTrak ({string.Join(", ", CatalogueLoader.CelesTrakGroups)}). Partial: CelesTrak does not publish the whole catalogue. Name a Space-Track account in {SpaceTrackUserVariable} and store its password in the OS credential store for all of it."));

		foreach (string note in load.Notes)
		{
			ImGui.TextWrapped(note);
		}
	}

	/// <summary>
	/// Offers to seed the selected object's element-set history from Space-Track, and reports how
	/// the last seeding went.
	/// </summary>
	private static void DrawHistorySeeding()
	{
		ElementSet? selected = CatalogueSelection.Selected;

		if (!Loader.Value.CanSeedHistory || selected is null)
		{
			return;
		}

		bool running = seeding is { IsCompleted: false };

		using (new ScopedDisable(running))
		{
			if (ImGui.Button(string.Create(CultureInfo.InvariantCulture, $"Seed history of {selected.NoradCatalogId} from Space-Track")))
			{
				seedingFor = selected.NoradCatalogId;
				seeding = SeedAsync(seedingFor);
			}
		}

		if (seeding is null)
		{
			return;
		}

		ImGui.SameLine();
		ImGui.TextUnformatted(!seeding.IsCompleted
			? "Seeding…"
			: seeding.IsCompletedSuccessfully
				? seeding.Result
				: seeding.Exception?.GetBaseException().Message ?? "Seeding failed.");
	}

	/// <summary>
	/// Seeds one object's history on a background thread.
	/// </summary>
	/// <param name="noradCatalogId">The catalogue number.</param>
	/// <returns>What to report.</returns>
	private static Task<string> SeedAsync(int noradCatalogId) => Task.Run(async () =>
	{
		SnapshotStore store = new(Path.Join(DataDirectory(), "snapshots"));
		int added = await Loader.Value.SeedHistoryAsync(noradCatalogId, store).ConfigureAwait(false);

		return string.Create(
			CultureInfo.InvariantCulture,
			$"{noradCatalogId}: {added:N0} new element sets archived, {store.History(noradCatalogId).Count:N0} held.");
	});

	/// <summary>
	/// Draws the visible rows of the table.
	/// </summary>
	/// <param name="rows">Every row that passes the filter.</param>
	private static void DrawTable(IReadOnlyList<CatalogueEntry> rows)
	{
		const ImGuiTableFlags flags = ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter
			| ImGuiTableFlags.BordersV | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit;

		if (!ImGui.BeginTable("catalogue", 10, flags, new Vector2(0, ImGui.GetContentRegionAvail().Y)))
		{
			return;
		}

		ImGui.TableSetupScrollFreeze(0, 1);
		ImGui.TableSetupColumn("NORAD");
		ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
		ImGui.TableSetupColumn("Designator");
		ImGui.TableSetupColumn("Type");
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
				ImGui.TextUnformatted(TypeNames[(int)entry.ObjectType]);
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
	/// <returns>The catalogue, in catalogue-number order, with where it came from.</returns>
	private static Task<CatalogueLoad> LoadAsync() => Task.Run(() => Loader.Value.LoadAsync());

	/// <summary>
	/// Gets the directory the panel's caches and archive live under, in the user's local application data.
	/// </summary>
	/// <returns>The directory.</returns>
	private static string DataDirectory() => Path.Join(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Osculator");

	/// <summary>
	/// Creates the loader, with a Space-Track client only when an account is named.
	/// </summary>
	/// <returns>The loader.</returns>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Reliability", "CA2000:Dispose objects before losing scope",
		Justification = "The Space-Track client lives as long as the process, like the shared HttpClient beside it.")]
	private static CatalogueLoader CreateLoader()
	{
		Http.DefaultRequestHeaders.UserAgent.ParseAdd("Osculator (+https://github.com/ktsu-dev/Osculator)");

		ResponseCache celesTrakCache = new(Path.Join(DataDirectory(), "celestrak"), RefetchInterval, TimeProvider.System);

		return new CatalogueLoader(
			new CelesTrakClient(Http, celesTrakCache),
			new SatcatClient(Http, celesTrakCache),
			CreateSpaceTrackClient());
	}

	/// <summary>
	/// Creates a Space-Track client for the account <see cref="SpaceTrackUserVariable"/> names.
	/// </summary>
	/// <returns>The client, or null when no account is named.</returns>
	/// <remarks>
	/// Only the account's login is read here. The password is read from the credential store when
	/// the client actually has to log in, which a fresh cache spares it.
	/// </remarks>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Reliability", "CA2000:Dispose objects before losing scope",
		Justification = "The transport and the client live as long as the process, like the shared HttpClient beside them.")]
	private static SpaceTrackClient? CreateSpaceTrackClient()
	{
		string? identity = Environment.GetEnvironmentVariable(SpaceTrackUserVariable);

		if (string.IsNullOrWhiteSpace(identity))
		{
			return null;
		}

		return new SpaceTrackClient(
			SpaceTrackClient.CreateHttpClient(),
			new ResponseCache(Path.Join(DataDirectory(), "spacetrack"), RefetchInterval, TimeProvider.System),
			new SpaceTrackRateLimiter(TimeProvider.System),
			new OsCredentialStore(identity.Trim()));
	}
}
