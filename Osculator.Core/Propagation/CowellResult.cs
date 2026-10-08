// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// What a <see cref="Cowell{T}"/> integration produced.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="State">The state at the end of the arc.</param>
/// <param name="ElapsedSeconds">The length of the arc integrated, in seconds.</param>
/// <param name="AcceptedSteps">Steps kept.</param>
/// <param name="RejectedSteps">Steps whose error estimate exceeded the tolerance and were retaken smaller.</param>
/// <param name="StepsAttempted">Every step taken, kept or not; each cost thirteen force evaluations.</param>
/// <param name="SmallestStepSeconds">
/// The smallest step kept, ignoring a final step clipped to land on the end of the arc.
/// </param>
/// <param name="LargestStepSeconds">The largest step kept.</param>
public readonly record struct CowellResult<T>(
	CartesianState<T> State,
	T ElapsedSeconds,
	int AcceptedSteps,
	int RejectedSteps,
	int StepsAttempted,
	double SmallestStepSeconds,
	double LargestStepSeconds)
	where T : struct, INumber<T>
{
	/// <summary>Gets the number of times the force model was evaluated.</summary>
	public int ForceEvaluations => StepsAttempted * DormandPrince87<T>.Stages;
}
