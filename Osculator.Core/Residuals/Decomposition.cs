// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Residuals;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// How the decomposition is run.
/// </summary>
/// <param name="Samples">
/// How many perturbed element sets the Δ_data ensemble propagates. Each is a full propagation in the
/// reference arithmetic, so this is the knob that sets the routine's cost. The RMS of n samples
/// scatters by about 1/√(2n) of itself: measured on the ISS, 64 samples landed within 25% of the
/// converged figure and 256 within 5%, at roughly ten milliseconds a sample in 30-digit arithmetic.
/// </param>
/// <param name="Seed">
/// The seed for the ensemble's perturbations. Fixed by default so that the same call reports the same
/// Δ_data twice; a Monte Carlo figure that moves between runs of identical input cannot be pinned by a
/// test or compared across storage types.
/// </param>
public sealed record DecompositionOptions(int Samples = 256, int Seed = 44)
{
	/// <summary>Gets the options used when none are given.</summary>
	public static DecompositionOptions Default { get; } = new();
}

/// <summary>
/// Δ_arith for one storage type: the same element set propagated over the same horizon by the same
/// algorithm, in that type and in the reference arithmetic.
/// </summary>
/// <typeparam name="TReference">The reference arithmetic every term is expressed in.</typeparam>
/// <param name="StorageType">The storage type the propagation ran in.</param>
/// <param name="Error">Why that propagation failed, or <see cref="Sgp4Error.None"/>.</param>
/// <param name="Residual">
/// That storage type's state minus the reference state, in the reference state's RIC frame, in
/// kilometres and km/s. Meaningful only when <paramref name="Error"/> is <see cref="Sgp4Error.None"/>.
/// </param>
/// <param name="PropagationTime">
/// Wall-clock time to initialize and propagate once in that storage type. Reported beside the error on
/// purpose (spec §7): the cost of precision belongs next to its benefit. It is a single timing, so it
/// is indicative rather than a benchmark.
/// </param>
public readonly record struct ArithmeticTerm<TReference>(
	Type StorageType,
	Sgp4Error Error,
	RswResidual<TReference> Residual,
	TimeSpan PropagationTime)
	where TReference : struct, INumber<TReference>
{
	/// <summary>Gets a value indicating whether the storage type produced a usable state.</summary>
	public bool IsSuccess => Error == Sgp4Error.None;

	/// <summary>
	/// Gets a value indicating whether every component of the residual is exactly zero — not small,
	/// zero. True of the reference arithmetic measured against itself, and of nothing else that has
	/// ever been measured.
	/// </summary>
	public bool IsExactlyZero => IsSuccess
		&& TReference.IsZero(Residual.Radial) && TReference.IsZero(Residual.AlongTrack) && TReference.IsZero(Residual.CrossTrack)
		&& TReference.IsZero(Residual.RadialRate) && TReference.IsZero(Residual.AlongTrackRate) && TReference.IsZero(Residual.CrossTrackRate);
}

/// <summary>
/// Δ_data as a Monte Carlo spread: how far apart the predictions of element sets that would all have
/// been written down identically end up.
/// </summary>
/// <typeparam name="TReference">The reference arithmetic the ensemble ran and was accumulated in.</typeparam>
/// <param name="Samples">How many perturbed element sets were drawn.</param>
/// <param name="Refusals">How many of those SGP4 refused, and so are not in the statistics.</param>
/// <param name="RmsRadial">Root-mean-square radial offset from the as-written prediction, in km.</param>
/// <param name="RmsAlongTrack">Root-mean-square along-track offset, in km.</param>
/// <param name="RmsCrossTrack">Root-mean-square cross-track offset, in km.</param>
/// <param name="RmsMagnitude">Root-mean-square distance from the as-written prediction, in km.</param>
/// <param name="MaxMagnitude">The furthest any member of the ensemble landed, in km.</param>
/// <remarks>
/// The offsets are taken in the as-written prediction's RIC frame. Root-mean-square about the
/// as-written prediction rather than a standard deviation about the ensemble mean, because the
/// question is how far the truth could be from the prediction actually made, and the as-written
/// element set is the one that was made.
/// </remarks>
public readonly record struct DataTermEnsemble<TReference>(
	int Samples,
	int Refusals,
	TReference RmsRadial,
	TReference RmsAlongTrack,
	TReference RmsCrossTrack,
	TReference RmsMagnitude,
	TReference MaxMagnitude)
	where TReference : struct, INumber<TReference>;

/// <summary>
/// The three terms the gap between a prediction and the truth is made of, for one object and one
/// horizon.
/// </summary>
/// <typeparam name="TReference">The reference arithmetic every term is computed and expressed in.</typeparam>
/// <param name="Elements">The element set the prediction is made from (element set A).</param>
/// <param name="Truth">The element set taken as truth, evaluated at its own epoch (A + Δt).</param>
/// <param name="HorizonMinutes">Δt: how far <paramref name="Elements"/> was propagated, in minutes.</param>
/// <param name="Error">
/// Why the reference prediction or the truth failed to propagate, or <see cref="Sgp4Error.None"/>.
/// When this is set the three terms are not computed and hold their defaults.
/// </param>
/// <param name="Model">
/// Δ_model: the reference prediction minus the truth, in the truth's RIC frame. The same number
/// <see cref="Divergence{T}.Between"/> reports, because it is that routine.
/// </param>
/// <param name="Data">Δ_data: the Monte Carlo spread of the prediction over the element set's quantization.</param>
/// <param name="Arithmetic">
/// Δ_arith for <see langword="float"/>, <see langword="double"/>, <see langword="decimal"/> and the
/// reference arithmetic itself, in that order. The last is the harness check and is exactly zero.
/// </param>
public sealed record DecompositionResult<TReference>(
	ElementSet Elements,
	ElementSet Truth,
	TReference HorizonMinutes,
	Sgp4Error Error,
	RswResidual<TReference> Model,
	DataTermEnsemble<TReference> Data,
	IReadOnlyList<ArithmeticTerm<TReference>> Arithmetic)
	where TReference : struct, INumber<TReference>
{
	/// <summary>Gets a value indicating whether the reference prediction and the truth both propagated.</summary>
	public bool IsSuccess => Error == Sgp4Error.None;

	/// <summary>Gets the epoch the prediction was made from.</summary>
	public DateTime PredictionEpoch => Elements.Epoch;

	/// <summary>Gets the epoch the prediction was compared at.</summary>
	public DateTime TruthEpoch => Truth.Epoch;

	/// <summary>Gets the reference arithmetic measured against itself: the term that validates the harness.</summary>
	public ArithmeticTerm<TReference> ReferenceAgainstItself => Arithmetic[^1];
}

/// <summary>
/// Decomposes the gap between a prediction and the truth into Δ_model, Δ_data and Δ_arith(T).
/// </summary>
/// <typeparam name="TReference">
/// The reference arithmetic. <c>PreciseNumber</c> in practice (spec §6); generic only because this
/// project does not depend on it, and so that the routine can be exercised in a cheap type.
/// </typeparam>
/// <remarks>
/// <para>
/// This is the M5 routine, and spec §6's table is its specification:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <strong>Δ_model</strong> — the reference SGP4 from element set A, against truth at A + Δt. Truth is
/// a later element set, evaluated at its own epoch. That truth is not the orbit: it is another fit of
/// the same model, so Δ_model here includes the fit residuals of both sets and is an upper bound on
/// SGP4's own error. An SP3 or Horizons truth replaces the later set when those clients exist.
/// </description></item>
/// <item><description>
/// <strong>Δ_data</strong> — Monte Carlo. Each field is perturbed independently and uniformly within
/// half its quantization step, all fields at once, the result re-propagated in the reference
/// arithmetic, and the ensemble's spread taken. This differs from <see cref="DataTerm"/>, which
/// perturbs one field at a time by exactly half a step and combines in quadrature. Both are honest;
/// they answer slightly different questions, and this one is the spec's. Expect it to read about
/// 1/√3 of <see cref="DataTerm"/>'s figure: a uniform rounding error has an RMS of 1/√12 of a step,
/// against the half step <see cref="DataTerm"/> uses.
/// </description></item>
/// <item><description>
/// <strong>Δ_arith(T)</strong> — the same element set propagated over the same horizon in each storage
/// type and in the reference arithmetic. Everything but the storage type is held fixed, so the
/// difference is arithmetic and nothing else. The reference is run against <em>itself</em> through
/// exactly the same path, and must come back zero to the last digit: that is the invariant that shows
/// the harness holds everything else fixed.
/// </description></item>
/// </list>
/// <para>
/// Every term is computed and accumulated in <typeparamref name="TReference"/>, so no statistic carries
/// round-off of its own into the comparison (spec §6, "accumulated in PreciseNumber").
/// </para>
/// </remarks>
public static class Decomposition<TReference>
	where TReference : struct, INumber<TReference>
{
	/// <summary>The minutes in a day.</summary>
	private const int MinutesPerDay = 1440;

	/// <summary>
	/// Decomposes the gap between <paramref name="elements"/>' prediction and <paramref name="truth"/>.
	/// </summary>
	/// <param name="elements">Element set A, the one the prediction is made from.</param>
	/// <param name="truth">A later element set for the same object, taken as truth at its own epoch.</param>
	/// <param name="reference">The reference arithmetic.</param>
	/// <param name="options">The ensemble size and seed, or <see langword="null"/> for the defaults.</param>
	/// <returns>The three terms. Carries an error rather than throwing when SGP4 refuses.</returns>
	/// <exception cref="ArgumentNullException">An argument is null.</exception>
	/// <exception cref="ArgumentException">
	/// The two sets describe different objects, <paramref name="truth"/> is not after
	/// <paramref name="elements"/>, or the ensemble size is not positive.
	/// </exception>
	public static DecompositionResult<TReference> Compute(
		ElementSet elements,
		ElementSet truth,
		IStorageMath<TReference> reference,
		DecompositionOptions? options = null)
	{
		Ensure.NotNull(elements);
		Ensure.NotNull(truth);
		Ensure.NotNull(reference);
		options ??= DecompositionOptions.Default;

		if (options.Samples <= 0)
		{
			throw new ArgumentException($"The ensemble needs at least one sample; {options.Samples} were asked for.", nameof(options));
		}

		// Δ_model, through the routine that defines it, so the decomposition and the M2 number can
		// never disagree about what the model term is. It also validates the pair.
		ElementSetDivergence<TReference> model = Divergence<TReference>.Between(elements, truth, reference);

		if (!model.IsSuccess)
		{
			return new DecompositionResult<TReference>(elements, truth, model.HorizonMinutes, model.Error, default, default, []);
		}

		// The reference prediction: the frame both other terms are resolved in.
		Sgp4Result<TReference> prediction = Sgp4<TReference>.Propagate(
			Sgp4<TReference>.Initialize(elements, reference), model.HorizonMinutes, reference);

		if (!prediction.IsSuccess)
		{
			return new DecompositionResult<TReference>(elements, truth, model.HorizonMinutes, prediction.Error, default, default, []);
		}

		JulianDate target = truth.EpochJulianDate;

		ArithmeticTerm<TReference>[] arithmetic =
		[
			Arithmetic(elements, target, prediction.State, FloatStorageMath.Instance, reference),
			Arithmetic(elements, target, prediction.State, DoubleStorageMath.Instance, reference),
			Arithmetic(elements, target, prediction.State, DecimalStorageMath.Instance, reference),
			Arithmetic(elements, target, prediction.State, reference, reference),
		];

		return new DecompositionResult<TReference>(
			elements,
			truth,
			model.HorizonMinutes,
			Sgp4Error.None,
			model.Residual,
			Ensemble(elements, target, prediction.State, reference, options),
			arithmetic);
	}

	/// <summary>
	/// Propagates in one storage type and measures it against the reference prediction.
	/// </summary>
	/// <remarks>
	/// The horizon is computed in <typeparamref name="T"/> by the same route
	/// <see cref="Divergence{T}"/> uses, from the two-part Julian dates, because representing Δt is part
	/// of what a storage type does. When <typeparamref name="T"/> is the reference itself this is a
	/// second, independent run through exactly the same code as the first, which is what makes its
	/// zero a statement about the harness rather than a comparison of a value with itself.
	/// </remarks>
	private static ArithmeticTerm<TReference> Arithmetic<T>(
		ElementSet elements,
		JulianDate target,
		TemeState<TReference> referenceState,
		IStorageMath<T> math,
		IStorageMath<TReference> reference)
		where T : struct, INumber<T>
	{
		Stopwatch clock = Stopwatch.StartNew();
		Sgp4Result<T> result = Sgp4<T>.Propagate(Sgp4<T>.Initialize(elements, math), MinutesBetween<T>(elements.EpochJulianDate, target), math);
		clock.Stop();

		if (!result.IsSuccess)
		{
			return new ArithmeticTerm<TReference>(typeof(T), result.Error, default, clock.Elapsed);
		}

		// Widening into the reference is the only conversion on this path. From decimal it is exact;
		// from float and double it goes through the shortest round-tripping text, which can differ
		// from the binary value by under half an ulp — about 1e-12 km at LEO radius for double, a
		// hundred times below the arithmetic error being measured, and zero for the reference itself.
		TemeState<TReference> widened = new(
			Widen(result.State.X),
			Widen(result.State.Y),
			Widen(result.State.Z),
			Widen(result.State.VelocityX),
			Widen(result.State.VelocityY),
			Widen(result.State.VelocityZ));

		return new ArithmeticTerm<TReference>(
			typeof(T),
			Sgp4Error.None,
			RswResidual<TReference>.Between(referenceState, widened, reference),
			clock.Elapsed);

		static TReference Widen(T value) => TReference.CreateChecked(value);
	}

	/// <summary>
	/// Runs the Δ_data ensemble and accumulates its spread in the reference arithmetic.
	/// </summary>
	/// <remarks>
	/// Every accumulator goes through <see cref="IStorageMath{T}.ToWorkingPrecision"/> after each step
	/// (domain trap 9): in an arbitrary-precision type a running sum of squares otherwise doubles its
	/// digit count on every sample.
	/// </remarks>
	private static DataTermEnsemble<TReference> Ensemble(
		ElementSet elements,
		JulianDate target,
		TemeState<TReference> asWritten,
		IStorageMath<TReference> reference,
		DecompositionOptions options)
	{
		Random random = new(options.Seed);
		TReference sumRadial = TReference.Zero;
		TReference sumAlongTrack = TReference.Zero;
		TReference sumCrossTrack = TReference.Zero;
		TReference max = TReference.Zero;
		int refusals = 0;

		for (int i = 0; i < options.Samples; i++)
		{
			ElementSet perturbed = Perturb(elements, random);
			Sgp4Result<TReference> result = Sgp4<TReference>.Propagate(
				Sgp4<TReference>.Initialize(perturbed, reference),
				MinutesBetween<TReference>(perturbed.EpochJulianDate, target),
				reference);

			if (!result.IsSuccess)
			{
				refusals++;
				continue;
			}

			RswResidual<TReference> offset = RswResidual<TReference>.Between(asWritten, result.State, reference);
			TReference radial = reference.ToWorkingPrecision(offset.Radial * offset.Radial);
			TReference alongTrack = reference.ToWorkingPrecision(offset.AlongTrack * offset.AlongTrack);
			TReference crossTrack = reference.ToWorkingPrecision(offset.CrossTrack * offset.CrossTrack);

			sumRadial = reference.ToWorkingPrecision(sumRadial + radial);
			sumAlongTrack = reference.ToWorkingPrecision(sumAlongTrack + alongTrack);
			sumCrossTrack = reference.ToWorkingPrecision(sumCrossTrack + crossTrack);
			max = TReference.Max(max, reference.Sqrt(reference.ToWorkingPrecision(radial + alongTrack + crossTrack)));
		}

		int accepted = options.Samples - refusals;

		if (accepted == 0)
		{
			return new DataTermEnsemble<TReference>(options.Samples, refusals, default, default, default, default, default);
		}

		TReference count = TReference.CreateChecked(accepted);
		TReference meanRadial = reference.Divide(sumRadial, count);
		TReference meanAlongTrack = reference.Divide(sumAlongTrack, count);
		TReference meanCrossTrack = reference.Divide(sumCrossTrack, count);

		return new DataTermEnsemble<TReference>(
			options.Samples,
			refusals,
			reference.Sqrt(meanRadial),
			reference.Sqrt(meanAlongTrack),
			reference.Sqrt(meanCrossTrack),
			reference.Sqrt(reference.ToWorkingPrecision(meanRadial + meanAlongTrack + meanCrossTrack)),
			max);
	}

	/// <summary>
	/// Draws one element set from the band of those that would have been written identically.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The eight fields <see cref="DataTerm"/> perturbs, plus the epoch. The epoch is written to
	/// 1e-8 day like the others, and moving it changes Δt as well: the prediction is still compared at
	/// the same instant, so a later epoch means a shorter propagation. <see cref="ElementSet.Epoch"/>
	/// moves with <see cref="ElementSet.EpochJulianDate"/> to keep the record consistent, though the
	/// propagator reads only the latter (domain trap 8).
	/// </para>
	/// <para>
	/// Uniform within half a step, because a value rounded to a step is equally likely to have come
	/// from anywhere within half a step of it. <c>MeanMotionDot</c> is drawn although SGP4 never reads
	/// it, so that adding the field to the model one day changes this figure without changing this code.
	/// </para>
	/// </remarks>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness",
		Justification = "A Monte Carlo ensemble that must replay from its seed; nothing here is a secret.")]
	private static ElementSet Perturb(ElementSet e, Random random)
	{
		double epochDays = Offset(random, ElementFieldQuantization.EpochDays);

		return e with
		{
			EpochJulianDate = new JulianDate(e.EpochJulianDate.Day, e.EpochJulianDate.DayFraction + epochDays),
			Epoch = e.Epoch.AddTicks((long)Math.Round(epochDays * TimeSpan.TicksPerDay)),
			MeanMotion = e.MeanMotion + Offset(random, ElementFieldQuantization.MeanMotion),
			Eccentricity = e.Eccentricity + Offset(random, ElementFieldQuantization.Eccentricity),
			Inclination = e.Inclination + Offset(random, ElementFieldQuantization.Inclination),
			RightAscensionOfAscendingNode = e.RightAscensionOfAscendingNode + Offset(random, ElementFieldQuantization.RightAscensionOfAscendingNode),
			ArgumentOfPericenter = e.ArgumentOfPericenter + Offset(random, ElementFieldQuantization.ArgumentOfPericenter),
			MeanAnomaly = e.MeanAnomaly + Offset(random, ElementFieldQuantization.MeanAnomaly),
			MeanMotionDot = e.MeanMotionDot + Offset(random, ElementFieldQuantization.MeanMotionDot),
			BStar = e.BStar + Offset(random, ElementFieldQuantization.StepForExponentialField(e.BStar)),
		};

		static double Offset(Random random, double step) => (random.NextDouble() - 0.5) * step;
	}

	/// <summary>The time from one epoch to another, in minutes, in <typeparamref name="T"/>.</summary>
	/// <remarks>
	/// The same construction as <see cref="Divergence{T}"/>: part by part from the two-part Julian
	/// dates, so neither epoch is collapsed into one <see langword="double"/> (domain trap 4).
	/// </remarks>
	private static T MinutesBetween<T>(JulianDate from, JulianDate to)
		where T : struct, INumber<T>
	{
		T days = T.CreateChecked(to.Day - from.Day);
		T fraction = T.CreateChecked(to.DayFraction) - T.CreateChecked(from.DayFraction);

		return (days + fraction) * T.CreateChecked(MinutesPerDay);
	}
}
