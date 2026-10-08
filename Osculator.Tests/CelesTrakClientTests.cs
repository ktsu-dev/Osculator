// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Data.CelesTrak;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the CelesTrak client, and above all the refetch policy it is obliged to keep.
/// </summary>
/// <remarks>
/// CelesTrak is free, is run by one person, and asks consumers to cache and refetch infrequently.
/// That obligation is the one thing here worth testing rather than documenting, so the network is
/// a counting stub and the clock is a fake: every assertion below is about how many times the
/// service was actually asked.
/// </remarks>
/// <remarks>
/// These were mutation-checked rather than merely watched to pass: inverting the freshness
/// comparison in <see cref="ResponseCache.Read"/> fails three of them, and deleting the age
/// term entirely — so that anything ever cached is forever fresh — fails one. A cache test
/// that has never been seen to fail is not evidence of a cache. The overlap, throttling,
/// staleness, status-classification and failed-write tests were checked the same way, each
/// against a reverted copy of the fix it covers.
/// </remarks>
[TestClass]
public sealed class CelesTrakClientTests
{
	/// <summary>One real response, as the live service returned it.</summary>
	private const string IssResponse = """
		[{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-09-22T20:26:37.026816",
		"MEAN_MOTION":15.49234213,"ECCENTRICITY":0.00047339,"INCLINATION":51.6316,
		"RA_OF_ASC_NODE":176.7315,"ARG_OF_PERICENTER":169.8211,"MEAN_ANOMALY":190.2874,
		"EPHEMERIS_TYPE":0,"CLASSIFICATION_TYPE":"U","NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,
		"REV_AT_EPOCH":58689,"BSTAR":0.00014639046,"MEAN_MOTION_DOT":7.689e-5,"MEAN_MOTION_DDOT":0}]
		""";

	/// <summary>The same object at a later epoch, for the history tests.</summary>
	private const string IssLaterResponse = """
		[{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-09-23T04:11:02.500000",
		"MEAN_MOTION":15.49236000,"ECCENTRICITY":0.00047400,"INCLINATION":51.6317,
		"RA_OF_ASC_NODE":172.1100,"ARG_OF_PERICENTER":171.0000,"MEAN_ANOMALY":189.0000,
		"NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,"REV_AT_EPOCH":58694,
		"BSTAR":0.00014700000,"MEAN_MOTION_DOT":7.700e-5,"MEAN_MOTION_DDOT":0}]
		""";

	private readonly List<IDisposable> owned = [];

	private string root = string.Empty;

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-celestrak-").FullName;

	[TestCleanup]
	public void TearDown()
	{
		foreach (IDisposable disposable in owned)
		{
			disposable.Dispose();
		}

		owned.Clear();
		Directory.Delete(root, recursive: true);
	}

	[TestMethod]
	public async Task TheSecondCallInsideTheWindowDoesNotReachTheNetwork()
	{
		CountingHandler handler = Transport(IssResponse);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
		CelesTrakClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetObjectAsync(25544).ConfigureAwait(false);
		await client.GetObjectAsync(25544).ConfigureAwait(false);
		await client.GetObjectAsync(25544).ConfigureAwait(false);

		// The obligation, measured rather than described: three calls, one request.
		Assert.AreEqual(1, handler.Requests);
	}

	[TestMethod]
	public async Task TheCallAfterTheWindowReachesTheNetworkAgain()
	{
		CountingHandler handler = Transport(IssResponse);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
		CelesTrakClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetObjectAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(3, 59));
		await client.GetObjectAsync(25544).ConfigureAwait(false);

		Assert.AreEqual(1, handler.Requests, "Still inside the window.");

		clock.Advance(TimeSpan.FromMinutes(2));
		await client.GetObjectAsync(25544).ConfigureAwait(false);

		Assert.AreEqual(2, handler.Requests, "Past the window.");
	}

	[TestMethod]
	public async Task DifferentObjectsAreCachedSeparately()
	{
		CountingHandler handler = Transport(IssResponse);
		CelesTrakClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));

		await client.GetObjectAsync(25544).ConfigureAwait(false);
		await client.GetObjectAsync(20580).ConfigureAwait(false);
		await client.GetObjectAsync(25544).ConfigureAwait(false);

		Assert.AreEqual(2, handler.Requests);
	}

	[TestMethod]
	public async Task AFailedRequestFallsBackToWhateverIsCachedHoweverOld()
	{
		CountingHandler handler = Transport(IssResponse);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
		CelesTrakClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetObjectAsync(25544).ConfigureAwait(false);

		// A week later, with the network gone. Last week's elements still propagate, and an
		// application that cannot start without a network is worse than one that starts stale.
		clock.Advance(TimeSpan.FromDays(7));
		handler.FailWith = new HttpRequestException("no route to host");

		IReadOnlyList<ElementSet> offline = (await client.GetObjectAsync(25544).ConfigureAwait(false)).Value;

		Assert.HasCount(1, offline);
		Assert.AreEqual(25544, offline[0].NoradCatalogId);
	}

	[TestMethod]
	public async Task AnUnparseableSuccessDoesNotDisplaceTheGoodCachedCopy()
	{
		CountingHandler handler = Transport(IssResponse);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
		CelesTrakClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetObjectAsync(25544).ConfigureAwait(false);

		// Past the window, a proxy answers 200 with an error page. That used to be cached as fresh,
		// and every later lookup — online or off — then failed to parse it.
		clock.Advance(TimeSpan.FromHours(5));
		handler.Body = "<html>503 upstream</html>";

		IReadOnlyList<ElementSet> afterBadBody = (await client.GetObjectAsync(25544).ConfigureAwait(false)).Value;
		Assert.HasCount(1, afterBadBody, "A bad body should fall back to the good copy.");
		Assert.AreEqual(25544, afterBadBody[0].NoradCatalogId);

		// And the cache should still hold the good copy when the network then goes away.
		handler.FailWith = new HttpRequestException("no route to host");
		clock.Advance(TimeSpan.FromHours(5));

		IReadOnlyList<ElementSet> offline = (await client.GetObjectAsync(25544).ConfigureAwait(false)).Value;
		Assert.HasCount(1, offline, "The good copy should have survived the bad body.");
		Assert.AreEqual(3, handler.Requests);
	}

	[TestMethod]
	public async Task TheCallersOwnCancellationIsNotAnsweredFromTheStaleCache()
	{
		CountingHandler handler = Transport(IssResponse);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
		CelesTrakClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetObjectAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(5));

		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync().ConfigureAwait(false);

		// Stale data from an abandoned call would read as a success. Cancelling is not a timeout.
		await Assert.ThrowsAsync<OperationCanceledException>(
			() => client.GetObjectAsync(25544, cancelled.Token)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task AFailedRequestWithNothingCachedIsAnError()
	{
		CountingHandler handler = Transport(IssResponse);
		handler.FailWith = new HttpRequestException("no route to host");
		CelesTrakClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));

		CelesTrakException failure = await Assert.ThrowsExactlyAsync<CelesTrakException>(
			() => client.GetObjectAsync(25544)).ConfigureAwait(false);

		Assert.IsInstanceOfType<HttpRequestException>(failure.InnerException);
	}

	[TestMethod]
	public async Task AnUnknownCatalogueNumberIsAnErrorRatherThanAnEmptyList()
	{
		// CelesTrak answers an unknown object with a 404 and a sentence. It used to be a 200 and the
		// same sentence; both are a lookup that failed, not a service that could not be reached.
		CountingHandler handler = Transport("No GP data found");
		handler.Status = HttpStatusCode.NotFound;
		CelesTrakClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));

		CelesTrakException failure = await Assert.ThrowsExactlyAsync<CelesTrakException>(
			() => client.GetObjectAsync(99999)).ConfigureAwait(false);

		Assert.Contains("has no element set", failure.Message);
	}

	[TestMethod]
	public async Task ANotFoundIsNotCached()
	{
		CountingHandler handler = Transport("No GP data found");
		handler.Status = HttpStatusCode.NotFound;
		CelesTrakClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));

		await Assert.ThrowsExactlyAsync<CelesTrakException>(() => client.GetObjectAsync(99999)).ConfigureAwait(false);
		handler.Body = IssResponse;
		handler.Status = HttpStatusCode.OK;

		// If the failure had been cached, this would still be failing four hours from now.
		IReadOnlyList<ElementSet> second = (await client.GetObjectAsync(99999).ConfigureAwait(false)).Value;

		Assert.HasCount(1, second);
	}

	[TestMethod]
	public async Task OverlappingCallsForOneObjectShareOneRequest()
	{
		// The cold-cache start-up case: several panels on background threads asking for the same
		// object before the first answer has arrived. Each used to send its own request.
		CountingHandler handler = Transport(IssResponse);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		handler.Gate = release.Task;
		CelesTrakClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));

		Task<Fetched<IReadOnlyList<ElementSet>>>[] calls = [.. Enumerable.Range(0, 8).Select(_ => Task.Run(() => client.GetObjectAsync(25544)))];
		await WaitForRequestsAsync(handler, 1).ConfigureAwait(false);
		release.SetResult();

		Fetched<IReadOnlyList<ElementSet>>[] results = await Task.WhenAll(calls).ConfigureAwait(false);

		Assert.AreEqual(1, handler.Requests);
		Assert.IsTrue(results.All(r => r.Value.Count == 1 && r.Value[0].NoradCatalogId == 25544));
	}

	[TestMethod]
	public async Task OverlappingCallsForDifferentObjectsEachMakeTheirOwnRequest()
	{
		CountingHandler handler = Transport(IssResponse);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		handler.Gate = release.Task;
		CelesTrakClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));

		Task<Fetched<IReadOnlyList<ElementSet>>> first = client.GetObjectAsync(25544);
		Task<Fetched<IReadOnlyList<ElementSet>>> second = client.GetObjectAsync(20580);
		await WaitForRequestsAsync(handler, 2).ConfigureAwait(false);
		release.SetResult();
		await Task.WhenAll(first, second).ConfigureAwait(false);

		Assert.AreEqual(2, handler.Requests);
	}

	[TestMethod]
	public async Task OneCallerCancellingDoesNotCancelTheRequestTheOthersShare()
	{
		CountingHandler handler = Transport(IssResponse);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		handler.Gate = release.Task;
		CelesTrakClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));
		using CancellationTokenSource leaving = new();

		Task<Fetched<IReadOnlyList<ElementSet>>> staying = client.GetObjectAsync(25544);
		Task<Fetched<IReadOnlyList<ElementSet>>> abandoned = client.GetObjectAsync(25544, leaving.Token);
		await WaitForRequestsAsync(handler, 1).ConfigureAwait(false);

		await leaving.CancelAsync().ConfigureAwait(false);
		await Assert.ThrowsAsync<OperationCanceledException>(() => abandoned).ConfigureAwait(false);

		release.SetResult();
		Fetched<IReadOnlyList<ElementSet>> result = await staying.ConfigureAwait(false);

		Assert.HasCount(1, result.Value);
		Assert.AreEqual(1, handler.Requests);
	}

	[TestMethod]
	public async Task AFreshFetchSaysItIsNotStale()
	{
		CountingHandler handler = Transport(IssResponse);
		DateTimeOffset start = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
		CelesTrakClient client = ClientOver(handler, new FakeClock(start), TimeSpan.FromHours(4));

		Fetched<IReadOnlyList<ElementSet>> fetched = await client.GetObjectAsync(25544).ConfigureAwait(false);

		Assert.IsFalse(fetched.IsStale);
		Assert.AreEqual(start, fetched.FetchedAt);
	}

	[TestMethod]
	public async Task AStaleFallbackSaysSoAndWhenItWasFetched()
	{
		CountingHandler handler = Transport(IssResponse);
		DateTimeOffset start = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
		FakeClock clock = new(start);
		CelesTrakClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetObjectAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(5));
		handler.FailWith = new HttpRequestException("no route to host");

		Fetched<IReadOnlyList<ElementSet>> offline = await client.GetObjectAsync(25544).ConfigureAwait(false);

		// An element set's age is one of the terms this application measures. A week-old set must
		// not be presentable as a current one.
		Assert.IsTrue(offline.IsStale);
		Assert.AreEqual(start, offline.FetchedAt);
		Assert.HasCount(1, offline.Value);
	}

	[TestMethod]
	public async Task AFailingServiceIsNotAskedAgainInsideTheWindow()
	{
		CountingHandler handler = Transport(IssResponse);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
		CelesTrakClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetObjectAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(5));

		// 403 is how CelesTrak answers a client it has blocked. Asking again on every call is how a
		// block gets longer.
		handler.Status = HttpStatusCode.Forbidden;

		for (int i = 0; i < 5; i++)
		{
			Fetched<IReadOnlyList<ElementSet>> stale = await client.GetObjectAsync(25544).ConfigureAwait(false);
			Assert.IsTrue(stale.IsStale);
		}

		Assert.AreEqual(2, handler.Requests, "One success, then one failed attempt for five calls.");

		clock.Advance(TimeSpan.FromHours(4));
		await client.GetObjectAsync(25544).ConfigureAwait(false);

		Assert.AreEqual(3, handler.Requests, "Past the window it may try once more.");
	}

	[TestMethod]
	public async Task AFailingServiceWithNothingCachedIsRetriedAfterTheBackoffOnly()
	{
		CountingHandler handler = Transport(IssResponse);
		handler.FailWith = new HttpRequestException("no route to host");
		FakeClock clock = new(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
		CelesTrakClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		for (int i = 0; i < 5; i++)
		{
			CelesTrakException failure = await Assert.ThrowsExactlyAsync<CelesTrakException>(
				() => client.GetObjectAsync(25544)).ConfigureAwait(false);
			Assert.IsInstanceOfType<HttpRequestException>(failure.InnerException);
		}

		Assert.AreEqual(1, handler.Requests);

		clock.Advance(ResponseCache.FailureBackoff);
		handler.FailWith = null;

		Fetched<IReadOnlyList<ElementSet>> recovered = await client.GetObjectAsync(25544).ConfigureAwait(false);

		Assert.HasCount(1, recovered.Value);
		Assert.AreEqual(2, handler.Requests);
	}

	[TestMethod]
	public async Task AnObjectThatLeavesTheCatalogueIsNotServedFromTheStaleCopy()
	{
		CountingHandler handler = Transport(IssResponse);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
		CelesTrakClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetObjectAsync(25544).ConfigureAwait(false);

		// Decayed since. The service was reached and said so; last month's elements for an object
		// that no longer exists are not a fallback, they are a wrong answer.
		clock.Advance(TimeSpan.FromHours(5));
		handler.Status = HttpStatusCode.NotFound;
		handler.Body = "No GP data found";

		CelesTrakException failure = await Assert.ThrowsExactlyAsync<CelesTrakException>(
			() => client.GetObjectAsync(25544)).ConfigureAwait(false);

		Assert.Contains("has no element set", failure.Message);
	}

	[TestMethod]
	public async Task A404IsNotFoundWhateverItsBodySays()
	{
		// The status is the answer; the sentence is a courtesy that a proxy or a change of wording
		// can drop. Without it the 404 used to read as an outage and the stale copy was served.
		CountingHandler handler = Transport(IssResponse);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
		CelesTrakClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetObjectAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(5));
		handler.Status = HttpStatusCode.NotFound;
		handler.Body = string.Empty;

		CelesTrakException failure = await Assert.ThrowsExactlyAsync<CelesTrakException>(
			() => client.GetObjectAsync(25544)).ConfigureAwait(false);

		Assert.Contains("has no element set", failure.Message);
	}

	[TestMethod]
	public async Task AnUnknownGroupIsNamedRatherThanReportedAsUnreachable()
	{
		CountingHandler handler = Transport("Invalid query: \"GROUP=notagroup&FORMAT=json\" (GROUP=notagroup not found)");
		CelesTrakClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));

		CelesTrakException failure = await Assert.ThrowsExactlyAsync<CelesTrakException>(
			() => client.GetGroupAsync("notagroup")).ConfigureAwait(false);

		Assert.Contains("rejected", failure.Message);
		Assert.Contains("GROUP=notagroup", failure.Message);
	}

	[TestMethod]
	public async Task ACacheThatCannotBeWrittenDoesNotFailTheCallOrRefetchEveryTime()
	{
		CountingHandler handler = Transport(IssResponse);

		// The cache directory's path is taken by a file, so nothing can be written under it.
		string blocked = Path.Join(root, "blocked");
		await File.WriteAllTextAsync(blocked, "not a directory").ConfigureAwait(false);
		HttpClient http = new(handler, disposeHandler: false);
		owned.Add(http);
		ResponseCache cache = new(blocked, TimeSpan.FromHours(4), new FakeClock(DateTimeOffset.UnixEpoch));
		CelesTrakClient client = new(http, cache);

		for (int i = 0; i < 3; i++)
		{
			Fetched<IReadOnlyList<ElementSet>> fetched = await client.GetObjectAsync(25544).ConfigureAwait(false);
			Assert.HasCount(1, fetched.Value);
		}

		Assert.AreEqual(1, handler.Requests);
		Assert.IsNotNull(cache.LastWriteFailure, "The failed write should be reported somewhere.");
	}

	[TestMethod]
	public async Task ACancelledCallWithNothingCachedIsACancellationNotAnOutage()
	{
		CountingHandler handler = Transport(IssResponse);
		CelesTrakClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));

		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync().ConfigureAwait(false);

		await Assert.ThrowsAsync<OperationCanceledException>(
			() => client.GetObjectAsync(25544, cancelled.Token)).ConfigureAwait(false);
		Assert.AreEqual(0, handler.Requests);
	}

	[TestMethod]
	public void TheArchiveKeepsOneCopyOfEachEpochAndOrdersThem()
	{
		SnapshotStore store = new(Path.Join(root, "archive"));

		Assert.AreEqual(1, store.Add(IssLaterResponse), "A set never seen before is new.");
		Assert.AreEqual(1, store.Add(IssResponse), "A different epoch is a different set.");
		Assert.AreEqual(0, store.Add(IssResponse), "The same epoch twice is the same set.");

		IReadOnlyList<ElementSet> history = store.History(25544);

		Assert.HasCount(2, history);
		Assert.IsLessThan(history[1].Epoch, history[0].Epoch, "History is oldest first.");
		Assert.HasCount(1, store.Objects());
		Assert.AreEqual(25544, store.Objects()[0]);
	}

	[TestMethod]
	public void TheArchiveIsEmptyRatherThanAbsentForAnObjectNeverSeen()
	{
		SnapshotStore store = new(Path.Join(root, "archive"));

		Assert.IsEmpty(store.History(25544));
		Assert.IsEmpty(store.Objects());
	}

	[TestMethod]
	public void TheArchiveKeepsTheSourcesOwnTextRatherThanOurReadingOfIt()
	{
		// The property that makes it an archive rather than a cache of this repository's parse: the
		// bytes the service served are still there to be re-read if the parser turns out wrong.
		SnapshotStore store = new(Path.Join(root, "archive"));
		store.Add(IssResponse);

		string[] archived = Directory.GetFiles(Path.Join(root, "archive", "25544"), "*.json");

		Assert.HasCount(1, archived);
		Assert.Contains("\"CLASSIFICATION_TYPE\":\"U\"", File.ReadAllText(archived[0]),
			"A field this repository's ElementSet does not carry should still be in the archive.");
	}

	/// <summary>Waits until a gated transport has received a number of requests.</summary>
	/// <param name="handler">The transport.</param>
	/// <param name="count">How many.</param>
	/// <returns>A task.</returns>
	internal static async Task WaitForRequestsAsync(CountingHandler handler, int count)
	{
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

		while (handler.Requests < count)
		{
			await Task.Delay(10, timeout.Token).ConfigureAwait(false);
		}

		// Any caller that was going to send a second request has had time to.
		await Task.Delay(100, timeout.Token).ConfigureAwait(false);
	}

	/// <summary>Creates a transport the fixture will dispose.</summary>
	/// <param name="body">The body it answers with.</param>
	/// <returns>The transport.</returns>
	private CountingHandler Transport(string body)
	{
		CountingHandler handler = new(body);
		owned.Add(handler);
		return handler;
	}

	private CelesTrakClient ClientOver(CountingHandler handler, FakeClock clock, TimeSpan window)
	{
		HttpClient http = new(handler, disposeHandler: false);
		owned.Add(http);
		return new CelesTrakClient(http, new ResponseCache(Path.Join(root, "cache"), window, clock));
	}

	/// <summary>A transport that counts requests and can be told to fail, or to wait.</summary>
	/// <param name="body">The body to answer with.</param>
	internal sealed class CountingHandler(string body) : HttpMessageHandler
	{
		private int requests;

		/// <summary>Gets how many requests have reached this handler.</summary>
		public int Requests => Volatile.Read(ref requests);

		/// <summary>Gets or sets the body to answer with.</summary>
		public string Body { get; set; } = body;

		/// <summary>Gets or sets the status to answer with.</summary>
		public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

		/// <summary>Gets or sets a failure to throw instead of answering.</summary>
		public Exception? FailWith { get; set; }

		/// <summary>Gets or sets a task every request waits on before answering, for overlap tests.</summary>
		public Task? Gate { get; set; }

		[System.Diagnostics.CodeAnalysis.SuppressMessage(
			"Reliability", "CA2000:Dispose objects before losing scope",
			Justification = "The response is handed to HttpClient, which owns it from here and disposes it.")]
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref requests);

			if (Gate is not null)
			{
				await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
			}

			// A real transport abandons a cancelled request, and so does this one.
			cancellationToken.ThrowIfCancellationRequested();

			if (FailWith is not null)
			{
				throw FailWith;
			}

			return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
		}
	}

	/// <summary>A clock the test moves by hand, so a four-hour window costs no waiting.</summary>
	/// <param name="start">The time to start at.</param>
	internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset now = start;

		public override DateTimeOffset GetUtcNow() => now;

		/// <summary>Moves the clock, forward or back.</summary>
		/// <param name="by">How far.</param>
		public void Advance(TimeSpan by) => now += by;
	}
}
