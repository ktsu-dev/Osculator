// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// The six mean elements the deep-space routines read and write together.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="MeanMotion">The mean motion, in radians per minute.</param>
/// <param name="Eccentricity">The eccentricity, dimensionless.</param>
/// <param name="Inclination">The inclination, in radians.</param>
/// <param name="MeanAnomaly">The mean anomaly, in radians.</param>
/// <param name="ArgumentOfPerigee">The argument of perigee, in radians.</param>
/// <param name="RightAscension">The right ascension of the ascending node, in radians.</param>
/// <remarks>
/// The published algorithm passes these six as mutable references through every deep-space routine,
/// which is what makes that code hard to check against the paper. Grouping them into a value that
/// goes in and comes back out says the same thing and keeps the routines free of side effects, so a
/// satellite can be propagated to two epochs in any order — or from two threads — and get the same
/// answer both times.
/// </remarks>
internal readonly record struct DeepSpaceElements<T>(
	T MeanMotion,
	T Eccentricity,
	T Inclination,
	T MeanAnomaly,
	T ArgumentOfPerigee,
	T RightAscension)
	where T : struct, INumber<T>;
