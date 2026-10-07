// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System.Collections.Generic;
using ktsu.Osculator.Core.Elements;

/// <summary>
/// One object's archived history, together with the files in it that could not be read.
/// </summary>
/// <remarks>
/// The unreadable files are reported rather than thrown, because one of them is not a reason to lose
/// every other element set for the object. They are also reported rather than silently dropped,
/// because an archive that quietly holds fewer entries than it should is the harder fault to find.
/// </remarks>
/// <param name="ElementSets">Every readable element set, oldest epoch first.</param>
/// <param name="UnreadableFiles">
/// The full path of every archived file that did not parse, in ordinal order. Empty when the whole
/// history was readable.
/// </param>
public sealed record SnapshotHistory(
	IReadOnlyList<ElementSet> ElementSets,
	IReadOnlyList<string> UnreadableFiles);
