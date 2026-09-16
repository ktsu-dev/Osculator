// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

using System.Collections.Generic;

/// <summary>
/// The quantization step of each element-set field, as fixed by the two-line element format.
/// </summary>
/// <remarks>
/// <para>
/// This is the foundation of the data error term. A two-line element set is a fixed-column text
/// format, so each field carries a specific, knowable number of digits and nothing finer survives
/// the round trip.
/// </para>
/// <para>
/// <strong>These steps describe the two-line format. The OMM JSON is not always equivalent.</strong>
/// The distributed JSON is generated from the originating values rather than by re-reading the text,
/// so the fields the text format compresses come through finer. Measured on one ISS element set:
/// mean motion and the first derivative are identical in both, eccentricity gains one digit
/// (0.0004923 against 0.00049233), and the drag term gains three (0.00012172 against 0.00012172288,
/// five significant digits against eight).
/// </para>
/// <para>
/// So the data error term depends on which representation was ingested, and an application that
/// mixes the two is comparing element sets of different precision. Read the source format off the
/// data rather than assuming, and prefer OMM where both are available.
/// </para>
/// <para>
/// The steps are exact properties of the format, not estimates. What is <em>not</em> known here is
/// what each step is worth in metres after propagation: that depends on the orbit and the horizon,
/// and is measured by perturbing each field by half a step and re-propagating.
/// </para>
/// </remarks>
public static class ElementFieldQuantization
{
	/// <summary>Gets the epoch step, in days. Columns 19-32 of line 1 carry eight decimals of a day, so about 864 microseconds.</summary>
	public static double EpochDays => 1e-8;

	/// <summary>Gets the mean motion step, in revolutions per day. Columns 53-63 of line 2 carry eight decimals.</summary>
	public static double MeanMotion => 1e-8;

	/// <summary>Gets the eccentricity step, dimensionless. Columns 27-33 of line 2 carry seven digits with an assumed leading decimal point.</summary>
	/// <remarks>The OMM JSON carries one further digit for this field; see the remarks on this class.</remarks>
	public static double Eccentricity => 1e-7;

	/// <summary>Gets the inclination step, in degrees. Columns 9-16 of line 2 carry four decimals.</summary>
	public static double Inclination => 1e-4;

	/// <summary>Gets the right ascension step, in degrees. Columns 18-25 of line 2 carry four decimals.</summary>
	public static double RightAscensionOfAscendingNode => 1e-4;

	/// <summary>Gets the argument of pericenter step, in degrees. Columns 35-42 of line 2 carry four decimals.</summary>
	public static double ArgumentOfPericenter => 1e-4;

	/// <summary>Gets the mean anomaly step, in degrees. Columns 44-51 of line 2 carry four decimals.</summary>
	public static double MeanAnomaly => 1e-4;

	/// <summary>Gets the first derivative of mean motion step, in revolutions per day squared. Columns 34-43 of line 1 carry eight decimals.</summary>
	public static double MeanMotionDot => 1e-8;

	/// <summary>
	/// Gets the number of significant digits carried by the fields written in the format's
	/// exponential notation, which quantize relatively rather than absolutely.
	/// </summary>
	/// <remarks>
	/// <see cref="ElementSet.BStar"/> and <see cref="ElementSet.MeanMotionDdot"/> are written as a
	/// five-digit mantissa with an assumed leading decimal point and a single-digit exponent, so
	/// their step is a fraction of the value rather than a fixed increment. This is the field the two
	/// distributed formats disagree about most: the OMM JSON carries eight significant digits where
	/// the text carries five.
	/// </remarks>
	public static int ExponentialFieldSignificantDigits => 5;

	/// <summary>
	/// Gets the absolute quantization step of a field written in exponential notation.
	/// </summary>
	/// <param name="value">The field's value.</param>
	/// <returns>The smallest change the format can express at <paramref name="value"/>, or zero when <paramref name="value"/> is zero.</returns>
	public static double StepForExponentialField(double value)
	{
		if (value == 0.0)
		{
			return 0.0;
		}

		double magnitude = System.Math.Abs(value);
		int decade = (int)System.Math.Floor(System.Math.Log10(magnitude));
		return System.Math.Pow(10.0, decade - (ExponentialFieldSignificantDigits - 1));
	}

	/// <summary>
	/// Gets the absolute quantization step of every fixed-decimal field, keyed by field name.
	/// </summary>
	/// <remarks>
	/// The exponential fields are excluded because their step depends on the value; use
	/// <see cref="StepForExponentialField(double)"/> for those.
	/// </remarks>
	public static IReadOnlyDictionary<string, double> FixedDecimalSteps { get; } = new Dictionary<string, double>
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
