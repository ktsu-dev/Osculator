// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Storage;

using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// Creates the generic propagation a storage facade delegates to.
/// </summary>
/// <remarks>
/// A factory rather than a constructor so that the storage type is inferred from the
/// <see cref="IStorageMath{T}"/> the facade passes in. A facade then writes
/// <c>Sgp4PropagatorHost.Create(DoubleStorageMath.Instance)</c>, and the only place its storage type
/// is spelled out is the alias package it references — which is the demonstration the four facade
/// projects exist to make.
/// </remarks>
public static class Sgp4PropagatorHost
{
	/// <summary>Creates a propagation over the storage type <paramref name="math"/> serves.</summary>
	/// <typeparam name="T">The numeric storage type, inferred from <paramref name="math"/>.</typeparam>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The propagation.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="math"/> is null.</exception>
	public static Sgp4PropagatorHost<T> Create<T>(IStorageMath<T> math)
		where T : struct, INumber<T> => new(math);
}

/// <summary>
/// SGP4 closed over one storage type, returning a state with the type erased.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// The four facades would otherwise each carry a copy of the same initialize-cache-propagate-time
/// sequence, differing only in the type argument. Kept here, generic, so there is one copy and the
/// facades stay one file each.
/// </para>
/// <para>
/// Initialized satellites are cached against the <see cref="ElementSet"/> instance by reference,
/// not by value, and weakly: an element set the caller drops takes its initialization with it, so a
/// catalogue sweep over thirty thousand objects does not pin thirty thousand initializations.
/// </para>
/// </remarks>
public sealed class Sgp4PropagatorHost<T>
	where T : struct, INumber<T>
{
	private readonly ConditionalWeakTable<ElementSet, Sgp4Satellite<T>> initialized = [];

	/// <summary>Initializes a new instance of the <see cref="Sgp4PropagatorHost{T}"/> class.</summary>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="math"/> is null.</exception>
	public Sgp4PropagatorHost(IStorageMath<T> math)
	{
		Ensure.NotNull(math);
		Math = math;
	}

	/// <summary>Gets the transcendental functions the propagation runs with.</summary>
	public IStorageMath<T> Math { get; }

	/// <summary>
	/// Propagates an element set to a time after its epoch.
	/// </summary>
	/// <param name="elements">The element set.</param>
	/// <param name="minutesSinceEpoch">Minutes since the element set's epoch; may be negative.</param>
	/// <returns>The state, rounded to <see langword="double"/> for display, with its timings.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="minutesSinceEpoch"/> is not finite.
	/// </exception>
	public PropagatedState Propagate(ElementSet elements, double minutesSinceEpoch)
	{
		Ensure.NotNull(elements);

		if (!double.IsFinite(minutesSinceEpoch))
		{
			throw new ArgumentOutOfRangeException(nameof(minutesSinceEpoch), minutesSinceEpoch, "The time since epoch must be finite.");
		}

		TimeSpan initializationElapsed = TimeSpan.Zero;

		if (!initialized.TryGetValue(elements, out Sgp4Satellite<T>? satellite))
		{
			long initializationStart = Stopwatch.GetTimestamp();
			satellite = Sgp4<T>.Initialize(elements, Math);
			initializationElapsed = Stopwatch.GetElapsedTime(initializationStart);

			// Two threads meeting the same new element set both initialize it, and the later one
			// replaces an identical result. Cheaper than a lock on every propagation.
			initialized.AddOrUpdate(elements, satellite);
		}

		// CreateChecked rather than a cast, so that a time the storage type cannot hold — a decimal
		// past 7.9e28 minutes — throws OverflowException instead of producing some other time.
		T minutes = T.CreateChecked(minutesSinceEpoch);

		long start = Stopwatch.GetTimestamp();
		Sgp4Result<T> result = Sgp4<T>.Propagate(satellite, minutes, Math);
		TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

		TemeState<T> state = result.State;

		return result.IsSuccess
			? new(
				Sgp4Error.None,
				ToDouble(state.X),
				ToDouble(state.Y),
				ToDouble(state.Z),
				ToDouble(state.VelocityX),
				ToDouble(state.VelocityY),
				ToDouble(state.VelocityZ),
				elapsed,
				initializationElapsed)
			: new(result.Error, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, elapsed, initializationElapsed);
	}

	private static double ToDouble(T value) => double.CreateTruncating(value);
}
