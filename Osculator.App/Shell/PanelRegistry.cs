// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Shell;

using System;
using System.Collections.Generic;
using Hexa.NET.ImGui;

/// <summary>
/// The panels the shell hosts, in the order they were registered.
/// </summary>
/// <param name="work">The queue every registered panel is given.</param>
internal sealed class PanelRegistry(BackgroundWork work)
{
	private readonly List<Panel> panels = [];

	/// <summary>
	/// Gets the registered panels.
	/// </summary>
	internal IReadOnlyList<Panel> All => panels;

	/// <summary>
	/// Registers a panel. It opens when the shell starts.
	/// </summary>
	/// <typeparam name="T">The panel type.</typeparam>
	/// <returns>The panel instance.</returns>
	/// <exception cref="InvalidOperationException">
	/// The type is already registered, or another panel has the same title. The title is the docked
	/// window's identity, so two panels sharing one would be drawn as a single window.
	/// </exception>
	internal T Register<T>()
		where T : Panel, new() => Register(new T());

	/// <summary>
	/// Registers a panel instance.
	/// </summary>
	/// <typeparam name="T">The panel type.</typeparam>
	/// <param name="panel">The panel.</param>
	/// <returns>The panel.</returns>
	/// <exception cref="InvalidOperationException">The type or the title is already registered.</exception>
	internal T Register<T>(T panel)
		where T : Panel
	{
		Ensure.NotNull(panel);

		foreach (Panel existing in panels)
		{
			if (existing.GetType() == panel.GetType())
			{
				throw new InvalidOperationException($"{typeof(T).Name} is already registered.");
			}

			if (string.Equals(existing.Caption, panel.Caption, StringComparison.Ordinal))
			{
				throw new InvalidOperationException(
					$"{typeof(T).Name} and {existing.GetType().Name} are both titled \"{panel.Caption}\". " +
					"The title is the docked window's identity, so it has to be unique.");
			}
		}

		panel.Attach(work);
		panels.Add(panel);
		return panel;
	}

	/// <summary>
	/// Opens every registered panel.
	/// </summary>
	internal void OpenAll()
	{
		foreach (Panel panel in panels)
		{
			panel.Open();
		}
	}

	/// <summary>
	/// Runs before the docked windows are drawn.
	/// </summary>
	internal void BeginFrame()
	{
		foreach (Panel panel in panels)
		{
			panel.BeginFrame();
		}
	}

	/// <summary>
	/// Runs after the docked windows are drawn.
	/// </summary>
	internal void EndFrame()
	{
		foreach (Panel panel in panels)
		{
			panel.EndFrame();
		}
	}

	/// <summary>
	/// Draws the View menu, one checkable item per panel.
	/// </summary>
	internal void DrawViewMenu()
	{
		if (!ImGui.BeginMenu("View"))
		{
			return;
		}

		foreach (Panel panel in panels)
		{
			if (ImGui.MenuItem(panel.Caption, string.Empty, panel.IsOpen))
			{
				if (panel.IsOpen)
				{
					panel.Dismiss();
				}
				else
				{
					panel.Open();
				}
			}
		}

		ImGui.EndMenu();
	}
}
