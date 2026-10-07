// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Storage;

using System;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// One propagation's outcome, with the storage type erased, for display.
/// </summary>
/// <param name="Error">Why the propagation failed, or <see cref="Sgp4Error.None"/>.</param>
/// <param name="X">
/// Position along the TEME x axis, in kilometres. This and the other five components are
/// <see cref="double.NaN"/> when <paramref name="Error"/> is not <see cref="Sgp4Error.None"/>, so a
/// failed propagation cannot be plotted as a point at the origin.
/// </param>
/// <param name="Y">Position along the TEME y axis, in kilometres.</param>
/// <param name="Z">Position along the TEME z axis, in kilometres.</param>
/// <param name="VelocityX">Velocity along the TEME x axis, in kilometres per second.</param>
/// <param name="VelocityY">Velocity along the TEME y axis, in kilometres per second.</param>
/// <param name="VelocityZ">Velocity along the TEME z axis, in kilometres per second.</param>
/// <param name="Elapsed">The wall-clock time the propagation itself took, excluding initialization.</param>
/// <param name="InitializationElapsed">
/// The wall-clock time spent initializing the element set during this call, or
/// <see cref="TimeSpan.Zero"/> when an earlier call had already initialized it.
/// </param>
/// <remarks>
/// <para>
/// <strong>The components are <see langword="double"/> for display, and nothing downstream should
/// difference them to measure the arithmetic error.</strong> Rounding a <c>PreciseNumber</c> state to
/// sixteen digits throws away exactly the digits the arithmetic comparison exists to see; a
/// difference between two of these records is the difference of two roundings. The comparison
/// belongs in the storage type, where <c>StorageComparisonTests</c> does it.
/// </para>
/// <para>
/// The frame is TEME, as SGP4 emits, and the units are the model's own kilometres. Treating this as
/// J2000 is the most common defect in amateur trackers; the frame layer converts it.
/// </para>
/// </remarks>
public readonly record struct PropagatedState(
	Sgp4Error Error,
	double X,
	double Y,
	double Z,
	double VelocityX,
	double VelocityY,
	double VelocityZ,
	TimeSpan Elapsed,
	TimeSpan InitializationElapsed)
{
	/// <summary>Gets a value indicating whether the propagation produced a usable state.</summary>
	public bool IsSuccess => Error == Sgp4Error.None;
}
