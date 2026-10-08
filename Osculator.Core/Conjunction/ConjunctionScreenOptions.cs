// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Conjunction;

using System;
using ktsu.Osculator.Core.Time;

/// <summary>
/// The window and thresholds a conjunction screen runs over.
/// </summary>
/// <remarks>
/// The window is an absolute instant rather than minutes since an epoch, because two element sets
/// in a catalogue almost never share an epoch and a conjunction is a meeting at one instant.
/// </remarks>
public sealed record ConjunctionScreenOptions
{
	/// <summary>Gets the start of the screening window.</summary>
	public required JulianDate WindowStart { get; init; }

	/// <summary>Gets the length of the screening window, in minutes.</summary>
	public required double WindowMinutes { get; init; }

	/// <summary>
	/// Gets the miss distance at or under which a closest approach is reported, in kilometres.
	/// </summary>
	public required double ThresholdKm { get; init; }

	/// <summary>
	/// Gets the step of the coarse scan for closest approaches, in minutes.
	/// </summary>
	/// <remarks>
	/// A closest approach is a sign change of the range rate, and the scan looks for sign changes
	/// between samples, so it misses two closest approaches that fall inside one step. The range
	/// between two orbiting objects changes on the time scale of the orbit itself, so one minute is
	/// far finer than that for anything in Earth orbit; it is coarser than a fly-by, which is fine,
	/// because the fly-by is found by refining the bracket, not by the scan landing on it.
	/// </remarks>
	public double StepMinutes { get; init; } = 1.0;

	/// <summary>
	/// Gets the padding added to the screening distance when comparing orbit shells, in kilometres.
	/// </summary>
	/// <remarks>
	/// <see cref="OrbitShell"/> is the mean elements' Keplerian band, and SGP4's short-period terms
	/// move the radius around it by the order of ten kilometres. Fifty covers that with room to
	/// spare. Drag lowers a decaying object's shell over the window too, so a long window over low
	/// objects wants more.
	/// </remarks>
	public double ShellMarginKm { get; init; } = 50.0;

	/// <summary>
	/// Gets how narrowly the time of closest approach is bracketed, in minutes.
	/// </summary>
	/// <remarks>
	/// At a relative speed of 15 km/s, an error of one nanominute — 60 nanoseconds — places the
	/// approach about a millimetre along the relative track, and since the range is at its minimum
	/// there that costs far less than a millimetre of miss distance. A storage type whose time
	/// resolution is coarser than this stops when the bracket can no longer be halved, and its miss
	/// distance carries that coarseness, which is part of what the comparison is for.
	/// </remarks>
	public double TimeToleranceMinutes { get; init; } = 1e-9;

	/// <summary>Validates the options.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
	public void Validate()
	{
		if (!double.IsFinite(WindowMinutes) || WindowMinutes <= 0.0)
		{
			throw new ArgumentOutOfRangeException(nameof(WindowMinutes), WindowMinutes, "The window must be finite and positive.");
		}

		if (!double.IsFinite(ThresholdKm) || ThresholdKm < 0.0)
		{
			throw new ArgumentOutOfRangeException(nameof(ThresholdKm), ThresholdKm, "The threshold must be finite and not negative.");
		}

		if (!double.IsFinite(StepMinutes) || StepMinutes <= 0.0)
		{
			throw new ArgumentOutOfRangeException(nameof(StepMinutes), StepMinutes, "The step must be finite and positive.");
		}

		if (!double.IsFinite(ShellMarginKm) || ShellMarginKm < 0.0)
		{
			throw new ArgumentOutOfRangeException(nameof(ShellMarginKm), ShellMarginKm, "The shell margin must be finite and not negative.");
		}

		if (!double.IsFinite(TimeToleranceMinutes) || TimeToleranceMinutes <= 0.0)
		{
			throw new ArgumentOutOfRangeException(nameof(TimeToleranceMinutes), TimeToleranceMinutes, "The time tolerance must be finite and positive.");
		}
	}
}
