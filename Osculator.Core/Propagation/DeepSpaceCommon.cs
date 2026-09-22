// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// The intermediate lunar-solar geometry the deep-space initialization computes once and then hands
/// on to the resonance initialization.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// These are not coefficients of the model — they do not survive initialization, and nothing
/// propagates against them. They exist because the published algorithm computes the solar and lunar
/// contributions in one routine and consumes them in the next, and carrying them in an object is
/// the alternative to a twenty-nine-parameter call.
/// </para>
/// <para>
/// The names are the paper's. <c>S</c> values come from the lunar pass, <c>Ss</c> and <c>Sz</c> from
/// the solar pass, which the routine runs first and then saves before reusing the same working set.
/// </para>
/// </remarks>
internal sealed class DeepSpaceCommon<T>
	where T : struct, INumber<T>
{
	internal T Sinim { get; init; }
	internal T Cosim { get; init; }
	internal T Emsq { get; init; }

	internal T S1 { get; init; }
	internal T S2 { get; init; }
	internal T S3 { get; init; }
	internal T S4 { get; init; }
	internal T S5 { get; init; }

	internal T Ss1 { get; init; }
	internal T Ss2 { get; init; }
	internal T Ss3 { get; init; }
	internal T Ss4 { get; init; }
	internal T Ss5 { get; init; }

	internal T Sz1 { get; init; }
	internal T Sz3 { get; init; }
	internal T Sz11 { get; init; }
	internal T Sz13 { get; init; }
	internal T Sz21 { get; init; }
	internal T Sz23 { get; init; }
	internal T Sz31 { get; init; }
	internal T Sz33 { get; init; }

	internal T Z1 { get; init; }
	internal T Z3 { get; init; }
	internal T Z11 { get; init; }
	internal T Z13 { get; init; }
	internal T Z21 { get; init; }
	internal T Z23 { get; init; }
	internal T Z31 { get; init; }
	internal T Z33 { get; init; }
}
