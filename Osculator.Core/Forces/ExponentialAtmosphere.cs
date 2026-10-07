// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System;
using System.Globalization;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// The piecewise exponential atmosphere: in each altitude band, density falls exponentially from a
/// tabulated base value with a tabulated scale height.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// Vallado, <em>Fundamentals of Astrodynamics and Applications</em>, Table 8-4, from CIRA-72 at
/// moderate solar activity. It is a static, mean model: no solar cycle, no day/night bulge, no
/// geomagnetic storms, which together move real density by a factor of ten at 400 km. Drag is
/// where Δ_model is largest for a low orbit, and this model is the reason; NRLMSISE-00 is the
/// refinement, behind the same density function.
/// </para>
/// <para>
/// The table was checked as transcribed, without a second copy to compare against: each band's
/// base density, run up its own scale height to the next band's base altitude, lands on the next
/// band's base density to within 0.1 % at every one of the 27 boundaries. A single mistyped digit
/// breaks two of them. The test asserts it.
/// </para>
/// <para>
/// Below the first band the sea-level band applies, and above 1000 km the last band continues. The
/// altitude is the caller's; <see cref="AtmosphericDrag{T}"/> measures it from the ellipsoid.
/// </para>
/// </remarks>
public sealed class ExponentialAtmosphere<T>
	where T : struct, INumber<T>
{
	// Base altitude (km), base density (kg/m³), scale height (km).
	private static readonly string[][] Table =
	[
		["0", "1.225", "7.249"],
		["25", "3.899e-2", "6.349"],
		["30", "1.774e-2", "6.682"],
		["40", "3.972e-3", "7.554"],
		["50", "1.057e-3", "8.382"],
		["60", "3.206e-4", "7.714"],
		["70", "8.770e-5", "6.549"],
		["80", "1.905e-5", "5.799"],
		["90", "3.396e-6", "5.382"],
		["100", "5.297e-7", "5.877"],
		["110", "9.661e-8", "7.263"],
		["120", "2.438e-8", "9.473"],
		["130", "8.484e-9", "12.636"],
		["140", "3.845e-9", "16.149"],
		["150", "2.070e-9", "22.523"],
		["180", "5.464e-10", "29.740"],
		["200", "2.789e-10", "37.105"],
		["250", "7.248e-11", "45.546"],
		["300", "2.418e-11", "53.628"],
		["350", "9.518e-12", "53.298"],
		["400", "3.725e-12", "58.515"],
		["450", "1.585e-12", "60.828"],
		["500", "6.967e-13", "63.822"],
		["600", "1.454e-13", "71.835"],
		["700", "3.614e-14", "88.667"],
		["800", "1.170e-14", "124.64"],
		["900", "5.245e-15", "181.05"],
		["1000", "3.019e-15", "268.00"],
	];

	// e to more digits than any storage type here holds, so Pow(e, x) is exp(x) in each.
	private const string EulerNumber = "2.718281828459045235360287471352662497757247093699959574966967627724076630353547594571382178525166427";

	private readonly T[] baseAltitudes;
	private readonly T[] baseDensities;
	private readonly T[] scaleHeights;
	private readonly T euler;
	private readonly IStorageMath<T> math;

	/// <summary>Initializes a new instance of the <see cref="ExponentialAtmosphere{T}"/> class.</summary>
	/// <param name="storageMath">The transcendental functions and working precision for <typeparamref name="T"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="storageMath"/> is null.</exception>
	public ExponentialAtmosphere(IStorageMath<T> storageMath)
	{
		Ensure.NotNull(storageMath);
		math = storageMath;
		int bands = Table.Length;
		baseAltitudes = new T[bands];
		baseDensities = new T[bands];
		scaleHeights = new T[bands];
		for (int i = 0; i < bands; i++)
		{
			baseAltitudes[i] = Parse(Table[i][0]);
			baseDensities[i] = Parse(Table[i][1]);
			scaleHeights[i] = Parse(Table[i][2]);
		}

		euler = Parse(EulerNumber);
	}

	/// <summary>Gets the number of altitude bands.</summary>
	public int BandCount => baseAltitudes.Length;

	/// <summary>Gets a band's base altitude, in kilometres.</summary>
	/// <param name="band">The band, from 0.</param>
	/// <returns>The altitude.</returns>
	public T BaseAltitude(int band) => baseAltitudes[band];

	/// <summary>Gets a band's base density, in kilograms per cubic metre.</summary>
	/// <param name="band">The band, from 0.</param>
	/// <returns>The density.</returns>
	public T BaseDensity(int band) => baseDensities[band];

	/// <summary>Gets a band's scale height, in kilometres.</summary>
	/// <param name="band">The band, from 0.</param>
	/// <returns>The scale height.</returns>
	public T ScaleHeight(int band) => scaleHeights[band];

	/// <summary>The density at an altitude.</summary>
	/// <param name="altitudeKm">The altitude above the ellipsoid, in kilometres.</param>
	/// <returns>The density, in kilograms per cubic metre.</returns>
	public T Density(T altitudeKm)
	{
		int band = 0;
		while (band + 1 < baseAltitudes.Length && altitudeKm >= baseAltitudes[band + 1])
		{
			band++;
		}

		T exponent = math.ToWorkingPrecision((baseAltitudes[band] - altitudeKm) / scaleHeights[band]);
		return math.ToWorkingPrecision(baseDensities[band] * math.Pow(euler, exponent));
	}

	private static T Parse(string literal) => T.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
}
