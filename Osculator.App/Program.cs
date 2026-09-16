// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App;

using ktsu.ImGui.App;

/// <summary>
/// The application entry point.
/// </summary>
internal static class Program
{
	/// <summary>
	/// Starts the window.
	/// </summary>
	private static void Main() => ImGuiApp.Start(new ImGuiAppConfig
	{
		Title = "Osculator",
		OnRender = _ => StorageComparisonPanel.Draw(),
	});
}
