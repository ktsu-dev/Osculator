// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Frames;

using System;
using ktsu.Osculator.Core.Time;

/// <summary>
/// Which expression for the equation of the equinoxes takes TEME to true of date.
/// </summary>
/// <remarks>
/// The two differ by the "kinematic" terms the IAU added in 1994, which reach 2.6
/// milliarcseconds: about <strong>9 cm</strong> at LEO. Small, but it is the difference between
/// reproducing the published reference vector and missing it in the fifth decimal.
/// </remarks>
public enum EquationOfEquinoxes
{
	/// <summary>
	/// <c>Δψ cos ε̄ + 0.00264″ sin Ω + 0.000063″ sin 2Ω</c>, the IAU 1994 expression.
	/// </summary>
	/// <remarks>
	/// The consistent choice. TEME is PEF rotated back by GMST82, and true of date is PEF rotated
	/// back by GAST, so the angle between them is GAST − GMST82, which is this. It is what the
	/// worked example of Vallado et al. 2006 reproduces.
	/// </remarks>
	Iau1994,

	/// <summary>
	/// <c>Δψ cos ε̄</c> alone, the geometric term.
	/// </summary>
	/// <remarks>
	/// What Vallado's <c>teme2eci</c> routine uses, and what AIAA 2006-6753 says the equation
	/// "may be approximated by". Offered so a caller can match that routine bit for bit.
	/// </remarks>
	Geometric,
}

/// <summary>
/// The IERS celestial pole offsets relative to the IAU 1976/1980 model.
/// </summary>
/// <param name="DeltaPsiArcseconds">The correction to the nutation in longitude, in arcseconds.</param>
/// <param name="DeltaEpsilonArcseconds">The correction to the nutation in obliquity, in arcseconds.</param>
/// <remarks>
/// <para>
/// <strong>These are what make the result GCRF rather than FK5 J2000.</strong> The 1976
/// precession and 1980 nutation are not quite the sky: the IERS publishes how far the observed
/// pole sits from where they put it, and those offsets absorb both the model's deficiencies and
/// the frame bias between the FK5 and the GCRF. Applied, the transform lands within a
/// milliarcsecond or so of the GCRF; ignored, the result is FK5 mean equator and equinox of J2000,
/// which for the reference example is <strong>91 cm</strong> away.
/// </para>
/// <para>
/// They are the <c>dPsi</c>/<c>dEps</c> columns of the IERS <c>finals.all</c> series (the 1980
/// one), not the <c>dX</c>/<c>dY</c> of <c>finals2000A.all</c>, which are offsets from a
/// different model and do not convert into these.
/// </para>
/// </remarks>
public readonly record struct CelestialPoleOffsets(double DeltaPsiArcseconds, double DeltaEpsilonArcseconds)
{
	/// <summary>
	/// Gets the offsets that pretend the IAU 1976/1980 model is the sky.
	/// </summary>
	/// <remarks>
	/// <strong>A choice, not a default</strong>, in the same sense as
	/// <see cref="EarthOrientation.Ignored"/>: it accepts the FK5 frame instead of the GCRF, which
	/// is tens of centimetres at LEO.
	/// </remarks>
	public static CelestialPoleOffsets Ignored { get; } = new(0.0, 0.0);
}

/// <summary>
/// The angles of IAU 1976 precession and IAU 1980 nutation at one instant.
/// </summary>
/// <param name="Zeta">Precession angle ζ, in radians.</param>
/// <param name="Theta">Precession angle θ, in radians.</param>
/// <param name="Z">Precession angle z, in radians.</param>
/// <param name="MeanObliquity">The mean obliquity of the ecliptic ε̄, in radians.</param>
/// <param name="DeltaPsi">Nutation in longitude Δψ, offsets included, in radians.</param>
/// <param name="DeltaEpsilon">Nutation in obliquity Δε, offsets included, in radians.</param>
/// <param name="EquationOfEquinoxes">GAST − GMST, in radians.</param>
public readonly record struct PrecessionNutationAngles(
	double Zeta,
	double Theta,
	double Z,
	double MeanObliquity,
	double DeltaPsi,
	double DeltaEpsilon,
	double EquationOfEquinoxes);

/// <summary>
/// IAU 1976 precession and IAU 1980 nutation: the FK5 reduction Vallado's TEME is defined against.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why the 1976/1980 models and not IAU 2006/2000A.</strong> TEME is defined by SGP4, and
/// SGP4 defines it through GMST82, which belongs to the FK5 system. Taking TEME to true of date
/// needs the equation of the equinoxes of that same system, so pairing it with the 2000A nutation
/// would mix two definitions of the equinox — the same mistake <see cref="EarthFixedFrame{T}"/>
/// avoids by using SGP4's own sidereal time. With the IERS celestial pole offsets applied, the
/// 1980 chain reaches the GCRF to the accuracy the offsets are published to.
/// </para>
/// <para>
/// <strong>The angles are a function of the instant, evaluated in <see langword="double"/></strong>,
/// for the reason <see cref="EarthFixedFrame{T}.SiderealAngle"/> gives: they are not a property of
/// the storage type the state is kept in. The largest, ε̄, is 0.41 rad, so its rounding is
/// 6e-17 rad, a few hundred-thousandths of a millimetre at LEO. The rotation they define is built
/// in the storage type.
/// </para>
/// <para>
/// The series is the 106-term IAU 1980 theory (Seidelmann 1982), transcribed as data and checked
/// against ERFA's <c>eraNut80</c>, whose table it matches term for term.
/// </para>
/// </remarks>
public static class PrecessionNutation
{
	/// <summary>Arcseconds to radians.</summary>
	private const double ArcsecondsToRadians = Math.PI / (180.0 * 3600.0);

	/// <summary>The series' units, 0.1 milliarcsecond, to radians.</summary>
	private const double SeriesUnitToRadians = ArcsecondsToRadians / 1e4;

	/// <summary>The J2000.0 epoch, as a Julian date on the TT scale.</summary>
	private const double J2000 = 2451545.0;

	/// <summary>Days in a Julian century.</summary>
	private const double DaysPerJulianCentury = 36525.0;

	/// <summary>
	/// The IAU 1980 nutation series: multiples of l, l′, F, D, Ω, then the longitude sine
	/// coefficient and its rate, then the obliquity cosine coefficient and its rate, in units of
	/// 0.1 mas and 0.1 mas per Julian century.
	/// </summary>
	private static readonly NutationTerm[] Series =
	[
		new( 0,  0,  0,  0,  1, -171996.0, -174.2, 92025.0, 8.9),
		new( 0,  0,  0,  0,  2, 2062.0, 0.2, -895.0, 0.5),
		new(-2,  0,  2,  0,  1, 46.0, 0.0, -24.0, 0.0),
		new( 2,  0, -2,  0,  0, 11.0, 0.0, 0.0, 0.0),
		new(-2,  0,  2,  0,  2, -3.0, 0.0, 1.0, 0.0),
		new( 1, -1,  0, -1,  0, -3.0, 0.0, 0.0, 0.0),
		new( 0, -2,  2, -2,  1, -2.0, 0.0, 1.0, 0.0),
		new( 2,  0, -2,  0,  1, 1.0, 0.0, 0.0, 0.0),
		new( 0,  0,  2, -2,  2, -13187.0, -1.6, 5736.0, -3.1),
		new( 0,  1,  0,  0,  0, 1426.0, -3.4, 54.0, -0.1),
		new( 0,  1,  2, -2,  2, -517.0, 1.2, 224.0, -0.6),
		new( 0, -1,  2, -2,  2, 217.0, -0.5, -95.0, 0.3),
		new( 0,  0,  2, -2,  1, 129.0, 0.1, -70.0, 0.0),
		new( 2,  0,  0, -2,  0, 48.0, 0.0, 1.0, 0.0),
		new( 0,  0,  2, -2,  0, -22.0, 0.0, 0.0, 0.0),
		new( 0,  2,  0,  0,  0, 17.0, -0.1, 0.0, 0.0),
		new( 0,  1,  0,  0,  1, -15.0, 0.0, 9.0, 0.0),
		new( 0,  2,  2, -2,  2, -16.0, 0.1, 7.0, 0.0),
		new( 0, -1,  0,  0,  1, -12.0, 0.0, 6.0, 0.0),
		new(-2,  0,  0,  2,  1, -6.0, 0.0, 3.0, 0.0),
		new( 0, -1,  2, -2,  1, -5.0, 0.0, 3.0, 0.0),
		new( 2,  0,  0, -2,  1, 4.0, 0.0, -2.0, 0.0),
		new( 0,  1,  2, -2,  1, 4.0, 0.0, -2.0, 0.0),
		new( 1,  0,  0, -1,  0, -4.0, 0.0, 0.0, 0.0),
		new( 2,  1,  0, -2,  0, 1.0, 0.0, 0.0, 0.0),
		new( 0,  0, -2,  2,  1, 1.0, 0.0, 0.0, 0.0),
		new( 0,  1, -2,  2,  0, -1.0, 0.0, 0.0, 0.0),
		new( 0,  1,  0,  0,  2, 1.0, 0.0, 0.0, 0.0),
		new(-1,  0,  0,  1,  1, 1.0, 0.0, 0.0, 0.0),
		new( 0,  1,  2, -2,  0, -1.0, 0.0, 0.0, 0.0),
		new( 0,  0,  2,  0,  2, -2274.0, -0.2, 977.0, -0.5),
		new( 1,  0,  0,  0,  0, 712.0, 0.1, -7.0, 0.0),
		new( 0,  0,  2,  0,  1, -386.0, -0.4, 200.0, 0.0),
		new( 1,  0,  2,  0,  2, -301.0, 0.0, 129.0, -0.1),
		new( 1,  0,  0, -2,  0, -158.0, 0.0, -1.0, 0.0),
		new(-1,  0,  2,  0,  2, 123.0, 0.0, -53.0, 0.0),
		new( 0,  0,  0,  2,  0, 63.0, 0.0, -2.0, 0.0),
		new( 1,  0,  0,  0,  1, 63.0, 0.1, -33.0, 0.0),
		new(-1,  0,  0,  0,  1, -58.0, -0.1, 32.0, 0.0),
		new(-1,  0,  2,  2,  2, -59.0, 0.0, 26.0, 0.0),
		new( 1,  0,  2,  0,  1, -51.0, 0.0, 27.0, 0.0),
		new( 0,  0,  2,  2,  2, -38.0, 0.0, 16.0, 0.0),
		new( 2,  0,  0,  0,  0, 29.0, 0.0, -1.0, 0.0),
		new( 1,  0,  2, -2,  2, 29.0, 0.0, -12.0, 0.0),
		new( 2,  0,  2,  0,  2, -31.0, 0.0, 13.0, 0.0),
		new( 0,  0,  2,  0,  0, 26.0, 0.0, -1.0, 0.0),
		new(-1,  0,  2,  0,  1, 21.0, 0.0, -10.0, 0.0),
		new(-1,  0,  0,  2,  1, 16.0, 0.0, -8.0, 0.0),
		new( 1,  0,  0, -2,  1, -13.0, 0.0, 7.0, 0.0),
		new(-1,  0,  2,  2,  1, -10.0, 0.0, 5.0, 0.0),
		new( 1,  1,  0, -2,  0, -7.0, 0.0, 0.0, 0.0),
		new( 0,  1,  2,  0,  2, 7.0, 0.0, -3.0, 0.0),
		new( 0, -1,  2,  0,  2, -7.0, 0.0, 3.0, 0.0),
		new( 1,  0,  2,  2,  2, -8.0, 0.0, 3.0, 0.0),
		new( 1,  0,  0,  2,  0, 6.0, 0.0, 0.0, 0.0),
		new( 2,  0,  2, -2,  2, 6.0, 0.0, -3.0, 0.0),
		new( 0,  0,  0,  2,  1, -6.0, 0.0, 3.0, 0.0),
		new( 0,  0,  2,  2,  1, -7.0, 0.0, 3.0, 0.0),
		new( 1,  0,  2, -2,  1, 6.0, 0.0, -3.0, 0.0),
		new( 0,  0,  0, -2,  1, -5.0, 0.0, 3.0, 0.0),
		new( 1, -1,  0,  0,  0, 5.0, 0.0, 0.0, 0.0),
		new( 2,  0,  2,  0,  1, -5.0, 0.0, 3.0, 0.0),
		new( 0,  1,  0, -2,  0, -4.0, 0.0, 0.0, 0.0),
		new( 1,  0, -2,  0,  0, 4.0, 0.0, 0.0, 0.0),
		new( 0,  0,  0,  1,  0, -4.0, 0.0, 0.0, 0.0),
		new( 1,  1,  0,  0,  0, -3.0, 0.0, 0.0, 0.0),
		new( 1,  0,  2,  0,  0, 3.0, 0.0, 0.0, 0.0),
		new( 1, -1,  2,  0,  2, -3.0, 0.0, 1.0, 0.0),
		new(-1, -1,  2,  2,  2, -3.0, 0.0, 1.0, 0.0),
		new(-2,  0,  0,  0,  1, -2.0, 0.0, 1.0, 0.0),
		new( 3,  0,  2,  0,  2, -3.0, 0.0, 1.0, 0.0),
		new( 0, -1,  2,  2,  2, -3.0, 0.0, 1.0, 0.0),
		new( 1,  1,  2,  0,  2, 2.0, 0.0, -1.0, 0.0),
		new(-1,  0,  2, -2,  1, -2.0, 0.0, 1.0, 0.0),
		new( 2,  0,  0,  0,  1, 2.0, 0.0, -1.0, 0.0),
		new( 1,  0,  0,  0,  2, -2.0, 0.0, 1.0, 0.0),
		new( 3,  0,  0,  0,  0, 2.0, 0.0, 0.0, 0.0),
		new( 0,  0,  2,  1,  2, 2.0, 0.0, -1.0, 0.0),
		new(-1,  0,  0,  0,  2, 1.0, 0.0, -1.0, 0.0),
		new( 1,  0,  0, -4,  0, -1.0, 0.0, 0.0, 0.0),
		new(-2,  0,  2,  2,  2, 1.0, 0.0, -1.0, 0.0),
		new(-1,  0,  2,  4,  2, -2.0, 0.0, 1.0, 0.0),
		new( 2,  0,  0, -4,  0, -1.0, 0.0, 0.0, 0.0),
		new( 1,  1,  2, -2,  2, 1.0, 0.0, -1.0, 0.0),
		new( 1,  0,  2,  2,  1, -1.0, 0.0, 1.0, 0.0),
		new(-2,  0,  2,  4,  2, -1.0, 0.0, 1.0, 0.0),
		new(-1,  0,  4,  0,  2, 1.0, 0.0, 0.0, 0.0),
		new( 1, -1,  0, -2,  0, 1.0, 0.0, 0.0, 0.0),
		new( 2,  0,  2, -2,  1, 1.0, 0.0, -1.0, 0.0),
		new( 2,  0,  2,  2,  2, -1.0, 0.0, 0.0, 0.0),
		new( 1,  0,  0,  2,  1, -1.0, 0.0, 0.0, 0.0),
		new( 0,  0,  4, -2,  2, 1.0, 0.0, 0.0, 0.0),
		new( 3,  0,  2, -2,  2, 1.0, 0.0, 0.0, 0.0),
		new( 1,  0,  2, -2,  0, -1.0, 0.0, 0.0, 0.0),
		new( 0,  1,  2,  0,  1, 1.0, 0.0, 0.0, 0.0),
		new(-1, -1,  0,  2,  1, 1.0, 0.0, 0.0, 0.0),
		new( 0,  0, -2,  0,  1, -1.0, 0.0, 0.0, 0.0),
		new( 0,  0,  2, -1,  2, -1.0, 0.0, 0.0, 0.0),
		new( 0,  1,  0,  2,  0, -1.0, 0.0, 0.0, 0.0),
		new( 1,  0, -2, -2,  0, -1.0, 0.0, 0.0, 0.0),
		new( 0, -1,  2,  0,  1, -1.0, 0.0, 0.0, 0.0),
		new( 1,  1,  0, -2,  1, -1.0, 0.0, 0.0, 0.0),
		new( 1,  0, -2,  2,  0, -1.0, 0.0, 0.0, 0.0),
		new( 2,  0,  0,  2,  0, 1.0, 0.0, 0.0, 0.0),
		new( 0,  0,  2,  4,  2, -1.0, 0.0, 0.0, 0.0),
		new( 0,  1,  0,  1,  0, 1.0, 0.0, 0.0, 0.0),
	];

	/// <summary>
	/// Evaluates every angle of the reduction at an instant.
	/// </summary>
	/// <param name="terrestrialTime">The instant, on the TT scale.</param>
	/// <param name="offsets">The IERS celestial pole offsets at that instant.</param>
	/// <param name="equation">Which equation of the equinoxes to use.</param>
	/// <returns>The angles.</returns>
	public static PrecessionNutationAngles At(JulianDate terrestrialTime, CelestialPoleOffsets offsets, EquationOfEquinoxes equation)
	{
		// The two parts are differenced against J2000 before they are added, so the century count
		// keeps the fraction's resolution rather than the whole date's.
		double t = (terrestrialTime.Day - J2000 + terrestrialTime.DayFraction) / DaysPerJulianCentury;

		// Lieske et al. 1977, from J2000 to the date, as ERFA's eraPrec76 with the start at J2000.
		double zeta = ((((0.017998 * t) + 0.30188) * t) + 2306.2181) * t * ArcsecondsToRadians;
		double z = ((((0.018203 * t) + 1.09468) * t) + 2306.2181) * t * ArcsecondsToRadians;
		double theta = ((((-0.041833 * t) - 0.42665) * t) + 2004.3109) * t * ArcsecondsToRadians;

		double meanObliquity = (84381.448 + (((((0.001813 * t) - 0.00059) * t) - 46.8150) * t)) * ArcsecondsToRadians;

		// Delaunay arguments. The whole revolutions are taken apart from the arcseconds so the large
		// rate does not cost the fraction its digits.
		double l = Argument(485866.733, 715922.633, 31.310, 0.064, 1325.0, t);
		double lPrime = Argument(1287099.804, 1292581.224, -0.577, -0.012, 99.0, t);
		double f = Argument(335778.877, 295263.137, -13.257, 0.011, 1342.0, t);
		double d = Argument(1072261.307, 1105601.328, -6.891, 0.019, 1236.0, t);
		double omega = Argument(450160.280, -482890.539, 7.455, 0.008, -5.0, t);

		double deltaPsi = 0.0;
		double deltaEpsilon = 0.0;

		// Smallest terms first, so they are not lost against the largest.
		for (int i = Series.Length - 1; i >= 0; i--)
		{
			NutationTerm term = Series[i];
			double argument = (term.L * l) + (term.LPrime * lPrime) + (term.F * f) + (term.D * d) + (term.Omega * omega);
			deltaPsi += (term.LongitudeSine + (term.LongitudeSineRate * t)) * Math.Sin(argument);
			deltaEpsilon += (term.ObliquityCosine + (term.ObliquityCosineRate * t)) * Math.Cos(argument);
		}

		deltaPsi = (deltaPsi * SeriesUnitToRadians) + (offsets.DeltaPsiArcseconds * ArcsecondsToRadians);
		deltaEpsilon = (deltaEpsilon * SeriesUnitToRadians) + (offsets.DeltaEpsilonArcseconds * ArcsecondsToRadians);

		// The offsets are part of the nutation, so they reach the equation of the equinoxes too:
		// leaving them out of it would rotate the equinox by Δψ while the equator moved by Δψ + δΔψ.
		double equationOfEquinoxes = deltaPsi * Math.Cos(meanObliquity);
		if (equation == EquationOfEquinoxes.Iau1994)
		{
			// Applied whatever the date. The IERS phased these in from 1997 so that published
			// sidereal times stayed continuous; that is a convention about bulletins, not a property
			// of the sky, and an element set from 1990 sits in the same sky.
			equationOfEquinoxes += ((0.00264 * Math.Sin(omega)) + (0.000063 * Math.Sin(2.0 * omega))) * ArcsecondsToRadians;
		}

		return new PrecessionNutationAngles(zeta, theta, z, meanObliquity, deltaPsi, deltaEpsilon, equationOfEquinoxes);
	}

	/// <summary>
	/// One fundamental argument, from its polynomial in arcseconds and its whole revolutions per
	/// century.
	/// </summary>
	/// <param name="constant">The value at J2000, in arcseconds.</param>
	/// <param name="linear">The rate beyond whole revolutions, in arcseconds per century.</param>
	/// <param name="quadratic">The quadratic coefficient, in arcseconds per century squared.</param>
	/// <param name="cubic">The cubic coefficient, in arcseconds per century cubed.</param>
	/// <param name="revolutions">Whole revolutions per century.</param>
	/// <param name="t">Julian centuries of TT since J2000.</param>
	/// <returns>The argument, in radians, in [0, 2π).</returns>
	private static double Argument(double constant, double linear, double quadratic, double cubic, double revolutions, double t)
	{
		double radians = ((constant + (((((cubic * t) + quadratic) * t) + linear) * t)) * ArcsecondsToRadians)
			+ (revolutions * t % 1.0 * 2.0 * Math.PI);
		double turn = 2.0 * Math.PI;
		double reduced = radians % turn;
		return reduced < 0.0 ? reduced + turn : reduced;
	}

	/// <summary>One term of the IAU 1980 nutation series.</summary>
	/// <param name="L">Multiple of the Moon's mean anomaly.</param>
	/// <param name="LPrime">Multiple of the Sun's mean anomaly.</param>
	/// <param name="F">Multiple of the Moon's argument of latitude.</param>
	/// <param name="D">Multiple of the Moon's mean elongation from the Sun.</param>
	/// <param name="Omega">Multiple of the longitude of the Moon's ascending node.</param>
	/// <param name="LongitudeSine">Coefficient of the sine in Δψ, 0.1 mas.</param>
	/// <param name="LongitudeSineRate">Its rate, 0.1 mas per century.</param>
	/// <param name="ObliquityCosine">Coefficient of the cosine in Δε, 0.1 mas.</param>
	/// <param name="ObliquityCosineRate">Its rate, 0.1 mas per century.</param>
	private readonly record struct NutationTerm(
		int L,
		int LPrime,
		int F,
		int D,
		int Omega,
		double LongitudeSine,
		double LongitudeSineRate,
		double ObliquityCosine,
		double ObliquityCosineRate);
}
