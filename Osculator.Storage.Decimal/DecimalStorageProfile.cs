// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Storage;

using ktsu.Osculator.Core.Storage;

/// <summary>
/// The storage profile for <c>decimal</c>.
/// </summary>
/// <remarks>
/// <para>
/// Twenty-eight significant digits with no binary rounding — but only at unit magnitude. Its
/// precision is <em>absolute</em> rather than relative: the type is a 96-bit integer with a scale
/// capped at twenty-eight decimal places, so a value of order 1e-20 has room for eight significant
/// digits where a <see langword="double"/> still has sixteen. The two cross at 1e-12.
/// </para>
/// <para>
/// That is not a footnote. Measured over the whole verification set, its twelve extra digits buy a
/// factor of 2.8 against <see langword="double"/> at the median and lose a factor of 5.6 at the
/// extreme, because SGP4's drag coefficients sit at and below the crossover. See
/// <c>StorageComparisonTests</c>.
/// </para>
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
