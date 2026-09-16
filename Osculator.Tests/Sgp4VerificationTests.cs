// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Runs the standard SGP4 verification set.
/// </summary>
/// <remarks>
/// This is the gate the whole repository rests on. A propagator that is subtly wrong produces an
/// error decomposition that is confidently wrong, which is worse than no decomposition at all — so
/// until this passes, nothing measured downstream of it means anything.
/// </remarks>
[TestClass]
public sealed class Sgp4VerificationTests
{
	/// <summary>One verification case: an element set and the time span to propagate it over.</summary>
	/// <param name="Elements">The element set.</param>
	/// <param name="StartMinutes">Minutes since epoch to start at.</param>
	/// <param name="StopMinutes">Minutes since epoch to stop at.</param>
	/// <param name="StepMinutes">Step in minutes.</param>
	private sealed record Case(ElementSet Elements, double StartMinutes, double StopMinutes, double StepMinutes);

	/// <summary>One expected state from the published output.</summary>
	/// <param name="Minutes">Minutes since epoch.</param>
	/// <param name="X">TEME x, in kilometres.</param>
	/// <param name="Y">TEME y, in kilometres.</param>
	/// <param name="Z">TEME z, in kilometres.</param>
	/// <param name="VelocityX">TEME x velocity, in kilometres per second.</param>
	/// <param name="VelocityY">TEME y velocity, in kilometres per second.</param>
	/// <param name="VelocityZ">TEME z velocity, in kilometres per second.</param>
	private sealed record Expected(double Minutes, double X, double Y, double Z, double VelocityX, double VelocityY, double VelocityZ);

	/// <summary>
	/// The position tolerance, in kilometres.
	/// </summary>
	/// <remarks>
	/// The figure the published paper specifies. This implementation currently achieves 7.3e-9 km
	/// across the whole near-earth set, so there is a little under one order of magnitude of
	/// headroom. Tighten this if that improves; never loosen it to make a change pass.
	/// </remarks>
	private const double PositionToleranceKm = 1e-8;

	/// <summary>
	/// The velocity tolerance, in kilometres per second.
	/// </summary>
	/// <remarks>
	/// Currently achieved: 7.8e-10 km/s. Velocity is asserted separately from position because the
	/// two scale by different factors, and an error in the velocity factor alone leaves position
	/// perfect — which is exactly the defect this suite caught while it was being written.
	/// </remarks>
	private const double VelocityToleranceKmPerSecond = 1e-9;

	private static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "Data");

	[TestMethod]
	public void NearEarthCases_MatchThePublishedVectors()
	{
		List<Case> cases = ReadCases();
		List<IReadOnlyList<Expected>> expectedBlocks = ReadExpected();

		Assert.AreEqual(cases.Count, expectedBlocks.Count, "Case count and expected-block count disagree; the two files are out of step.");

		double worstPosition = 0.0;
		double worstVelocity = 0.0;
		int nearEarthCases = 0;
		int comparedRows = 0;
		List<string> failures = [];

		for (int i = 0; i < cases.Count; i++)
		{
			Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(cases[i].Elements, DoubleStorageMath.Instance);

			if (satellite.IsDeepSpace)
			{
				continue;
			}

			nearEarthCases++;

			foreach (Expected expected in expectedBlocks[i])
			{
				Sgp4Result<double> result = Sgp4<double>.Propagate(satellite, expected.Minutes, DoubleStorageMath.Instance);

				if (!result.IsSuccess && result.Error != Sgp4Error.Decayed)
				{
					// The published output stops emitting rows once the model errors, so a row that
					// exists here should have produced a state.
					failures.Add($"{cases[i].Elements.NoradCatalogId} at {expected.Minutes} min: {result.Error}");
					continue;
				}

				double dPosition = Distance(result.State.X - expected.X, result.State.Y - expected.Y, result.State.Z - expected.Z);
				double dVelocity = Distance(result.State.VelocityX - expected.VelocityX, result.State.VelocityY - expected.VelocityY, result.State.VelocityZ - expected.VelocityZ);

				worstPosition = System.Math.Max(worstPosition, dPosition);
				worstVelocity = System.Math.Max(worstVelocity, dVelocity);
				comparedRows++;

				if (dPosition > PositionToleranceKm)
				{
					failures.Add($"{cases[i].Elements.NoradCatalogId} at {expected.Minutes} min: position off by {dPosition:E3} km");
				}

				if (dVelocity > VelocityToleranceKmPerSecond)
				{
					failures.Add($"{cases[i].Elements.NoradCatalogId} at {expected.Minutes} min: velocity off by {dVelocity:E3} km/s");
				}
			}
		}

		Console.WriteLine($"near-earth cases {nearEarthCases}, rows compared {comparedRows}");
		Console.WriteLine($"worst position error {worstPosition:E3} km, worst velocity error {worstVelocity:E3} km/s");

		Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
		Assert.IsGreaterThan(0, comparedRows, "No rows were compared; the harness did not run.");
	}

	[TestMethod]
	public void DeepSpaceCases_AreReportedRatherThanAnswered()
	{
		List<Case> cases = ReadCases();
		int deepSpace = 0;

		foreach (Case verification in cases)
		{
			Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(verification.Elements, DoubleStorageMath.Instance);

			if (!satellite.IsDeepSpace)
			{
				continue;
			}

			deepSpace++;
			Sgp4Result<double> result = Sgp4<double>.Propagate(satellite, verification.StartMinutes, DoubleStorageMath.Instance);

			Assert.AreEqual(
				Sgp4Error.DeepSpaceNotImplemented,
				result.Error,
				$"{verification.Elements.NoradCatalogId} is deep-space and must say so rather than return a near-earth answer.");
		}

		Console.WriteLine($"deep-space cases {deepSpace}");
		Assert.IsGreaterThan(0, deepSpace, "The verification set contains deep-space cases; none were detected.");
	}

	private static double Distance(double x, double y, double z) => System.Math.Sqrt((x * x) + (y * y) + (z * z));

	private static List<Case> ReadCases()
	{
		string[] lines = File.ReadAllLines(Path.Combine(DataDirectory, "SGP4-VER.TLE"));
		List<Case> cases = [];
		string? pending = null;

		foreach (string line in lines)
		{
			if (line.StartsWith('#'))
			{
				continue;
			}

			if (line.StartsWith("1 ", StringComparison.Ordinal))
			{
				pending = line;
				continue;
			}

			if (!line.StartsWith("2 ", StringComparison.Ordinal) || pending is null)
			{
				continue;
			}

			// The verification file appends start, stop and step in minutes after the element fields.
			string[] tail = line[69..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

			cases.Add(new Case(
				TleParser.Parse(pending, line),
				double.Parse(tail[0], CultureInfo.InvariantCulture),
				double.Parse(tail[1], CultureInfo.InvariantCulture),
				double.Parse(tail[2], CultureInfo.InvariantCulture)));

			pending = null;
		}

		return cases;
	}

	private static List<IReadOnlyList<Expected>> ReadExpected()
	{
		string[] lines = File.ReadAllLines(Path.Combine(DataDirectory, "sgp4-ver-expected.out"));
		List<IReadOnlyList<Expected>> blocks = [];
		List<Expected>? current = null;

		foreach (string line in lines)
		{
			string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

			if (fields.Length == 0)
			{
				continue;
			}

			if (fields.Length == 2 && fields[1] == "xx")
			{
				current = [];
				blocks.Add(current);
				continue;
			}

			if (current is null || fields.Length < 7)
			{
				continue;
			}

			current.Add(new Expected(
				double.Parse(fields[0], CultureInfo.InvariantCulture),
				double.Parse(fields[1], CultureInfo.InvariantCulture),
				double.Parse(fields[2], CultureInfo.InvariantCulture),
				double.Parse(fields[3], CultureInfo.InvariantCulture),
				double.Parse(fields[4], CultureInfo.InvariantCulture),
				double.Parse(fields[5], CultureInfo.InvariantCulture),
				double.Parse(fields[6], CultureInfo.InvariantCulture)));
		}

		return blocks;
	}
}
