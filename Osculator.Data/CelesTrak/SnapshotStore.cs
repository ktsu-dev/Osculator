// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
	/// <returns>How many sets were new.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="ommJson"/> is null.</exception>
	public int Add(string ommJson)
	{
		Ensure.NotNull(ommJson);

		int added = 0;

		foreach ((ElementSet elements, string record) in OmmJson.ReadWithSource(ommJson))
		{
			string path = PathFor(elements.NoradCatalogId, elements.Epoch);

			if (File.Exists(path))
			{
				continue;
			}

			System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, record);
			added++;
		}

		return added;
	}

	/// <summary>
	/// Reads every archived element set for one object, oldest epoch first.
	/// </summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <returns>The history, which is empty when the object has never been seen.</returns>
	public IReadOnlyList<ElementSet> History(int noradCatalogId)
	{
		string folder = FolderFor(noradCatalogId);

		if (!System.IO.Directory.Exists(folder))
		{
			return [];
		}

		List<ElementSet> history = [];

		foreach (string path in System.IO.Directory.EnumerateFiles(folder, "*.json"))
		{
			history.AddRange(OmmJson.Read(File.ReadAllText(path)));
		}

		history.Sort((left, right) => left.Epoch.CompareTo(right.Epoch));

		return history;
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
