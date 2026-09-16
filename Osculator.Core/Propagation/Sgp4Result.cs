// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// Why a propagation could not produce a state.
/// </summary>
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

	/// <summary>
	/// The element set is deep-space and the deep-space model is not implemented yet.
	/// </summary>
	/// <remarks>
	/// Not one of the model's own error codes. An orbital period of 225 minutes or more selects
	/// SDP4, whose lunar-solar and resonance terms are a separate body of work.
	/// </remarks>
	DeepSpaceNotImplemented = 100,
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
