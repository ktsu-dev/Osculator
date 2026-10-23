// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Shell;

using ktsu.Osculator.App.Panels;

/// <summary>
/// Hosts <see cref="ResidualPlotsPanel"/> as a docked panel.
/// </summary>
/// <remarks>
/// The panel predates the shell and draws through a static method, so this adapts it, as
/// <see cref="StorageComparisonWindow"/> does.
/// </remarks>
internal sealed class ResidualPlotsWindow : Panel
{
	/// <inheritdoc/>
	protected override string Title => "Residual plots";

	/// <inheritdoc/>
	protected override void Draw() => ResidualPlotsPanel.Draw();
}
