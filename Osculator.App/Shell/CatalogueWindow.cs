// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Shell;

using ktsu.Osculator.App.Panels;

/// <summary>
/// Hosts <see cref="CataloguePanel"/> as a docked panel.
/// </summary>
/// <remarks>
/// The catalogue is where <see cref="CatalogueSelection"/> is set, so until this is drawn every panel
/// that follows the selection shows its built-in example. The panel predates the shell and draws
/// through a static method, so this adapts it, as <see cref="StorageComparisonWindow"/> does.
/// </remarks>
internal sealed class CatalogueWindow : Panel
{
	/// <inheritdoc/>
	protected override string Title => "Catalogue";

	/// <inheritdoc/>
	protected override void Draw() => CataloguePanel.Draw();
}
