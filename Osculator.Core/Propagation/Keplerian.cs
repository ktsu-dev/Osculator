// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System;
using System.Numerics;

/// <summary>
/// A position and velocity in an inertial frame, for the two-body problem.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="X">Position along x, in kilometres.</param>
/// <param name="Y">Position along y, in kilometres.</param>
/// <param name="Z">Position along z, in kilometres.</param>
/// <param name="VelocityX">Velocity along x, in kilometres per second.</param>
/// <param name="VelocityY">Velocity along y, in kilometres per second.</param>
/// <param name="VelocityZ">Velocity along z, in kilometres per second.</param>
/// <remarks>
/// Deliberately not <see cref="TemeState{T}"/>. Two-body motion is the same in any inertial frame,
/// so this type names none; the caller knows which frame its initial state was in, and the
/// propagated state is in that one.
/// </remarks>
public readonly record struct TwoBodyState<T>(T X, T Y, T Z, T VelocityX, T VelocityY, T VelocityZ)
	where T : struct, INumber<T>;

/// <summary>
/// The outcome of one two-body propagation.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="State">The propagated state.</param>
/// <param name="Solution">How the universal Kepler equation was solved on the way.</param>
public readonly record struct KeplerianResult<T>(TwoBodyState<T> State, KeplerSolution<T> Solution)
	where T : struct, INumber<T>;

/// <summary>
/// The two-body Keplerian propagator in the universal-variable formulation, generic over the
/// storage type.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// One equation for every conic: the universal variable χ and the Stumpff functions carry an
/// ellipse, a parabola and a hyperbola through the same arithmetic, so an orbit whose eccentricity
/// is 0.999999 or 1.000001 needs no special case and no division by <c>1 − e</c>. This is spec §5.2,
/// the analytic baseline the Cowell integrator is checked against, and one half of demonstration 3;
/// the other half is <see cref="KeplerSolvers{T}.NaiveNewton"/>.
/// </para>
/// <para>
/// The solution is advanced with the Lagrange f and g coefficients, Vallado algorithm 8, with the
/// universal Kepler equation solved by Laguerre–Conway rather than Newton; see
/// <see cref="KeplerSolvers{T}.UniversalVariable"/>. Nothing here
/// knows a gravitational parameter: μ is an argument, because two-body motion is a property of the
/// pair rather than of WGS-72, and the caller decides which constant its comparison needs.
/// </para>
/// </remarks>
public static class Keplerian<T>
	where T : struct, INumber<T>
{
	/// <summary>
	/// The band of |α·r₀| either side of zero inside which an orbit is treated as near-parabolic
	/// for the purpose of choosing a starting guess.
	/// </summary>
	private const double NearParabolicBand = 0.01;

	/// <summary>
	/// Propagates a two-body state through a span of time.
	/// </summary>
	/// <param name="initial">The state at the start of the span, in km and km/s.</param>
	/// <param name="seconds">The span, in seconds; negative propagates backwards.</param>
	/// <param name="gravitationalParameter">The gravitational parameter μ, in km³/s².</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The state at the end of the span, with how the Kepler equation was solved.</returns>
	/// <remarks>
	/// An elliptic span longer than one period is first reduced modulo the period. That is not an
	/// approximation — two-body motion is exactly periodic — but it does mean the period itself is
	/// computed in <typeparamref name="T"/>, so the reduction carries the storage type's error in it
	/// multiplied by the number of whole periods removed.
	/// </remarks>
	/// <exception cref="System.ArgumentNullException"><paramref name="math"/> is null.</exception>
	public static KeplerianResult<T> Propagate(TwoBodyState<T> initial, T seconds, T gravitationalParameter, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T mu = gravitationalParameter;
		T sqrtMu = math.Sqrt(mu);

		T radius = math.Sqrt((initial.X * initial.X) + (initial.Y * initial.Y) + (initial.Z * initial.Z));
		T speedSquared = (initial.VelocityX * initial.VelocityX) + (initial.VelocityY * initial.VelocityY) + (initial.VelocityZ * initial.VelocityZ);
		T radialDot = (initial.X * initial.VelocityX) + (initial.Y * initial.VelocityY) + (initial.Z * initial.VelocityZ);

		T alpha = math.ToWorkingPrecision((N(2) / radius) - (speedSquared / mu));
		T radialTerm = math.ToWorkingPrecision(radialDot / sqrtMu);

		T span = seconds;
		if (alpha > T.Zero)
		{
			// An ellipse: reduce to within one period, since two-body motion is exactly periodic.
			T period = math.ToWorkingPrecision(N(2) * math.Pi / (sqrtMu * alpha * math.Sqrt(alpha)));
			if (T.Abs(span) > period)
			{
				long wholePeriods = long.CreateTruncating(span / period);
				span = math.ToWorkingPrecision(span - (T.CreateChecked(wholePeriods) * period));
			}
		}

		// Vallado's elliptic guess χ₀ = √μ·Δt·α is the eccentric anomaly swept, scaled by √a. Near the
		// parabola and beyond it the guesses are computed in double: they only have to start the
		// iteration, which then refines to the storage type's own floor.
		double conic = double.CreateChecked(alpha * radius);
		T initialGuess = conic > NearParabolicBand
			? math.ToWorkingPrecision(sqrtMu * span * alpha)
			: T.CreateChecked(conic < -NearParabolicBand
				? HyperbolicGuess(double.CreateChecked(radius), double.CreateChecked(radialDot), double.CreateChecked(alpha), double.CreateChecked(mu), double.CreateChecked(span))
				: ParabolicGuess(double.CreateChecked(radius), double.CreateChecked(radialDot), double.CreateChecked(alpha), double.CreateChecked(mu), double.CreateChecked(span)));

		T scaledTime = math.ToWorkingPrecision(sqrtMu * span);
		KeplerSolution<T> solution = KeplerSolvers<T>.UniversalVariable(radius, radialTerm, alpha, scaledTime, initialGuess, math);

		T chi = solution.Anomaly;
		T chiSquared = chi * chi;
		T psi = math.ToWorkingPrecision(chiSquared * alpha);
		(T c2, T c3) = KeplerSolvers<T>.Stumpff(psi, math);

		T finalRadius = math.ToWorkingPrecision(
			(chiSquared * c2) + (radialTerm * chi * (T.One - (psi * c3))) + (radius * (T.One - (psi * c2))));

		T f = math.ToWorkingPrecision(T.One - (chiSquared * c2 / radius));
		T g = math.ToWorkingPrecision(span - (chiSquared * chi * c3 / sqrtMu));
		T gDot = math.ToWorkingPrecision(T.One - (chiSquared * c2 / finalRadius));
		T fDot = math.ToWorkingPrecision(sqrtMu / (finalRadius * radius) * chi * ((psi * c3) - T.One));

		TwoBodyState<T> state = new(
			math.ToWorkingPrecision((f * initial.X) + (g * initial.VelocityX)),
			math.ToWorkingPrecision((f * initial.Y) + (g * initial.VelocityY)),
			math.ToWorkingPrecision((f * initial.Z) + (g * initial.VelocityZ)),
			math.ToWorkingPrecision((fDot * initial.X) + (gDot * initial.VelocityX)),
			math.ToWorkingPrecision((fDot * initial.Y) + (gDot * initial.VelocityY)),
			math.ToWorkingPrecision((fDot * initial.Z) + (gDot * initial.VelocityZ)));

		return new(state, solution);
	}

	/// <summary>
	/// Builds a state from classical elements, for any conic.
	/// </summary>
	/// <param name="semiLatusRectum">The semi-latus rectum p, in km — finite for a parabola, where the semi-major axis is not.</param>
	/// <param name="eccentricity">The eccentricity.</param>
	/// <param name="inclination">The inclination, in radians.</param>
	/// <param name="rightAscension">The right ascension of the ascending node, in radians.</param>
	/// <param name="argumentOfPerigee">The argument of perigee, in radians.</param>
	/// <param name="trueAnomaly">The true anomaly, in radians.</param>
	/// <param name="gravitationalParameter">The gravitational parameter μ, in km³/s².</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The state.</returns>
	/// <remarks>
	/// Taking the true anomaly rather than the mean anomaly is deliberate: it needs no Kepler
	/// equation, so a state built here carries no solver's error into whatever it is used to test.
	/// </remarks>
	/// <exception cref="System.ArgumentNullException"><paramref name="math"/> is null.</exception>
	public static TwoBodyState<T> FromElements(
		T semiLatusRectum,
		T eccentricity,
		T inclination,
		T rightAscension,
		T argumentOfPerigee,
		T trueAnomaly,
		T gravitationalParameter,
		IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T cosNu = math.Cos(trueAnomaly);
		T sinNu = math.Sin(trueAnomaly);
		T radius = semiLatusRectum / (T.One + (eccentricity * cosNu));
		T speedScale = math.Sqrt(gravitationalParameter / semiLatusRectum);

		// Perifocal frame: x towards perigee, z along the angular momentum.
		T px = radius * cosNu;
		T py = radius * sinNu;
		T vx = -speedScale * sinNu;
		T vy = speedScale * (eccentricity + cosNu);

		T cosO = math.Cos(rightAscension);
		T sinO = math.Sin(rightAscension);
		T cosW = math.Cos(argumentOfPerigee);
		T sinW = math.Sin(argumentOfPerigee);
		T cosI = math.Cos(inclination);
		T sinI = math.Sin(inclination);

		// The first two columns of R3(−Ω)·R1(−i)·R3(−ω); the perifocal z components are zero.
		T r11 = (cosO * cosW) - (sinO * sinW * cosI);
		T r12 = (-cosO * sinW) - (sinO * cosW * cosI);
		T r21 = (sinO * cosW) + (cosO * sinW * cosI);
		T r22 = (-sinO * sinW) + (cosO * cosW * cosI);
		T r31 = sinW * sinI;
		T r32 = cosW * sinI;

		return new(
			math.ToWorkingPrecision((r11 * px) + (r12 * py)),
			math.ToWorkingPrecision((r21 * px) + (r22 * py)),
			math.ToWorkingPrecision((r31 * px) + (r32 * py)),
			math.ToWorkingPrecision((r11 * vx) + (r12 * vy)),
			math.ToWorkingPrecision((r21 * vx) + (r22 * vy)),
			math.ToWorkingPrecision((r31 * vx) + (r32 * vy)));
	}

	/// <summary>Vallado's starting guess for a hyperbola, which grows with the logarithm of time.</summary>
	/// <param name="radius">The initial radius, in km.</param>
	/// <param name="radialDot">The initial r₀·v₀, in km²/s.</param>
	/// <param name="alpha">The reciprocal semi-major axis, negative, in 1/km.</param>
	/// <param name="mu">The gravitational parameter, in km³/s².</param>
	/// <param name="seconds">The span, in seconds.</param>
	/// <returns>The guess for χ, in √km.</returns>
	/// <remarks>
	/// A hyperbola's radius grows linearly in time, so χ grows with its logarithm, and a guess that
	/// ignores that — √μ·Δt / r₀ was tried first — leaves the iteration walking thousands of √km
	/// towards an answer near four hundred, and running out of steps on a ten-day arc. Falls back to
	/// the parabolic guess where the logarithm's argument is not positive.
	/// </remarks>
	private static double HyperbolicGuess(double radius, double radialDot, double alpha, double mu, double seconds)
	{
		double a = 1.0 / alpha;
		double sign = Math.Sign(seconds);
		double argument = -2.0 * mu * alpha * seconds / (radialDot + (sign * Math.Sqrt(-mu * a) * (1.0 - (radius * alpha))));
		double guess = sign * Math.Sqrt(-a) * Math.Log(argument);
		return double.IsFinite(guess) && argument > 1.0 ? guess : ParabolicGuess(radius, radialDot, alpha, mu, seconds);
	}

	/// <summary>A starting guess from the parabola through the same point: Barker's equation.</summary>
	/// <param name="radius">The initial radius, in km.</param>
	/// <param name="radialDot">The initial r₀·v₀, in km²/s.</param>
	/// <param name="alpha">The reciprocal semi-major axis, in 1/km.</param>
	/// <param name="mu">The gravitational parameter, in km³/s².</param>
	/// <param name="seconds">The span, in seconds.</param>
	/// <returns>The guess for χ, in √km.</returns>
	/// <remarks>
	/// On a parabola χ is exactly <c>√p·(D − D₀)</c> with <c>D = tan(ν/2)</c>, and Barker's equation
	/// gives D in closed form. Near e = 1 that is nearly the answer, which is where the elliptic and
	/// hyperbolic guesses are at their worst. The semi-latus rectum comes from the vis-viva energy:
	/// <c>p = r₀(2 − αr₀) − σ²</c> with <c>σ = r₀·v₀/√μ</c>, since <c>h² = r₀²v₀² − (r₀·v₀)²</c>.
	/// </remarks>
	private static double ParabolicGuess(double radius, double radialDot, double alpha, double mu, double seconds)
	{
		double sigma = radialDot / Math.Sqrt(mu);
		double p = (radius * (2.0 - (alpha * radius))) - (sigma * sigma);

		// On the parabola through this point, D₀ = tan(ν₀/2) = σ / √p, from r = p(1 + D²)/2 and r·v.
		double d0 = sigma / Math.Sqrt(p);
		double sqrtMuOverP3 = Math.Sqrt(mu / (p * p * p));
		double perigeeTime = (d0 + (d0 * d0 * d0 / 3.0)) / (2.0 * sqrtMuOverP3);
		double w = 3.0 * sqrtMuOverP3 * (perigeeTime + seconds);
		double root = Math.Sqrt((w * w) + 1.0);
		double d = Math.Cbrt(w + root) - Math.Cbrt(root - w);
		double guess = Math.Sqrt(p) * (d - d0);
		return double.IsFinite(guess) ? guess : Math.Sqrt(mu) * seconds / radius;
	}

	/// <summary>Converts a literal into the storage type.</summary>
	/// <param name="value">The literal.</param>
	/// <returns>The value in <typeparamref name="T"/>.</returns>
	private static T N(double value) => T.CreateChecked(value);
}
