// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Conjunction;

using System.Collections.Generic;

/// <summary>
/// The outcome of screening a set of objects against each other.
/// </summary>
/// <param name="Conjunctions">Every closest approach within the threshold, ordered by miss distance.</param>
/// <param name="PairCount">The number of distinct pairs in the set.</param>
/// <param name="PairsPropagated">The number of pairs that survived the pre-filter and were propagated.</param>
/// <param name="Failures">The objects the screen could not follow, each listed once.</param>
public sealed record ConjunctionScreenResult(
	IReadOnlyList<ConjunctionEvent> Conjunctions,
	long PairCount,
	long PairsPropagated,
	IReadOnlyList<ScreeningFailure> Failures)
{
	/// <summary>Gets the number of pairs the pre-filter rejected without propagating them.</summary>
	public long PairsRejectedByFilter => PairCount - PairsPropagated;
}
