// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Conjunction;

/// <summary>
/// A closest approach with the storage type erased, for display.
/// </summary>
/// <param name="PrimaryCatalogId">The NORAD catalogue number of the first object.</param>
/// <param name="SecondaryCatalogId">The NORAD catalogue number of the second object.</param>
/// <param name="MinutesFromWindowStart">The time of closest approach, in minutes after the window start.</param>
/// <param name="MissDistanceKm">The miss distance, in kilometres.</param>
/// <param name="RelativeSpeedKmPerSecond">The relative speed at closest approach, in km/s.</param>
/// <remarks>
/// Unlike a pair of propagated states rounded to <see langword="double"/>, comparing this record
/// across storage types is a fair comparison of their arithmetic: the miss distance was computed in
/// the storage type, from states in the storage type, and only the answer was rounded. A difference
/// between the <see langword="float"/> and <c>PreciseNumber</c> miss distances for the same pair is
/// the cancellation the spec names, not an artefact of display.
/// </remarks>
public readonly record struct ConjunctionEvent(
	int PrimaryCatalogId,
	int SecondaryCatalogId,
	double MinutesFromWindowStart,
	double MissDistanceKm,
	double RelativeSpeedKmPerSecond);
