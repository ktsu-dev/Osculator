// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Storage;

using System.Numerics;

/// <summary>
/// Measures what a numeric type can represent, in that type's own arithmetic.
/// </summary>
public static class StorageProbe
{
	/// <summary>
	/// The smallest step the search will report before giving up and calling the type exact enough.
	/// </summary>
	/// <remarks>
	/// An arbitrary-precision type has no smallest step, so the halving below would never terminate
	/// on one. The floor is far below any physically meaningful length — a metre divided by 10^40 is
	/// some 25 orders of magnitude smaller than a Planck length — so reaching it means the type is
	/// exact for every purpose this application has.
	/// </remarks>
	public const int MinimumProbeExponent = -40;

	/// <summary>
	/// Finds the smallest increment that changes <paramref name="magnitude"/> in <typeparamref name="T"/>.
	/// </summary>
	/// <typeparam name="T">The storage type to probe.</typeparam>
	/// <param name="magnitude">The magnitude to probe at.</param>
	/// <returns>
	/// The smallest step that <typeparamref name="T"/> can still distinguish, or a value at
	/// <see cref="MinimumProbeExponent"/> when the type distinguishes everything down to the floor.
	/// </returns>
	/// <remarks>
	/// Halves a trial step until adding it to <paramref name="magnitude"/> stops changing the value,
	/// then reports the last step that did. This is the definition of the quantity rather than a
	/// property read off the type, so it works the same for a binary float, a decimal float, and an
	/// arbitrary-precision type without any of them being special-cased.
	/// </remarks>
	public static T SmallestDistinguishableStep<T>(T magnitude)
		where T : struct, INumber<T>
	{
		T two = T.One + T.One;
		T step = T.Abs(magnitude);

		if (T.IsZero(step))
		{
			step = T.One;
		}

		T floor = Pow10<T>(MinimumProbeExponent);
		T lastDistinguishable = step;

		while (step > floor)
		{
			T halved = step / two;

			if (magnitude + halved == magnitude)
			{
				return lastDistinguishable;
			}

			lastDistinguishable = halved;
			step = halved;
		}

		return lastDistinguishable;
	}

	/// <summary>
	/// Raises ten to an integer power in <typeparamref name="T"/>'s own arithmetic.
	/// </summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <param name="exponent">The power to raise ten to.</param>
	/// <returns>Ten raised to <paramref name="exponent"/>.</returns>
	private static T Pow10<T>(int exponent)
		where T : struct, INumber<T>
	{
		T ten = T.CreateChecked(10);
		T result = T.One;

		for (int i = 0; i < System.Math.Abs(exponent); i++)
		{
			result = exponent < 0 ? result / ten : result * ten;
		}

		return result;
	}
}
