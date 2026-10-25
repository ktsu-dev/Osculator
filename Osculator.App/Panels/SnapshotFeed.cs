// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using ktsu.Osculator.Data.CelesTrak;

/// <summary>
/// Copies every element set the application fetches from CelesTrak into a <see cref="SnapshotStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// CelesTrak serves only the current element set, and the response cache keeps only the latest
/// response per query, so an element set that is not archived before the next refetch is gone. The
/// decomposition needs two sets for one object, the earlier one to predict from and the later one to
/// take as truth, and this is where the earlier ones are kept.
/// </para>
/// <para>
/// It reads the response cache's directory rather than sitting inside the clients, so every panel that
/// fetches through a <see cref="CelesTrakClient"/> on that directory feeds the archive without knowing
/// about it, and nothing reaches the network on its account. A cached body is offered to the store each
/// time its file changes; the store keeps one copy of each element set, so offering the same response
/// twice costs a read and adds nothing.
/// </para>
/// <para>
/// The cache's refetch window is at least <see cref="ResponseCache.MinimumAllowedAge"/>, so polling it
/// every <see cref="PollInterval"/> sees every response the cache ever holds.
/// </para>
/// </remarks>
/// <param name="cacheDirectory">The CelesTrak response cache's directory.</param>
/// <param name="store">The archive to add to.</param>
internal sealed class SnapshotFeed(string cacheDirectory, SnapshotStore store)
{
	/// <summary>How often the application's feed looks at the cache.</summary>
	internal static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

	/// <summary>The suffix the response cache writes bodies with. Stamps and temporary files have others.</summary>
	private const string BodyPattern = "*.json";

	private static readonly Lazy<SnapshotFeed> ApplicationFeed = new(CreateApplicationFeed, LazyThreadSafetyMode.ExecutionAndPublication);

	private static Timer? timer;

	private readonly Dictionary<string, DateTime> seen = new(StringComparer.Ordinal);

	private readonly Lock gate = new();

	/// <summary>Gets the feed the application runs, over its own cache and archive directories.</summary>
	internal static SnapshotFeed Application => ApplicationFeed.Value;

	/// <summary>Gets the directory the application archives element sets in.</summary>
	internal static string ApplicationSnapshotDirectory => Path.Join(ApplicationDataDirectory, "snapshots");

	/// <summary>Gets the CelesTrak cache directory the application's clients share.</summary>
	internal static string ApplicationCacheDirectory => Path.Join(ApplicationDataDirectory, "celestrak");

	/// <summary>Gets the archive this feed adds to.</summary>
	internal SnapshotStore Store { get; } = store;

	/// <summary>Gets the cache directory this feed reads.</summary>
	internal string CacheDirectory { get; } = cacheDirectory;

	/// <summary>Gets how many changed cache files the last sweep could not read as element sets.</summary>
	internal int UnreadableResponses { get; private set; }

	private static string ApplicationDataDirectory => Path.Join(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Osculator");

	/// <summary>
	/// Starts the application's feed polling in the background, once per process.
	/// </summary>
	/// <remarks>
	/// Called from the decomposition panel's constructor, which the shell runs at start-up whether or
	/// not the panel is ever opened, so the archive grows from the first fetch.
	/// </remarks>
	internal static void StartApplicationFeed()
	{
		if (timer is not null)
		{
			return;
		}

		Timer started = new(_ => Application.TrySweep(), null, TimeSpan.Zero, PollInterval);

		if (Interlocked.CompareExchange(ref timer, started, null) is not null)
		{
			started.Dispose();
		}
	}

	/// <summary>
	/// Offers every cached response that changed since the last sweep to the archive.
	/// </summary>
	/// <returns>How many element sets were new to the archive.</returns>
	/// <remarks>
	/// A cached body that is not OMM JSON (another client's response, or one damaged on disk) is
	/// skipped and counted in <see cref="UnreadableResponses"/>, rather than stopping the sweep.
	/// </remarks>
	internal int Sweep()
	{
		lock (gate)
		{
			if (!Directory.Exists(CacheDirectory))
			{
				return 0;
			}

			int added = 0;
			int unreadable = 0;

			foreach (string path in Directory.EnumerateFiles(CacheDirectory, BodyPattern))
			{
				DateTime written = File.GetLastWriteTimeUtc(path);

				if (seen.TryGetValue(path, out DateTime last) && last == written)
				{
					continue;
				}

				try
				{
					added += Store.Add(File.ReadAllText(path));
					seen[path] = written;
				}
				catch (Exception exception) when (exception is JsonException or FormatException)
				{
					seen[path] = written;
					unreadable++;
				}
			}

			UnreadableResponses = unreadable;
			return added;
		}
	}

	/// <summary>Sweeps from the timer, where a disk fault must not take the process down.</summary>
	private void TrySweep()
	{
		try
		{
			_ = Sweep();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// The cache is being rewritten or is not readable right now; the next poll tries again.
		}
	}

	/// <summary>Creates the application's feed.</summary>
	/// <returns>The feed.</returns>
	private static SnapshotFeed CreateApplicationFeed() =>
		new(ApplicationCacheDirectory, new SnapshotStore(ApplicationSnapshotDirectory));
}
