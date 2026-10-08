// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// A disk cache that will not let a response be refetched before a minimum age has passed.
/// </summary>
/// <remarks>
/// <para>
/// CelesTrak's usage guidelines ask consumers to cache and to refetch infrequently. This enforces
/// that rather than documenting it, because a showcase that hammers a free service run by one
/// person is a bad advertisement for everything else in it. The client cannot opt out: the only
/// path to the network goes through <see cref="FetchAsync"/>, and the window cannot be set below
/// <see cref="MinimumAllowedAge"/>.
/// </para>
/// <para>
/// The policy has to live entirely on this side, because there is nothing on the other side to
/// negotiate with. Measured against the live service, <c>gp.php</c> returns no <c>Last-Modified</c>,
/// no <c>ETag</c> and no <c>Cache-Control</c>, so a conditional request is not available and there
/// is no way to ask cheaply whether anything changed. A timer is the whole of what can be done.
/// </para>
/// <para>
/// "Enforced" has to survive more than a healthy service and one caller, so <see cref="FetchAsync"/>
/// also holds it under the other conditions that used to defeat it:
/// </para>
/// <list type="bullet">
/// <item>Concurrent callers for one key share one request rather than each sending their own.</item>
/// <item>A failed request is throttled too: a blocked or erroring server is not asked again on
/// every call. See <see cref="FailureBackoff"/>.</item>
/// <item>A stamp from the future — a clock that was ahead when it was written — is stale rather
/// than fresh until the clock catches up. See <see cref="MaximumClockSkew"/>.</item>
/// <item>A cache that cannot be written does not fail a fetch that succeeded, and does not become
/// an unthrottled refetch loop: the response is kept in memory for the life of this instance.</item>
/// <item>Writes are atomic, so an interrupted one never leaves a truncated body under the real
/// name for the offline path to choke on.</item>
/// </list>
/// <para>
/// Stale entries are kept rather than evicted. An element set from last week still propagates, and
/// an application that cannot start without a network is worse than one that starts with a warning
/// — so the offline path serves them, and marks them <see cref="Fetched{T}.IsStale"/> so the
/// caller can say so.
/// </para>
/// <para>
/// The in-flight and in-memory state is per instance. Share one instance between every client of
/// a cache directory, as <see cref="CelesTrakClient"/> and the IERS client are designed to.
/// </para>
/// </remarks>
public sealed class ResponseCache
{
	/// <summary>
	/// The shortest refetch window a cache may be given. Space-Track asks that its <c>gp</c> class
	/// be queried no more than hourly, and CelesTrak recomputes element sets about every two hours,
	/// so no source this cache fronts gains anything from being asked more often than this.
	/// </summary>
	public static readonly TimeSpan MinimumAllowedAge = TimeSpan.FromHours(1);

	/// <summary>
	/// How far in the future a stamp may be before it is treated as wrong rather than as fresh.
	/// </summary>
	public static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);

	/// <summary>
	/// How long after a failed request, with nothing cached at all, before the source may be asked
	/// again. With something cached the back-off is the whole <see cref="MinimumAge"/> instead:
	/// the caller has something to show, so there is no hurry.
	/// </summary>
	public static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(15);

	/// <summary>The suffix every cached body is written with.</summary>
	private const string BodySuffix = ".json";

	/// <summary>The suffix every cached body's fetch time is written with.</summary>
	private const string StampSuffix = ".fetched";

	/// <summary>The suffix the time of the last failed attempt is written with.</summary>
	private const string AttemptSuffix = ".attempted";

	private readonly TimeProvider time;

	/// <summary>Successful responses that could not be written to disk.</summary>
	private readonly ConcurrentDictionary<string, Entry> unpersisted = new(StringComparer.Ordinal);

	/// <summary>When each key last failed, and with what.</summary>
	private readonly ConcurrentDictionary<string, Attempt> attempts = new(StringComparer.Ordinal);

	/// <summary>The one request per key currently on the wire.</summary>
	private readonly ConcurrentDictionary<string, Lazy<Task<Fetched<string>>>> inFlight = new(StringComparer.Ordinal);

	/// <summary>Creates a cache.</summary>
	/// <param name="directory">The directory to hold cached responses in.</param>
	/// <param name="minimumAge">How old an entry must be before the network may be asked again.</param>
	/// <param name="time">The clock, so the freshness window can be tested without waiting on it.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="minimumAge"/> is below <see cref="MinimumAllowedAge"/>.
	/// </exception>
	public ResponseCache(string directory, TimeSpan minimumAge, TimeProvider time)
	{
		Ensure.NotNull(directory);
		Ensure.NotNull(time);

		// A zero or negative window would make every entry stale and every call a request, which
		// is the opt-out the remarks promise does not exist.
		if (minimumAge < MinimumAllowedAge)
		{
			throw new ArgumentOutOfRangeException(
				nameof(minimumAge),
				minimumAge,
				FormattableString.Invariant($"The refetch window may not be shorter than {MinimumAllowedAge}."));
		}

		Directory = directory;
		MinimumAge = minimumAge;
		this.time = time;
	}

	/// <summary>Gets the directory cached responses are held in.</summary>
	public string Directory { get; }

	/// <summary>Gets how old an entry must be before the network may be asked again.</summary>
	public TimeSpan MinimumAge { get; }

	/// <summary>
	/// Gets the most recent failure to write a response to disk, or null when every write so far
	/// succeeded. A failed write does not fail the fetch; this is where it is reported instead.
	/// </summary>
	public Exception? LastWriteFailure { get; private set; }

	/// <summary>
	/// Returns the cached body when it is fresh enough, and otherwise asks the source — at most
	/// once at a time per key, and not again soon after a failure.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <param name="download">
	/// Asks the source. It is given the body currently cached at any age, or null, so it can refuse
	/// a response that is worse than what is already held. It returns the body to cache, already
	/// validated, or throws.
	/// </param>
	/// <param name="isUnavailable">
	/// Whether an exception from <paramref name="download"/> means the source could not answer —
	/// so the stale copy is served and the attempt is throttled. Any other exception reaches the
	/// caller unchanged, and is neither cached nor throttled.
	/// </param>
	/// <param name="cancellationToken">
	/// Cancels this caller's wait. It does not cancel a request other callers are sharing.
	/// </param>
	/// <returns>The body, with when it was fetched and whether it is a stale fallback.</returns>
	/// <exception cref="OperationCanceledException">The caller cancelled.</exception>
	/// <exception cref="ResponseUnavailableException">
	/// The source could not answer, or was not asked because it failed recently, and nothing is
	/// cached at any age.
	/// </exception>
	public async Task<Fetched<string>> FetchAsync(
		string key,
		Func<string?, CancellationToken, Task<string>> download,
		Func<Exception, bool> isUnavailable,
		CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(key);
		Ensure.NotNull(download);
		Ensure.NotNull(isUnavailable);

		// The caller's own cancellation is never answered from the cache: data handed back from a
		// call the caller abandoned would read as a success.
		cancellationToken.ThrowIfCancellationRequested();

		Entry? current = Current(key);

		if (current is not null && IsFresh(current.FetchedAt))
		{
			return new Fetched<string>(current.Body, current.FetchedAt, IsStale: false);
		}

		return await Join(key, download, isUnavailable).WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Reads a cached response, but only if it is younger than <see cref="MinimumAge"/>.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <returns>The body, or <see langword="null"/> when there is nothing fresh enough.</returns>
	public string? Read(string key)
	{
		Entry? current = Current(key);

		return current is not null && IsFresh(current.FetchedAt) ? current.Body : null;
	}

	/// <summary>
	/// Reads a cached response whatever its age, for the path where the network is unavailable.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <returns>The body, or <see langword="null"/> when the key has never been cached.</returns>
	public string? ReadAtAnyAge(string key) => Current(key)?.Body;

	/// <summary>Gets when a key was last fetched, or null when it never was.</summary>
	/// <param name="key">The cache key.</param>
	/// <returns>The fetch time, in UTC.</returns>
	public DateTimeOffset? FetchedAt(string key) =>
		unpersisted.TryGetValue(key, out Entry? held) ? held.FetchedAt : ReadStamp(PathFor(key, StampSuffix));

	/// <summary>Stores a response and stamps it with the current time.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="body">The response body.</param>
	/// <remarks>
	/// Each file is written beside its real name and moved over it, so an interrupted write leaves
	/// the previous complete copy in place rather than a truncated one. The body goes first: a
	/// failure between the two leaves a new body under an old stamp, which only makes it look
	/// older than it is.
	/// </remarks>
	/// <exception cref="IOException">The cache directory could not be written.</exception>
	/// <exception cref="UnauthorizedAccessException">The cache directory could not be written.</exception>
	public void Write(string key, string body) => WriteAt(key, body, time.GetUtcNow());

	/// <summary>Writes a key's body and stamp atomically.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="body">The response body.</param>
	/// <param name="now">The stamp.</param>
	private void WriteAt(string key, string body, DateTimeOffset now)
	{
		Ensure.NotNull(body);

		System.IO.Directory.CreateDirectory(Directory);
		WriteAtomically(PathFor(key, BodySuffix), body);
		WriteAtomically(PathFor(key, StampSuffix), Format(now));

		_ = unpersisted.TryRemove(key, out _);
	}

	/// <summary>Writes a file beside its real name and moves it into place.</summary>
	/// <param name="path">The real name.</param>
	/// <param name="contents">The contents.</param>
	private static void WriteAtomically(string path, string contents)
	{
		// Unique, so two writers of one key never share a temporary file.
		string temporary = FormattableString.Invariant($"{path}.{Guid.NewGuid():N}.tmp");

		bool moved = false;

		try
		{
			File.WriteAllText(temporary, contents);
			File.Move(temporary, path, overwrite: true);
			moved = true;
		}
		finally
		{
			if (!moved)
			{
				TryDelete(temporary);
			}
		}
	}

	/// <summary>Deletes a file, ignoring a failure to.</summary>
	/// <param name="path">The file.</param>
	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			// A stray temporary file is harmless: nothing reads a name ending .tmp.
		}
	}

	/// <summary>Formats a stamp.</summary>
	/// <param name="stamp">The time.</param>
	/// <returns>Its round-trip text.</returns>
	private static string Format(DateTimeOffset stamp) => stamp.ToString("O", CultureInfo.InvariantCulture);

	/// <summary>Reads a stamp file.</summary>
	/// <param name="path">The file.</param>
	/// <returns>The time, or null when there is none or it does not parse.</returns>
	private static DateTimeOffset? ReadStamp(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}

		return DateTimeOffset.TryParse(
			File.ReadAllText(path),
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out DateTimeOffset parsed)
			? parsed
			: null;
	}

	/// <summary>Gets the age of a stamp, or null when the stamp is from the future.</summary>
	/// <param name="stamp">The stamp.</param>
	/// <returns>The age.</returns>
	/// <remarks>
	/// A stamp ahead of the clock means the clock was ahead when it was written and has since been
	/// corrected. Taken at face value its age is negative, which is never past any window, so the
	/// entry would be fresh for as long as the clock had been wrong. Within
	/// <see cref="MaximumClockSkew"/> it is ordinary drift between machines and counts as new.
	/// </remarks>
	private TimeSpan? AgeOf(DateTimeOffset stamp)
	{
		TimeSpan age = time.GetUtcNow() - stamp;

		return age < -MaximumClockSkew ? null : age;
	}

	/// <summary>Whether something fetched at a time is still inside the window.</summary>
	/// <param name="fetchedAt">When it was fetched.</param>
	/// <returns>True when the source should not be asked again yet.</returns>
	private bool IsFresh(DateTimeOffset fetchedAt) => AgeOf(fetchedAt) is TimeSpan age && age < MinimumAge;

	/// <summary>The newest copy of a key, from memory or disk.</summary>
	/// <param name="key">The cache key.</param>
	/// <returns>The entry, or null when the key has never been cached.</returns>
	private Entry? Current(string key)
	{
		// Held in memory only because its write failed, so it is newer than anything on disk.
		if (unpersisted.TryGetValue(key, out Entry? held))
		{
			return held;
		}

		string path = PathFor(key, BodySuffix);

		if (!File.Exists(path))
		{
			return null;
		}

		// A body with no readable stamp is as old as can be: still served offline, never fresh.
		return new Entry(File.ReadAllText(path), ReadStamp(PathFor(key, StampSuffix)) ?? DateTimeOffset.MinValue);
	}

	/// <summary>Joins the request in flight for a key, or starts one.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="download">Asks the source.</param>
	/// <param name="isUnavailable">Classifies its failures.</param>
	/// <returns>The shared request.</returns>
	/// <remarks>
	/// The request is held behind a <see cref="Lazy{T}"/> so that losing the race to add it never
	/// starts a second one: only the instance that was added is ever run, and only the caller that
	/// added it removes it again once it has finished.
	/// </remarks>
	private Task<Fetched<string>> Join(
		string key,
		Func<string?, CancellationToken, Task<string>> download,
		Func<Exception, bool> isUnavailable)
	{
		Lazy<Task<Fetched<string>>> mine = new(() => RunAsync(key, download, isUnavailable));
		Lazy<Task<Fetched<string>>> shared = inFlight.GetOrAdd(key, mine);

		if (ReferenceEquals(shared, mine))
		{
			_ = mine.Value.ContinueWith(
				_ => inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<Fetched<string>>>>(key, mine)),
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}

		return shared.Value;
	}

	/// <summary>Decides whether to ask the source, asks it, and stores what comes back.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="download">Asks the source.</param>
	/// <param name="isUnavailable">Classifies its failures.</param>
	/// <returns>The outcome.</returns>
	private async Task<Fetched<string>> RunAsync(
		string key,
		Func<string?, CancellationToken, Task<string>> download,
		Func<Exception, bool> isUnavailable)
	{
		Entry? current = Current(key);

		// Another request for this key may have finished between the caller's check and this one.
		if (current is not null && IsFresh(current.FetchedAt))
		{
			return new Fetched<string>(current.Body, current.FetchedAt, IsStale: false);
		}

		if (IsBackingOff(key, hasFallback: current is not null, out Exception? lastFailure))
		{
			return current is not null
				? new Fetched<string>(current.Body, current.FetchedAt, IsStale: true)
				: throw new ResponseUnavailableException(
					FormattableString.Invariant($"{key} failed recently and is not asked again yet, and nothing is cached."),
					lastFailure);
		}

		string body;

		try
		{
			// Not the caller's token: other callers share this request, and one of them giving
			// up is not a reason to abandon it for the rest. A transport timeout still ends it.
			body = await download(current?.Body, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception failure) when (isUnavailable(failure))
		{
			RecordFailure(key, failure);

			return current is not null
				? new Fetched<string>(current.Body, current.FetchedAt, IsStale: true)
				: throw new ResponseUnavailableException(
					FormattableString.Invariant($"{key} could not be fetched and nothing is cached."),
					failure);
		}

		return Store(key, body);
	}

	/// <summary>Stores a successful response, in memory when the disk will not take it.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="body">The response body.</param>
	/// <returns>The fresh outcome.</returns>
	private Fetched<string> Store(string key, string body)
	{
		DateTimeOffset now = time.GetUtcNow();

		_ = attempts.TryRemove(key, out _);

		try
		{
			WriteAt(key, body, now);
			TryDelete(PathFor(key, AttemptSuffix));
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			// The response was good; only keeping it failed. Failing the call would throw away what
			// was just downloaded, and forgetting it would download it again on the next call.
			unpersisted[key] = new Entry(body, now);
			LastWriteFailure = failure;
		}

		return new Fetched<string>(body, now, IsStale: false);
	}

	/// <summary>Records a failed attempt, in memory and, where it can be written, on disk.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="failure">What went wrong.</param>
	private void RecordFailure(string key, Exception failure)
	{
		DateTimeOffset now = time.GetUtcNow();

		attempts[key] = new Attempt(now, failure);

		try
		{
			System.IO.Directory.CreateDirectory(Directory);
			WriteAtomically(PathFor(key, AttemptSuffix), Format(now));
		}
		catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
		{
			// The in-memory record still throttles this instance.
			LastWriteFailure = writeFailure;
		}
	}

	/// <summary>Whether a recent failure means the source should not be asked yet.</summary>
	/// <param name="key">The cache key.</param>
	/// <param name="hasFallback">Whether there is a stale copy to serve meanwhile.</param>
	/// <param name="lastFailure">The failure, when this instance saw it.</param>
	/// <returns>True when the source should not be asked.</returns>
	private bool IsBackingOff(string key, bool hasFallback, out Exception? lastFailure)
	{
		DateTimeOffset? attemptedAt;

		if (attempts.TryGetValue(key, out Attempt? attempt))
		{
			attemptedAt = attempt.At;
			lastFailure = attempt.Failure;
		}
		else
		{
			attemptedAt = ReadStamp(PathFor(key, AttemptSuffix));
			lastFailure = null;
		}

		TimeSpan backoff = hasFallback ? MinimumAge : FailureBackoff;

		return attemptedAt is DateTimeOffset at && AgeOf(at) is TimeSpan age && age < backoff;
	}

	/// <summary>
	/// Turns a cache key into a file path.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <param name="suffix">The file suffix.</param>
	/// <returns>The path.</returns>
	/// <remarks>
	/// The key goes through a hash rather than into the name, because a key is a query string and a
	/// query string contains characters a file name may not. The readable part is kept as a prefix
	/// so the cache directory can still be understood by looking at it.
	/// </remarks>
	private string PathFor(string key, string suffix)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
		string readable = new([.. key.Select(c => char.IsLetterOrDigit(c) ? c : '-')]);

		if (readable.Length > 48)
		{
			readable = readable[..48];
		}

		return Path.Join(Directory, $"{readable}-{Convert.ToHexString(hash)[..16]}{suffix}");
	}

	/// <summary>A cached body and when it was fetched.</summary>
	/// <param name="Body">The body.</param>
	/// <param name="FetchedAt">When it was fetched.</param>
	private sealed record Entry(string Body, DateTimeOffset FetchedAt);

	/// <summary>A failed attempt.</summary>
	/// <param name="At">When.</param>
	/// <param name="Failure">What went wrong.</param>
	private sealed record Attempt(DateTimeOffset At, Exception Failure);
}
