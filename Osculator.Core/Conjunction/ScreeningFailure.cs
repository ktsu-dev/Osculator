// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Conjunction;

using ktsu.Osculator.Core.Propagation;

/// <summary>
/// An object the screen could not follow through the whole window, and why.
/// </summary>
/// <param name="CatalogId">The NORAD catalogue number.</param>
/// <param name="Error">What SGP4 reported.</param>
/// <param name="MinutesFromWindowStart">
/// When in the window it was reported, in minutes after the window start; zero for an element set
/// the model refused at initialization.
/// </param>
/// <remarks>
/// Reported rather than dropped. A screen that silently skips the objects it cannot propagate looks
/// exactly like one that found nothing near them, and a decayed object is precisely the kind most
/// likely to be skipped.
/// </remarks>
public readonly record struct ScreeningFailure(int CatalogId, Sgp4Error Error, double MinutesFromWindowStart);
