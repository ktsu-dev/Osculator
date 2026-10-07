// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App;

using ktsu.ImGui.App;
using ktsu.Osculator.App.Shell;

/// <summary>
/// The application entry point.
/// </summary>
internal static class Program
{
	/// <summary>
	/// Starts the window.
	/// </summary>
	private static void Main()
	{
		using AppShell shell = new(Panels.Register);
		ImGuiApp.Start(shell.BuildConfig());
	}
}
