// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Data.Horizons;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers fetching vector tables from JPL Horizons.
/// </summary>
/// <remarks>
/// The CelesTrak and IERS discipline: a counting transport and a clock the test moves by hand, so
/// every claim about caching is a claim about how many times the service was actually asked.
/// </remarks>
[TestClass]
public sealed class HorizonsClientTests
{
	private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0);

	private static readonly DateTime Stop = new(2026, 1, 1, 3, 0, 0);

	private readonly List<IDisposable> owned = [];

	private string root = string.Empty;

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-horizons-").FullName;

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
	public async Task TheMoonIsAskedForAsAGeocentricIcrfTableInKilometres()
	{
		CountingHandler handler = Transport(HorizonsSample.MoonJson);
		HorizonsClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromDays(1));

		HorizonsEphemeris moon = await client.GetVectorsAsync(HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1))).ConfigureAwait(false);

		Assert.HasCount(4, moon.Vectors);

		string asked = Uri.UnescapeDataString(handler.LastRequest!.Query);

		Assert.AreEqual(HorizonsClient.Endpoint, handler.LastRequest.GetLeftPart(UriPartial.Path));
		Assert.Contains("COMMAND='301'", asked);
		Assert.Contains("CENTER='500@399'", asked);
		Assert.Contains("EPHEM_TYPE='VECTORS'", asked);
		Assert.Contains("START_TIME='2026-01-01 00:00:00.000'", asked);
		Assert.Contains("STOP_TIME='2026-01-01 03:00:00.000'", asked);
		Assert.Contains("STEP_SIZE='60 m'", asked);
		Assert.Contains("TIME_TYPE='TDB'", asked);
		Assert.Contains("REF_SYSTEM='ICRF'", asked);
		Assert.Contains("REF_PLANE='FRAME'", asked);
		Assert.Contains("OUT_UNITS='KM-S'", asked);
		Assert.Contains("VEC_CORR='NONE'", asked);
	}

	[TestMethod]
	public void TheSunAndASpacecraftAreAskedForByNumber()
	{
		HorizonsRequest sun = HorizonsRequest.Sun(Start, Stop, TimeSpan.FromHours(1));
		HorizonsRequest hubble = HorizonsRequest.Spacecraft(-48, Start, Stop, TimeSpan.FromMinutes(10));

		Assert.Contains("COMMAND='10'", Uri.UnescapeDataString(sun.ToQuery()));
		Assert.Contains("COMMAND='-48'", Uri.UnescapeDataString(hubble.ToQuery()));
		Assert.Contains("STEP_SIZE='10 m'", Uri.UnescapeDataString(hubble.ToQuery()));
	}

	[TestMethod]
	public void RequestsHorizonsCannotTakeAreRefusedBeforeAnythingIsSent()
	{
		Assert.ThrowsExactly<ArgumentException>(() => HorizonsRequest.Moon(Stop, Start, TimeSpan.FromHours(1)).ToQuery());
		Assert.ThrowsExactly<ArgumentException>(() => HorizonsRequest.Moon(Start, Stop, TimeSpan.FromSeconds(30)).ToQuery());
		Assert.ThrowsExactly<ArgumentException>(() => HorizonsRequest.Moon(Start, Stop, TimeSpan.FromSeconds(90)).ToQuery());
		Assert.ThrowsExactly<ArgumentException>(() => (HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1)) with { Command = "Hubble's" }).ToQuery());
		Assert.ThrowsExactly<ArgumentException>(() => (HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1)) with { Center = " " }).ToQuery());
	}

	[TestMethod]
	public async Task ATableIsFetchedOnceAndThenReadFromDisk()
	{
		CountingHandler handler = Transport(HorizonsSample.MoonJson);
		FakeClock clock = new(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));
		HorizonsClient client = ClientOver(handler, clock, TimeSpan.FromDays(7));
		HorizonsRequest request = HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1));

		await client.GetVectorsAsync(request).ConfigureAwait(false);
		await client.GetVectorsAsync(request).ConfigureAwait(false);

		Assert.AreEqual(1, handler.Requests);

		await client.GetVectorsAsync(request with { Step = TimeSpan.FromMinutes(30) }).ConfigureAwait(false);

		Assert.AreEqual(2, handler.Requests, "A different step is a different table.");

		clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
		await client.GetVectorsAsync(request).ConfigureAwait(false);

		Assert.AreEqual(3, handler.Requests, "Past the window the table may be asked for again.");
	}

	[TestMethod]
	public async Task ARefusalIsRaisedAndNotCached()
	{
		CountingHandler handler = Transport("""{"error":"Cannot interpret date."}""");
		handler.Status = HttpStatusCode.BadRequest;
		HorizonsClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromDays(1));
		HorizonsRequest request = HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1));

		HorizonsException refusal = await Assert.ThrowsExactlyAsync<HorizonsException>(
			() => client.GetVectorsAsync(request)).ConfigureAwait(false);

		Assert.Contains("Cannot interpret date", refusal.Message);

		handler.Status = HttpStatusCode.OK;
		handler.Body = HorizonsSample.MoonJson;

		await client.GetVectorsAsync(request).ConfigureAwait(false);

		Assert.AreEqual(2, handler.Requests, "A refusal must not sit in the cache for the whole window.");
	}

	[TestMethod]
	public async Task ARefusalIsNotAnsweredFromAnOlderCopy()
	{
		// An ambiguous target is Horizons saying something. Answering it with last week's table for
		// the same query would be hiding what it said, so the stale fallback is for outages only.
		CountingHandler handler = Transport(HorizonsSample.MoonJson);
		FakeClock clock = new(DateTimeOffset.UnixEpoch);
		HorizonsClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));
		HorizonsRequest request = HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1));

		await client.GetVectorsAsync(request).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromDays(2));
		handler.Body = HorizonsSample.Wrap("API VERSION: 1.2\n Multiple major-bodies match string \"301*\"\n");

		await Assert.ThrowsExactlyAsync<HorizonsException>(() => client.GetVectorsAsync(request)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ABadDownloadDoesNotDisplaceAGoodCachedCopy()
	{
		CountingHandler handler = Transport(HorizonsSample.MoonJson);
		FakeClock clock = new(DateTimeOffset.UnixEpoch);
		HorizonsClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));
		HorizonsRequest request = HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1));

		await client.GetVectorsAsync(request).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromDays(2));
		handler.Body = "<html>Sign in to continue</html>";

		HorizonsEphemeris served = await client.GetVectorsAsync(request).ConfigureAwait(false);

		Assert.HasCount(4, served.Vectors);

		handler.Body = HorizonsSample.Wrap(HorizonsSample.Report(endMarker: string.Empty));
		clock.Advance(TimeSpan.FromDays(2));

		served = await client.GetVectorsAsync(request).ConfigureAwait(false);

		Assert.HasCount(4, served.Vectors, "A truncated table must not have replaced the good copy.");
	}

	[TestMethod]
	public async Task AnOutageWithAnErrorStatusFallsBackToTheCache()
	{
		CountingHandler handler = Transport(HorizonsSample.MoonJson);
		FakeClock clock = new(DateTimeOffset.UnixEpoch);
		HorizonsClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));
		HorizonsRequest request = HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1));

		await client.GetVectorsAsync(request).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromDays(2));
		handler.Status = HttpStatusCode.ServiceUnavailable;
		handler.Body = "<html>503</html>";

		HorizonsEphemeris served = await client.GetVectorsAsync(request).ConfigureAwait(false);

		Assert.HasCount(4, served.Vectors);
	}

	[TestMethod]
	public async Task WithTheNetworkGoneItFallsBackToWhateverIsCached()
	{
		CountingHandler handler = Transport(HorizonsSample.MoonJson);
		FakeClock clock = new(DateTimeOffset.UnixEpoch);
		HorizonsClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));
		HorizonsRequest request = HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1));

		await client.GetVectorsAsync(request).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromDays(365));
		handler.FailWith = new HttpRequestException("no route to host");

		HorizonsEphemeris offline = await client.GetVectorsAsync(request).ConfigureAwait(false);

		Assert.HasCount(4, offline.Vectors);
	}

	[TestMethod]
	public async Task WithTheNetworkGoneAndNothingCachedItSaysSo()
	{
		CountingHandler handler = Transport(HorizonsSample.MoonJson);
		handler.FailWith = new HttpRequestException("no route to host");
		HorizonsClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromDays(1));

		HorizonsException failure = await Assert.ThrowsExactlyAsync<HorizonsException>(
			() => client.GetVectorsAsync(HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1)))).ConfigureAwait(false);

		Assert.IsInstanceOfType<HttpRequestException>(failure.InnerException);
	}

	[TestMethod]
	public async Task TheCallersOwnCancellationIsNotAnsweredFromTheStaleCache()
	{
		CountingHandler handler = Transport(HorizonsSample.MoonJson);
		FakeClock clock = new(DateTimeOffset.UnixEpoch);
		HorizonsClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));
		HorizonsRequest request = HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1));

		await client.GetVectorsAsync(request).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromDays(2));

		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync().ConfigureAwait(false);

		await Assert.ThrowsAsync<OperationCanceledException>(
			() => client.GetVectorsAsync(request, cancelled.Token)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task OneRequestIsInFlightAtATimeAndAQueuedDuplicateIsServedFromTheCache()
	{
		CountingHandler handler = Transport(HorizonsSample.MoonJson);
		handler.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		HorizonsClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromDays(1));
		HorizonsRequest request = HorizonsRequest.Moon(Start, Stop, TimeSpan.FromHours(1));

		Task<HorizonsEphemeris>[] calls =
		[
			client.GetVectorsAsync(request),
			client.GetVectorsAsync(request),
			client.GetVectorsAsync(request with { Step = TimeSpan.FromMinutes(30) }),
		];

		await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

		Assert.AreEqual(1, handler.Requests, "The others should be queued behind the first.");

		handler.Hold.SetResult();
		await Task.WhenAll(calls).ConfigureAwait(false);

		Assert.AreEqual(1, handler.MostInFlight);
		Assert.AreEqual(2, handler.Requests, "The duplicate should have found the first one's table in the cache.");
	}

	private CountingHandler Transport(string body)
	{
		CountingHandler handler = new(body);
		owned.Add(handler);
		return handler;
	}

	private HorizonsClient ClientOver(CountingHandler handler, FakeClock clock, TimeSpan window)
	{
		HttpClient http = new(handler, disposeHandler: false);
		owned.Add(http);
		return new HorizonsClient(http, new ResponseCache(Path.Join(root, "cache"), window, clock));
	}

	private sealed class CountingHandler(string body) : HttpMessageHandler
	{
		private int inFlight;

		public int Requests { get; private set; }

		public int MostInFlight { get; private set; }

		public Uri? LastRequest { get; private set; }

		public string Body { get; set; } = body;

		public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

		public Exception? FailWith { get; set; }

		public TaskCompletionSource? Hold { get; set; }

		[System.Diagnostics.CodeAnalysis.SuppressMessage(
			"Reliability", "CA2000:Dispose objects before losing scope",
			Justification = "The response is handed to HttpClient, which owns it from here and disposes it.")]
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			Requests++;
			LastRequest = request.RequestUri;
			MostInFlight = Math.Max(MostInFlight, Interlocked.Increment(ref inFlight));

			try
			{
				if (Hold is not null)
				{
					await Hold.Task.ConfigureAwait(false);
				}

				return FailWith is not null
					? throw FailWith
					: new HttpResponseMessage(Status) { Content = new StringContent(Body) };
			}
			finally
			{
				Interlocked.Decrement(ref inFlight);
			}
		}
	}

	private sealed class FakeClock(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset now = start;

		public override DateTimeOffset GetUtcNow() => now;

		public void Advance(TimeSpan by) => now += by;
	}
}
