// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Storage;

/// <summary>
/// What one numeric storage type can and cannot represent, reported by a facade that is bound to
/// that type.
/// </summary>
/// <remarks>
/// <para>
/// The four storage facades exist because the alias packages inject global usings project-wide —
/// only one storage type can win per project — so binding four of them takes four projects. This
/// interface is the seam the application holds them through.
/// </para>
/// <para>
/// The arithmetic error term is measured, not asserted, and this is the first thing that can be
/// measured before a propagator exists: what the type's smallest representable step is at an
/// orbital radius. At roughly 7,000 km a <see langword="float"/> cannot express half a metre, which
/// makes a comparison against a centimetre-accurate laser-ranging orbit impossible before any
/// physics is computed.
/// </para>
/// </remarks>
public interface IStorageProfile
{
	/// <summary>Gets the storage type's name as it appears in the user interface.</summary>
	public string StorageName { get; }

	/// <summary>Gets the approximate number of significant decimal digits the type carries.</summary>
	public int ApproximateSignificantDigits { get; }

	/// <summary>
	/// Gets the smallest increment distinguishable at a given magnitude, computed in the facade's
	/// own storage type.
	/// </summary>
	/// <param name="magnitudeMeters">The magnitude to probe, in metres.</param>
	/// <returns>
	/// The smallest step that changes the value, in metres. Returned as a <see langword="double"/>
	/// for reporting only; the search itself runs in the facade's storage type.
	/// </returns>
	public double SmallestDistinguishableStepMeters(double magnitudeMeters);
}
