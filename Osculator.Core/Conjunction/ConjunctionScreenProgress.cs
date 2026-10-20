// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Conjunction;

/// <summary>
/// How far a conjunction screen has got.
/// </summary>
/// <param name="PairsScanned">The pairs scanned so far.</param>
/// <param name="PairsToScan">The pairs that survived the shell pre-filter, which is all the scan will take.</param>
public readonly record struct ConjunctionScreenProgress(long PairsScanned, long PairsToScan);
