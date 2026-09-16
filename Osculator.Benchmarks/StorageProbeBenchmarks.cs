// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Benchmarks;

using BenchmarkDotNet.Attributes;
using ktsu.Osculator.Core.Storage;
using ktsu.PreciseNumber;

/// <summary>
/// The cost of one arithmetic probe in each storage type.
/// </summary>
/// <remarks>
/// <para>
/// The precision comparison is only honest if the cost of precision is reported beside it. This is
/// the smallest thing worth measuring and it establishes the shape early: the ratio between
/// <see langword="double"/> and <see cref="PreciseNumber"/> here is the floor of what a propagator
/// will pay, not the ceiling.
/// </para>
/// <para>
/// Milestone M3 carries a hard gate on that ratio. If one <see cref="PreciseNumber"/> propagation is
/// too slow to serve as an on-demand reference, the design is revisited before anything is built on
/// top of it — which is why this project exists before the propagator does.
/// </para>
/// <para>
/// Operands are prepared in <see cref="Setup"/> rather than converted inside each benchmark, so the
/// measurement is the probe rather than the conversion.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class StorageProbeBenchmarks
{
	/// <summary>A nominal low Earth orbital radius, in metres.</summary>
	private const double LeoRadiusMeters = 7_000_000.0;

	private float singlePrecisionRadius;
	private double doublePrecisionRadius;
	private decimal decimalRadius;
	private PreciseNumber preciseRadius = PreciseNumber.Zero;

	/// <summary>
	/// Prepares one orbital radius in each storage type.
	/// </summary>
	[GlobalSetup]
	public void Setup()
	{
		singlePrecisionRadius = (float)LeoRadiusMeters;
		doublePrecisionRadius = LeoRadiusMeters;
		decimalRadius = (decimal)LeoRadiusMeters;
		preciseRadius = LeoRadiusMeters.ToPreciseNumber();
	}

	/// <summary>Probes the smallest distinguishable step in single precision.</summary>
	/// <returns>The step, so the call is not optimised away.</returns>
	[Benchmark(Baseline = true)]
	public float ProbeSinglePrecision() => StorageProbe.SmallestDistinguishableStep(singlePrecisionRadius);

	/// <summary>Probes the smallest distinguishable step in double precision.</summary>
	/// <returns>The step, so the call is not optimised away.</returns>
	[Benchmark]
	public double ProbeDoublePrecision() => StorageProbe.SmallestDistinguishableStep(doublePrecisionRadius);

	/// <summary>Probes the smallest distinguishable step in the decimal floating point type.</summary>
	/// <returns>The step, so the call is not optimised away.</returns>
	[Benchmark]
	public decimal ProbeDecimal() => StorageProbe.SmallestDistinguishableStep(decimalRadius);

	/// <summary>Probes the smallest distinguishable step in the arbitrary-precision reference type.</summary>
	/// <returns>The step, so the call is not optimised away.</returns>
	[Benchmark]
	public PreciseNumber ProbePrecise() => StorageProbe.SmallestDistinguishableStep(preciseRadius);
}
