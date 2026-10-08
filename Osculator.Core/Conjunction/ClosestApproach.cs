// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Conjunction;

using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// One closest approach between two objects, computed in a storage type.
/// </summary>
/// <typeparam name="T">The numeric storage type the approach was found and measured in.</typeparam>
/// <param name="PrimaryCatalogId">The NORAD catalogue number of the first object.</param>
/// <param name="SecondaryCatalogId">The NORAD catalogue number of the second object.</param>
/// <param name="MinutesFromWindowStart">The time of closest approach, in minutes after the window start.</param>
/// <param name="Primary">The first object's TEME state at that time.</param>
/// <param name="Secondary">The second object's TEME state at that time.</param>
/// <param name="MissDistanceKm">The distance between the two at that time, in kilometres.</param>
/// <param name="RelativeSpeedKmPerSecond">The magnitude of their relative velocity there, in km/s.</param>
/// <remarks>
/// <para>
/// <strong>The miss distance is computed in <typeparamref name="T"/>, from the two states in
/// <typeparamref name="T"/>, and nowhere else.</strong> It is the difference of two vectors each
/// about seven thousand kilometres long that agree to within metres, which is the textbook
/// cancellation: the leading digits of each component cancel and what is left is however many
/// digits the type had beyond them. Differencing two states that had already been rounded to
/// <see langword="double"/> for display would measure <see langword="double"/>, whatever type
/// produced them — which is why this record carries the states themselves rather than a summary.
/// </para>
/// <para>
/// Kilometres and km/s, the propagator's own units, as everywhere else in the model.
/// </para>
/// </remarks>
public readonly record struct ClosestApproach<T>(
	int PrimaryCatalogId,
	int SecondaryCatalogId,
	T MinutesFromWindowStart,
	TemeState<T> Primary,
	TemeState<T> Secondary,
	T MissDistanceKm,
	T RelativeSpeedKmPerSecond)
	where T : struct, INumber<T>
{
	/// <summary>Rounds the approach to <see langword="double"/> for display, once, at the end.</summary>
	/// <returns>The approach with its storage type erased.</returns>
	public ConjunctionEvent ToEvent() => new(
		PrimaryCatalogId,
		SecondaryCatalogId,
		double.CreateSaturating(MinutesFromWindowStart),
		double.CreateSaturating(MissDistanceKm),
		double.CreateSaturating(RelativeSpeedKmPerSecond));
}
