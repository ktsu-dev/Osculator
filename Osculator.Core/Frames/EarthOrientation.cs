// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Frames;

/// <summary>
/// How the Earth is actually oriented at an instant, as measured rather than modelled.
/// </summary>
/// <param name="PoleXArcseconds">
/// The Celestial Intermediate Pole's offset from the ITRS pole towards the Greenwich meridian,
/// in arcseconds.
/// </param>
/// <param name="PoleYArcseconds">
/// The same pole's offset towards 90° west longitude, in arcseconds.
/// </param>
/// <param name="Ut1MinusUtcSeconds">
/// UT1 − UTC, in seconds. How far the Earth's actual rotation has drifted from the atomic clock
/// that leap seconds keep it within 0.9 s of.
/// </param>
/// <param name="IsPrediction">
/// Whether the IERS published these as measured values or as a forecast.
/// </param>
/// <remarks>
/// <para>
/// None of this is derivable. The Earth wobbles on its axis and its rotation rate varies with
/// what the atmosphere and oceans are doing, so these are observations, published daily by the
/// IERS, and a frame transform that wants better than a few hundred metres has to read them.
/// </para>
/// <para>
/// <strong><see cref="IsPrediction"/> is not bookkeeping.</strong> The IERS publishes final
/// values about a week in arrears and forecasts beyond that, and the forecast's stated uncertainty
/// on UT1 − UTC starts at four times the final one and reaches a thousand times it a year out —
/// 2.7e-5 s against 2.5e-2 s, which is a metre against eleven metres of rotation at the equator.
/// A caller propagating into the future is using predictions whether or not it knows, and this
/// says so.
/// </para>
/// </remarks>
public readonly record struct EarthOrientation(
	double PoleXArcseconds,
	double PoleYArcseconds,
	double Ut1MinusUtcSeconds,
	bool IsPrediction)
{
	/// <summary>
	/// Gets the orientation that pretends the Earth is exactly where the model says.
	/// </summary>
	/// <remarks>
	/// <strong>This is a choice, not a default.</strong> It accepts up to 415 m of along-track
	/// error from UT1 − UTC and about 9 m from polar motion. That is fine for drawing a satellite
	/// on a map and not fine for a residual, which is the whole reason this type exists rather
	/// than the frame transform quietly assuming zero.
	/// </remarks>
	public static EarthOrientation Ignored { get; } = new(0.0, 0.0, 0.0, IsPrediction: false);
}
