// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System;
using System.Numerics;
using ktsu.Osculator.Core.Forces;

/// <summary>
/// Cowell's method: integrate the equations of motion directly, under whatever force model is given.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// Where SGP4 is a closed-form fit evaluated once per instant, a numerical integrator reaches an
/// instant by taking thousands of steps from the epoch, and every step rounds. That is what makes it
/// the vehicle for measuring <em>accumulated</em> round-off: the arithmetic error is not a fixed
/// property of one evaluation but something that grows with the length of the arc.
/// </para>
/// <para>
/// Two ways to run it. <see cref="Propagate"/> chooses its own steps from the pair's error
/// estimate, which is what an application wants. <see cref="PropagateFixedStep"/> takes a step the
/// caller names, which is what an experiment wants: two storage types integrating the same arc with
/// adaptive steps choose different steps, so the difference between them would mix truncation error
/// with round-off. With the step sequence held fixed, everything but the arithmetic is held fixed.
/// </para>
/// <para>
/// Step-size control is computed in <see langword="double"/> in every storage type. It decides how
/// far to step, not what the step computes, and a controller is a heuristic with a safety factor of
/// nine tenths — carrying it in fifty digits would be precision spent on a guess.
/// </para>
/// </remarks>
public sealed class Cowell<T>
	where T : struct, INumber<T>
{
	private const double SafetyFactor = 0.9;
	private const double MinimumGrowth = 0.2;
	private const double MaximumGrowth = 5.0;

	private readonly IForceModel<T> force;
	private readonly DormandPrince87<T> pair;

	/// <summary>Initializes a new instance of the <see cref="Cowell{T}"/> class.</summary>
	/// <param name="forceModel">The force model to integrate.</param>
	/// <param name="math">The transcendental functions and working precision for <typeparamref name="T"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="forceModel"/> or <paramref name="math"/> is null.</exception>
	public Cowell(IForceModel<T> forceModel, IStorageMath<T> math)
	{
		Ensure.NotNull(forceModel);
		Ensure.NotNull(math);

		force = forceModel;
		pair = new DormandPrince87<T>(math);
	}

	/// <summary>Integrates an arc with steps chosen to meet a tolerance.</summary>
	/// <param name="initial">The state at the epoch.</param>
	/// <param name="durationSeconds">The length of the arc, in seconds. Must be positive.</param>
	/// <param name="tolerance">The local error tolerance, and the step limits.</param>
	/// <returns>The state at the end of the arc, and what it took to get there.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="tolerance"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="durationSeconds"/> is not positive.</exception>
	/// <exception cref="ArithmeticException">
	/// The step fell below <see cref="CowellTolerance.MinimumStepSeconds"/>, or the step count passed
	/// <see cref="CowellTolerance.MaximumSteps"/>. A tolerance tighter than the storage type's own
	/// resolution does this, and is refused rather than allowed to spin.
	/// </exception>
	public CowellResult<T> Propagate(CartesianState<T> initial, T durationSeconds, CowellTolerance tolerance)
	{
		Ensure.NotNull(tolerance);
		if (durationSeconds <= T.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(durationSeconds), "The arc must have a positive length.");
		}

		IStorageMath<T> math = pair.Arithmetic;
		T elapsed = T.Zero;
		CartesianState<T> state = initial;
		double step = tolerance.InitialStepSeconds;
		int accepted = 0;
		int rejected = 0;
		double smallest = double.PositiveInfinity;
		double largest = 0.0;

		while (elapsed < durationSeconds)
		{
			if (accepted + rejected >= tolerance.MaximumSteps)
			{
				throw new ArithmeticException($"Gave up after {tolerance.MaximumSteps} steps without reaching the end of the arc.");
			}

			T remaining = durationSeconds - elapsed;
			T h = T.CreateChecked(step);
			bool last = h >= remaining;
			if (last)
			{
				h = remaining;
			}

			DormandPrince87Step<T> result = pair.Step(force, elapsed, state, h);
			double errorRatio = ErrorRatio(state, result, tolerance);
			double hTaken = double.CreateChecked(h);

			if (errorRatio <= 1.0)
			{
				state = result.State;
				elapsed = last ? durationSeconds : math.ToWorkingPrecision(elapsed + h);
				accepted++;
				if (!last)
				{
					smallest = Math.Min(smallest, hTaken);
				}

				largest = Math.Max(largest, hTaken);
			}
			else
			{
				rejected++;
			}

			double growth = errorRatio == 0.0
				? MaximumGrowth
				: Math.Clamp(SafetyFactor * Math.Pow(errorRatio, -1.0 / DormandPrince87<T>.Order), MinimumGrowth, MaximumGrowth);

			// A rejected last step was clipped to fit the arc, so grow from the clipped step rather
			// than the one the controller asked for.
			step = Math.Min(hTaken * growth, tolerance.MaximumStepSeconds);
			if (step < tolerance.MinimumStepSeconds)
			{
				throw new ArithmeticException(
					$"The step fell to {step:G3} s, below the {tolerance.MinimumStepSeconds:G3} s floor. The tolerance is likely tighter than this storage type can resolve.");
			}
		}

		return new CowellResult<T>(state, durationSeconds, accepted, rejected, accepted + rejected, double.IsPositiveInfinity(smallest) ? largest : smallest, largest);
	}

	/// <summary>Integrates an arc in a fixed number of equal steps.</summary>
	/// <param name="initial">The state at the epoch.</param>
	/// <param name="stepSeconds">The step, in seconds. Must be positive.</param>
	/// <param name="stepCount">The number of steps. Must not be negative.</param>
	/// <returns>The state after <paramref name="stepCount"/> steps.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="stepSeconds"/> is not positive, or <paramref name="stepCount"/> is negative.
	/// </exception>
	/// <remarks>
	/// The time at the start of each step is computed as <c>i · h</c> rather than accumulated, so a
	/// time-dependent force model sees the same instants in every storage type.
	/// </remarks>
	public CowellResult<T> PropagateFixedStep(CartesianState<T> initial, T stepSeconds, int stepCount)
	{
		if (stepSeconds <= T.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(stepSeconds), "The step must be positive.");
		}

		ArgumentOutOfRangeException.ThrowIfNegative(stepCount);

		IStorageMath<T> math = pair.Arithmetic;
		CartesianState<T> state = initial;
		for (int i = 0; i < stepCount; i++)
		{
			T elapsed = math.ToWorkingPrecision(T.CreateChecked(i) * stepSeconds);
			state = pair.Step(force, elapsed, state, stepSeconds).State;
		}

		double h = double.CreateChecked(stepSeconds);
		return new CowellResult<T>(
			state,
			math.ToWorkingPrecision(T.CreateChecked(stepCount) * stepSeconds),
			stepCount,
			0,
			stepCount,
			h,
			h);
	}

	private static double ErrorRatio(CartesianState<T> start, DormandPrince87Step<T> result, CowellTolerance tolerance)
	{
		CartesianState<T> end = result.State;
		CartesianState<T> error = result.ErrorEstimate;

		double worst = 0.0;
		worst = Math.Max(worst, Component(error.X, start.X, end.X, tolerance.AbsolutePositionKm, tolerance.Relative));
		worst = Math.Max(worst, Component(error.Y, start.Y, end.Y, tolerance.AbsolutePositionKm, tolerance.Relative));
		worst = Math.Max(worst, Component(error.Z, start.Z, end.Z, tolerance.AbsolutePositionKm, tolerance.Relative));
		worst = Math.Max(worst, Component(error.VelocityX, start.VelocityX, end.VelocityX, tolerance.AbsoluteVelocityKmPerSecond, tolerance.Relative));
		worst = Math.Max(worst, Component(error.VelocityY, start.VelocityY, end.VelocityY, tolerance.AbsoluteVelocityKmPerSecond, tolerance.Relative));
		worst = Math.Max(worst, Component(error.VelocityZ, start.VelocityZ, end.VelocityZ, tolerance.AbsoluteVelocityKmPerSecond, tolerance.Relative));
		return worst;
	}

	private static double Component(T error, T before, T after, double absolute, double relative)
	{
		double size = Math.Max(Math.Abs(double.CreateChecked(before)), Math.Abs(double.CreateChecked(after)));
		return Math.Abs(double.CreateChecked(error)) / (absolute + (relative * size));
	}
}
