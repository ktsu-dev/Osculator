// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Storage;

using System;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Storage;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;

/// <summary>
/// The storage profile for <see cref="PreciseNumber"/>.
/// </summary>
/// <remarks>
/// The reference the other three are measured against. Its own arithmetic error is negligible at
/// the scale of the question — 50 significant digits against a kilometre is an error near ten to the
/// minus forty-four metres — which is what makes every other storage type's error measurable at all,
/// by holding the algorithm and the inputs fixed and differencing the results.
/// </remarks>
public sealed class PreciseStorageProfile : IPropagatorHost
{
	// The storage type is inferred from the math instance and never written here.
	private readonly Func<ElementSet, double, PropagatedState> propagate =
		Sgp4PropagatorHost.Create(PreciseStorageMath.Instance).Propagate;

	/// <inheritdoc />
	public string StorageName => "PreciseNumber";

	/// <inheritdoc />
	public int ApproximateSignificantDigits => PreciseNumber.MinimumDivisionPrecision;

	/// <inheritdoc />
	/// <remarks>
	/// The probe halves a trial step until the sum stops changing, which never happens for an exact
	/// type, so this reports the probe's own floor rather than a property of the arithmetic. That is
	/// the correct answer: within any distance this application deals in, the type distinguishes
	/// everything.
	/// </remarks>
	public double SmallestDistinguishableStepMeters(double magnitudeMeters) =>
		StorageProbe.SmallestDistinguishableStep(magnitudeMeters.ToPreciseNumber()).To<double>();

	/// <inheritdoc />
	/// <remarks>
	/// Runs at <see cref="PreciseStorageMath.DefaultSignificantDigits"/>, and costs orders of
	/// magnitude more than the fixed-width facades per call — which <see cref="PropagatedState.Elapsed"/>
	/// reports rather than hides. Rounded to <see langword="double"/> on the way out, so the state
	/// shown here cannot itself demonstrate the extra digits; only a comparison carried out in this
	/// storage type can.
	/// </remarks>
	public PropagatedState Propagate(ElementSet elements, double minutesSinceEpoch) =>
		propagate(elements, minutesSinceEpoch);

	/// <summary>
	/// Gets a nominal low Earth orbital radius, built through the alias package's quantity types.
	/// </summary>
	/// <remarks>
	/// <c>Length</c> here means <c>Length&lt;PreciseNumber&gt;</c>, supplied by the alias package's
	/// global usings — the same expression as in the other three facades, bound to a different
	/// arithmetic.
	/// </remarks>
	public static Length ReferenceOrbitalRadius => Length.FromKilometer(7000.ToPreciseNumber());
}
