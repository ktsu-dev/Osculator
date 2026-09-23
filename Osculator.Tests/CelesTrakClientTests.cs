// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
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
/// that has never been seen to fail is not evidence of a cache.
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

		IReadOnlyList<ElementSet> offline = await client.GetObjectAsync(25544).ConfigureAwait(false);

		Assert.HasCount(1, offline);
		Assert.AreEqual(25544, offline[0].NoradCatalogId);
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
		// CelesTrak answers an unknown object with a two-hundred and a sentence, so a successful
		// request is not on its own a successful lookup. Left unhandled this reaches the JSON reader
		// as a parse failure, which says nothing about what went wrong.
		CountingHandler handler = Transport("No GP data found");
		CelesTrakClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));

		await Assert.ThrowsExactlyAsync<CelesTrakException>(() => client.GetObjectAsync(99999)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ANotFoundIsNotCached()
	{
		CountingHandler handler = Transport("No GP data found");
		CelesTrakClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));

		await Assert.ThrowsExactlyAsync<CelesTrakException>(() => client.GetObjectAsync(99999)).ConfigureAwait(false);
		handler.Body = IssResponse;

		// If the failure had been cached, this would still be failing four hours from now.
		IReadOnlyList<ElementSet> second = await client.GetObjectAsync(99999).ConfigureAwait(false);

		Assert.HasCount(1, second);
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

	/// <summary>A transport that counts requests and can be told to fail.</summary>
	/// <param name="body">The body to answer with.</param>
	private sealed class CountingHandler(string body) : HttpMessageHandler
	{
		/// <summary>Gets how many requests have reached this handler.</summary>
		public int Requests { get; private set; }

		/// <summary>Gets or sets the body to answer with.</summary>
		public string Body { get; set; } = body;

		/// <summary>Gets or sets a failure to throw instead of answering.</summary>
		public Exception? FailWith { get; set; }

		[System.Diagnostics.CodeAnalysis.SuppressMessage(
			"Reliability", "CA2000:Dispose objects before losing scope",
			Justification = "The response is handed to HttpClient, which owns it from here and disposes it.")]
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests++;

			return FailWith is not null
				? Task.FromException<HttpResponseMessage>(FailWith)
				: Task.FromResult(Respond());
		}

		private HttpResponseMessage Respond() => new(HttpStatusCode.OK) { Content = new StringContent(Body) };
	}

	/// <summary>A clock the test moves by hand, so a four-hour window costs no waiting.</summary>
	/// <param name="start">The time to start at.</param>
	private sealed class FakeClock(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset now = start;

		public override DateTimeOffset GetUtcNow() => now;

		/// <summary>Moves the clock forward.</summary>
		/// <param name="by">How far.</param>
		public void Advance(TimeSpan by) => now += by;
	}
}
