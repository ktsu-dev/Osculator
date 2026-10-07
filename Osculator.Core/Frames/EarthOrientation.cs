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
/// <param name="PoleXSigmaArcseconds">
/// The IERS's stated one-sigma uncertainty on <paramref name="PoleXArcseconds"/>, in arcseconds.
/// <see cref="double.NaN"/> when the source did not state one.
/// </param>
/// <param name="PoleYSigmaArcseconds">
/// The stated one-sigma uncertainty on <paramref name="PoleYArcseconds"/>, in arcseconds.
/// <see cref="double.NaN"/> when the source did not state one.
/// </param>
/// <param name="Ut1MinusUtcSigmaSeconds">
/// The stated one-sigma uncertainty on <paramref name="Ut1MinusUtcSeconds"/>, in seconds.
/// <see cref="double.NaN"/> when the source did not state one.
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
/// <para>
/// <strong>The sigmas are part of Δ_data, and dropping them would put them in Δ_model.</strong>
/// Once a residual is taken in the ITRF, every metre the orientation is uncertain by shows up in
/// it, and nothing downstream could tell that apart from the model being wrong. The sigmas are
/// carried here so <c>EarthOrientationTerm</c> can measure that share in metres. They default to
/// zero only so a caller stating an orientation by hand — a test, a fixed scenario — need not
/// invent an uncertainty; a parsed bulletin always sets them, and sets
/// <see cref="double.NaN"/> rather than zero where the file is silent, because a missing sigma
/// read as zero is a claim of perfect knowledge.
/// </para>
/// </remarks>
public readonly record struct EarthOrientation(
	double PoleXArcseconds,
	double PoleYArcseconds,
	double Ut1MinusUtcSeconds,
	bool IsPrediction,
	double PoleXSigmaArcseconds = 0.0,
	double PoleYSigmaArcseconds = 0.0,
	double Ut1MinusUtcSigmaSeconds = 0.0)
{
	/// <summary>
	/// Gets the orientation that pretends the Earth is exactly where the model says.
	/// </summary>
	/// <remarks>
	/// <strong>This is a choice, not a default.</strong> It accepts up to 415 m of along-track
	/// error from UT1 − UTC and about 12 m from polar motion. That is fine for drawing a satellite
	/// on a map and not fine for a residual, which is the whole reason this type exists rather
	/// than the frame transform quietly assuming zero. Its sigmas are zero: having chosen to ignore
	/// the orientation, there is no measured uncertainty left to report, and the cost of the choice
	/// is the 415 m above rather than anything a sigma could express.
	/// </remarks>
	public static EarthOrientation Ignored { get; } = new(0.0, 0.0, 0.0, IsPrediction: false);
}
