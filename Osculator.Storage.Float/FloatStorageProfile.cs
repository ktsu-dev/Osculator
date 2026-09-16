// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Storage;

using ktsu.Osculator.Core.Storage;

/// <summary>
/// The storage profile for <c>float</c>.
/// </summary>
/// <remarks>
/// The type that cannot represent the residual at all. A 24-bit mantissa at a magnitude of seven
/// million metres has an ulp of half a metre, so a comparison against a centimetre-accurate
/// laser-ranging orbit is not inaccurate here — it is impossible, before any physics is computed.
/// </remarks>
public sealed class FloatStorageProfile : IStorageProfile
{
	/// <inheritdoc />
	public string StorageName => "float";

	/// <inheritdoc />
	public int ApproximateSignificantDigits => 7;

	/// <inheritdoc />
	public double SmallestDistinguishableStepMeters(double magnitudeMeters) =>
		double.CreateTruncating(StorageProbe.SmallestDistinguishableStep(float.CreateTruncating(magnitudeMeters)));

	/// <summary>
	/// Gets a nominal low Earth orbital radius, built through the alias package's quantity types.
	/// </summary>
	/// <remarks>
	/// Nothing in this file names a storage type in the quantity: <c>Length</c> here means
	/// <c>Length&lt;float&gt;</c>, supplied by the alias package's global usings. That is the whole
	/// demonstration — the same expression appears in all four facades and binds to a different
	/// arithmetic in each.
	/// </remarks>
	public static Length ReferenceOrbitalRadius => Length.FromKilometer(7000.0f);
}
