// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// One force in a <see cref="CombinedForceModel{T}"/>, with a switch.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="model">The force.</param>
public sealed class ForceTerm<T>(IForceModel<T> model)
	where T : struct, INumber<T>
{
	/// <summary>Gets the force.</summary>
	public IForceModel<T> Model { get; } = Ensure.NotNull(model);

	/// <summary>Gets or sets a value indicating whether the force is summed.</summary>
	public bool Enabled { get; set; } = true;
}

/// <summary>
/// The sum of several forces, each of which can be switched off on its own.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// What <see cref="Cowell{T}"/> integrates when the force model is more than one thing: the central
/// term, the harmonics, the Sun and Moon, drag and radiation pressure are separate objects summed
/// here, so an experiment can measure what one of them is worth by switching it off and leaving
/// everything else as it was.
/// </para>
/// <para>
/// Summed in reverse order of the list, so that a list written largest-first — central term, then
/// harmonics, then the small perturbations — adds the small terms together before they meet the
/// large one. Addition does not commute in finite precision, and the order a sum is taken in is
/// part of its arithmetic error.
/// </para>
/// </remarks>
public sealed class CombinedForceModel<T> : IForceModel<T>
	where T : struct, INumber<T>
{
	private readonly ForceTerm<T>[] terms;
	private readonly IStorageMath<T> math;

	/// <summary>Initializes a new instance of the <see cref="CombinedForceModel{T}"/> class.</summary>
	/// <param name="storageMath">The working precision for <typeparamref name="T"/>.</param>
	/// <param name="models">The forces, largest first; all start enabled.</param>
	/// <exception cref="System.ArgumentNullException">An argument or a force is null.</exception>
	public CombinedForceModel(IStorageMath<T> storageMath, params IEnumerable<IForceModel<T>> models)
	{
		Ensure.NotNull(storageMath);
		Ensure.NotNull(models);
		math = storageMath;
		List<ForceTerm<T>> list = [];
		foreach (IForceModel<T> model in models)
		{
			list.Add(new ForceTerm<T>(model));
		}

		terms = [.. list];
	}

	/// <summary>Gets the forces, in the order they were given.</summary>
	public IReadOnlyList<ForceTerm<T>> Terms => terms;

	/// <inheritdoc />
	public CartesianAcceleration<T> Acceleration(T secondsSinceEpoch, CartesianState<T> state)
	{
		T x = T.Zero;
		T y = T.Zero;
		T z = T.Zero;
		for (int i = terms.Length - 1; i >= 0; i--)
		{
			if (!terms[i].Enabled)
			{
				continue;
			}

			CartesianAcceleration<T> a = terms[i].Model.Acceleration(secondsSinceEpoch, state);
			x = math.ToWorkingPrecision(x + a.X);
			y = math.ToWorkingPrecision(y + a.Y);
			z = math.ToWorkingPrecision(z + a.Z);
		}

		return new(x, y, z);
	}
}
