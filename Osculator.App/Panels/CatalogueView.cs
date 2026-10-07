// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.Linq;
using ktsu.Osculator.Core.Elements;
using ktsu.TextFilter;

/// <summary>
/// The rows the catalogue shows: the loaded catalogue, filtered by orbit class and ranked by search.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here runs per frame. <see cref="Update"/> is called every frame, and does work only when
/// the query or the class filter differs from the last call; otherwise it hands back the list it
/// built then. That is what keeps the frame time independent of the catalogue's size, together
/// with the clipper that draws only the visible rows: a fuzzy rank over thirty thousand names is
/// cheap once per keystroke and ruinous sixty times a second.
/// </para>
/// <para>
/// This is why the panel does not call <c>ImGuiWidgets.SearchBoxRanked</c>, which ranks the whole
/// collection on every frame its text is non-empty. It draws the same box with
/// <c>ImGuiWidgets.SearchBox</c> and the same ranked options, and ranks here with the same
/// <see cref="TextFilter.Rank{TItem}(IEnumerable{TItem}, Func{TItem, string}, string)"/>, once.
/// </para>
/// </remarks>
internal sealed class CatalogueView
{
	/// <summary>A mask with every orbit class shown.</summary>
	internal const int AllClasses = ~0;

	private string query = string.Empty;

	private int shownClasses = AllClasses;

	/// <summary>
	/// Initializes a new instance of the <see cref="CatalogueView"/> class.
	/// </summary>
	/// <param name="entries">The catalogue, in the order it is shown when nothing is searched for.</param>
	internal CatalogueView(IReadOnlyList<CatalogueEntry> entries)
	{
		Ensure.NotNull(entries);

		All = entries;
		Rows = entries;

		int[] counts = new int[Enum.GetValues<OrbitClass>().Length];

		foreach (CatalogueEntry entry in entries)
		{
			counts[(int)entry.Geometry.Class]++;
		}

		ClassCounts = counts;
		NewestEpoch = entries.Count == 0 ? null : entries.Max(entry => entry.Elements.Epoch);
	}

	/// <summary>Gets the whole catalogue.</summary>
	internal IReadOnlyList<CatalogueEntry> All { get; }

	/// <summary>Gets the rows that pass the current filter, in the order to show them.</summary>
	internal IReadOnlyList<CatalogueEntry> Rows { get; private set; }

	/// <summary>Gets how many objects in the whole catalogue fall in each class, indexed by class.</summary>
	internal IReadOnlyList<int> ClassCounts { get; }

	/// <summary>Gets the newest epoch in the catalogue, which says how current the whole of it is.</summary>
	internal DateTime? NewestEpoch { get; }

	/// <summary>Gets how many times the rows have been rebuilt, so a test can see a frame that rebuilt nothing.</summary>
	internal int Rebuilds { get; private set; }

	/// <summary>
	/// Gets the bit a class occupies in a class mask.
	/// </summary>
	/// <param name="orbitClass">The class.</param>
	/// <returns>The bit.</returns>
	internal static int MaskOf(OrbitClass orbitClass) => 1 << (int)orbitClass;

	/// <summary>
	/// Brings the rows up to date with a query and a class filter, rebuilding them only if either changed.
	/// </summary>
	/// <param name="newQuery">The search text. Empty or white space shows everything in catalogue order.</param>
	/// <param name="newShownClasses">A mask of the classes to show, built from <see cref="MaskOf"/>.</param>
	/// <returns>The rows.</returns>
	internal IReadOnlyList<CatalogueEntry> Update(string newQuery, int newShownClasses)
	{
		Ensure.NotNull(newQuery);

		if (string.Equals(newQuery, query, StringComparison.Ordinal) && newShownClasses == shownClasses)
		{
			return Rows;
		}

		query = newQuery;
		shownClasses = newShownClasses;
		Rebuilds++;

		IEnumerable<CatalogueEntry> filtered = newShownClasses == AllClasses
			? All
			: All.Where(entry => (newShownClasses & MaskOf(entry.Geometry.Class)) != 0);

		Rows = string.IsNullOrWhiteSpace(newQuery)
			? [.. filtered]
			: [.. TextFilter.Rank(filtered, entry => entry.SearchKey, newQuery)];

		return Rows;
	}
}
