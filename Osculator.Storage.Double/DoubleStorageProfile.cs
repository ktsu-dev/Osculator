// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Storage;

using ktsu.Osculator.Core.Storage;

/// <summary>
/// The storage profile for <c>double</c>.
/// </summary>
/// <remarks>
/// The baseline every other storage type is compared against, and the one almost every astrodynamics
/// library uses throughout. One ulp at an orbital radius is around a nanometre, which is far below
/// the model error — proving that, rather than assuming it, is the point of the comparison.
/// </remarks>
public sealed class DoubleStorageProfile : IStorageProfile
{
	/// <inheritdoc />
	public string StorageName => "double";

	/// <inheritdoc />
	public int ApproximateSignificantDigits => 15;

	/// <inheritdoc />
	public double SmallestDistinguishableStepMeters(double magnitudeMeters) =>
		double.CreateTruncating(StorageProbe.SmallestDistinguishableStep(double.CreateTruncating(magnitudeMeters)));

	/// <summary>
	/// Gets a nominal low Earth orbital radius, built through the alias package's quantity types.
	/// </summary>
	/// <remarks>
	/// Nothing in this file names a storage type in the quantity: <c>Length</c> here means
	/// <c>Length&lt;double&gt;</c>, supplied by the alias package's global usings. That is the whole
	/// demonstration — the same expression appears in all four facades and binds to a different
	/// arithmetic in each.
	/// </remarks>
	public static Length ReferenceOrbitalRadius => Length.FromKilometer(7000.0);
}
