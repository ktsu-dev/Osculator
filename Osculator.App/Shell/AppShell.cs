// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Shell;

using System;
using ktsu.ImGui.App;
using ktsu.ImGui.Widgets;

/// <summary>
/// The application shell: a dockspace of panels, a work queue that keeps propagation off the UI
/// thread, and frame-rate throttling for when nobody is looking.
/// </summary>
internal sealed class AppShell : IDisposable
{
	/// <summary>
	/// Initializes a new instance of the <see cref="AppShell"/> class.
	/// </summary>
	/// <param name="register">Registers the panels. The application passes <see cref="Panels.Register"/>.</param>
	/// <param name="work">The work queue. Defaults to one marshalling through <see cref="ImGuiApp.Invoker"/>.</param>
	internal AppShell(Action<PanelRegistry> register, BackgroundWork? work = null)
	{
		Ensure.NotNull(register);

		Work = work ?? BackgroundWork.ForApplication();
		Registry = new PanelRegistry(Work);
		register(Registry);
	}

	/// <summary>
	/// Gets the frame rates the shell runs at.
	/// </summary>
	/// <remarks>
	/// A long background sweep should not compete with a 60 Hz redraw of a screen nobody is
	/// looking at, so the rate drops when the window loses focus, after half a minute without input,
	/// and further when the window is hidden. Results arriving from the work queue are applied on
	/// the next frame at whatever rate is current, which at the idle rate is a tenth of a second.
	/// </remarks>
	internal static ImGuiAppPerformanceSettings Performance { get; } = new()
	{
		EnableThrottledRendering = true,
		FocusedFps = 60.0,
		UnfocusedFps = 5.0,
		EnableIdleDetection = true,
		IdleTimeoutSeconds = 30.0,
		IdleFps = 10.0,
		NotVisibleFps = 2.0,
	};

	/// <summary>
	/// Gets the panels.
	/// </summary>
	internal PanelRegistry Registry { get; }

	/// <summary>
	/// Gets the work queue the panels share.
	/// </summary>
	internal BackgroundWork Work { get; }

	/// <summary>
	/// Builds the configuration <see cref="ImGuiApp.Start"/> runs.
	/// </summary>
	/// <returns>The configuration.</returns>
	internal ImGuiAppConfig BuildConfig() => new()
	{
		Title = "Osculator",
		EnableDocking = true,
		PerformanceSettings = Performance,
		OnStart = Registry.OpenAll,
		OnAppMenu = Registry.DrawViewMenu,
		OnRender = _ => Render(),
		OnClosing = () =>
		{
			Work.CancelAll();
			return true;
		},
	};

	/// <inheritdoc/>
	public void Dispose() => Work.Dispose();

	/// <summary>
	/// Draws one frame.
	/// </summary>
	/// <remarks>
	/// <see cref="ImGuiWidgets.DrawDeferredDocked"/> is the only pump. It already draws every dialog,
	/// message box and popup, so calling <see cref="ImGuiWidgets.DrawDeferred"/> as well would draw
	/// each of them twice.
	/// </remarks>
	private void Render()
	{
		Registry.BeginFrame();
		ImGuiWidgets.DrawDeferredDocked();
		Registry.EndFrame();
	}
}
