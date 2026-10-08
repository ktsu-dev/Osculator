// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Sp3;

using ktsu.Osculator.Core.Time;

/// <summary>
/// One satellite's entry at one epoch of an SP3 file.
/// </summary>
/// <param name="Instant">The epoch, on the file's own time scale (see <see cref="Sp3File.TimeSystem"/>).</param>
/// <param name="SecondsSinceStart">
/// The epoch in seconds after the file's start time, computed from the calendar fields rather than
/// by subtracting Julian dates, so a whole-second epoch is an exact whole number here.
/// </param>
/// <param name="X">Position along the file's x axis, in kilometres.</param>
/// <param name="Y">Position along the file's y axis, in kilometres.</param>
/// <param name="Z">Position along the file's z axis, in kilometres.</param>
/// <param name="ClockMicroseconds">
/// The satellite clock correction in microseconds, or <see langword="null"/> where the file has
/// none — either the field is absent, as it is in laser-ranging orbits, or it holds the format's
/// bad-value marker <c>999999.999999</c>.
/// </param>
/// <param name="Velocity">The velocity record, for a file whose header declares velocities.</param>
public sealed record Sp3Record(
	JulianDate Instant,
	double SecondsSinceStart,
	double X,
	double Y,
	double Z,
	double? ClockMicroseconds,
	Sp3Velocity? Velocity);
