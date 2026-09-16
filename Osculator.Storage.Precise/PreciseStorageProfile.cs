// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Storage;

using ktsu.Osculator.Core.Storage;
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
public sealed class PreciseStorageProfile : IStorageProfile
{
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
