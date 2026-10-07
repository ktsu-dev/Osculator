// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Conjunction;

using System.Collections.Generic;
using ktsu.Osculator.Core.Elements;

/// <summary>
/// A conjunction screen held without naming its storage type.
/// </summary>
/// <remarks>
/// The same seam as <c>IPropagatorHost</c>: the application shows four storage types
/// side by side and cannot be generic over any of them, so it holds four of these and loops.
/// </remarks>
public interface IConjunctionScreener
{
	/// <summary>
	/// Screens every pair in a set of objects for closest approaches within a threshold.
	/// </summary>
	/// <param name="objects">The objects to screen against each other.</param>
	/// <param name="options">The window and thresholds.</param>
	/// <returns>The approaches found, with the pre-filter's counts and any objects that failed.</returns>
	public ConjunctionScreenResult Screen(IReadOnlyList<ElementSet> objects, ConjunctionScreenOptions options);
}
