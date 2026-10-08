// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.IO;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Slr;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The CPF reader, against a real ILRS prediction for LAGEOS-2. See <c>Data/Slr/ATTRIBUTION.md</c>.
/// </summary>
[TestClass]
public sealed class CpfFileTests
{
	internal static string Lageos2 => File.ReadAllText(Path.Join(VerificationSet.DataDirectory, "Slr", "lageos2_cpf_160213_5441.sgf"));

	[TestMethod]
	public void TheHeaderAndEveryPositionAreRead()
	{
		CpfFile file = CpfFile.Parse(Lageos2);

		Assert.AreEqual(1, file.Version);
		Assert.AreEqual(22195, file.NoradCatalogId);
		Assert.HasCount(288, file.Records);
		Assert.AreEqual(JulianDate.FromCalendar(2016, 2, 13, 0, 0, 0.0), file.Records[0].Instant);
		Assert.AreEqual(JulianDate.FromCalendar(2016, 2, 13, 23, 55, 0.0), file.Records[^1].Instant);
		Assert.AreEqual(300.0, file.Records[1].SecondsSinceStart);
		Assert.AreEqual(86100.0, file.Records[^1].SecondsSinceStart);
	}

	[TestMethod]
	public void PositionsAreConvertedFromMetresToKilometres()
	{
		CpfRecord first = CpfFile.Parse(Lageos2).Records[0];

		Assert.AreEqual(7049.498186, first.X, 1e-12);
		Assert.AreEqual(5346.456274, first.Y, 1e-12);
		Assert.AreEqual(8307.028039, first.Z, 1e-12);
	}

	[TestMethod]
	public void AFileWithNoEndRecordIsRefusedAsTruncated()
	{
		string truncated = Lageos2[..Lageos2.LastIndexOf("99", StringComparison.Ordinal)];

		FormatException refused = Assert.ThrowsExactly<FormatException>(() => CpfFile.Parse(truncated));

		StringAssert.Contains(refused.Message, "truncated");
	}

	[TestMethod]
	public void PositionsOutOfTimeOrderAreRefused()
	{
		string[] lines = Lageos2.Split('\n');
		(lines[4], lines[5]) = (lines[5], lines[4]);

		FormatException refused = Assert.ThrowsExactly<FormatException>(() => CpfFile.Parse(string.Join('\n', lines)));

		StringAssert.Contains(refused.Message, "out of time order");
	}

	[TestMethod]
	public void AnotherFormatIsRefusedByName()
	{
		FormatException refused = Assert.ThrowsExactly<FormatException>(() => CpfFile.Parse(Sp3ParserTests.Lageos));

		StringAssert.Contains(refused.Message, "not a CPF file");
	}

	[TestMethod]
	public void AnUnsupportedVersionIsRefused()
	{
		string version3 = Lageos2.Replace("H1 CPF  1", "H1 CPF  3", StringComparison.Ordinal);

		FormatException refused = Assert.ThrowsExactly<FormatException>(() => CpfFile.Parse(version3));

		StringAssert.Contains(refused.Message, "version 3");
	}
}
