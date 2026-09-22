// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// The deep-space half of SGP4 — the SDP4 extension.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// An orbital period of 225 minutes or more puts an object where the near-earth model's assumptions
/// stop holding: the sun and the moon move it by more than the truncated geopotential does, and at
/// one and two revolutions a day it sits close enough to a resonance with the Earth's tesseral
/// harmonics for those to accumulate rather than average away. This adds the three pieces that
/// answer for that — lunar-solar secular rates, lunar-solar periodics, and a numerically integrated
/// resonance — to the secular and short-period terms the near-earth model already has.
/// </para>
/// <para>
/// Written from the same published algorithm as <see cref="Sgp4{T}"/>: Spacetrack Report #3 (1980)
/// with the corrections in <em>Revisiting Spacetrack Report #3</em> (AIAA 2006-6753).
/// </para>
/// <para>
/// Two deliberate departures from the way the paper's own code is written, both of which leave the
/// arithmetic identical:
/// </para>
/// <list type="bullet">
/// <item>
/// The five accumulated periodic offsets the paper carries as <c>peo</c>, <c>pinco</c>, <c>plo</c>,
/// <c>pgho</c> and <c>pho</c> are not stored. The published correction sets them to zero and never
/// writes them again, so every use subtracts zero.
/// </item>
/// <item>
/// The resonance integrator starts from the epoch on every call rather than resuming from where the
/// last call left it. It marches in fixed steps from a fixed starting point, so resuming produces
/// the same numbers it would have produced anyway; not resuming is what makes propagation a
/// function of its arguments rather than of call order.
/// </item>
/// </list>
/// </remarks>
internal static class DeepSpace<T>
	where T : struct, INumber<T>
{
	/// <summary>The resonance integrator's step, in minutes.</summary>
	private const double StepMinutes = 720.0;

	/// <summary>Half the square of the integrator's step, the second-order term's coefficient.</summary>
	private const double HalfStepSquared = 259200.0;

	/// <summary>The Earth's rotation rate, in radians per minute.</summary>
	private const double EarthRotationPerMinute = 4.37526908801129966e-3;

	/// <summary>The sun's mean motion, in radians per minute.</summary>
	private const double SolarMeanMotion = 1.19459e-5;

	/// <summary>The moon's mean motion, in radians per minute.</summary>
	private const double LunarMeanMotion = 1.5835218e-4;

	/// <summary>The sun's orbital eccentricity.</summary>
	private const double SolarEccentricity = 0.01675;

	/// <summary>The moon's orbital eccentricity.</summary>
	private const double LunarEccentricity = 0.05490;

	/// <summary>
	/// Greenwich mean sidereal time at a Julian date, in radians.
	/// </summary>
	/// <param name="julianDate">The Julian date, UT1.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The sidereal angle, in radians, in [0, 2π).</returns>
	/// <remarks>
	/// The resonance terms measure the satellite's longitude against the rotating Earth, so this is
	/// where the geopotential's tesseral harmonics get their phase. Its precision matters less than
	/// it looks: the same value sets both the integrator's starting longitude and the longitude it
	/// is later compared against, so an error in it cancels rather than accumulating.
	/// </remarks>
	internal static T GreenwichSiderealTime(T julianDate, IStorageMath<T> math)
	{
		T twoPi = math.Pi * N(2);
		T centuries = (julianDate - N(2451545.0)) / N(36525.0);

		T seconds = (N(-6.2e-6) * centuries * centuries * centuries)
			+ (N(0.093104) * centuries * centuries)
			+ (N((876600.0 * 3600.0) + 8640184.812866) * centuries)
			+ N(67310.54841);

		T radians = seconds * (math.Pi / N(180)) / N(240);
		radians %= twoPi;

		return radians < T.Zero ? radians + twoPi : radians;
	}

	/// <summary>
	/// Computes the lunar and solar geometry at the element set's epoch.
	/// </summary>
	/// <param name="satellite">The satellite, whose periodic coefficients this fills in.</param>
	/// <param name="daysSinceDayZero">Days from 1949 December 31 to the element set's epoch.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The intermediates the resonance initialization needs.</returns>
	/// <remarks>
	/// Runs one loop twice: once against the sun's orbit and once against the moon's, which differ
	/// only in the constants fed in. The solar pass is saved before the lunar pass overwrites the
	/// working set, which is why the results come back in two families.
	/// </remarks>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Maintainability", "CA1502:Avoid excessive complexity",
		Justification = "One published closed-form routine. Splitting it would scatter mutually dependent intermediates across helpers that mean nothing apart, and make it harder to check against the paper it comes from.")]
	internal static DeepSpaceCommon<T> InitializeCommon(Sgp4Satellite<T> satellite, T daysSinceDayZero, IStorageMath<T> math)
	{
		T two = N(2);
		T twoPi = math.Pi * two;

		T em = satellite.Eccentricity;
		T emsq = em * em;
		T betasq = T.One - emsq;
		T rtemsq = math.Sqrt(betasq);

		T snodm = math.Sin(satellite.RightAscension);
		T cnodm = math.Cos(satellite.RightAscension);
		T sinomm = math.Sin(satellite.ArgumentOfPerigee);
		T cosomm = math.Cos(satellite.ArgumentOfPerigee);
		T sinim = math.Sin(satellite.Inclination);
		T cosim = math.Cos(satellite.Inclination);

		// The moon's node and perigee regress and precess on their own schedules, so the lunar
		// orientation has to be worked out for this particular epoch rather than taken as fixed.
		T day = daysSinceDayZero + N(18261.5);
		T xnodce = (N(4.5236020) - (N(9.2422029e-4) * day)) % twoPi;
		T stem = math.Sin(xnodce);
		T ctem = math.Cos(xnodce);
		T zcosil = N(0.91375164) - (N(0.03568096) * ctem);
		T zsinil = math.Sqrt(T.One - (zcosil * zcosil));
		T zsinhl = N(0.089683511) * stem / zsinil;
		T zcoshl = math.Sqrt(T.One - (zsinhl * zsinhl));
		T gam = N(5.8351514) + (N(0.0019443680) * day);
		T zx = N(0.39785416) * stem / zsinil;
		T zy = (zcoshl * ctem) + (N(0.91744867) * zsinhl * stem);
		zx = math.Atan2(zx, zy);
		zx = gam + zx - xnodce;
		T zcosgl = math.Cos(zx);
		T zsingl = math.Sin(zx);

		// The solar pass runs first, against the sun's fixed orientation.
		T zcosg = N(0.1945905);
		T zsing = N(-0.98088458);
		T zcosi = N(0.91744867);
		T zsini = N(0.39785416);
		T zcosh = cnodm;
		T zsinh = snodm;
		T cc = N(2.9864797e-6);
		T xnoi = T.One / satellite.MeanMotion;

		T s1 = T.Zero, s2 = T.Zero, s3 = T.Zero, s4 = T.Zero, s5 = T.Zero, s6 = T.Zero, s7 = T.Zero;
		T z1 = T.Zero, z2 = T.Zero, z3 = T.Zero;
		T z11 = T.Zero, z12 = T.Zero, z13 = T.Zero;
		T z21 = T.Zero, z22 = T.Zero, z23 = T.Zero;
		T z31 = T.Zero, z32 = T.Zero, z33 = T.Zero;
		T ss1 = T.Zero, ss2 = T.Zero, ss3 = T.Zero, ss4 = T.Zero, ss5 = T.Zero, ss6 = T.Zero, ss7 = T.Zero;
		T sz1 = T.Zero, sz2 = T.Zero, sz3 = T.Zero;
		T sz11 = T.Zero, sz12 = T.Zero, sz13 = T.Zero;
		T sz21 = T.Zero, sz22 = T.Zero, sz23 = T.Zero;
		T sz31 = T.Zero, sz32 = T.Zero, sz33 = T.Zero;

		for (int pass = 1; pass <= 2; pass++)
		{
			T a1 = (zcosg * zcosh) + (zsing * zcosi * zsinh);
			T a3 = (-zsing * zcosh) + (zcosg * zcosi * zsinh);
			T a7 = (-zcosg * zsinh) + (zsing * zcosi * zcosh);
			T a8 = zsing * zsini;
			T a9 = (zsing * zsinh) + (zcosg * zcosi * zcosh);
			T a10 = zcosg * zsini;
			T a2 = (cosim * a7) + (sinim * a8);
			T a4 = (cosim * a9) + (sinim * a10);
			T a5 = (-sinim * a7) + (cosim * a8);
			T a6 = (-sinim * a9) + (cosim * a10);

			T x1 = (a1 * cosomm) + (a2 * sinomm);
			T x2 = (a3 * cosomm) + (a4 * sinomm);
			T x3 = (-a1 * sinomm) + (a2 * cosomm);
			T x4 = (-a3 * sinomm) + (a4 * cosomm);
			T x5 = a5 * sinomm;
			T x6 = a6 * sinomm;
			T x7 = a5 * cosomm;
			T x8 = a6 * cosomm;

			z31 = (N(12) * x1 * x1) - (N(3) * x3 * x3);
			z32 = (N(24) * x1 * x2) - (N(6) * x3 * x4);
			z33 = (N(12) * x2 * x2) - (N(3) * x4 * x4);
			z1 = (N(3) * ((a1 * a1) + (a2 * a2))) + (z31 * emsq);
			z2 = (N(6) * ((a1 * a3) + (a2 * a4))) + (z32 * emsq);
			z3 = (N(3) * ((a3 * a3) + (a4 * a4))) + (z33 * emsq);
			z11 = (-N(6) * a1 * a5) + (emsq * ((-N(24) * x1 * x7) - (N(6) * x3 * x5)));
			z12 = (-N(6) * ((a1 * a6) + (a3 * a5)))
				+ (emsq * ((-N(24) * ((x2 * x7) + (x1 * x8))) - (N(6) * ((x3 * x6) + (x4 * x5)))));
			z13 = (-N(6) * a3 * a6) + (emsq * ((-N(24) * x2 * x8) - (N(6) * x4 * x6)));
			z21 = (N(6) * a2 * a5) + (emsq * ((N(24) * x1 * x5) - (N(6) * x3 * x7)));
			z22 = (N(6) * ((a4 * a5) + (a2 * a6)))
				+ (emsq * ((N(24) * ((x2 * x5) + (x1 * x6))) - (N(6) * ((x4 * x7) + (x3 * x8)))));
			z23 = (N(6) * a4 * a6) + (emsq * ((N(24) * x2 * x6) - (N(6) * x4 * x8)));
			z1 = z1 + z1 + (betasq * z31);
			z2 = z2 + z2 + (betasq * z32);
			z3 = z3 + z3 + (betasq * z33);
			s3 = cc * xnoi;
			s2 = -N(0.5) * s3 / rtemsq;
			s4 = s3 * rtemsq;
			s1 = -N(15) * em * s4;
			s5 = (x1 * x3) + (x2 * x4);
			s6 = (x2 * x3) + (x1 * x4);
			s7 = (x2 * x4) - (x1 * x3);

			if (pass != 1)
			{
				continue;
			}

			ss1 = s1;
			ss2 = s2;
			ss3 = s3;
			ss4 = s4;
			ss5 = s5;
			ss6 = s6;
			ss7 = s7;
			sz1 = z1;
			sz2 = z2;
			sz3 = z3;
			sz11 = z11;
			sz12 = z12;
			sz13 = z13;
			sz21 = z21;
			sz22 = z22;
			sz23 = z23;
			sz31 = z31;
			sz32 = z32;
			sz33 = z33;

			// Swap in the moon's orientation for the second pass.
			zcosg = zcosgl;
			zsing = zsingl;
			zcosi = zcosil;
			zsini = zsinil;
			zcosh = (zcoshl * cnodm) + (zsinhl * snodm);
			zsinh = (snodm * zcoshl) - (cnodm * zsinhl);
			cc = N(4.7968065e-7);
		}

		satellite.Zmol = math.ToWorkingPrecision((N(4.7199672) + (N(0.22997150) * day) - gam) % twoPi);
		satellite.Zmos = math.ToWorkingPrecision((N(6.2565837) + (N(0.017201977) * day)) % twoPi);

		T zes = N(SolarEccentricity);
		satellite.Se2 = math.ToWorkingPrecision(two * ss1 * ss6);
		satellite.Se3 = math.ToWorkingPrecision(two * ss1 * ss7);
		satellite.Si2 = math.ToWorkingPrecision(two * ss2 * sz12);
		satellite.Si3 = math.ToWorkingPrecision(two * ss2 * (sz13 - sz11));
		satellite.Sl2 = math.ToWorkingPrecision(-two * ss3 * sz2);
		satellite.Sl3 = math.ToWorkingPrecision(-two * ss3 * (sz3 - sz1));
		satellite.Sl4 = math.ToWorkingPrecision(-two * ss3 * (-N(21) - (N(9) * emsq)) * zes);
		satellite.Sgh2 = math.ToWorkingPrecision(two * ss4 * sz32);
		satellite.Sgh3 = math.ToWorkingPrecision(two * ss4 * (sz33 - sz31));
		satellite.Sgh4 = math.ToWorkingPrecision(-N(18) * ss4 * zes);
		satellite.Sh2 = math.ToWorkingPrecision(-two * ss2 * sz22);
		satellite.Sh3 = math.ToWorkingPrecision(-two * ss2 * (sz23 - sz21));

		T zel = N(LunarEccentricity);
		satellite.Ee2 = math.ToWorkingPrecision(two * s1 * s6);
		satellite.E3 = math.ToWorkingPrecision(two * s1 * s7);
		satellite.Xi2 = math.ToWorkingPrecision(two * s2 * z12);
		satellite.Xi3 = math.ToWorkingPrecision(two * s2 * (z13 - z11));
		satellite.Xl2 = math.ToWorkingPrecision(-two * s3 * z2);
		satellite.Xl3 = math.ToWorkingPrecision(-two * s3 * (z3 - z1));
		satellite.Xl4 = math.ToWorkingPrecision(-two * s3 * (-N(21) - (N(9) * emsq)) * zel);
		satellite.Xgh2 = math.ToWorkingPrecision(two * s4 * z32);
		satellite.Xgh3 = math.ToWorkingPrecision(two * s4 * (z33 - z31));
		satellite.Xgh4 = math.ToWorkingPrecision(-N(18) * s4 * zel);
		satellite.Xh2 = math.ToWorkingPrecision(-two * s2 * z22);
		satellite.Xh3 = math.ToWorkingPrecision(-two * s2 * (z23 - z21));

		return new DeepSpaceCommon<T>
		{
			Sinim = sinim,
			Cosim = cosim,
			Emsq = emsq,
			S1 = s1,
			S2 = s2,
			S3 = s3,
			S4 = s4,
			S5 = s5,
			Ss1 = ss1,
			Ss2 = ss2,
			Ss3 = ss3,
			Ss4 = ss4,
			Ss5 = ss5,
			Sz1 = sz1,
			Sz3 = sz3,
			Sz11 = sz11,
			Sz13 = sz13,
			Sz21 = sz21,
			Sz23 = sz23,
			Sz31 = sz31,
			Sz33 = sz33,
			Z1 = z1,
			Z3 = z3,
			Z11 = z11,
			Z13 = z13,
			Z21 = z21,
			Z23 = z23,
			Z31 = z31,
			Z33 = z33,
		};
	}

	/// <summary>
	/// Turns the lunar-solar geometry into secular rates, and sets up the resonance integrator.
	/// </summary>
	/// <param name="satellite">The satellite, whose deep-space coefficients this fills in.</param>
	/// <param name="common">The intermediates from <see cref="InitializeCommon"/>.</param>
	/// <param name="apsidalNodalRate">The sum of the argument-of-perigee and node rates.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Maintainability", "CA1502:Avoid excessive complexity",
		Justification = "One published closed-form routine, most of whose branches are the eccentricity and inclination polynomials of the tesseral harmonics. Splitting it would separate coefficients from the ranges they are fitted over.")]
	internal static void InitializeResonance(
		Sgp4Satellite<T> satellite,
		DeepSpaceCommon<T> common,
		T apsidalNodalRate,
		IStorageMath<T> math)
	{
		T two = N(2);
		T twoPi = math.Pi * two;
		T zns = N(SolarMeanMotion);
		T znl = N(LunarMeanMotion);
		T nm = satellite.MeanMotion;
		T sinim = common.Sinim;
		T cosim = common.Cosim;
		T emsq = common.Emsq;

		// Which resonance, if any, the orbit sits close enough to for the tesseral terms to build up
		// rather than average away: one revolution a day, or two with an eccentric orbit.
		satellite.Resonance = 0;

		if (nm < N(0.0052359877) && nm > N(0.0034906585))
		{
			satellite.Resonance = 1;
		}

		if (nm >= N(8.26e-3) && nm <= N(9.24e-3) && satellite.Eccentricity >= N(0.5))
		{
			satellite.Resonance = 2;
		}

		// Solar secular contributions.
		T ses = common.Ss1 * zns * common.Ss5;
		T sis = common.Ss2 * zns * (common.Sz11 + common.Sz13);
		T sls = -zns * common.Ss3 * (common.Sz1 + common.Sz3 - N(14) - (N(6) * emsq));
		T sghs = common.Ss4 * zns * (common.Sz31 + common.Sz33 - N(6));
		T shs = -zns * common.Ss2 * (common.Sz21 + common.Sz23);

		// Near-equatorial and near-polar orbits have no well-defined node to perturb, so the node
		// term is dropped rather than divided by a sine that is about to be zero.
		T polarGuard = N(5.2359877e-2);
		bool nodeIsIllDefined = satellite.Inclination < polarGuard || satellite.Inclination > math.Pi - polarGuard;

		if (nodeIsIllDefined)
		{
			shs = T.Zero;
		}

		if (sinim != T.Zero)
		{
			shs /= sinim;
		}

		T sgs = sghs - (cosim * shs);

		// Lunar secular contributions, added to the solar ones.
		satellite.Dedt = math.ToWorkingPrecision(ses + (common.S1 * znl * common.S5));
		satellite.Didt = math.ToWorkingPrecision(sis + (common.S2 * znl * (common.Z11 + common.Z13)));
		satellite.Dmdt = math.ToWorkingPrecision(sls - (znl * common.S3 * (common.Z1 + common.Z3 - N(14) - (N(6) * emsq))));

		T sghl = common.S4 * znl * (common.Z31 + common.Z33 - N(6));
		T shll = -znl * common.S2 * (common.Z21 + common.Z23);

		if (nodeIsIllDefined)
		{
			shll = T.Zero;
		}

		satellite.Domdt = math.ToWorkingPrecision(sgs + sghl);
		satellite.Dnodt = math.ToWorkingPrecision(shs);

		if (sinim != T.Zero)
		{
			satellite.Domdt = math.ToWorkingPrecision(satellite.Domdt - (cosim / sinim * shll));
			satellite.Dnodt = math.ToWorkingPrecision(satellite.Dnodt + (shll / sinim));
		}

		if (satellite.Resonance == 0)
		{
			return;
		}

		T rptim = N(EarthRotationPerMinute);
		T theta = satellite.Gsto % twoPi;
		T aonv = math.Pow(nm / Wgs72<T>.Xke(math), two / N(3));

		if (satellite.Resonance == 2)
		{
			InitializeHalfDayResonance(satellite, common, aonv, theta, math);
		}
		else
		{
			InitializeSynchronousResonance(satellite, common, aonv, theta, apsidalNodalRate, math);
		}
	}

	/// <summary>
	/// Sets up the twice-a-day geopotential resonance, which the Molniya orbits sit in.
	/// </summary>
	/// <param name="satellite">The satellite.</param>
	/// <param name="common">The lunar-solar intermediates.</param>
	/// <param name="aonv">The semi-major axis in the model's own units, to the two-thirds power.</param>
	/// <param name="theta">Greenwich sidereal time at epoch, reduced to [0, 2π).</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <remarks>
	/// The eccentricity polynomials come in three fitted ranges and the paper's own branch points —
	/// 0.65, 0.7 and 0.715 — are kept exactly, because a coefficient set is only meaningful over the
	/// range it was fitted on. The verification set has a case sitting in each.
	/// </remarks>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Maintainability", "CA1502:Avoid excessive complexity",
		Justification = "One published closed-form routine: three fitted eccentricity ranges and the inclination functions of six tesseral harmonics.")]
	private static void InitializeHalfDayResonance(
		Sgp4Satellite<T> satellite,
		DeepSpaceCommon<T> common,
		T aonv,
		T theta,
		IStorageMath<T> math)
	{
		T two = N(2);
		T twoPi = math.Pi * two;
		T cosim = common.Cosim;
		T sinim = common.Sinim;
		T cosisq = cosim * cosim;
		T em = satellite.Eccentricity;
		T emsq = em * em;
		T eoc = em * emsq;

		T g201 = -N(0.306) - ((em - N(0.64)) * N(0.440));
		T g211, g310, g322, g410, g422, g520;

		if (em <= N(0.65))
		{
			g211 = N(3.616) - (N(13.2470) * em) + (N(16.2900) * emsq);
			g310 = -N(19.302) + (N(117.3900) * em) - (N(228.4190) * emsq) + (N(156.5910) * eoc);
			g322 = -N(18.9068) + (N(109.7927) * em) - (N(214.6334) * emsq) + (N(146.5816) * eoc);
			g410 = -N(41.122) + (N(242.6940) * em) - (N(471.0940) * emsq) + (N(313.9530) * eoc);
			g422 = -N(146.407) + (N(841.8800) * em) - (N(1629.014) * emsq) + (N(1083.4350) * eoc);
			g520 = -N(532.114) + (N(3017.977) * em) - (N(5740.032) * emsq) + (N(3708.2760) * eoc);
		}
		else
		{
			g211 = -N(72.099) + (N(331.819) * em) - (N(508.738) * emsq) + (N(266.724) * eoc);
			g310 = -N(346.844) + (N(1582.851) * em) - (N(2415.925) * emsq) + (N(1246.113) * eoc);
			g322 = -N(342.585) + (N(1554.908) * em) - (N(2366.899) * emsq) + (N(1215.972) * eoc);
			g410 = -N(1052.797) + (N(4758.686) * em) - (N(7193.992) * emsq) + (N(3651.957) * eoc);
			g422 = -N(3581.690) + (N(16178.110) * em) - (N(24462.770) * emsq) + (N(12422.520) * eoc);

			// The one coefficient with a third range of its own, and the reason the verification set
			// distinguishes "0.7 to 0.715" from "above 0.715" when nothing else splits there.
			g520 = em > N(0.715)
				? -N(5149.66) + (N(29936.92) * em) - (N(54087.36) * emsq) + (N(31324.56) * eoc)
				: N(1464.74) - (N(4664.75) * em) + (N(3763.64) * emsq);
		}

		T g533, g521, g532;

		if (em < N(0.7))
		{
			g533 = -N(919.22770) + (N(4988.6100) * em) - (N(9064.7700) * emsq) + (N(5542.21) * eoc);
			g521 = -N(822.71072) + (N(4568.6173) * em) - (N(8491.4146) * emsq) + (N(5337.524) * eoc);
			g532 = -N(853.66600) + (N(4690.2500) * em) - (N(8624.7700) * emsq) + (N(5341.4) * eoc);
		}
		else
		{
			g533 = -N(37995.780) + (N(161616.52) * em) - (N(229838.20) * emsq) + (N(109377.94) * eoc);
			g521 = -N(51752.104) + (N(218913.95) * em) - (N(309468.16) * emsq) + (N(146349.42) * eoc);
			g532 = -N(40023.880) + (N(170470.89) * em) - (N(242699.48) * emsq) + (N(115605.82) * eoc);
		}

		T sini2 = sinim * sinim;
		T f220 = N(0.75) * (T.One + (two * cosim) + cosisq);
		T f221 = N(1.5) * sini2;
		T f321 = N(1.875) * sinim * (T.One - (two * cosim) - (N(3) * cosisq));
		T f322 = -N(1.875) * sinim * (T.One + (two * cosim) - (N(3) * cosisq));
		T f441 = N(35) * sini2 * f220;
		T f442 = N(39.3750) * sini2 * sini2;
		T f522 = N(9.84375) * sinim * ((sini2 * (T.One - (two * cosim) - (N(5) * cosisq)))
			+ (N(0.33333333) * (-two + (N(4) * cosim) + (N(6) * cosisq))));
		T f523 = sinim * ((N(4.92187512) * sini2 * (-two - (N(4) * cosim) + (N(10) * cosisq)))
			+ (N(6.56250012) * (T.One + (two * cosim) - (N(3) * cosisq))));
		T f542 = N(29.53125) * sinim * (two - (N(8) * cosim)
			+ (cosisq * (-N(12) + (N(8) * cosim) + (N(10) * cosisq))));
		T f543 = N(29.53125) * sinim * (-two - (N(8) * cosim)
			+ (cosisq * (N(12) + (N(8) * cosim) - (N(10) * cosisq))));

		T nm = satellite.MeanMotion;
		T scale = N(3) * nm * nm * aonv * aonv;

		T term = scale * N(1.7891679e-6);
		satellite.D2201 = math.ToWorkingPrecision(term * f220 * g201);
		satellite.D2211 = math.ToWorkingPrecision(term * f221 * g211);

		scale *= aonv;
		term = scale * N(3.7393792e-7);
		satellite.D3210 = math.ToWorkingPrecision(term * f321 * g310);
		satellite.D3222 = math.ToWorkingPrecision(term * f322 * g322);

		scale *= aonv;
		term = two * scale * N(7.3636953e-9);
		satellite.D4410 = math.ToWorkingPrecision(term * f441 * g410);
		satellite.D4422 = math.ToWorkingPrecision(term * f442 * g422);

		scale *= aonv;
		term = scale * N(1.1428639e-7);
		satellite.D5220 = math.ToWorkingPrecision(term * f522 * g520);
		satellite.D5232 = math.ToWorkingPrecision(term * f523 * g532);

		term = two * scale * N(2.1765803e-9);
		satellite.D5421 = math.ToWorkingPrecision(term * f542 * g521);
		satellite.D5433 = math.ToWorkingPrecision(term * f543 * g533);

		satellite.Xlamo = math.ToWorkingPrecision((satellite.MeanAnomaly + satellite.RightAscension + satellite.RightAscension - theta - theta) % twoPi);
		satellite.Xfact = math.ToWorkingPrecision(satellite.MDot + satellite.Dmdt
				+ (two * (satellite.NodeDot + satellite.Dnodt - N(EarthRotationPerMinute)))
				- satellite.MeanMotion);
	}

	/// <summary>
	/// Sets up the once-a-day geopotential resonance, which the geosynchronous orbits sit in.
	/// </summary>
	/// <param name="satellite">The satellite.</param>
	/// <param name="common">The lunar-solar intermediates.</param>
	/// <param name="aonv">The semi-major axis in the model's own units, to the two-thirds power.</param>
	/// <param name="theta">Greenwich sidereal time at epoch, reduced to [0, 2π).</param>
	/// <param name="apsidalNodalRate">The sum of the argument-of-perigee and node rates.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	private static void InitializeSynchronousResonance(
		Sgp4Satellite<T> satellite,
		DeepSpaceCommon<T> common,
		T aonv,
		T theta,
		T apsidalNodalRate,
		IStorageMath<T> math)
	{
		T two = N(2);
		T twoPi = math.Pi * two;
		T cosim = common.Cosim;
		T sinim = common.Sinim;
		T emsq = common.Emsq;

		T g200 = T.One + (emsq * (-N(2.5) + (N(0.8125) * emsq)));
		T g310 = T.One + (two * emsq);
		T g300 = T.One + (emsq * (-N(6) + (N(6.60937) * emsq)));
		T f220 = N(0.75) * (T.One + cosim) * (T.One + cosim);
		T f311 = (N(0.9375) * sinim * sinim * (T.One + (N(3) * cosim))) - (N(0.75) * (T.One + cosim));
		T f330 = T.One + cosim;
		f330 = N(1.875) * f330 * f330 * f330;

		T del1 = N(3) * satellite.MeanMotion * satellite.MeanMotion * aonv * aonv;
		satellite.Del2 = math.ToWorkingPrecision(two * del1 * f220 * g200 * N(1.7891679e-6));
		satellite.Del3 = math.ToWorkingPrecision(N(3) * del1 * f330 * g300 * N(2.2123015e-7) * aonv);
		satellite.Del1 = math.ToWorkingPrecision(del1 * f311 * g310 * N(2.1460748e-6) * aonv);

		satellite.Xlamo = math.ToWorkingPrecision((satellite.MeanAnomaly + satellite.RightAscension + satellite.ArgumentOfPerigee - theta) % twoPi);
		satellite.Xfact = math.ToWorkingPrecision(satellite.MDot + apsidalNodalRate - N(EarthRotationPerMinute)
				+ satellite.Dmdt + satellite.Domdt + satellite.Dnodt - satellite.MeanMotion);
	}

	/// <summary>
	/// Applies the deep-space secular rates and, where the orbit resonates, integrates the
	/// resonance forward from the epoch.
	/// </summary>
	/// <param name="satellite">The satellite.</param>
	/// <param name="elements">The mean elements after the near-earth secular and drag terms.</param>
	/// <param name="minutesSinceEpoch">Minutes since the element set's epoch; may be negative.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The mean elements with the deep-space contributions added.</returns>
	/// <remarks>
	/// The integration is Euler-Maclaurin in fixed twelve-hour steps, which is the published
	/// algorithm's and not a choice made here: the coefficients are fitted to it, so a better
	/// integrator would move the answer away from the model rather than towards the physics.
	/// </remarks>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Maintainability", "CA1502:Avoid excessive complexity",
		Justification = "One published closed-form routine plus its integrator. Splitting the loop body out would separate the two resonance cases from the step that consumes them.")]
	internal static DeepSpaceElements<T> ApplyResonance(
		Sgp4Satellite<T> satellite,
		DeepSpaceElements<T> elements,
		T minutesSinceEpoch,
		IStorageMath<T> math)
	{
		T two = N(2);
		T twoPi = math.Pi * two;
		T t = minutesSinceEpoch;

		T theta = (satellite.Gsto + (t * N(EarthRotationPerMinute))) % twoPi;
		T em = elements.Eccentricity + (satellite.Dedt * t);
		T inclm = elements.Inclination + (satellite.Didt * t);
		T argpm = elements.ArgumentOfPerigee + (satellite.Domdt * t);
		T nodem = elements.RightAscension + (satellite.Dnodt * t);
		T mm = elements.MeanAnomaly + (satellite.Dmdt * t);
		T nm = elements.MeanMotion;

		if (satellite.Resonance == 0)
		{
			return Reduced(new DeepSpaceElements<T>(nm, em, inclm, mm, argpm, nodem), math);
		}

		T stepp = N(StepMinutes);
		T step2 = N(HalfStepSquared);
		T delt = t > T.Zero ? stepp : -stepp;

		// Always from the epoch: see the note on this class about why the integrator does not resume.
		T atime = T.Zero;
		T xli = satellite.Xlamo;
		T xni = satellite.MeanMotion;
		T xndt = T.Zero;
		T xldot = T.Zero;
		T xnddt = T.Zero;
		T ft = T.Zero;
		bool stepping = true;

		while (stepping)
		{
			if (satellite.Resonance != 2)
			{
				T fasx2 = N(0.13130908);
				T fasx4 = N(2.8843198);
				T fasx6 = N(0.37448087);

				xndt = (satellite.Del1 * math.Sin(xli - fasx2))
					+ (satellite.Del2 * math.Sin(two * (xli - fasx4)))
					+ (satellite.Del3 * math.Sin(N(3) * (xli - fasx6)));
				xldot = xni + satellite.Xfact;
				xnddt = (satellite.Del1 * math.Cos(xli - fasx2))
					+ (two * satellite.Del2 * math.Cos(two * (xli - fasx4)))
					+ (N(3) * satellite.Del3 * math.Cos(N(3) * (xli - fasx6)));
				xnddt *= xldot;
			}
			else
			{
				T g22 = N(5.7686396);
				T g32 = N(0.95240898);
				T g44 = N(1.8014998);
				T g52 = N(1.0508330);
				T g54 = N(4.4108898);

				T xomi = satellite.ArgumentOfPerigee + (satellite.ArgpDot * atime);
				T x2omi = xomi + xomi;
				T x2li = xli + xli;

				xndt = (satellite.D2201 * math.Sin(x2omi + xli - g22))
					+ (satellite.D2211 * math.Sin(xli - g22))
					+ (satellite.D3210 * math.Sin(xomi + xli - g32))
					+ (satellite.D3222 * math.Sin(-xomi + xli - g32))
					+ (satellite.D4410 * math.Sin(x2omi + x2li - g44))
					+ (satellite.D4422 * math.Sin(x2li - g44))
					+ (satellite.D5220 * math.Sin(xomi + xli - g52))
					+ (satellite.D5232 * math.Sin(-xomi + xli - g52))
					+ (satellite.D5421 * math.Sin(xomi + x2li - g54))
					+ (satellite.D5433 * math.Sin(-xomi + x2li - g54));
				xldot = xni + satellite.Xfact;
				xnddt = (satellite.D2201 * math.Cos(x2omi + xli - g22))
					+ (satellite.D2211 * math.Cos(xli - g22))
					+ (satellite.D3210 * math.Cos(xomi + xli - g32))
					+ (satellite.D3222 * math.Cos(-xomi + xli - g32))
					+ (satellite.D5220 * math.Cos(xomi + xli - g52))
					+ (satellite.D5232 * math.Cos(-xomi + xli - g52))
					+ (two * ((satellite.D4410 * math.Cos(x2omi + x2li - g44))
						+ (satellite.D4422 * math.Cos(x2li - g44))
						+ (satellite.D5421 * math.Cos(xomi + x2li - g54))
						+ (satellite.D5433 * math.Cos(-xomi + x2li - g54))));
				xnddt *= xldot;
			}

			if (T.Abs(t - atime) >= stepp)
			{
				// The one accumulation in the model that runs for an unbounded number of steps, and
				// so the one place where an arbitrary-precision type would otherwise grow without
				// limit rather than by the length of an expression.
				xli = math.ToWorkingPrecision(xli + (xldot * delt) + (xndt * step2));
				xni = math.ToWorkingPrecision(xni + (xndt * delt) + (xnddt * step2));
				atime += delt;
			}
			else
			{
				ft = t - atime;
				stepping = false;
			}
		}

		nm = xni + (xndt * ft) + (xnddt * ft * ft * N(0.5));
		T xl = xli + (xldot * ft) + (xndt * ft * ft * N(0.5));

		mm = satellite.Resonance != 1
			? xl - (two * nodem) + (two * theta)
			: xl - nodem - argpm + theta;

		// The paper carries the integrated mean motion back as a delta from the unperturbed one and
		// then adds it again. In exact arithmetic that is the identity; in floating point it is not,
		// and it is the form the published test vectors were produced with.
		nm = satellite.MeanMotion + (nm - satellite.MeanMotion);

		return Reduced(new DeepSpaceElements<T>(nm, em, inclm, mm, argpm, nodem), math);
	}

	/// <summary>
	/// Applies the lunar and solar periodic perturbations.
	/// </summary>
	/// <param name="satellite">The satellite.</param>
	/// <param name="elements">The singly-averaged mean elements.</param>
	/// <param name="minutesSinceEpoch">Minutes since the element set's epoch; may be negative.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The elements with the periodics added. The mean motion is returned unchanged.</returns>
	/// <remarks>
	/// Below about eleven and a half degrees of inclination the node and the argument of perigee are
	/// too weakly defined to perturb separately, so the corrections are applied to the equinoctial
	/// combination of the two instead — the Lyddane modification. The verification set contains two
	/// cases chosen to cross that boundary while propagating, which is what makes the branch worth
	/// having rather than a footnote.
	/// </remarks>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Maintainability", "CA1502:Avoid excessive complexity",
		Justification = "One published closed-form routine and its Lyddane alternative, which share every intermediate above them.")]
	internal static DeepSpaceElements<T> ApplyPeriodics(
		Sgp4Satellite<T> satellite,
		DeepSpaceElements<T> elements,
		T minutesSinceEpoch,
		IStorageMath<T> math)
	{
		T two = N(2);
		T twoPi = math.Pi * two;
		T t = minutesSinceEpoch;

		T zm = satellite.Zmos + (N(SolarMeanMotion) * t);
		T zf = zm + (two * N(SolarEccentricity) * math.Sin(zm));
		T sinzf = math.Sin(zf);
		T f2 = (N(0.5) * sinzf * sinzf) - N(0.25);
		T f3 = -N(0.5) * sinzf * math.Cos(zf);

		T ses = (satellite.Se2 * f2) + (satellite.Se3 * f3);
		T sis = (satellite.Si2 * f2) + (satellite.Si3 * f3);
		T sls = (satellite.Sl2 * f2) + (satellite.Sl3 * f3) + (satellite.Sl4 * sinzf);
		T sghs = (satellite.Sgh2 * f2) + (satellite.Sgh3 * f3) + (satellite.Sgh4 * sinzf);
		T shs = (satellite.Sh2 * f2) + (satellite.Sh3 * f3);

		zm = satellite.Zmol + (N(LunarMeanMotion) * t);
		zf = zm + (two * N(LunarEccentricity) * math.Sin(zm));
		sinzf = math.Sin(zf);
		f2 = (N(0.5) * sinzf * sinzf) - N(0.25);
		f3 = -N(0.5) * sinzf * math.Cos(zf);

		T sel = (satellite.Ee2 * f2) + (satellite.E3 * f3);
		T sil = (satellite.Xi2 * f2) + (satellite.Xi3 * f3);
		T sll = (satellite.Xl2 * f2) + (satellite.Xl3 * f3) + (satellite.Xl4 * sinzf);
		T sghl = (satellite.Xgh2 * f2) + (satellite.Xgh3 * f3) + (satellite.Xgh4 * sinzf);
		T shll = (satellite.Xh2 * f2) + (satellite.Xh3 * f3);

		T pe = ses + sel;
		T pinc = sis + sil;
		T pl = sls + sll;
		T pgh = sghs + sghl;
		T ph = shs + shll;

		T inclp = elements.Inclination + pinc;
		T ep = elements.Eccentricity + pe;
		T nodep = elements.RightAscension;
		T argpp = elements.ArgumentOfPerigee;
		T mp = elements.MeanAnomaly;
		T sinip = math.Sin(inclp);
		T cosip = math.Cos(inclp);

		if (inclp >= N(0.2))
		{
			ph /= sinip;
			pgh -= cosip * ph;
			argpp += pgh;
			nodep += ph;
			mp += pl;

			return Reduced(new DeepSpaceElements<T>(elements.MeanMotion, ep, inclp, mp, argpp, nodep), math);
		}

		T sinop = math.Sin(nodep);
		T cosop = math.Cos(nodep);
		T alfdp = (sinip * sinop) + ((ph * cosop) + (pinc * cosip * sinop));
		T betdp = (sinip * cosop) + ((-ph * sinop) + (pinc * cosip * cosop));

		nodep %= twoPi;

		T xls = mp + argpp + (cosip * nodep);
		T dls = pl + pgh - (pinc * nodep * sinip);
		xls += dls;

		T previousNode = nodep;
		nodep = math.Atan2(alfdp, betdp);

		// atan2 answers in its own branch, which after a long propagation need not be the branch the
		// node was already on. Without this the node jumps by a full turn part way through an arc.
		if (T.Abs(previousNode - nodep) > math.Pi)
		{
			nodep = nodep < previousNode ? nodep + twoPi : nodep - twoPi;
		}

		mp += pl;
		argpp = xls - mp - (cosip * nodep);

		return Reduced(new DeepSpaceElements<T>(elements.MeanMotion, ep, inclp, mp, argpp, nodep), math);
	}

	/// <summary>
	/// Reduces every element of a set to the storage type's working precision.
	/// </summary>
	/// <param name="elements">The elements.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same elements, carrying no more precision than the type is worked to.</returns>
	/// <remarks>
	/// These six values cross back into the propagator and are multiplied into everything after
	/// them, so they are the boundary at which the deep-space routines hand their precision on.
	/// </remarks>
	private static DeepSpaceElements<T> Reduced(DeepSpaceElements<T> elements, IStorageMath<T> math) => new(
		math.ToWorkingPrecision(elements.MeanMotion),
		math.ToWorkingPrecision(elements.Eccentricity),
		math.ToWorkingPrecision(elements.Inclination),
		math.ToWorkingPrecision(elements.MeanAnomaly),
		math.ToWorkingPrecision(elements.ArgumentOfPerigee),
		math.ToWorkingPrecision(elements.RightAscension));

	/// <summary>Converts a literal into the storage type.</summary>
	/// <param name="value">The literal.</param>
	/// <returns>The value in <typeparamref name="T"/>.</returns>
	private static T N(double value) => T.CreateChecked(value);
}
