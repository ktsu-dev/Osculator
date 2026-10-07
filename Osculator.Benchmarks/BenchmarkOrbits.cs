// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Benchmarks;

using ktsu.Osculator.Core.Elements;

/// <summary>
/// The orbit classes the propagation benchmarks are run over.
/// </summary>
public enum BenchmarkOrbit
{
	/// <summary>A low Earth orbit, which runs the near-earth half of the model only.</summary>
	NearEarth,

	/// <summary>A Molniya orbit in the half-day resonance, which runs the deep-space half as well.</summary>
	DeepSpace,
}

/// <summary>
/// The element sets behind <see cref="BenchmarkOrbit"/>, taken from the published verification set.
/// </summary>
/// <remarks>
/// <para>
/// Copied rather than read from <c>Osculator.Tests/Data</c>, because a benchmark host has to run from
/// wherever BenchmarkDotNet puts its generated project, and two lines of text are cheaper to carry
/// than a path back into another project's output.
/// </para>
/// <para>
/// One of each half of the model, because the two cost different amounts. The deep-space case is
/// 21897, a Molniya orbit in the half-day resonance: that is the path that integrates the resonance
/// terms in fixed steps from epoch, so its cost grows with the arc where the near-earth model's does
/// not. Neither is a 333xx object; those are constructed to provoke error codes and would measure an
/// early return.
/// </para>
/// </remarks>
public static class BenchmarkOrbits
{
	/// <summary>How far past epoch every benchmark propagates, in minutes: one day.</summary>
	/// <remarks>
	/// A day is the horizon the data term is quoted at. It is also long enough that the deep-space
	/// resonance integrator takes a realistic number of steps rather than none.
	/// </remarks>
	public const double MinutesSinceEpoch = 1440.0;

	/// <summary>Gets the element set for an orbit class.</summary>
	/// <param name="orbit">The orbit class.</param>
	/// <returns>The parsed element set.</returns>
	public static ElementSet Elements(BenchmarkOrbit orbit) => orbit switch
	{
		BenchmarkOrbit.NearEarth => TleParser.Parse(
			"1 06251U 62025E   06176.82412014  .00008885  00000-0  12808-3 0  3985",
			"2 06251  58.0579  54.0425 0030035 139.1568 221.1854 15.56387291  6774"),
		BenchmarkOrbit.DeepSpace => TleParser.Parse(
			"1 21897U 92011A   06176.02341244 -.00001273  00000-0 -13525-3 0  3044",
			"2 21897  62.1749 198.0096 7421690 253.0462  20.1561  2.01269994104880"),
		_ => throw new System.ArgumentOutOfRangeException(nameof(orbit), orbit, "Not a benchmark orbit."),
	};
}
