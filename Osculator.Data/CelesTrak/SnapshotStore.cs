// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ktsu.Osculator.Core.Elements;

/// <summary>
/// An append-only archive of every distinct element set this application has seen.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes divergence measurable at all. The measurement is "propagate an old element
/// set forward and compare against a later one for the same object", and CelesTrak serves the
/// <em>current</em> elements rather than past ones — so the history has to be accumulated here, by
/// keeping every distinct set as it goes by.
/// </para>
/// <para>
/// <strong>It archives the source's own JSON, not a parsed element set.</strong> That is the
/// difference between an archive and a cache of this repository's reading: if the parser changes,
/// or turns out to have been wrong, what the source actually served is still here to re-read. It
/// also means a snapshot from CelesTrak and one seeded from Space-Track sit side by side without
/// the store needing to know which is which.
/// </para>
/// <para>
/// Keyed by catalogue number and epoch, because that pair is what identifies an element set. The
/// same epoch seen twice is the same set — element sets are reissued unchanged far more often than
/// they are updated — so a second sighting is dropped rather than stored again.
/// </para>
/// <para>
/// <strong>One bad file never costs an object its history.</strong> Every file is written beside its
/// final name and moved into place, so an interrupted write leaves a stray temporary file rather
/// than a truncated snapshot under a real name. A file that does not parse anyway — written before
/// that was true, damaged on disk, or rejected by a parser that has since become stricter — is
/// skipped by <see cref="ReadHistory"/> and named in its result, and a later <see cref="Add"/> of
/// the same element set replaces it instead of treating it as already archived.
/// </para>
/// </remarks>
/// <param name="directory">The directory to hold the archive in.</param>
public sealed class SnapshotStore(string directory)
{
	/// <summary>Gets the directory the archive is held in.</summary>
	public string Directory { get; } = directory;

	/// <summary>
	/// Adds every element set in an OMM document that is not already archived.
	/// </summary>
	/// <param name="ommJson">The document, as the source served it.</param>
	/// <returns>
	/// How many sets were new. A set whose archived file existed but could not be read counts as new,
	/// because until now the archive did not hold it in any usable form.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="ommJson"/> is null.</exception>
	public int Add(string ommJson)
	{
		Ensure.NotNull(ommJson);

		int added = 0;

		foreach ((ElementSet elements, string record) in OmmJson.ReadWithSource(ommJson))
		{
			string path = PathFor(elements.NoradCatalogId, elements.Epoch);

			if (File.Exists(path) && TryRead(path, out _))
			{
				continue;
			}

			System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			WriteAtomically(path, record);
			added++;
		}

		return added;
	}

	/// <summary>
	/// Reads every readable archived element set for one object, oldest epoch first.
	/// </summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <returns>The history, which is empty when the object has never been seen.</returns>
	/// <remarks>
	/// A file that does not parse is skipped rather than thrown for. <see cref="ReadHistory"/> says
	/// which ones were.
	/// </remarks>
	public IReadOnlyList<ElementSet> History(int noradCatalogId) => ReadHistory(noradCatalogId).ElementSets;

	/// <summary>
	/// Reads every archived element set for one object, and names every archived file that could not
	/// be read.
	/// </summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <returns>
	/// The readable history, oldest epoch first, and the unreadable files. Both are empty when the
	/// object has never been seen.
	/// </returns>
	public SnapshotHistory ReadHistory(int noradCatalogId)
	{
		string folder = FolderFor(noradCatalogId);

		if (!System.IO.Directory.Exists(folder))
		{
			return new([], []);
		}

		List<ElementSet> history = [];
		List<string> unreadable = [];

		foreach (string path in System.IO.Directory.EnumerateFiles(folder, "*.json"))
		{
			if (TryRead(path, out IReadOnlyList<ElementSet> sets))
			{
				history.AddRange(sets);
			}
			else
			{
				unreadable.Add(path);
			}
		}

		history.Sort((left, right) => left.Epoch.CompareTo(right.Epoch));
		unreadable.Sort(StringComparer.Ordinal);

		return new(history, unreadable);
	}

	/// <summary>Gets every catalogue number the archive holds at least one set for.</summary>
	/// <returns>The catalogue numbers, ascending.</returns>
	public IReadOnlyList<int> Objects()
	{
		if (!System.IO.Directory.Exists(Directory))
		{
			return [];
		}

		return [.. System.IO.Directory
			.EnumerateDirectories(Directory)
			.Select(Path.GetFileName)
			.Where(name => int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
			.Select(name => int.Parse(name!, NumberStyles.Integer, CultureInfo.InvariantCulture))
			.OrderBy(id => id)];
	}

	/// <summary>Reads one archived file, reporting rather than throwing when it does not parse.</summary>
	/// <param name="path">The file.</param>
	/// <param name="sets">The element sets it holds, or empty when it does not parse.</param>
	/// <returns>Whether the file parsed.</returns>
	/// <remarks>
	/// A parse failure surfaces as <see cref="JsonException"/> for malformed or truncated JSON and for
	/// a record the parser rejects, and as <see cref="FormatException"/> for an epoch it does not
	/// recognise. Anything else — an I/O fault, a permissions problem — is not a property of what the
	/// file says, and still propagates.
	/// </remarks>
	private static bool TryRead(string path, out IReadOnlyList<ElementSet> sets)
	{
		try
		{
			sets = OmmJson.Read(File.ReadAllText(path));
			return true;
		}
		catch (Exception exception) when (exception is JsonException or FormatException)
		{
			sets = [];
			return false;
		}
	}

	/// <summary>
	/// Writes a snapshot so that no reader can ever see part of it under its real name.
	/// </summary>
	/// <param name="path">The snapshot's final path.</param>
	/// <param name="contents">What to write.</param>
	/// <remarks>
	/// The text goes to a uniquely named temporary file in the same folder and is then moved over the
	/// final name. A move within one folder is a rename, which the file system performs whole, so a
	/// crash leaves the old file, the new one, or a stray temporary file — never a truncated snapshot.
	/// The temporary name does not end in <c>.json</c>, so a stray one is invisible to
	/// <see cref="ReadHistory"/>.
	/// </remarks>
	private static void WriteAtomically(string path, string contents)
	{
		string temporary = FormattableString.Invariant($"{path}.{Guid.NewGuid():N}.tmp");

		try
		{
			File.WriteAllText(temporary, contents);
			File.Move(temporary, path, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporary))
			{
				File.Delete(temporary);
			}
		}
	}

	/// <summary>The folder one object's history lives in.</summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <returns>The folder path.</returns>
	private string FolderFor(int noradCatalogId) =>
		Path.Join(Directory, noradCatalogId.ToString(CultureInfo.InvariantCulture));

	/// <summary>The file one element set is archived at.</summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <param name="epoch">The element set's epoch.</param>
	/// <returns>The file path.</returns>
	/// <remarks>
	/// The epoch goes into the name at hundred-nanosecond resolution, sortable and with nothing a
	/// file system objects to. Two sets with the same epoch are the same set.
	/// </remarks>
	private string PathFor(int noradCatalogId, DateTime epoch) => Path.Join(
		FolderFor(noradCatalogId),
		FormattableString.Invariant($"{epoch.ToUniversalTime():yyyyMMdd'T'HHmmss'.'fffffff}.json"));
}
