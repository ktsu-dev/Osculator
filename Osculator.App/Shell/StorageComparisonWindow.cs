// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Shell;

/// <summary>
/// Hosts <see cref="StorageComparisonPanel"/> as a docked panel.
/// </summary>
/// <remarks>
/// The panel itself predates the shell and draws through a static method, so this adapts it rather
/// than rewriting it. A panel written for the shell derives from <see cref="Panel"/> directly.
/// </remarks>
internal sealed class StorageComparisonWindow : Panel
{
	/// <inheritdoc/>
	protected override string Title => "Storage comparison";

	/// <inheritdoc/>
	protected override void Draw() => StorageComparisonPanel.Draw();
}
