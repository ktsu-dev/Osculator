// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Storage;

using ktsu.Osculator.Core.Storage;

/// <summary>
/// The storage profile for <c>decimal</c>.
/// </summary>
/// <remarks>
/// Twenty-eight significant digits with no binary rounding, at a large cost in speed. It sits between
/// the two binary floats and the arbitrary-precision reference, and is the storage type that shows
/// the arithmetic term shrinking smoothly rather than collapsing to zero.
/// </remarks>
public sealed class DecimalStorageProfile : IStorageProfile
{
	/// <inheritdoc />
	public string StorageName => "decimal";

	/// <inheritdoc />
	public int ApproximateSignificantDigits => 28;

	/// <inheritdoc />
	public double SmallestDistinguishableStepMeters(double magnitudeMeters) =>
		double.CreateTruncating(StorageProbe.SmallestDistinguishableStep(decimal.CreateTruncating(magnitudeMeters)));

	/// <summary>
	/// Gets a nominal low Earth orbital radius, built through the alias package's quantity types.
	/// </summary>
	/// <remarks>
	/// Nothing in this file names a storage type in the quantity: <c>Length</c> here means
	/// <c>Length&lt;decimal&gt;</c>, supplied by the alias package's global usings. That is the whole
	/// demonstration — the same expression appears in all four facades and binds to a different
	/// arithmetic in each.
	/// </remarks>
	public static Length ReferenceOrbitalRadius => Length.FromKilometer(7000.0m);
}
