// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ktsu.Osculator.Core.Elements;

/// <summary>
/// The published SGP4 verification set, read from the files committed beside it.
/// </summary>
/// <remarks>
/// Shared because more than one thing is measured against it: the model's agreement with the
/// published vectors, and the agreement of the storage types with each other. Reading it twice from
/// two copies of the same parser would let those two measurements drift apart over the same data.
/// </remarks>
internal static class VerificationSet
{
	/// <summary>One verification case: an element set and the time span to propagate it over.</summary>
	/// <param name="Elements">The element set.</param>
	/// <param name="StartMinutes">Minutes since epoch to start at.</param>
	/// <param name="StopMinutes">Minutes since epoch to stop at.</param>
	/// <param name="StepMinutes">Step in minutes.</param>
	internal sealed record Case(ElementSet Elements, double StartMinutes, double StopMinutes, double StepMinutes);

	/// <summary>One expected state from the published output.</summary>
	/// <param name="Minutes">Minutes since epoch.</param>
	/// <param name="X">TEME x, in kilometres.</param>
	/// <param name="Y">TEME y, in kilometres.</param>
	/// <param name="Z">TEME z, in kilometres.</param>
	/// <param name="VelocityX">TEME x velocity, in kilometres per second.</param>
	/// <param name="VelocityY">TEME y velocity, in kilometres per second.</param>
	/// <param name="VelocityZ">TEME z velocity, in kilometres per second.</param>
	internal sealed record Expected(double Minutes, double X, double Y, double Z, double VelocityX, double VelocityY, double VelocityZ);

	/// <summary>Gets the directory the committed verification vectors are copied to.</summary>
	/// <remarks>
	/// <see cref="Path.Join(string, string)"/> rather than <see cref="Path.Combine(string, string)"/>
	/// throughout: <c>Combine</c> discards everything before a rooted later segment, and while every
	/// segment here is a string literal that cannot be rooted, <c>Join</c> is the right default when
	/// the later segment is known to be relative and costs nothing.
	/// </remarks>
	internal static string DataDirectory => Path.Join(AppContext.BaseDirectory, "Data");

	/// <summary>Reads every case in the verification file, in file order.</summary>
	/// <returns>The cases.</returns>
	internal static List<Case> ReadCases()
	{
		string[] lines = File.ReadAllLines(Path.Join(DataDirectory, "SGP4-VER.TLE"));
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

	/// <summary>Reads the expected states, one block per case, in the same order.</summary>
	/// <returns>The blocks.</returns>
	internal static List<IReadOnlyList<Expected>> ReadExpected()
	{
		string[] lines = File.ReadAllLines(Path.Join(DataDirectory, "sgp4-ver-expected.out"));
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
