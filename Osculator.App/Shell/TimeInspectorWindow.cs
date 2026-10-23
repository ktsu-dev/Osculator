// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Shell;

using ktsu.Osculator.App.Panels;

/// <summary>
/// Hosts <see cref="TimeInspectorPanel"/> as a docked panel.
/// </summary>
/// <remarks>
/// The panel predates the shell. It keeps its sweep's state on the instance, so this owns one for
/// the life of the window rather than adapting a static method.
/// </remarks>
internal sealed class TimeInspectorWindow : Panel
{
	private readonly TimeInspectorPanel panel = new();

	/// <inheritdoc/>
	protected override string Title => TimeInspectorPanel.Title;

	/// <inheritdoc/>
	protected override void Draw() => panel.Draw();
}
