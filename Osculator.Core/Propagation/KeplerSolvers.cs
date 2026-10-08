// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// The outcome of an iterative Kepler-equation solve.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="Anomaly">
/// The solved variable: the eccentric anomaly in radians for
/// <see cref="KeplerSolvers{T}.NaiveNewton"/>, the universal variable in √km for
/// <see cref="KeplerSolvers{T}.UniversalVariable"/>.
/// </param>
/// <param name="Iterations">How many iteration steps were evaluated.</param>
/// <param name="Converged">
/// Whether the iteration stopped because its steps reached the storage type's round-off floor,
/// rather than because it ran out of iterations.
/// </param>
public readonly record struct KeplerSolution<T>(T Anomaly, int Iterations, bool Converged)
	where T : struct, INumber<T>;

/// <summary>
/// Two solvers for Kepler's equation, generic over the storage type: a deliberately naive Newton
/// iteration on the eccentric anomaly, and the universal-variable formulation solved by Laguerre–Conway.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// These are the two halves of spec demonstration 3, which runs them side by side in every storage
/// type and measures where each loses digits. <see cref="NaiveNewton"/> is the textbook iteration
/// with the textbook starting guess and nothing done about cancellation; it is naive on purpose and
/// should not be made clever, because the comparison is what it is for.
/// </para>
/// <para>
/// Neither solver takes a tolerance. A tolerance would have to be chosen per storage type, and a
/// tolerance chosen for <see langword="double"/> is wrong for every other one. Both instead iterate
/// until a step stops shrinking — which, for a quadratically convergent iteration, is exactly the
/// point where it has reached the round-off floor of the arithmetic it is running in. That is what
/// lets one source carry seven digits or thirty without being told which.
/// </para>
/// </remarks>
public static class KeplerSolvers<T>
	where T : struct, INumber<T>
{
	/// <summary>The most steps either solver takes before reporting that it did not converge.</summary>
	public const int MaximumIterations = 100;

	/// <summary>
	/// The relative step size below which a step that fails to shrink is taken as round-off rather
	/// than as Newton's iteration still finding its way in.
	/// </summary>
	/// <remarks>
	/// Either iteration is only fast once it is close; before that a step can
	/// legitimately be larger than the one before it. Stopping on the first non-shrinking step would
	/// stop there too.
	/// </remarks>
	private const double QuadraticRegime = 1e-4;

	/// <summary>The order n of the Laguerre–Conway iteration; five is Conway's choice.</summary>
	private const double LaguerreOrder = 5.0;

	/// <summary>The positive z below which the Stumpff functions are summed as series.</summary>
	/// <remarks>
	/// The closed forms subtract nearly equal values near zero — <c>1 − cos √z</c> loses every digit
	/// of itself as z goes to zero, which is the near-parabolic case — so below this bound the series
	/// is used instead. At z = 4 the closed forms lose under one digit and the alternating series'
	/// largest term is under its sum, so either side of the boundary is sound.
	/// </remarks>
	private const double PositiveSeriesBound = 4.0;

	/// <summary>The negative z above which the Stumpff functions are summed as series.</summary>
	/// <remarks>
	/// For negative z every term of both series is positive, so the series has no cancellation at all
	/// and is the more accurate form everywhere; the closed forms are only there so a very long
	/// hyperbolic arc does not cost hundreds of terms. A boundary at −4 was tried first and measured
	/// a seam of eight ulps in <see langword="double"/> across it, from the closed form's
	/// <c>cosh − 1</c> and the squarings in its exponential. At −2500, √−z = 50 and the series
	/// converges in about a hundred terms.
	/// </remarks>
	private const double NegativeSeriesBound = -2500.0;

	/// <summary>The most terms a series is summed to.</summary>
	private const int MaximumSeriesTerms = 400;

	/// <summary>
	/// Solves <c>E − e·sin E = M</c> for the eccentric anomaly by Newton's method, starting from
	/// <c>E₀ = M</c>.
	/// </summary>
	/// <param name="meanAnomaly">The mean anomaly M, in radians.</param>
	/// <param name="eccentricity">The eccentricity e, in [0, 1).</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The eccentric anomaly, with how it was reached.</returns>
	/// <remarks>
	/// The residual <c>E − e·sin E − M</c> is evaluated exactly as written. Near perigee, where E is
	/// small, its first two terms are nearly proportional and their difference is all that carries the
	/// answer; how many digits that costs in each storage type is the measurement this solver exists
	/// to make.
	/// </remarks>
	/// <exception cref="System.ArgumentNullException"><paramref name="math"/> is null.</exception>
	public static KeplerSolution<T> NaiveNewton(T meanAnomaly, T eccentricity, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T anomaly = meanAnomaly;
		T previousStep = T.Zero;
		T regime = N(QuadraticRegime);

		for (int iteration = 1; iteration <= MaximumIterations; iteration++)
		{
			T residual = anomaly - (eccentricity * math.Sin(anomaly)) - meanAnomaly;
			T slope = T.One - (eccentricity * math.Cos(anomaly));
			T step = math.ToWorkingPrecision(residual / slope);

			if (HasReachedFloor(step, previousStep, anomaly, regime, iteration))
			{
				return new(anomaly, iteration, true);
			}

			anomaly = math.ToWorkingPrecision(anomaly - step);
			previousStep = step;
		}

		return new(anomaly, MaximumIterations, false);
	}

	/// <summary>
	/// Solves the universal Kepler equation for the universal variable χ by the Laguerre–Conway
	/// iteration.
	/// </summary>
	/// <param name="radius">The initial radius r₀, in km.</param>
	/// <param name="radialTerm">The initial <c>r₀·v₀ / √μ</c>, in √km.</param>
	/// <param name="alpha">The reciprocal of the semi-major axis, <c>2/r₀ − v₀²/μ</c>, in 1/km; zero for a parabola, negative for a hyperbola.</param>
	/// <param name="scaledTime">The elapsed time scaled by <c>√μ</c>, in km^(3/2).</param>
	/// <param name="initialGuess">The starting value of χ, in √km.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The universal variable, with how it was reached.</returns>
	/// <remarks>
	/// <para>
	/// The equation is <c>F(χ) = σ₀χ²C(z) + (1 − αr₀)χ³S(z) + r₀χ − √μ·Δt = 0</c> with
	/// <c>z = αχ²</c>. One equation covers ellipse, parabola and hyperbola, and nothing in it divides
	/// by <c>1 − e</c>, which is why it is the formulation that does not degrade as the eccentricity
	/// rises. Its derivative <c>F′</c> is the radius at χ, and <c>F″ = σ₀(1 − zC) + (1 − αr₀)χ(1 − zS)</c>.
	/// </para>
	/// <para>
	/// Laguerre's method rather than Newton's, after Conway (1986), because Newton's overshoots on a
	/// near-parabolic orbit: at e = 0.999999 with a perigee close to the focus it was measured
	/// running off to a χ ten times the answer and never coming back. Laguerre's step with n = 5
	/// converges from essentially any starting point on this equation and costs one extra term per
	/// step. The naive solver keeps Newton, because being naive is its job.
	/// </para>
	/// <para>
	/// Written from Vallado, <em>Fundamentals of Astrodynamics and Applications</em>, algorithm 8, in
	/// the <c>c₂</c>/<c>c₃</c> form, where <c>C</c> and <c>S</c> are <see cref="Stumpff"/>.
	/// </para>
	/// </remarks>
	/// <exception cref="System.ArgumentNullException"><paramref name="math"/> is null.</exception>
	public static KeplerSolution<T> UniversalVariable(T radius, T radialTerm, T alpha, T scaledTime, T initialGuess, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T chi = initialGuess;
		T previousStep = T.Zero;
		T regime = N(QuadraticRegime);
		T order = N(LaguerreOrder);
		T orderLessOne = order - T.One;
		T energyTerm = T.One - (alpha * radius);

		for (int iteration = 1; iteration <= MaximumIterations; iteration++)
		{
			T chiSquared = chi * chi;
			T psi = math.ToWorkingPrecision(chiSquared * alpha);
			(T c2, T c3) = Stumpff(psi, math);

			T oneLessPsiC3 = T.One - (psi * c3);
			T oneLessPsiC2 = T.One - (psi * c2);
			T equation = math.ToWorkingPrecision((radialTerm * chiSquared * c2) + (energyTerm * chiSquared * chi * c3) + (radius * chi) - scaledTime);
			T slope = math.ToWorkingPrecision((chiSquared * c2) + (radialTerm * chi * oneLessPsiC3) + (radius * oneLessPsiC2));
			T curvature = math.ToWorkingPrecision((radialTerm * oneLessPsiC2) + (energyTerm * chi * oneLessPsiC3));

			T discriminant = T.Abs((orderLessOne * orderLessOne * slope * slope) - (order * orderLessOne * equation * curvature));
			T root = math.Sqrt(discriminant);
			T denominator = slope >= T.Zero ? slope + root : slope - root;
			T step = math.ToWorkingPrecision(order * equation / denominator);

			if (HasReachedFloor(step, previousStep, chi, regime, iteration))
			{
				return new(chi, iteration, true);
			}

			chi = math.ToWorkingPrecision(chi - step);
			previousStep = step;
		}

		return new(chi, MaximumIterations, false);
	}

	/// <summary>
	/// Computes the Stumpff functions <c>C(z)</c> and <c>S(z)</c>.
	/// </summary>
	/// <param name="z">The argument, <c>αχ²</c>: positive for an ellipse, zero for a parabola, negative for a hyperbola.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>
	/// <c>C(z) = (1 − cos √z)/z</c> and <c>S(z) = (√z − sin √z)/√z³</c>, continued through zero, where
	/// they are 1/2 and 1/6, and into negative z with the hyperbolic functions.
	/// </returns>
	/// <remarks>
	/// Near zero both closed forms are the difference of two nearly equal values divided by a small
	/// one, which is the whole of what makes a near-parabolic orbit hard. Between
	/// <see cref="NegativeSeriesBound"/> and <see cref="PositiveSeriesBound"/> they are therefore
	/// summed from their Maclaurin series,
	/// <c>C = Σ (−z)ᵏ/(2k+2)!</c> and <c>S = Σ (−z)ᵏ/(2k+3)!</c>, which have no cancellation there.
	/// The hyperbolic side needs <c>cosh</c> and <c>sinh</c>, which <see cref="IStorageMath{T}"/> does
	/// not declare, so they come from an exponential summed here in <typeparamref name="T"/>.
	/// </remarks>
	/// <exception cref="System.ArgumentNullException"><paramref name="math"/> is null.</exception>
	public static (T C, T S) Stumpff(T z, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		if (z >= N(PositiveSeriesBound))
		{
			T root = math.Sqrt(z);
			T c = (T.One - math.Cos(root)) / z;
			T s = (root - math.Sin(root)) / (z * root);
			return (math.ToWorkingPrecision(c), math.ToWorkingPrecision(s));
		}

		if (z <= N(NegativeSeriesBound))
		{
			T negated = -z;
			T root = math.Sqrt(negated);
			T growing = Exp(root, math);
			T decaying = T.One / growing;
			T two = N(2);
			T cosh = (growing + decaying) / two;
			T sinh = (growing - decaying) / two;
			T c = (cosh - T.One) / negated;
			T s = (sinh - root) / (negated * root);
			return (math.ToWorkingPrecision(c), math.ToWorkingPrecision(s));
		}

		return StumpffSeries(z, math);
	}

	/// <summary>Sums the Stumpff series, for z between the two series bounds.</summary>
	/// <param name="z">The argument.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>C(z) and S(z).</returns>
	private static (T C, T S) StumpffSeries(T z, IStorageMath<T> math)
	{
		T negated = -z;
		T termC = T.One / N(2);
		T termS = T.One / N(6);
		T sumC = termC;
		T sumS = termS;

		for (int k = 1; k <= MaximumSeriesTerms; k++)
		{
			// (2k+2)! = (2k)! · (2k+1)(2k+2), and (2k+3)! = (2k+1)! · (2k+2)(2k+3).
			T twoK = N(2) * N(k);
			termC = math.ToWorkingPrecision(termC * negated / ((twoK + T.One) * (twoK + N(2))));
			termS = math.ToWorkingPrecision(termS * negated / ((twoK + N(2)) * (twoK + N(3))));

			T nextC = math.ToWorkingPrecision(sumC + termC);
			T nextS = math.ToWorkingPrecision(sumS + termS);
			if (nextC == sumC && nextS == sumS)
			{
				break;
			}

			sumC = nextC;
			sumS = nextS;
		}

		return (sumC, sumS);
	}

	/// <summary>Computes <c>eˣ</c> for a non-negative x in the storage type's own arithmetic.</summary>
	/// <param name="value">The exponent, non-negative.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The exponential.</returns>
	/// <remarks>
	/// Halved until under one half so the Taylor series converges in a few dozen terms, then squared
	/// back. Each squaring doubles the relative error, so the argument of fifty at which
	/// <see cref="Stumpff"/> first calls this costs about two digits; that is the price of a hyperbolic
	/// arc that long, and it is paid in every storage type alike.
	/// </remarks>
	private static T Exp(T value, IStorageMath<T> math)
	{
		T half = T.One / N(2);
		int halvings = 0;
		T reduced = value;
		while (reduced > half)
		{
			reduced /= N(2);
			halvings++;
		}

		T term = T.One;
		T sum = T.One;
		for (int n = 1; n <= MaximumSeriesTerms; n++)
		{
			term = math.ToWorkingPrecision(term * reduced / N(n));
			T next = math.ToWorkingPrecision(sum + term);
			if (next == sum)
			{
				break;
			}

			sum = next;
		}

		for (int i = 0; i < halvings; i++)
		{
			sum = math.ToWorkingPrecision(sum * sum);
		}

		return sum;
	}

	/// <summary>Decides whether a Newton step is round-off rather than progress.</summary>
	/// <param name="step">The step just computed.</param>
	/// <param name="previousStep">The step before it.</param>
	/// <param name="current">The current iterate.</param>
	/// <param name="regime">The relative size below which a step is in the quadratic regime.</param>
	/// <param name="iteration">The iteration number, from one.</param>
	/// <returns>Whether to stop and keep <paramref name="current"/>.</returns>
	private static bool HasReachedFloor(T step, T previousStep, T current, T regime, int iteration)
	{
		if (step == T.Zero)
		{
			return true;
		}

		if (iteration == 1)
		{
			return false;
		}

		T size = T.Abs(step);
		T previousSize = T.Abs(previousStep);
		T scale = T.Max(T.Abs(current), T.One);
		return size >= previousSize && previousSize <= regime * scale;
	}

	/// <summary>Converts a literal into the storage type.</summary>
	/// <param name="value">The literal.</param>
	/// <returns>The value in <typeparamref name="T"/>.</returns>
	private static T N(double value) => T.CreateChecked(value);
}
