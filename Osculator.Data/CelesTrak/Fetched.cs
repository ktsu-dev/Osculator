// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System;

/// <summary>
/// A value obtained through <see cref="ResponseCache"/>, with where it came from.
/// </summary>
/// <typeparam name="T">The value's type.</typeparam>
/// <param name="Value">The value.</param>
/// <param name="FetchedAt">When the body behind it was fetched from the source, in UTC.</param>
/// <param name="IsStale">
/// Whether the source could not be asked or could not answer and this is the last good copy
/// instead, however old. A fresh fetch and an entry inside the refetch window are both not stale.
/// </param>
/// <remarks>
/// The age of an element set is one of the terms this application decomposes, so a week-old set
/// served because the network was down must not look like a current one. A caller that shows the
/// value is expected to say so when <paramref name="IsStale"/> is set.
/// </remarks>
public readonly record struct Fetched<T>(T Value, DateTimeOffset FetchedAt, bool IsStale)
{
	/// <summary>Transforms the value and keeps its provenance.</summary>
	/// <typeparam name="TResult">The transformed value's type.</typeparam>
	/// <param name="transform">The transformation.</param>
	/// <returns>The transformed value, with the same fetch time and staleness.</returns>
	public Fetched<TResult> Map<TResult>(Func<T, TResult> transform)
	{
		Ensure.NotNull(transform);

		return new Fetched<TResult>(transform(Value), FetchedAt, IsStale);
	}
}
