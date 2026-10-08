// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Forces;

/// <summary>
/// The Prince–Dormand embedded Runge–Kutta pair RK8(7)13M, generic over the numeric storage type.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// Thirteen stages, an eighth-order solution that is propagated and a seventh-order one that is
/// used only to estimate the eighth's local error. From P. J. Prince and J. R. Dormand, "High order
/// embedded Runge–Kutta formulae", <em>J. Comp. Appl. Math.</em> 7 (1981), 67–75, whose
/// coefficients are rational approximations good to about eighteen digits. That is the method's
/// own definition, not a defect: every storage type here uses the same rationals, so the method is
/// identical across them and only the arithmetic differs.
/// </para>
/// <para>
/// The coefficients are held as integer numerators and denominators and divided in
/// <typeparamref name="T"/>, so an arbitrary-precision type gets the quotient at its own precision
/// rather than the seventeen digits a <see langword="double"/> literal would have given it.
/// </para>
/// <para>
/// The table was checked independently of this file before it was trusted: the local error
/// estimate it produces falls by 2⁸ per halving of the step on an eccentric two-body orbit, which
/// is the order the pair claims. Changing one digit of one coupling coefficient drops that to 2¹.
/// <c>CowellTests</c> repeats the measurement against this implementation.
/// </para>
/// </remarks>
public sealed class DormandPrince87<T>
	where T : struct, INumber<T>
{
	/// <summary>The number of stages, and so of force evaluations, per step.</summary>
	public const int Stages = 13;

	/// <summary>The order of the propagated solution.</summary>
	public const int Order = 8;

	/// <summary>The order of the embedded solution used for the error estimate.</summary>
	public const int EmbeddedOrder = 7;

	/// <summary>The nodes, cᵢ.</summary>
	private static readonly (long Numerator, long Denominator)[] NodeFractions =
	[
		(0, 1), (1, 18), (1, 12), (1, 8), (5, 16), (3, 8), (59, 400), (93, 200),
		(5490023248, 9719169821), (13, 20), (1201146811, 1299019798), (1, 1), (1, 1),
	];

	/// <summary>The coupling coefficients, aᵢⱼ, row by row; zeros are listed so the columns line up.</summary>
	private static readonly (long Numerator, long Denominator)[][] CouplingFractions =
	[
		[],
		[(1, 18)],
		[(1, 48), (1, 16)],
		[(1, 32), (0, 1), (3, 32)],
		[(5, 16), (0, 1), (-75, 64), (75, 64)],
		[(3, 80), (0, 1), (0, 1), (3, 16), (3, 20)],
		[(29443841, 614563906), (0, 1), (0, 1), (77736538, 692538347), (-28693883, 1125000000), (23124283, 1800000000)],
		[(16016141, 946692911), (0, 1), (0, 1), (61564180, 158732637), (22789713, 633445777), (545815736, 2771057229), (-180193667, 1043307555)],
		[(39632708, 573591083), (0, 1), (0, 1), (-433636366, 683701615), (-421739975, 2616292301), (100302831, 723423059), (790204164, 839813087), (800635310, 3783071287)],
		[(246121993, 1340847787), (0, 1), (0, 1), (-37695042795, 15268766246), (-309121744, 1061227803), (-12992083, 490766935), (6005943493, 2108947869), (393006217, 1396673457), (123872331, 1001029789)],
		[(-1028468189, 846180014), (0, 1), (0, 1), (8478235783, 508512852), (1311729495, 1432422823), (-10304129995, 1701304382), (-48777925059, 3047939560), (15336726248, 1032824649), (-45442868181, 3398467696), (3065993473, 597172653)],
		[(185892177, 718116043), (0, 1), (0, 1), (-3185094517, 667107341), (-477755414, 1098053517), (-703635378, 230739211), (5731566787, 1027545527), (5232866602, 850066563), (-4093664535, 808688257), (3962137247, 1805957418), (65686358, 487910083)],
		[(403863854, 491063109), (0, 1), (0, 1), (-5068492393, 434740067), (-411421997, 543043805), (652783627, 914296604), (11173962825, 925320556), (-13158990841, 6184727034), (3936647629, 1978049680), (-160528059, 685178525), (248638103, 1413531060), (0, 1)],
	];

	/// <summary>The eighth-order weights, the solution that is propagated.</summary>
	private static readonly (long Numerator, long Denominator)[] EighthOrderFractions =
	[
		(14005451, 335480064), (0, 1), (0, 1), (0, 1), (0, 1), (-59238493, 1068277825), (181606767, 758867731),
		(561292985, 797845732), (-1041891430, 1371343529), (760417239, 1151165299), (118820643, 751138087),
		(-528747749, 2220607170), (1, 4),
	];

	/// <summary>The seventh-order weights, used only for the error estimate.</summary>
	private static readonly (long Numerator, long Denominator)[] SeventhOrderFractions =
	[
		(13451932, 455176623), (0, 1), (0, 1), (0, 1), (0, 1), (-808719846, 976000145), (1757004468, 5645159321),
		(656045339, 265891186), (-3867574721, 1518517206), (465885868, 322736535), (53011238, 667516719),
		(2, 45), (0, 1),
	];

	private readonly T[] nodes;
	private readonly (int Column, T Value)[][] coupling;
	private readonly (int Stage, T Value)[] weights;
	private readonly (int Stage, T Value)[] errorWeights;

	/// <summary>Initializes a new instance of the <see cref="DormandPrince87{T}"/> class.</summary>
	/// <param name="math">The transcendental functions and working precision for <typeparamref name="T"/>.</param>
	/// <exception cref="System.ArgumentNullException"><paramref name="math"/> is null.</exception>
	public DormandPrince87(IStorageMath<T> math)
	{
		Ensure.NotNull(math);
		Arithmetic = math;

		nodes = new T[Stages];
		coupling = new (int, T)[Stages][];
		for (int i = 0; i < Stages; i++)
		{
			nodes[i] = Fraction(NodeFractions[i]);

			List<(int, T)> row = [];
			for (int j = 0; j < CouplingFractions[i].Length; j++)
			{
				if (CouplingFractions[i][j].Numerator != 0)
				{
					row.Add((j, Fraction(CouplingFractions[i][j])));
				}
			}

			coupling[i] = [.. row];
		}

		List<(int, T)> eighth = [];
		List<(int, T)> difference = [];
		for (int i = 0; i < Stages; i++)
		{
			T b8 = Fraction(EighthOrderFractions[i]);
			T b7 = Fraction(SeventhOrderFractions[i]);
			if (EighthOrderFractions[i].Numerator != 0)
			{
				eighth.Add((i, b8));
			}

			if (EighthOrderFractions[i] != SeventhOrderFractions[i])
			{
				difference.Add((i, Arithmetic.ToWorkingPrecision(b8 - b7)));
			}
		}

		weights = [.. eighth];
		errorWeights = [.. difference];
	}

	/// <summary>Gets the transcendental functions and working precision the pair computes with.</summary>
	public IStorageMath<T> Arithmetic { get; }

	/// <summary>Takes one step.</summary>
	/// <param name="forceModel">The force model whose acceleration is integrated.</param>
	/// <param name="secondsSinceEpoch">The time at the start of the step, in seconds since the integration's epoch.</param>
	/// <param name="state">The state at the start of the step.</param>
	/// <param name="stepSeconds">The step, in seconds.</param>
	/// <returns>The eighth-order state at the end of the step, and an estimate of its local error.</returns>
	/// <exception cref="System.ArgumentNullException"><paramref name="forceModel"/> is null.</exception>
	public DormandPrince87Step<T> Step(IForceModel<T> forceModel, T secondsSinceEpoch, CartesianState<T> state, T stepSeconds)
	{
		Ensure.NotNull(forceModel);

		T[] start = [state.X, state.Y, state.Z, state.VelocityX, state.VelocityY, state.VelocityZ];
		T[][] slopes = new T[Stages][];
		T[] stage = new T[6];

		for (int i = 0; i < Stages; i++)
		{
			for (int m = 0; m < 6; m++)
			{
				T sum = T.Zero;
				foreach ((int column, T value) in coupling[i])
				{
					sum += value * slopes[column][m];
				}

				stage[m] = Arithmetic.ToWorkingPrecision(start[m] + (stepSeconds * sum));
			}

			T time = Arithmetic.ToWorkingPrecision(secondsSinceEpoch + (nodes[i] * stepSeconds));
			CartesianAcceleration<T> acceleration = forceModel.Acceleration(
				time,
				new CartesianState<T>(stage[0], stage[1], stage[2], stage[3], stage[4], stage[5]));

			slopes[i] = [stage[3], stage[4], stage[5], acceleration.X, acceleration.Y, acceleration.Z];
		}

		T[] next = Combine(start, slopes, weights, stepSeconds);
		T[] error = Combine(new T[6], slopes, errorWeights, stepSeconds);

		return new DormandPrince87Step<T>(
			new CartesianState<T>(next[0], next[1], next[2], next[3], next[4], next[5]),
			new CartesianState<T>(error[0], error[1], error[2], error[3], error[4], error[5]));
	}

	private T[] Combine(T[] origin, T[][] slopes, (int Stage, T Value)[] coefficients, T stepSeconds)
	{
		T[] result = new T[6];
		for (int m = 0; m < 6; m++)
		{
			T sum = T.Zero;
			foreach ((int index, T value) in coefficients)
			{
				sum += value * slopes[index][m];
			}

			result[m] = Arithmetic.ToWorkingPrecision(origin[m] + (stepSeconds * sum));
		}

		return result;
	}

	private T Fraction((long Numerator, long Denominator) fraction) =>
		Arithmetic.ToWorkingPrecision(T.CreateChecked(fraction.Numerator) / T.CreateChecked(fraction.Denominator));
}
