// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

using System.Collections.Generic;

/// <summary>
/// The quantization step of every element-set field in one distributed format.
/// </summary>
/// <remarks>
/// Obtain one from <see cref="ElementFieldQuantization.For(ElementSetFormat)"/>, which says where
/// each format's numbers come from.
/// </remarks>
public sealed class ElementFieldSteps
{
	internal ElementFieldSteps(ElementSetFormat format, double eccentricity, int exponentialFieldSignificantDigits)
	{
		Format = format;
		Eccentricity = eccentricity;
		ExponentialFieldSignificantDigits = exponentialFieldSignificantDigits;
		FixedDecimalSteps = new Dictionary<string, double>
		{
			[nameof(ElementSet.Epoch)] = EpochDays,
			[nameof(ElementSet.MeanMotion)] = MeanMotion,
			[nameof(ElementSet.Eccentricity)] = Eccentricity,
			[nameof(ElementSet.Inclination)] = Inclination,
			[nameof(ElementSet.RightAscensionOfAscendingNode)] = RightAscensionOfAscendingNode,
			[nameof(ElementSet.ArgumentOfPericenter)] = ArgumentOfPericenter,
			[nameof(ElementSet.MeanAnomaly)] = MeanAnomaly,
			[nameof(ElementSet.MeanMotionDot)] = MeanMotionDot,
		};
	}

	/// <summary>Gets the format these steps describe.</summary>
	public ElementSetFormat Format { get; }

	/// <summary>Gets the epoch step, in days: eight decimals of a day, so 864 microseconds.</summary>
	public double EpochDays { get; } = 1e-8;

	/// <summary>Gets the mean motion step, in revolutions per day: eight decimals.</summary>
	public double MeanMotion { get; } = 1e-8;

	/// <summary>Gets the eccentricity step, dimensionless: seven decimals in a TLE, eight in OMM.</summary>
	public double Eccentricity { get; }

	/// <summary>Gets the inclination step, in degrees: four decimals.</summary>
	public double Inclination { get; } = 1e-4;

	/// <summary>Gets the right ascension step, in degrees: four decimals.</summary>
	public double RightAscensionOfAscendingNode { get; } = 1e-4;

	/// <summary>Gets the argument of pericenter step, in degrees: four decimals.</summary>
	public double ArgumentOfPericenter { get; } = 1e-4;

	/// <summary>Gets the mean anomaly step, in degrees: four decimals.</summary>
	public double MeanAnomaly { get; } = 1e-4;

	/// <summary>Gets the first derivative of mean motion step, in revolutions per day squared: eight decimals.</summary>
	public double MeanMotionDot { get; } = 1e-8;

	/// <summary>
	/// Gets the number of significant digits carried by the fields that quantize relatively rather
	/// than absolutely: five in a TLE, eight for B* in OMM.
	/// </summary>
	public int ExponentialFieldSignificantDigits { get; }

	/// <summary>
	/// Gets the absolute quantization step of every fixed-decimal field, keyed by field name.
	/// </summary>
	/// <remarks>
	/// The exponential fields are excluded because their step depends on the value; use
	/// <see cref="StepForExponentialField(double)"/> for those.
	/// </remarks>
	public IReadOnlyDictionary<string, double> FixedDecimalSteps { get; }

	/// <summary>
	/// Gets the absolute quantization step of a field that carries a fixed number of significant digits.
	/// </summary>
	/// <param name="value">The field's value.</param>
	/// <returns>The smallest change the format can express at <paramref name="value"/>, or zero when <paramref name="value"/> is zero.</returns>
	public double StepForExponentialField(double value)
	{
		if (value == 0.0)
		{
			return 0.0;
		}

		double magnitude = System.Math.Abs(value);
		int decade = (int)System.Math.Floor(System.Math.Log10(magnitude));
		return System.Math.Pow(10.0, decade - (ExponentialFieldSignificantDigits - 1));
	}
}
