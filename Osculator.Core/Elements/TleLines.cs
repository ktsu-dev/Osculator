// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

/// <summary>
/// The two 69-column lines of a two-line element set, as <see cref="TleWriter"/> writes them.
/// </summary>
/// <param name="Line1">The first line, beginning with <c>1</c>.</param>
/// <param name="Line2">The second line, beginning with <c>2</c>.</param>
public readonly record struct TleLines(string Line1, string Line2);
