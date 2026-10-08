// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using ktsu.Osculator.Data.Cddis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers how an ILRS orbit file is found in the CDDIS archive, and how the Earthdata token is read
/// from the credential store.
/// </summary>
/// <remarks>
/// The archive layout is checked against the one example CDDIS publishes,
/// <c>ilrsa.orb.lageos1.160220.v01.sp3.gz</c> — Saturday 20 February 2016 — rather than against
/// anything this code derived. The credential store is checked through a stand-in for the platform
/// tools, because a test run has no keyring; what is pinned is which tool is asked, with which
/// arguments, and that its output goes nowhere but into the token.
/// </remarks>
[TestClass]
public sealed class CddisNamingAndCredentialTests
{
	[TestMethod]
	public void AWeekIsNamedForTheSaturdayItEndsOn()
	{
		DateOnly published = new(2016, 2, 20);

		Assert.AreEqual(DayOfWeek.Saturday, published.DayOfWeek);
		Assert.AreEqual(published, IlrsOrbitFile.WeekEnding(published));
		Assert.AreEqual(published, IlrsOrbitFile.WeekEnding(new DateOnly(2016, 2, 14)), "Sunday opens the week.");
		Assert.AreEqual(published, IlrsOrbitFile.WeekEnding(new DateOnly(2016, 2, 19)));
		Assert.AreEqual(new DateOnly(2016, 2, 27), IlrsOrbitFile.WeekEnding(new DateOnly(2016, 2, 21)));
	}

	[TestMethod]
	public void TheDirectoryIsTheArchivesOwnLayout()
	{
		Uri directory = IlrsOrbitFile.DirectoryAddress("lageos1", new DateOnly(2016, 2, 20));

		Assert.AreEqual("https://cddis.nasa.gov/archive/slr/products/orbits/lageos1/160220/", directory.ToString());
		Assert.ThrowsExactly<ArgumentException>(() => IlrsOrbitFile.DirectoryAddress("lageos1", new DateOnly(2016, 2, 19)));
		Assert.ThrowsExactly<ArgumentException>(() => IlrsOrbitFile.DirectoryAddress("iss", new DateOnly(2016, 2, 20)));
	}

	[TestMethod]
	public void ThePublishedExampleNameParses()
	{
		IlrsOrbitFile file = IlrsOrbitFile.Parse("ilrsa.orb.lageos1.160220.v01.sp3.gz").Single();

		Assert.AreEqual("ilrsa", file.Centre);
		Assert.AreEqual("lageos1", file.Satellite);
		Assert.AreEqual(new DateOnly(2016, 2, 20), file.WeekEnd);
		Assert.AreEqual(1, file.Version);
		Assert.IsTrue(file.IsGzip);
		Assert.AreEqual(
			"https://cddis.nasa.gov/archive/slr/products/orbits/lageos1/160220/ilrsa.orb.lageos1.160220.v01.sp3.gz",
			file.Address.ToString());
	}

	[TestMethod]
	public void TheNewestReadableVersionForTheCentreIsChosen()
	{
		const string listing = """
			<a href="ilrsa.orb.lageos1.160220.v01.sp3.gz">ilrsa.orb.lageos1.160220.v01.sp3.gz</a>
			ilrsa.orb.lageos1.160220.v02.sp3.gz   400000
			ilrsa.orb.lageos1.160220.v03.sp3.Z    500000
			ilrsb.orb.lageos1.160220.v09.sp3.gz   400000
			ilrsa.orb.lageos2.160220.v07.sp3.gz   400000
			ilrsa.orb.lageos1.160213.v08.sp3.gz   400000
			""";

		IlrsOrbitFile? newest = IlrsOrbitFile.Latest(listing, "ilrsa", "lageos1", new DateOnly(2016, 2, 20));

		Assert.IsNotNull(newest);
		Assert.AreEqual("ilrsa.orb.lageos1.160220.v02.sp3.gz", newest.Name, "v03 is Unix compress; the others are another centre, satellite or week.");
		Assert.IsNull(IlrsOrbitFile.Latest(listing, "jcet", "lageos1", new DateOnly(2016, 2, 20)));
	}

	[TestMethod]
	public void LinuxAsksTheSecretServiceAndTrimsTheNewline()
	{
		Recorder tools = new(new(0, "the-token\n"));
		OsCredentialStore store = new(OSPlatform.Linux, tools.Run);

		EarthdataToken? token = store.GetToken();

		Assert.IsNotNull(token);
		Assert.AreEqual("the-token", token.Value);
		Assert.AreEqual("secret-tool lookup service osculator-earthdata", tools.Calls.Single());
	}

	[TestMethod]
	public void MacOsAsksTheKeychain()
	{
		Recorder tools = new(new(0, "the-token\n"));
		OsCredentialStore store = new(OSPlatform.OSX, tools.Run);

		Assert.AreEqual("the-token", store.GetToken()?.Value);
		Assert.AreEqual("security find-generic-password -s osculator-earthdata -w", tools.Calls.Single());
	}

	[TestMethod]
	public void NoStoredEntryIsNoToken()
	{
		Assert.IsNull(new OsCredentialStore(OSPlatform.Linux, new Recorder(new(1, string.Empty)).Run).GetToken());
		Assert.IsNull(new OsCredentialStore(OSPlatform.OSX, new Recorder(new(44, "noise")).Run).GetToken(), "A failed lookup's output is not a token.");
		Assert.IsNull(new OsCredentialStore(OSPlatform.Linux, new Recorder(new(0, "  \n")).Run).GetToken());
	}

	[TestMethod]
	public void AMissingToolIsNamedRatherThanReportedAsNoToken()
	{
		OsCredentialStore store = new(OSPlatform.Linux, (_, _) => throw new Win32Exception(2));

		InvalidOperationException missing = Assert.ThrowsExactly<InvalidOperationException>(store.GetToken);

		StringAssert.Contains(missing.Message, "secret-tool");
	}

	[TestMethod]
	public void ACommandsOutputIsNeverPrinted()
	{
		OsCredentialStore.CommandResult result = new(0, "the-token");

		Assert.IsFalse(result.ToString().Contains("the-token", StringComparison.Ordinal));
	}

	[TestMethod]
	public void ATokenIsOneWord()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new EarthdataToken("   "));
		Assert.ThrowsExactly<ArgumentException>(() => new EarthdataToken("two words"));
		Assert.ThrowsExactly<ArgumentException>(() => new EarthdataToken("a\r\nInjected: header"));
		Assert.AreEqual("EarthdataToken(redacted)", new EarthdataToken("abc").ToString());
	}

	private sealed class Recorder(OsCredentialStore.CommandResult result)
	{
		public List<string> Calls { get; } = [];

		public OsCredentialStore.CommandResult Run(string tool, string[] arguments)
		{
			Calls.Add(string.Join(' ', [tool, .. arguments]));
			return result;
		}
	}
}
