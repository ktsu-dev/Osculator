// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Shell;

using ktsu.Osculator.App.Panels;

/// <summary>
/// Hosts <see cref="GlobePanel"/> as a docked panel, following the catalogue's selection.
/// </summary>
/// <remarks>
/// The globe keeps its own object list and its own selection, which predate
/// <see cref="CatalogueSelection"/>. This hands it each new catalogue selection, which adds the
/// object to the globe if it is not already there and selects it, and otherwise leaves the globe's
/// list alone, so objects pasted into it stay. The selection is applied after the panel has drawn,
/// because its first draw resets its state and would discard one applied before.
/// </remarks>
internal sealed class GlobeWindow : Panel
{
	private long seenSelectionVersion;

	/// <inheritdoc/>
	protected override string Title => GlobePanel.Title;

	/// <inheritdoc/>
	protected override void Draw()
	{
		GlobePanel.Draw();

		if (CatalogueSelection.Version != seenSelectionVersion)
		{
			seenSelectionVersion = CatalogueSelection.Version;

			if (CatalogueSelection.Selected is { } selected)
			{
				GlobePanel.Select(selected);
			}
		}
	}
}
