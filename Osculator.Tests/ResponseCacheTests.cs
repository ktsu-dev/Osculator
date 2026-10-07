// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.IO;
using ktsu.Osculator.Data.CelesTrak;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FakeClock = ktsu.Osculator.Tests.CelesTrakClientTests.FakeClock;

/// <summary>
/// Covers the cache's own rules, below either client: the window's floor, clocks that are wrong,
/// and writes that are interrupted.
/// </summary>
[TestClass]
public sealed class ResponseCacheTests
{
	private const string Key = "CATNR=25544&FORMAT=json";

	private string root = string.Empty;

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-cache-").FullName;

	[TestCleanup]
	public void TearDown() => Directory.Delete(root, recursive: true);

	[TestMethod]
	[DataRow(0)]
	[DataRow(-60)]
	[DataRow(1)]
	[DataRow(59)]
	public void AWindowShorterThanTheFloorIsRefused(int minutes)
	{
		// A zero window made every entry stale and every call a request: an opt-out from a policy
		// that is documented as having none.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new ResponseCache(root, TimeSpan.FromMinutes(minutes), TimeProvider.System));
	}

	[TestMethod]
	public void TheFloorItselfIsAllowed()
	{
		ResponseCache cache = new(root, ResponseCache.MinimumAllowedAge, TimeProvider.System);

		Assert.AreEqual(ResponseCache.MinimumAllowedAge, cache.MinimumAge);
	}

	[TestMethod]
	public void AnEntryStampedInTheFutureIsStaleRatherThanFreshForAsLongAsTheClockWasWrong()
	{
		// Written while the clock was a day ahead, read after it was corrected. Its age is minus a
		// day, which is never past any window, so it used to stay fresh for a day and four hours.
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		ResponseCache cache = new(root, TimeSpan.FromHours(4), clock);

		cache.Write(Key, "body");
		clock.Advance(TimeSpan.FromDays(-1));

		Assert.IsNull(cache.Read(Key));
		Assert.AreEqual("body", cache.ReadAtAnyAge(Key), "Still there for the offline path.");
	}

	[TestMethod]
	public void ASmallSkewIsOrdinaryDriftAndStillFresh()
	{
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		ResponseCache cache = new(root, TimeSpan.FromHours(4), clock);

		cache.Write(Key, "body");
		clock.Advance(-ResponseCache.MaximumClockSkew);

		Assert.AreEqual("body", cache.Read(Key));
	}

	[TestMethod]
	public void AReaderHoldingTheOldCopyStillReadsAllOfItAcrossAWrite()
	{
		// The property an atomic replace has and an in-place overwrite does not: the old file is
		// never truncated, so anything reading it — or a process killed mid-write — sees the whole
		// of the previous copy rather than a prefix of the next one.
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		ResponseCache cache = new(root, TimeSpan.FromHours(4), clock);
		string previous = new('a', 100_000);

		cache.Write(Key, previous);
		string bodyPath = Directory.GetFiles(root, "*.json")[0];

		using (FileStream reader = new(bodyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
		{
			cache.Write(Key, new string('b', 10));

			using StreamReader text = new(reader);
			Assert.AreEqual(previous, text.ReadToEnd());
		}

		Assert.AreEqual(new string('b', 10), cache.ReadAtAnyAge(Key));
	}

	[TestMethod]
	public void ALeftoverTemporaryFileFromAnInterruptedWriteIsIgnored()
	{
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		ResponseCache cache = new(root, TimeSpan.FromHours(4), clock);

		cache.Write(Key, "complete");
		string bodyPath = Directory.GetFiles(root, "*.json")[0];

		// What a write killed between creating its temporary file and moving it into place leaves.
		File.WriteAllText(bodyPath + ".0123456789abcdef0123456789abcdef.tmp", "trunc");

		Assert.AreEqual("complete", cache.ReadAtAnyAge(Key));
		Assert.AreEqual("complete", cache.Read(Key));
	}

	[TestMethod]
	public void NoWriteLeavesATemporaryFileBehind()
	{
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		ResponseCache cache = new(root, TimeSpan.FromHours(4), clock);

		cache.Write(Key, "one");
		cache.Write(Key, "two");

		Assert.IsEmpty(Directory.GetFiles(root, "*.tmp"));
		Assert.HasCount(2, Directory.GetFiles(root), "One body and one stamp, under their real names.");
	}

	[TestMethod]
	public void ABodyWhoseStampIsUnreadableIsServedOfflineButNeverFresh()
	{
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		ResponseCache cache = new(root, TimeSpan.FromHours(4), clock);

		cache.Write(Key, "body");
		File.WriteAllText(Directory.GetFiles(root, "*.fetched")[0], "garbage");

		Assert.IsNull(cache.Read(Key));
		Assert.AreEqual("body", cache.ReadAtAnyAge(Key));
	}
}
