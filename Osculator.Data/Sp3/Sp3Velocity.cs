// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Sp3;

/// <summary>
/// The velocity half of an SP3 entry, converted from the file's units.
/// </summary>
/// <param name="X">Velocity along the file's x axis, in kilometres per second.</param>
/// <param name="Y">Velocity along the file's y axis, in kilometres per second.</param>
/// <param name="Z">Velocity along the file's z axis, in kilometres per second.</param>
/// <param name="ClockRateMicrosecondsPerSecond">
/// The rate of change of the clock correction, or <see langword="null"/> where the file has none.
/// </param>
/// <remarks>
/// SP3 writes velocities in decimetres per second and clock rates in units of 10⁻⁴ µs/s — not the
/// kilometres its positions are in. The conversion is done once, here, so nothing downstream has to
/// remember it; a velocity read in the file's own unit is out by a factor of ten thousand.
/// </remarks>
public readonly record struct Sp3Velocity(double X, double Y, double Z, double? ClockRateMicrosecondsPerSecond);
