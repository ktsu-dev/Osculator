// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Data.SpaceTrack;

/// <summary>Where a loaded catalogue's element sets came from.</summary>
internal enum CatalogueSource
{
	/// <summary>Space-Track's <c>gp</c> class: every object on orbit, debris and rocket bodies included.</summary>
	SpaceTrack,

	/// <summary>CelesTrak's public groups, which together are only part of the catalogue.</summary>
	CelesTrak,
}

/// <summary>
/// A loaded catalogue, and what the panel has to say about how it was loaded.
/// </summary>
/// <param name="View">The rows.</param>
/// <param name="Source">Where the element sets came from.</param>
/// <param name="IsPartial">Whether the catalogue is known to leave objects out.</param>
/// <param name="HasObjectTypes">Whether the satellite catalogue was joined, so object types mean something.</param>
/// <param name="Notes">Anything that did not load, one sentence each, for the panel to show.</param>
internal sealed record CatalogueLoad(
	CatalogueView View,
	CatalogueSource Source,
	bool IsPartial,
	bool HasObjectTypes,
	IReadOnlyList<string> Notes);

/// <summary>
/// Loads the catalogue from whichever source can give the most of it, and joins it to the satellite
/// catalogue for object types.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Space-Track first, when there is an account.</strong> Its <c>gp</c> class is the whole
/// on-orbit catalogue, which is the only way to get every rocket body and every fragment. If it
/// fails — no password stored, a refused login, the rate limiter, no network and nothing cached —
/// the load falls back to CelesTrak and says why, rather than showing nothing.
/// </para>
/// <para>
/// <strong>CelesTrak otherwise, and it is partial.</strong> CelesTrak does not redistribute the
/// full catalogue as a group. <c>active</c> is payloads only; the four debris groups add the
/// fragments of the four great fragmentation events, and <c>last-30-days</c> adds whatever was
/// launched recently, which is where the rocket bodies CelesTrak publishes are. Together that is
/// the part of the debris population the public groups cover, not all of it, and
/// <see cref="CatalogueLoad.IsPartial"/> says so. Groups overlap, so an object in several is kept
/// once, with its newest element set.
/// </para>
/// <para>
/// Every request goes through the clients' caches and, for Space-Track, its rate limiter; nothing
/// here talks to the network directly. The groups are fetched one after another rather than
/// together, because CelesTrak is run by one person. Only <c>active</c> is required: a debris
/// group the service no longer answers for is a note, not a failure. The satellite catalogue is
/// optional the same way: without it every object is <see cref="SatcatObjectType.Unknown"/>.
/// </para>
/// </remarks>
/// <param name="celesTrak">The CelesTrak element-set client.</param>
/// <param name="satcat">The satellite catalogue client.</param>
/// <param name="spaceTrack">The Space-Track client, or null when no account is configured.</param>
internal sealed class CatalogueLoader(CelesTrakClient celesTrak, SatcatClient satcat, SpaceTrackClient? spaceTrack)
{
	/// <summary>The CelesTrak group without which there is no catalogue.</summary>
	internal const string RequiredGroup = "active";

	/// <summary>
	/// The CelesTrak groups the catalogue is assembled from without a Space-Track account, required
	/// group first.
	/// </summary>
	internal static readonly IReadOnlyList<string> CelesTrakGroups =
	[
		RequiredGroup,
		"last-30-days",
		"cosmos-1408-debris",
		"fengyun-1c-debris",
		"iridium-33-debris",
		"cosmos-2251-debris",
	];

	/// <summary>Gets whether a Space-Track account is configured, which is what history seeding needs.</summary>
	internal bool CanSeedHistory => spaceTrack is not null;

	/// <summary>
	/// Loads the catalogue.
	/// </summary>
	/// <param name="cancellationToken">Cancels the load.</param>
	/// <returns>The catalogue, in catalogue-number order, with how it was loaded.</returns>
	/// <exception cref="CelesTrakException">
	/// Space-Track was not used or failed, and CelesTrak's <c>active</c> group could not be loaded
	/// from the service or the cache.
	/// </exception>
	internal async Task<CatalogueLoad> LoadAsync(CancellationToken cancellationToken = default)
	{
		List<string> notes = [];
		IReadOnlyList<ElementSet>? elements = null;
		CatalogueSource source = CatalogueSource.SpaceTrack;

		if (spaceTrack is not null)
		{
			try
			{
				elements = await spaceTrack.GetCatalogueAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (SpaceTrackException failure)
			{
				notes.Add("Space-Track could not be used, so the catalogue is CelesTrak's: " + failure.Message);
			}
		}

		if (elements is null)
		{
			source = CatalogueSource.CelesTrak;
			elements = await LoadCelesTrakAsync(notes, cancellationToken).ConfigureAwait(false);
		}

		IReadOnlyDictionary<int, SatcatObjectType>? types = await LoadTypesAsync(notes, cancellationToken).ConfigureAwait(false);

		CatalogueEntry[] entries = [.. elements
			.OrderBy(set => set.NoradCatalogId)
			.Select(set => new CatalogueEntry(
				set,
				types is not null && types.TryGetValue(set.NoradCatalogId, out SatcatObjectType type) ? type : SatcatObjectType.Unknown))];

		return new CatalogueLoad(
			new CatalogueView(entries),
			source,
			IsPartial: source == CatalogueSource.CelesTrak,
			HasObjectTypes: types is not null,
			notes);
	}

	/// <summary>
	/// Archives the selected object's element-set history from Space-Track.
	/// </summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <param name="store">The archive.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>How many sets were new to the archive.</returns>
	/// <exception cref="InvalidOperationException">No Space-Track account is configured.</exception>
	/// <exception cref="SpaceTrackException">The request failed and nothing was cached.</exception>
	internal Task<int> SeedHistoryAsync(int noradCatalogId, SnapshotStore store, CancellationToken cancellationToken = default) =>
		spaceTrack is null
			? throw new InvalidOperationException("Seeding a history needs a Space-Track account, and none is configured.")
			: spaceTrack.SeedHistoryAsync(noradCatalogId, store, cancellationToken: cancellationToken);

	/// <summary>
	/// Merges element sets from several sources, keeping each object once with its newest epoch.
	/// </summary>
	/// <param name="groups">The element sets, in any order and with any overlap.</param>
	/// <returns>One element set per catalogue number.</returns>
	internal static IReadOnlyList<ElementSet> Merge(IEnumerable<IReadOnlyList<ElementSet>> groups)
	{
		Dictionary<int, ElementSet> merged = [];

		foreach (ElementSet set in groups.SelectMany(group => group))
		{
			if (!merged.TryGetValue(set.NoradCatalogId, out ElementSet? kept) || set.Epoch > kept.Epoch)
			{
				merged[set.NoradCatalogId] = set;
			}
		}

		return [.. merged.Values];
	}

	/// <summary>Loads and merges CelesTrak's groups.</summary>
	/// <param name="notes">Where a group that did not load is recorded.</param>
	/// <param name="cancellationToken">Cancels the load.</param>
	/// <returns>The merged element sets.</returns>
	private async Task<IReadOnlyList<ElementSet>> LoadCelesTrakAsync(List<string> notes, CancellationToken cancellationToken)
	{
		List<IReadOnlyList<ElementSet>> groups = [];
		bool stale = false;

		foreach (string group in CelesTrakGroups)
		{
			try
			{
				Fetched<IReadOnlyList<ElementSet>> fetched = await celesTrak.GetGroupAsync(group, cancellationToken).ConfigureAwait(false);
				groups.Add(fetched.Value);
				stale |= fetched.IsStale;
			}
			catch (CelesTrakException failure) when (!string.Equals(group, RequiredGroup, StringComparison.Ordinal))
			{
				notes.Add(string.Create(CultureInfo.InvariantCulture, $"CelesTrak's {group} group did not load: {failure.Message}"));
			}
		}

		if (stale)
		{
			notes.Add("CelesTrak could not be reached; this is the last catalogue the cache holds.");
		}

		return Merge(groups);
	}

	/// <summary>Loads the satellite catalogue as a map from catalogue number to object type.</summary>
	/// <param name="notes">Where a failure is recorded.</param>
	/// <param name="cancellationToken">Cancels the load.</param>
	/// <returns>The map, or null when the satellite catalogue could not be loaded.</returns>
	private async Task<IReadOnlyDictionary<int, SatcatObjectType>?> LoadTypesAsync(List<string> notes, CancellationToken cancellationToken)
	{
		try
		{
			IReadOnlyList<SatcatRecord> records = await satcat.GetCatalogueAsync(cancellationToken).ConfigureAwait(false);

			return records.ToDictionary(record => record.NoradCatalogId, record => record.ObjectType);
		}
		catch (CelesTrakException failure)
		{
			notes.Add("The satellite catalogue did not load, so every object's type is unknown: " + failure.Message);
			return null;
		}
	}
}
