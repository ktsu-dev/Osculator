// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using ktsu.Osculator.Core.Elements;

/// <summary>
/// The object the user picked in the catalogue, which every other panel reads.
/// </summary>
/// <remarks>
/// Read and written on the UI thread only, like everything else ImGui draws from. A panel that does
/// work for the selection on a background thread copies <see cref="Selected"/> before it starts,
/// and compares <see cref="Version"/> when the result comes back to tell whether it is still wanted.
/// </remarks>
internal static class CatalogueSelection
{
	/// <summary>Gets the selected element set, or <see langword="null"/> when nothing is selected.</summary>
	internal static ElementSet? Selected { get; private set; }

	/// <summary>Gets a number that changes every time the selection does, starting at zero.</summary>
	internal static long Version { get; private set; }

	/// <summary>
	/// Selects an element set, or clears the selection.
	/// </summary>
	/// <param name="elements">The element set, or <see langword="null"/> to select nothing.</param>
	internal static void Select(ElementSet? elements)
	{
		if (ReferenceEquals(elements, Selected))
		{
			return;
		}

		Selected = elements;
		Version++;
	}
}
