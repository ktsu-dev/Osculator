// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System.Collections.Generic;
using ktsu.Osculator.Core.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers what each storage type can represent at an orbital radius.
/// </summary>
/// <remarks>
/// These are the first measurements the application can make, and they need no propagator: they are
/// properties of the arithmetic alone. They also pin the claim the whole project rests on — that the
/// four storage types differ by orders of magnitude in what they can express about a residual.
/// </remarks>
[TestClass]
public sealed class StorageProfileTests
{
	/// <summary>A nominal low Earth orbital radius, in metres.</summary>
	private const double LeoRadiusMeters = 7_000_000.0;

	private static IReadOnlyList<IStorageProfile> AllProfiles =>
	[
		new ktsu.Osculator.Storage.FloatStorageProfile(),
		new ktsu.Osculator.Storage.DoubleStorageProfile(),
		new ktsu.Osculator.Storage.DecimalStorageProfile(),
		new ktsu.Osculator.Storage.PreciseStorageProfile(),
	];

	[TestMethod]
	public void Float_CannotExpressHalfAMetreAtAnOrbitalRadius()
	{
		// The headline consequence: a residual against a centimetre-accurate laser-ranging orbit
		// is not merely inaccurate in float, it is unrepresentable before any physics is computed.
		ktsu.Osculator.Storage.FloatStorageProfile profile = new();

		double step = profile.SmallestDistinguishableStepMeters(LeoRadiusMeters);

		Assert.IsGreaterThanOrEqualTo(0.25, step, $"float resolved {step} m at LEO radius; expected roughly half a metre.");
		Assert.IsLessThanOrEqualTo(1.0, step);
	}

	[TestMethod]
	public void Double_ResolvesFarBelowAnyModelError()
	{
		ktsu.Osculator.Storage.DoubleStorageProfile profile = new();

		double step = profile.SmallestDistinguishableStepMeters(LeoRadiusMeters);

		// Around a nanometre — nine orders of magnitude below the element-set quantization term.
		Assert.IsLessThanOrEqualTo(1e-6, step);
	}

	[TestMethod]
	public void EachStorageTypeResolvesFinerThanTheLast()
	{
		IReadOnlyList<IStorageProfile> profiles = AllProfiles;

		for (int i = 1; i < profiles.Count; i++)
		{
			double coarser = profiles[i - 1].SmallestDistinguishableStepMeters(LeoRadiusMeters);
			double finer = profiles[i].SmallestDistinguishableStepMeters(LeoRadiusMeters);

			Assert.IsLessThan(coarser, finer, $"{profiles[i].StorageName} did not resolve finer than {profiles[i - 1].StorageName}.");
		}
	}

	[TestMethod]
	public void EveryProfileReportsADistinctName()
	{
		HashSet<string> names = [];

		foreach (IStorageProfile profile in AllProfiles)
		{
			Assert.IsTrue(names.Add(profile.StorageName), $"Duplicate storage name {profile.StorageName}.");
		}

		Assert.HasCount(4, names);
	}
}
