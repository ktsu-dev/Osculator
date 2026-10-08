// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Shell;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The one place panels are registered.
/// </summary>
internal static class Panels
{
	/// <summary>
	/// Registers every panel the application shows, in the order they appear in the View menu.
	/// </summary>
	/// <param name="registry">The registry.</param>
	/// <remarks>
	/// Adding a panel is one line here. Keep it one line per panel, so panels added in parallel merge
	/// without conflict beyond the line itself.
	/// </remarks>
	[SuppressMessage("Style", "IDE0022:Use expression body for method", Justification = "A list of registrations, one per line, which an expression body would stop being once there are two.")]
	internal static void Register(PanelRegistry registry)
	{
		registry.Register<StorageComparisonWindow>();
	}
}
