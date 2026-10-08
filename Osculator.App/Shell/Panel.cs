// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Shell;

using Hexa.NET.ImGui;
using ktsu.ImGui.Widgets;

/// <summary>
/// A dockable panel in the application shell.
/// </summary>
/// <remarks>
/// <para>
/// Derive from this, override <c>Title</c> and <see cref="Draw"/>, and add one
/// <see cref="PanelRegistry.Register{T}()"/> line to <see cref="Panels.Register"/>. The panel is a
/// <see cref="ImGuiWidgets.DockedWindow"/>, so it is drawn by
/// <see cref="ImGuiWidgets.DrawDeferredDocked"/> and nothing else.
/// </para>
/// <para>
/// <see cref="IsOpen"/> is this panel's own record of whether the user wants it, and it is kept
/// separately from the docked window's registration on purpose. The docked window unregisters
/// itself whenever <c>ImGui.Begin</c> returns <see langword="false"/>, which ImGui also does for a
/// collapsed window and for a docked tab that is not the selected one, so tabbing two panels
/// together would otherwise close whichever was behind. <see cref="PanelRegistry"/> re-shows every
/// open panel each frame and decides from the frame itself whether the user really closed one.
/// Remove that once ktsu-dev/ImGuiApp#600 is fixed upstream.
/// </para>
/// </remarks>
internal abstract class Panel : ImGuiWidgets.DockedWindow
{
	/// <summary>
	/// Gets the window caption, which is also the panel's identity: two panels may not share one.
	/// </summary>
	internal string Caption => Title;

	/// <summary>
	/// Gets a value indicating whether the user has this panel open.
	/// </summary>
	internal bool IsOpen { get; private set; }

	/// <summary>
	/// Gets a value indicating whether <see cref="Draw"/> ran during the current frame.
	/// </summary>
	internal bool DrawnThisFrame { get; private set; }

	/// <summary>
	/// Gets the queue panels hand long-running work to. Assigned by the registry before the panel
	/// is first shown.
	/// </summary>
	protected BackgroundWork Work { get; private set; } = null!;

	/// <summary>
	/// Opens the panel, or keeps it open.
	/// </summary>
	internal void Open()
	{
		IsOpen = true;
		Show();
	}

	/// <summary>
	/// Closes the panel until the user opens it again.
	/// </summary>
	internal void Dismiss()
	{
		IsOpen = false;
		Close();
		OnDismissed();
	}

	/// <summary>
	/// Attaches the panel to the shell's work queue.
	/// </summary>
	/// <param name="work">The queue.</param>
	internal void Attach(BackgroundWork work) => Work = work;

	/// <summary>
	/// Prepares the panel for a frame: re-registers it if it is open, so a tab that was hidden last
	/// frame is still there to be selected this one.
	/// </summary>
	internal void BeginFrame()
	{
		DrawnThisFrame = false;

		if (IsOpen)
		{
			Show();
		}
	}

	/// <summary>
	/// Decides, after the docked windows have been drawn, whether the user closed this panel.
	/// </summary>
	/// <remarks>
	/// The window's close button is the only way <c>ImGui.Begin</c> can be reached, report the
	/// window as visible, and still leave the contents undrawn. A collapsed window and a docked tab
	/// that is not selected both skip their items, and a window that was not begun at all this frame
	/// is not active. So: active, not skipping its items, and not drawn means closed.
	/// </remarks>
	internal void EndFrame()
	{
		if (!IsOpen || DrawnThisFrame)
		{
			return;
		}

		ImGuiWindowPtr window = ImGuiP.FindWindowByName(Title);
		if (!window.IsNull && window.Active && !window.SkipItems)
		{
			Dismiss();
		}
	}

	/// <summary>
	/// Draws the panel's contents for one frame.
	/// </summary>
	protected abstract void Draw();

	/// <summary>
	/// Called when the panel is closed, by the user or by the shell. Cancel this panel's background
	/// work here if nothing else will want its result.
	/// </summary>
	protected virtual void OnDismissed()
	{
	}

	/// <inheritdoc/>
	protected sealed override void DrawContent()
	{
		DrawnThisFrame = true;
		Draw();
	}
}
