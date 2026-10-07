// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Storage;
using ktsu.Osculator.Numerics.Precise;
using ktsu.Osculator.Storage;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the storage facades as propagators: the non-generic seam every panel calls.
/// </summary>
/// <remarks>
/// The facades add nothing to the model, so the claim worth pinning is that they add nothing to it:
/// each one returns exactly what the generic propagator returns in its storage type, rounded to
/// <see langword="double"/> once on the way out. A facade that converted the time argument through
/// a different path, or initialized with different math, would agree to many digits and still be a
/// different measurement — so the comparison is for equality, not for a tolerance.
/// </remarks>
[TestClass]
public sealed class PropagatorHostTests
{
	/// <summary>The first case in the verification set, a near-earth object with a short published arc.</summary>
	private const int FirstCaseCatalogId = 5;

	/// <summary>The constructed deep-space case whose perturbed eccentricity leaves the model's range.</summary>
	private const int RefusedCatalogId = 33334;

	private static IReadOnlyList<IPropagatorHost> AllHosts =>
	[
		new FloatStorageProfile(),
		new DoubleStorageProfile(),
		new DecimalStorageProfile(),
		new PreciseStorageProfile(),
	];

	[TestMethod]
	public void DoubleFacade_IsExactlyTheGenericPropagator()
	{
		AssertMatches(new DoubleStorageProfile(), (e, t) =>
		{
			Sgp4Result<double> r = Sgp4<double>.Propagate(Sgp4<double>.Initialize(e, DoubleStorageMath.Instance), t, DoubleStorageMath.Instance);
			return (r.Error, [r.State.X, r.State.Y, r.State.Z, r.State.VelocityX, r.State.VelocityY, r.State.VelocityZ]);
		});
	}

	[TestMethod]
	public void FloatFacade_IsExactlyTheGenericPropagator()
	{
		AssertMatches(new FloatStorageProfile(), (e, t) =>
		{
			Sgp4Result<float> r = Sgp4<float>.Propagate(Sgp4<float>.Initialize(e, FloatStorageMath.Instance), (float)t, FloatStorageMath.Instance);
			return (r.Error, [r.State.X, r.State.Y, r.State.Z, r.State.VelocityX, r.State.VelocityY, r.State.VelocityZ]);
		});
	}

	[TestMethod]
	public void DecimalFacade_IsExactlyTheGenericPropagator()
	{
		AssertMatches(new DecimalStorageProfile(), (e, t) =>
		{
			Sgp4Result<decimal> r = Sgp4<decimal>.Propagate(Sgp4<decimal>.Initialize(e, DecimalStorageMath.Instance), (decimal)t, DecimalStorageMath.Instance);
			return (r.Error, [(double)r.State.X, (double)r.State.Y, (double)r.State.Z, (double)r.State.VelocityX, (double)r.State.VelocityY, (double)r.State.VelocityZ]);
		});
	}

	[TestMethod]
	public void PreciseFacade_IsExactlyTheGenericPropagator()
	{
		AssertMatches(new PreciseStorageProfile(), (e, t) =>
		{
			PreciseStorageMath math = PreciseStorageMath.Instance;
			Sgp4Result<PreciseNumber> r = Sgp4<PreciseNumber>.Propagate(Sgp4<PreciseNumber>.Initialize(e, math), t.ToPreciseNumber(), math);
			return (r.Error, [r.State.X.To<double>(), r.State.Y.To<double>(), r.State.Z.To<double>(), r.State.VelocityX.To<double>(), r.State.VelocityY.To<double>(), r.State.VelocityZ.To<double>()]);
		});
	}

	[TestMethod]
	public void DoubleFacade_ReproducesThePublishedVectors()
	{
		// Equality with the generic propagator only means something if the generic propagator is
		// right, which gate 1 establishes. This is the thread back to it through the facade.
		VerificationSet.Case first = FindCase(FirstCaseCatalogId);
		IReadOnlyList<VerificationSet.Expected> rows = ExpectedFor(FirstCaseCatalogId);
		DoubleStorageProfile host = new();

		foreach (VerificationSet.Expected expected in rows)
		{
			PropagatedState state = host.Propagate(first.Elements, expected.Minutes);

			Assert.IsTrue(state.IsSuccess, $"t={expected.Minutes} failed with {state.Error}.");
			double error = Math.Sqrt(Square(state.X - expected.X) + Square(state.Y - expected.Y) + Square(state.Z - expected.Z));
			Assert.IsLessThanOrEqualTo(1e-8, error, $"t={expected.Minutes}: {error:E3} km from the published position.");
		}
	}

	[TestMethod]
	public void Initialization_IsPaidOncePerElementSet()
	{
		VerificationSet.Case first = FindCase(FirstCaseCatalogId);
		DoubleStorageProfile host = new();

		PropagatedState once = host.Propagate(first.Elements, 0.0);
		PropagatedState twice = host.Propagate(first.Elements, 360.0);

		Assert.IsGreaterThan(TimeSpan.Zero, once.InitializationElapsed, "The first propagation of an element set must report the initialization it did.");
		Assert.AreEqual(TimeSpan.Zero, twice.InitializationElapsed, "The second propagation re-initialized an element set it had already seen.");
		Assert.IsGreaterThan(TimeSpan.Zero, twice.Elapsed, "A propagation reported taking no time at all.");
	}

	[TestMethod]
	public void Initialization_IsKeyedOnTheElementSetInstance()
	{
		// A different element set with the same catalogue number is a different element set. Keying
		// on the number would hand a later fit the earlier fit's coefficients.
		VerificationSet.Case first = FindCase(FirstCaseCatalogId);
		ElementSet nudged = first.Elements with { MeanAnomaly = first.Elements.MeanAnomaly + 1.0 };
		DoubleStorageProfile host = new();

		PropagatedState original = host.Propagate(first.Elements, 0.0);
		PropagatedState moved = host.Propagate(nudged, 0.0);

		Assert.AreNotEqual(TimeSpan.Zero, moved.InitializationElapsed, "The nudged element set was served the original's initialization.");
		Assert.AreNotEqual(original.X, moved.X);
	}

	[TestMethod]
	public void EveryFacade_ReportsAModelRefusalRatherThanAState()
	{
		VerificationSet.Case refused = FindCase(RefusedCatalogId);

		foreach (IPropagatorHost host in AllHosts)
		{
			PropagatedState state = host.Propagate(refused.Elements, 0.0);

			Assert.IsFalse(state.IsSuccess, $"{host.StorageName} produced a state for an element set the model refuses.");
			Assert.IsTrue(double.IsNaN(state.X), $"{host.StorageName} reported a failed propagation as a position.");
		}
	}

	[TestMethod]
	public void ANonFiniteTime_IsRejected()
	{
		VerificationSet.Case first = FindCase(FirstCaseCatalogId);
		DoubleStorageProfile host = new();

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => host.Propagate(first.Elements, double.NaN));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => host.Propagate(first.Elements, double.PositiveInfinity));
	}

	private static void AssertMatches(IPropagatorHost host, Func<ElementSet, double, (Sgp4Error Error, double[] Components)> direct)
	{
		VerificationSet.Case first = FindCase(FirstCaseCatalogId);
		IReadOnlyList<VerificationSet.Expected> rows = ExpectedFor(FirstCaseCatalogId);

		Assert.IsNotEmpty(rows, "The first verification case has no published rows to compare at.");

		foreach (VerificationSet.Expected expected in rows)
		{
			PropagatedState fromHost = host.Propagate(first.Elements, expected.Minutes);
			(Sgp4Error error, double[] components) = direct(first.Elements, expected.Minutes);

			Assert.AreEqual(error, fromHost.Error, $"{host.StorageName}, t={expected.Minutes}.");
			Assert.AreEqual(components[0], fromHost.X, 0.0, $"{host.StorageName} x, t={expected.Minutes}.");
			Assert.AreEqual(components[1], fromHost.Y, 0.0, $"{host.StorageName} y, t={expected.Minutes}.");
			Assert.AreEqual(components[2], fromHost.Z, 0.0, $"{host.StorageName} z, t={expected.Minutes}.");
			Assert.AreEqual(components[3], fromHost.VelocityX, 0.0, $"{host.StorageName} vx, t={expected.Minutes}.");
			Assert.AreEqual(components[4], fromHost.VelocityY, 0.0, $"{host.StorageName} vy, t={expected.Minutes}.");
			Assert.AreEqual(components[5], fromHost.VelocityZ, 0.0, $"{host.StorageName} vz, t={expected.Minutes}.");
		}
	}

	private static VerificationSet.Case FindCase(int catalogId) =>
		VerificationSet.ReadCases().Find(c => c.Elements.NoradCatalogId == catalogId)
			?? throw new InvalidOperationException($"Object {catalogId} is no longer in the verification file.");

	private static IReadOnlyList<VerificationSet.Expected> ExpectedFor(int catalogId)
	{
		List<VerificationSet.Case> cases = VerificationSet.ReadCases();
		int index = cases.FindIndex(c => c.Elements.NoradCatalogId == catalogId);
		return VerificationSet.ReadExpected()[index];
	}

	private static double Square(double value) => value * value;
}
