// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Math.Precise;

using ktsu.PreciseNumber;

/// <summary>
/// The transcendental functions SGP4 needs and <see cref="PreciseNumber"/> does not yet provide.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PreciseNumber"/> implements <c>INumber&lt;PreciseNumber&gt;</c> and nothing else — no
/// <c>IRootFunctions</c>, no <c>ITrigonometricFunctions</c> — so there is no <c>Sqrt</c>,
/// <c>Sin</c>, <c>Cos</c> or <c>Atan2</c>. SGP4 is saturated with all four. This type exists to
/// supply them until <see href="https://github.com/ktsu-dev/PreciseNumber/issues/78">
/// ktsu-dev/PreciseNumber#78</see> lands, at which point it should shrink to nothing.
/// </para>
/// <para>
/// Two constraints govern every function added here, both established in that issue and its
/// children. Intermediates must be truncated with <see cref="PreciseNumber.ReduceSignificance(int)"/>
/// on every step: multiplication is exact, so an un-truncated series carries a significand that
/// grows without bound while only its leading digits are correct. And the accuracy contract is
/// <em>faithful</em> rounding — within one unit in the last place at the requested precision — not
/// correct rounding, which runs into the table-maker's dilemma and is not worth its cost here.
/// </para>
/// <para>
/// Argument reduction is separately capped by the precision of the constant it reduces by.
/// <see cref="PreciseNumber.Pi"/> carries 26 significant digits today against a default division
/// precision of 50, so reducing a large angle modulo two pi cannot produce more than about 21
/// correct digits however many are asked for. See
/// <see href="https://github.com/ktsu-dev/PreciseNumber/issues/79">ktsu-dev/PreciseNumber#79</see>;
/// it blocks any trigonometry here from being better than its constants.
/// </para>
/// </remarks>
public static class PreciseMath
{
	/// <summary>
	/// Gets the working precision the functions here compute at, in significant digits.
	/// </summary>
	/// <remarks>
	/// Matches <see cref="PreciseNumber.MinimumDivisionPrecision"/>, so a result composed of a
	/// division and a transcendental carries one precision rather than two.
	/// </remarks>
	public static int DefaultSignificantDigits => PreciseNumber.MinimumDivisionPrecision;
}
