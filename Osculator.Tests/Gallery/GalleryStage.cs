// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests.Gallery;

using System;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using ktsu.ImGui.App.Testing;
using ktsu.Osculator.App.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HexaImGui = Hexa.NET.ImGui.ImGui;

/// <summary>
/// The running application a gallery entry stages: the headless harness, the shell it drives, and the
/// two layouts the pictures are taken in.
/// </summary>
/// <remarks>
/// <para>
/// Both layouts leave the panels floating, as they open for a user, and move and size them the way a
/// user would by dragging. Neither docks them. ImGuiApp draws its own full-viewport
/// <c>##mainWindow</c> with an almost opaque background, and the dockspace a panel docks into is
/// created behind it and kept there, so a docked panel is painted over and shows through at a
/// fraction of its brightness. A floating panel is drawn above that window and looks as it should.
/// </para>
/// <para>
/// Neither layout overlaps two panels, so no picture depends on which one ImGui last brought to the
/// front.
/// </para>
/// </remarks>
/// <param name="harness">The harness the application runs in.</param>
/// <param name="shell">The shell, with every panel registered.</param>
internal sealed class GalleryStage(ImGuiAppHarness harness, AppShell shell)
{
	/// <summary>Gets the harness the application runs in.</summary>
	internal ImGuiAppHarness Harness { get; } = harness;

	/// <summary>Gets the shell, with every panel the application registers.</summary>
	internal AppShell Shell { get; } = shell;

	/// <summary>Gets the one registered panel of a type.</summary>
	/// <typeparam name="T">The panel's type.</typeparam>
	/// <returns>The panel.</returns>
	internal T Panel<T>()
		where T : Panel => Shell.Registry.All.OfType<T>().Single();

	/// <summary>
	/// Closes every other panel and spreads one over the whole area below the menu bar.
	/// </summary>
	/// <typeparam name="T">The type of the panel to show.</typeparam>
	internal void ShowAlone<T>()
		where T : Panel
	{
		T shown = Panel<T>();

		foreach (Panel panel in Shell.Registry.All.Where(panel => !ReferenceEquals(panel, shown)))
		{
			panel.Dismiss();
		}

		ImGuiViewportPtr viewport = HexaImGui.GetMainViewport();
		Place(shown, viewport.WorkPos, viewport.WorkSize);
		Settle(() => shown.DrawnThisFrame, 10, $"the {shown.Caption} panel drawing");
	}

	/// <summary>
	/// Lays three panels out in two columns: two stacked down the left half, the first only as tall as
	/// asked, and the third down the whole of the right half. Every other panel is closed.
	/// </summary>
	/// <typeparam name="TTopLeft">The panel at the top of the left column.</typeparam>
	/// <typeparam name="TBottomLeft">The panel under it, taking the rest of the column.</typeparam>
	/// <typeparam name="TRight">The panel down the right column.</typeparam>
	/// <param name="topLeftHeight">The height of the top-left panel.</param>
	internal void ShowOverview<TTopLeft, TBottomLeft, TRight>(float topLeftHeight)
		where TTopLeft : Panel
		where TBottomLeft : Panel
		where TRight : Panel
	{
		Panel[] shown = [Panel<TTopLeft>(), Panel<TBottomLeft>(), Panel<TRight>()];

		foreach (Panel panel in Shell.Registry.All.Where(panel => !shown.Contains(panel)))
		{
			panel.Dismiss();
		}

		ImGuiViewportPtr viewport = HexaImGui.GetMainViewport();
		Vector2 origin = viewport.WorkPos;
		Vector2 size = viewport.WorkSize;
		float half = MathF.Floor(size.X / 2f);

		Place(shown[0], origin, new Vector2(half, topLeftHeight));
		Place(shown[1], origin + new Vector2(0f, topLeftHeight), new Vector2(half, size.Y - topLeftHeight));
		Place(shown[2], origin + new Vector2(half, 0f), new Vector2(size.X - half, size.Y));

		Settle(() => shown.All(panel => panel.DrawnThisFrame), 10, "every panel drawing in the overview");
	}

	/// <summary>Steps frames until a condition holds, failing the entry if it never does.</summary>
	/// <param name="condition">What the picture is waiting for.</param>
	/// <param name="frames">How many frames to wait at most.</param>
	/// <param name="what">What is being waited for, for the failure message.</param>
	internal void Settle(Func<bool> condition, int frames, string what) =>
		Assert.IsTrue(Harness.StepUntil(condition, frames), $"The gallery gave up waiting for {what}.");

	/// <summary>Moves and sizes a panel's window, as dragging its title bar and corner would.</summary>
	private static void Place(Panel panel, Vector2 position, Vector2 size)
	{
		Assert.IsFalse(ImGuiP.FindWindowByName(panel.Caption).IsNull, $"The {panel.Caption} window has not been drawn yet.");
		HexaImGui.SetWindowPos(panel.Caption, position, ImGuiCond.Always);
		HexaImGui.SetWindowSize(panel.Caption, size, ImGuiCond.Always);
	}
}
