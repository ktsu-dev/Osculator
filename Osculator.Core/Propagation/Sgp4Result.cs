// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// Why a propagation could not produce a state.
/// </summary>
/// <remarks>
/// The numbers are the published model's own, not chosen here, which is why 5 is missing: it is
/// reserved for an element set that is sub-orbital at its epoch, a condition this implementation
/// does not test for separately. They are reported one at a time and never combined.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
	"Design", "CA1027:Mark enums with FlagsAttribute",
	Justification = "The values are the published model's error numbers, and 1, 2 and 4 being powers of two is a coincidence of that numbering. They are not a bit field and combining them would be meaningless.")]
public enum Sgp4Error
{
	/// <summary>The propagation succeeded.</summary>
	None = 0,

	/// <summary>Eccentricity left the range the model is defined over.</summary>
	EccentricityOutOfRange = 1,

	/// <summary>Mean motion fell to zero or below.</summary>
	MeanMotionNotPositive = 2,

	/// <summary>Perturbed eccentricity left the range the model is defined over.</summary>
	PerturbedEccentricityOutOfRange = 3,

	/// <summary>Semi-latus rectum went negative.</summary>
	SemiLatusRectumNegative = 4,

	/// <summary>The satellite has decayed: the orbit radius fell below the Earth's.</summary>
	Decayed = 6,
}

/// <summary>
/// The outcome of one propagation.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="Error">Why the propagation failed, or <see cref="Sgp4Error.None"/>.</param>
/// <param name="State">The state, meaningful only when <paramref name="Error"/> is <see cref="Sgp4Error.None"/>.</param>
/// <remarks>
/// A result rather than an exception because the verification suite contains element sets chosen to
/// provoke each error, and because a catalogue sweep across thirty thousand objects will meet decayed
/// ones as a matter of course. An error is an ordinary outcome here, not an exceptional one.
/// </remarks>
public readonly record struct Sgp4Result<T>(Sgp4Error Error, TemeState<T> State)
	where T : struct, INumber<T>
{
	/// <summary>Gets a value indicating whether the propagation produced a usable state.</summary>
	public bool IsSuccess => Error == Sgp4Error.None;
}
