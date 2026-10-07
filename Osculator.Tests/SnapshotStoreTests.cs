// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using ktsu.Osculator.Data.CelesTrak;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers what the snapshot archive does when one of its own files is bad.
/// </summary>
/// <remarks>
/// The archive is what the divergence measurement reads, and it is append-only: nothing else ever
/// rewrites a snapshot. So a bad file used to be permanent — <see cref="SnapshotStore.History"/>
/// threw for the whole object, and <see cref="SnapshotStore.Add"/> saw the name was taken and
/// skipped it. Mutation-checked: letting the parse failure escape fails four of these tests, and
/// restoring the bare existence check in <c>Add</c> fails the repair test.
/// </remarks>
/// <remarks>
/// The atomic write itself is not mutation-checked. Telling it apart from an in-place write needs a
/// process to die between two system calls, which no test here can arrange without a seam the store
/// has no other use for. What is tested is the half that can be: a stray temporary file, which is
/// what an interrupted write now leaves, is never read as a snapshot, and no write leaves one behind.
/// </remarks>
[TestClass]
public sealed class SnapshotStoreTests
{
	private const string IssResponse = """
		[{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-09-22T20:26:37.026816",
		"MEAN_MOTION":15.49234213,"ECCENTRICITY":0.00047339,"INCLINATION":51.6316,
		"RA_OF_ASC_NODE":176.7315,"ARG_OF_PERICENTER":169.8211,"MEAN_ANOMALY":190.2874,
		"EPHEMERIS_TYPE":0,"CLASSIFICATION_TYPE":"U","NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,
		"REV_AT_EPOCH":58689,"BSTAR":0.00014639046,"MEAN_MOTION_DOT":7.689e-5,"MEAN_MOTION_DDOT":0}]
		""";

	private const string IssLaterResponse = """
		[{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-09-23T04:11:02.500000",
		"MEAN_MOTION":15.49236000,"ECCENTRICITY":0.00047400,"INCLINATION":51.6317,
		"RA_OF_ASC_NODE":172.1100,"ARG_OF_PERICENTER":171.0000,"MEAN_ANOMALY":189.0000,
		"NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,"REV_AT_EPOCH":58694,
		"BSTAR":0.00014700000,"MEAN_MOTION_DOT":7.700e-5,"MEAN_MOTION_DDOT":0}]
		""";

	private string root = string.Empty;

	private string ArchiveRoot => Path.Join(root, "archive");

	private string IssFolder => Path.Join(ArchiveRoot, "25544");

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-snapshots-").FullName;

	[TestCleanup]
	public void TearDown() => Directory.Delete(root, recursive: true);

	[TestMethod]
	public void ATruncatedFileIsSkippedAndNamedRatherThanCostingTheWholeHistory()
	{
		SnapshotStore store = new(ArchiveRoot);
		store.Add(IssResponse);
		store.Add(IssLaterResponse);
		string truncated = Truncate(OldestFile());

		SnapshotHistory history = store.ReadHistory(25544);

		Assert.HasCount(1, history.ElementSets, "The readable set survives the unreadable one.");
		Assert.AreEqual(15.49236000, history.ElementSets[0].MeanMotion, "It is the later set that survived.");
		Assert.HasCount(1, history.UnreadableFiles);
		Assert.AreEqual(truncated, history.UnreadableFiles[0], "The report names the file to look at.");
		Assert.HasCount(1, store.History(25544), "The plain accessor skips it too, rather than throwing.");
	}

	[TestMethod]
	public void AddingTheSameSetAgainRepairsATruncatedFile()
	{
		SnapshotStore store = new(ArchiveRoot);
		store.Add(IssResponse);
		Truncate(OldestFile());

		int added = store.Add(IssResponse);

		Assert.AreEqual(1, added, "A file that does not read is not an archived set.");
		SnapshotHistory history = store.ReadHistory(25544);
		Assert.HasCount(1, history.ElementSets);
		Assert.IsEmpty(history.UnreadableFiles);
		Assert.AreEqual(0, store.Add(IssResponse), "Once repaired, the same set is a duplicate again.");
	}

	[TestMethod]
	public void ARecordTheParserRejectsIsSkippedRatherThanThrown()
	{
		// The other way into the same state, with no crash at all: the archive keeps the source's own
		// text so a parser can be re-run over it, and a stricter parser can reject what an older one
		// accepted. Both of the parser's failure exceptions are covered — a record missing a field
		// (JsonException) and an epoch in a form it does not read (FormatException).
		SnapshotStore store = new(ArchiveRoot);
		store.Add(IssLaterResponse);
		File.WriteAllText(
			Path.Join(IssFolder, "20260101T000000.0000000.json"),
			IssResponse.Replace("\"EPOCH\":\"2026-09-22T20:26:37.026816\",", string.Empty, StringComparison.Ordinal));
		File.WriteAllText(
			Path.Join(IssFolder, "20260102T000000.0000000.json"),
			IssResponse.Replace("2026-09-22T20:26:37.026816", "22 September 2026", StringComparison.Ordinal));

		SnapshotHistory history = store.ReadHistory(25544);

		Assert.HasCount(1, history.ElementSets);
		Assert.HasCount(2, history.UnreadableFiles);
	}

	[TestMethod]
	public void AStrayTemporaryFileIsNotPartOfTheHistory()
	{
		// What an interrupted write now leaves behind: a temporary file beside the snapshot rather
		// than a truncated snapshot under its real name. It must not be read as one.
		SnapshotStore store = new(ArchiveRoot);
		store.Add(IssResponse);
		File.WriteAllText(Path.Join(IssFolder, "20260923T041102.5000000.json.0123456789abcdef.tmp"), "[{\"OBJE");

		SnapshotHistory history = store.ReadHistory(25544);

		Assert.HasCount(1, history.ElementSets);
		Assert.IsEmpty(history.UnreadableFiles);
	}

	[TestMethod]
	public void AddLeavesOnlyTheSnapshotsBehind()
	{
		SnapshotStore store = new(ArchiveRoot);
		store.Add(IssResponse);
		store.Add(IssLaterResponse);
		Truncate(OldestFile());
		store.Add(IssResponse);

		string[] files = Directory.GetFiles(IssFolder);

		Assert.HasCount(2, files, "No temporary file outlives a write, including a repair.");
		foreach (string file in files)
		{
			Assert.EndsWith(".json", file);
		}
	}

	[TestMethod]
	public void AnObjectNeverSeenHasAnEmptyHistoryAndNothingUnreadable()
	{
		SnapshotHistory history = new SnapshotStore(ArchiveRoot).ReadHistory(25544);

		Assert.IsEmpty(history.ElementSets);
		Assert.IsEmpty(history.UnreadableFiles);
	}

	/// <summary>Gets the archived file with the earliest epoch, which the names sort by.</summary>
	/// <returns>Its path.</returns>
	private string OldestFile()
	{
		List<string> files = [.. Directory.GetFiles(IssFolder, "*.json")];
		files.Sort(StringComparer.Ordinal);
		return files[0];
	}

	/// <summary>Cuts a file to half its length, as an interrupted in-place write would.</summary>
	/// <param name="path">The file.</param>
	/// <returns>The same path.</returns>
	private static string Truncate(string path)
	{
		string text = File.ReadAllText(path);
		File.WriteAllText(path, text[..(text.Length / 2)]);
		return path;
	}
}
