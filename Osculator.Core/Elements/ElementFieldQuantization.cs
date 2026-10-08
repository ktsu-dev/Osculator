// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

using System.Collections.Generic;

/// <summary>
/// The quantization step of each element-set field, which depends on the format it was read from.
/// </summary>
/// <remarks>
/// <para>
/// This is the foundation of the data error term. Each field carries a specific, knowable number of
/// digits and nothing finer survives the round trip.
/// </para>
/// <para>
/// <strong>The two distributed formats do not carry the same digits.</strong> The OMM JSON is
/// generated from the originating values rather than by re-reading the text, so the fields the text
/// format compresses come through finer. Measured on one ISS element set carried in both: mean motion
/// and the first derivative are identical, eccentricity gains one digit (0.0004923 against
/// 0.00049233), and the drag term gains three (0.00012172 against 0.00012172288, five significant
/// digits against eight). The angles carry four decimals in both.
/// </para>
/// <para>
/// <strong>The epoch is the same in both, which the JSON's notation hides.</strong> OMM writes it
/// to the microsecond, which reads as a step 864 times finer than a TLE's eighth decimal of a day.
/// It is not: both ISS epochs committed in the tests, <c>08:51:14.158368</c> and
/// <c>21:14:23.428032</c>, are exact whole multiples of 1e-8 day, which a finer underlying value
/// would land on by chance about once in 750,000 pairs. The JSON renders the text's epoch, and 864 µs
/// happens to divide evenly into microseconds.
/// </para>
/// <para>
/// So every <see cref="ElementSet"/> records the format it was read from, and the steps are taken
/// from that record rather than assumed. The properties on this class are the two-line format's, kept
/// because they are what a TLE column holds; anything measuring an element set should go through
/// <see cref="For(ElementSet)"/>.
/// </para>
/// <para>
/// The steps are exact properties of the format, not estimates. What is <em>not</em> known here is
/// what each step is worth in metres after propagation: that depends on the orbit and the horizon,
/// and is measured by perturbing each field by half a step and re-propagating.
/// </para>
/// </remarks>
public static class ElementFieldQuantization
{
	/// <summary>Gets the steps of the fixed-column two-line format.</summary>
	public static ElementFieldSteps Tle { get; } = new(ElementSetFormat.Tle, eccentricity: 1e-7, exponentialFieldSignificantDigits: 5);

	/// <summary>Gets the steps of the OMM JSON CelesTrak distributes.</summary>
	/// <remarks>
	/// The eight significant digits are B*'s, measured. <see cref="ElementSet.MeanMotionDdot"/>
	/// shares the setting but is unmeasured, because every element set sampled carried it as zero;
	/// SGP4 never reads it, so nothing here depends on the guess.
	/// </remarks>
	public static ElementFieldSteps Omm { get; } = new(ElementSetFormat.Omm, eccentricity: 1e-8, exponentialFieldSignificantDigits: 8);

	/// <summary>Gets the steps of one format.</summary>
	/// <param name="format">The format.</param>
	/// <returns>The steps that format writes each field to.</returns>
	/// <exception cref="System.ArgumentOutOfRangeException">The format is not one this class knows.</exception>
	public static ElementFieldSteps For(ElementSetFormat format) => format switch
	{
		ElementSetFormat.Tle => Tle,
		ElementSetFormat.Omm => Omm,
		_ => throw new System.ArgumentOutOfRangeException(nameof(format), format, "Unknown element set format."),
	};

	/// <summary>Gets the steps of the format an element set was read from.</summary>
	/// <param name="elements">The element set.</param>
	/// <returns>The steps its <see cref="ElementSet.Format"/> writes each field to.</returns>
	public static ElementFieldSteps For(ElementSet elements)
	{
		Ensure.NotNull(elements);
		return For(elements.Format);
	}

	/// <summary>Gets the epoch step, in days. Columns 19-32 of line 1 carry eight decimals of a day, so about 864 microseconds.</summary>
	public static double EpochDays => Tle.EpochDays;

	/// <summary>Gets the mean motion step, in revolutions per day. Columns 53-63 of line 2 carry eight decimals.</summary>
	public static double MeanMotion => Tle.MeanMotion;

	/// <summary>Gets the eccentricity step, dimensionless. Columns 27-33 of line 2 carry seven digits with an assumed leading decimal point.</summary>
	/// <remarks>The OMM JSON carries one further digit for this field; see <see cref="Omm"/>.</remarks>
	public static double Eccentricity => Tle.Eccentricity;

	/// <summary>Gets the inclination step, in degrees. Columns 9-16 of line 2 carry four decimals.</summary>
	public static double Inclination => Tle.Inclination;

	/// <summary>Gets the right ascension step, in degrees. Columns 18-25 of line 2 carry four decimals.</summary>
	public static double RightAscensionOfAscendingNode => Tle.RightAscensionOfAscendingNode;

	/// <summary>Gets the argument of pericenter step, in degrees. Columns 35-42 of line 2 carry four decimals.</summary>
	public static double ArgumentOfPericenter => Tle.ArgumentOfPericenter;

	/// <summary>Gets the mean anomaly step, in degrees. Columns 44-51 of line 2 carry four decimals.</summary>
	public static double MeanAnomaly => Tle.MeanAnomaly;

	/// <summary>Gets the first derivative of mean motion step, in revolutions per day squared. Columns 34-43 of line 1 carry eight decimals.</summary>
	public static double MeanMotionDot => Tle.MeanMotionDot;

	/// <summary>
	/// Gets the number of significant digits the two-line format carries in the fields it writes in
	/// exponential notation, which quantize relatively rather than absolutely.
	/// </summary>
	/// <remarks>
	/// <see cref="ElementSet.BStar"/> and <see cref="ElementSet.MeanMotionDdot"/> are written as a
	/// five-digit mantissa with an assumed leading decimal point and a single-digit exponent, so
	/// their step is a fraction of the value rather than a fixed increment. This is the field the two
	/// distributed formats disagree about most: the OMM JSON carries eight significant digits where
	/// the text carries five.
	/// </remarks>
	public static int ExponentialFieldSignificantDigits => Tle.ExponentialFieldSignificantDigits;

	/// <summary>
	/// Gets the absolute quantization step of a field the two-line format writes in exponential notation.
	/// </summary>
	/// <param name="value">The field's value.</param>
	/// <returns>The smallest change the format can express at <paramref name="value"/>, or zero when <paramref name="value"/> is zero.</returns>
	public static double StepForExponentialField(double value) => Tle.StepForExponentialField(value);

	/// <summary>
	/// Gets the absolute quantization step of every fixed-decimal field in the two-line format, keyed by field name.
	/// </summary>
	/// <remarks>
	/// The exponential fields are excluded because their step depends on the value; use
	/// <see cref="StepForExponentialField(double)"/> for those.
	/// </remarks>
	public static IReadOnlyDictionary<string, double> FixedDecimalSteps => Tle.FixedDecimalSteps;
}
