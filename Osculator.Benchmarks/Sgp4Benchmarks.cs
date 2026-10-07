// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Benchmarks;

using BenchmarkDotNet.Attributes;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;

/// <summary>
/// The cost of one SGP4 propagation in each storage type: validation gate 6, and the M3 gate.
/// </summary>
/// <remarks>
/// <para>
/// Each benchmark is <em>initialize plus one propagation</em>, because that is what a caller pays to
/// get one state from one element set it has not seen before, which is the on-demand reference the
/// precise path exists to be. Initialization is also where trap 9 lived: before
/// <c>ToWorkingPrecision</c>, one <see cref="PreciseNumber"/> initialization took 18.8 seconds.
/// </para>
/// <para>
/// The reference type runs at thirty significant digits, the precision the storage comparison uses,
/// so the cost here is the cost of the figure quoted beside it.
/// </para>
/// <para>
/// Every result is reduced to its x coordinate so nothing is optimised away and the four methods
/// return comparable work.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class Sgp4Benchmarks
{
	/// <summary>The working precision of the reference arithmetic, in significant digits.</summary>
	public const int ReferenceDigits = 30;

	private readonly PreciseStorageMath precise = new(ReferenceDigits);
	private ElementSet elements = BenchmarkOrbits.Elements(BenchmarkOrbit.NearEarth);
	private PreciseNumber preciseMinutes = PreciseNumber.Zero;

	/// <summary>Gets or sets the orbit class being propagated.</summary>
	[ParamsAllValues]
	public BenchmarkOrbit Orbit { get; set; }

	/// <summary>Parses the element set once, so the measurement is the model rather than the parser.</summary>
	[GlobalSetup]
	public void Setup()
	{
		elements = BenchmarkOrbits.Elements(Orbit);
		preciseMinutes = BenchmarkOrbits.MinutesSinceEpoch.ToPreciseNumber();
	}

	/// <summary>Initializes and propagates in single precision.</summary>
	/// <returns>The x coordinate, so the call is not optimised away.</returns>
	[Benchmark]
	public float SinglePrecision()
	{
		Sgp4Satellite<float> satellite = Sgp4<float>.Initialize(elements, FloatStorageMath.Instance);
		return Sgp4<float>.Propagate(satellite, (float)BenchmarkOrbits.MinutesSinceEpoch, FloatStorageMath.Instance).State.X;
	}

	/// <summary>Initializes and propagates in double precision.</summary>
	/// <returns>The x coordinate, so the call is not optimised away.</returns>
	[Benchmark(Baseline = true)]
	public double DoublePrecision()
	{
		Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(elements, DoubleStorageMath.Instance);
		return Sgp4<double>.Propagate(satellite, BenchmarkOrbits.MinutesSinceEpoch, DoubleStorageMath.Instance).State.X;
	}

	/// <summary>Initializes and propagates in the decimal floating point type.</summary>
	/// <returns>The x coordinate, so the call is not optimised away.</returns>
	[Benchmark]
	public decimal DecimalPrecision()
	{
		Sgp4Satellite<decimal> satellite = Sgp4<decimal>.Initialize(elements, DecimalStorageMath.Instance);
		return Sgp4<decimal>.Propagate(satellite, (decimal)BenchmarkOrbits.MinutesSinceEpoch, DecimalStorageMath.Instance).State.X;
	}

	/// <summary>Initializes and propagates in the arbitrary-precision reference type.</summary>
	/// <returns>The x coordinate, so the call is not optimised away.</returns>
	[Benchmark]
	public PreciseNumber ReferencePrecision()
	{
		Sgp4Satellite<PreciseNumber> satellite = Sgp4<PreciseNumber>.Initialize(elements, precise);
		return Sgp4<PreciseNumber>.Propagate(satellite, preciseMinutes, precise).State.X;
	}
}
