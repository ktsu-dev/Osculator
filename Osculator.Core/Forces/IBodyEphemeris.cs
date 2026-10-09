// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System.Numerics;

/// <summary>
/// The position of a perturbing body — the Sun or the Moon — as a function of time.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// The seam between the forces that need a body's position and wherever it comes from. This
/// assembly has no I/O, so it does not fetch one: <see cref="TabulatedEphemeris{T}"/> interpolates a
/// table, and <c>Osculator.Data</c> fills one from JPL Horizons.
/// </remarks>
public interface IBodyEphemeris<T>
	where T : struct, INumber<T>
{
	/// <summary>Gets the body's position, relative to the integration's centre, in its frame.</summary>
	/// <param name="secondsSinceEpoch">The time, in seconds since the integration's epoch.</param>
	/// <returns>The position, in kilometres.</returns>
	public BodyPosition<T> PositionAt(T secondsSinceEpoch);
}
