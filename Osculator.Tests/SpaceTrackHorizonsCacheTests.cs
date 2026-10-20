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
using ktsu.Osculator.Data.Horizons;
using ktsu.Osculator.Data.SpaceTrack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the Space-Track and Horizons clients going through <see cref="ResponseCache.FetchAsync"/>:
/// a failing query is throttled, and a stale fallback says it is one.
/// </summary>
/// <remarks>
/// <para>
/// Before, both clients ran their own copy of read, fetch, read-at-any-age: a failure with something
/// cached returned it as if it were current, and a failure with nothing cached was sent again on
/// every call (#136). Every test here counts requests against a transport the test scripts and a
/// clock the test moves by hand.
/// </para>
/// <para>
/// Mutation-checked against the client: classifying every <see cref="SpaceTrackException"/> as an
/// outage fails <see cref="AQueryRejectedBySpaceTrackIsAnAnswerNotAnOutage"/>, and classifying
/// Space-Track's rate-limit error as a rejection fails
/// <see cref="SpaceTracksOwnRateLimitErrorIsAnOutageServedStale"/> and
/// <see cref="SpaceTracksOwnRateLimitErrorIsNotSentAgainWithinTheBackOff"/>.
/// </para>
/// </remarks>
[TestClass]
public sealed class SpaceTrackHorizonsCacheTests
{
	private static readonly DateTimeOffset Start = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

	private static readonly DateTime HorizonsStart = new(2026, 1, 1, 0, 0, 0);

	private readonly List<IDisposable> owned = [];

	private string root = string.Empty;

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-r14-").FullName;

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
	public async Task AFreshSpaceTrackAnswerIsNotStaleAndSaysWhenItWasFetched()
	{
		ScriptedSpaceTrack server = Server(History());
		ManualClock clock = new(Start);
		SpaceTrackClient client = SpaceTrackOver(server, clock);

		Fetched<IReadOnlyList<ElementSet>> history = await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.IsFalse(history.IsStale);
		Assert.AreEqual(Start, history.FetchedAt);
		Assert.HasCount(3, history.Value);
	}

	[TestMethod]
	public async Task AFailingSpaceTrackQueryWithNothingCachedIsNotSentAgainWithinTheBackOff()
	{
		ScriptedSpaceTrack server = Server(History());
		server.QueryStatus = HttpStatusCode.ServiceUnavailable;
		ManualClock clock = new(Start);
		SpaceTrackClient client = SpaceTrackOver(server, clock);

		await Assert.ThrowsAsync<SpaceTrackException>(() => client.GetHistoryAsync(25544)).ConfigureAwait(false);
		Assert.AreEqual(1, server.Queries);

		clock.Advance(ResponseCache.FailureBackoff - TimeSpan.FromMinutes(1));
		await Assert.ThrowsAsync<SpaceTrackException>(() => client.GetHistoryAsync(25544)).ConfigureAwait(false);
		Assert.AreEqual(1, server.Queries, "Inside the back-off the failing query must not be sent again.");
		Assert.AreEqual(1, server.Logins, "Nor may the back-off spend a login.");

		clock.Advance(TimeSpan.FromMinutes(2));
		server.QueryStatus = HttpStatusCode.OK;
		Fetched<IReadOnlyList<ElementSet>> recovered = await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.AreEqual(2, server.Queries, "Past the back-off it may be asked again.");
		Assert.IsFalse(recovered.IsStale);
	}

	[TestMethod]
	public async Task ASpaceTrackOutageWithACachedCopyServesItMarkedStale()
	{
		ScriptedSpaceTrack server = Server(History());
		ManualClock clock = new(Start);
		SpaceTrackClient client = SpaceTrackOver(server, clock);

		await client.GetHistoryAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromDays(3));
		server.QueryStatus = HttpStatusCode.BadGateway;

		Fetched<IReadOnlyList<ElementSet>> stale = await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.IsTrue(stale.IsStale, "Three-day-old data served because the service failed must say so.");
		Assert.AreEqual(Start, stale.FetchedAt);
		Assert.HasCount(3, stale.Value);

		clock.Advance(TimeSpan.FromMinutes(30));
		_ = await client.GetHistoryAsync(25544).ConfigureAwait(false);
		Assert.AreEqual(2, server.Queries, "With a copy to show, the back-off is the whole window.");
	}

	[TestMethod]
	public async Task ALimiterRefusalServedFromTheCacheIsMarkedStale()
	{
		ScriptedSpaceTrack server = Server(History());
		ManualClock clock = new(Start);
		SpaceTrackClient client = SpaceTrackOver(server, clock, new SpaceTrackRateLimiter(clock, 2, 299));

		await client.GetHistoryAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(2));

		// Spend this minute's two requests on two other objects.
		server.QueryBody = "[]";
		await client.GetObjectAsync(1).ConfigureAwait(false);
		await client.GetObjectAsync(2).ConfigureAwait(false);

		Fetched<IReadOnlyList<ElementSet>> stale = await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.IsTrue(stale.IsStale, "Refused by the limiter and answered from the cache: that is a stale answer.");
		Assert.HasCount(3, stale.Value);
	}

	[TestMethod]
	public async Task ALimiterRefusalWithNothingCachedKeepsItsRetryAfter()
	{
		ScriptedSpaceTrack server = Server("[]");
		ManualClock clock = new(Start);
		SpaceTrackClient client = SpaceTrackOver(server, clock, new SpaceTrackRateLimiter(clock, 2, 299));

		await client.GetObjectAsync(1).ConfigureAwait(false);

		SpaceTrackRateLimitException refusal = await Assert.ThrowsExactlyAsync<SpaceTrackRateLimitException>(
			() => client.GetObjectAsync(2)).ConfigureAwait(false);

		Assert.AreEqual(TimeSpan.FromMinutes(1), refusal.RetryAfter);
	}

	[TestMethod]
	public async Task AQueryRejectedBySpaceTrackIsAnAnswerNotAnOutage()
	{
		ScriptedSpaceTrack server = Server(History());
		ManualClock clock = new(Start);
		SpaceTrackClient client = SpaceTrackOver(server, clock);

		await client.GetHistoryAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(2));
		server.QueryBody = """{"error":"Class 'gp_history' does not have a field named 'NORAD_CAT_IDX'"}""";

		SpaceTrackQueryRejectedException rejected = await Assert.ThrowsExactlyAsync<SpaceTrackQueryRejectedException>(
			() => client.GetHistoryAsync(25544)).ConfigureAwait(false);

		StringAssert.Contains(rejected.Message, "NORAD_CAT_IDX");

		server.QueryBody = History();
		Fetched<IReadOnlyList<ElementSet>> next = await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.AreEqual(3, server.Queries, "An answer is not throttled: the next call asks again.");
		Assert.IsFalse(next.IsStale);
	}

	[TestMethod]
	public async Task SpaceTracksOwnRateLimitErrorIsAnOutageServedStale()
	{
		ScriptedSpaceTrack server = Server(History());
		ManualClock clock = new(Start);
		SpaceTrackClient client = SpaceTrackOver(server, clock);

		await client.GetHistoryAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(2));
		server.QueryBody = """{"error":"You've violated your query rate limit."}""";

		Fetched<IReadOnlyList<ElementSet>> stale = await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.IsTrue(stale.IsStale);
		Assert.HasCount(3, stale.Value);
	}

	[TestMethod]
	public async Task SpaceTracksOwnRateLimitErrorIsNotSentAgainWithinTheBackOff()
	{
		ScriptedSpaceTrack server = Server("""{"error":"You've violated your query rate limit."}""");
		ManualClock clock = new(Start);
		SpaceTrackClient client = SpaceTrackOver(server, clock);

		await Assert.ThrowsExactlyAsync<SpaceTrackRateLimitException>(() => client.GetCatalogueAsync()).ConfigureAwait(false);

		for (int call = 0; call < 10; call++)
		{
			await Assert.ThrowsAsync<SpaceTrackException>(() => client.GetCatalogueAsync()).ConfigureAwait(false);
		}

		Assert.AreEqual(2, server.Requests, "One login and one query; the ten retries never left.");
	}

	[TestMethod]
	public async Task AFailingHorizonsQueryWithNothingCachedIsNotSentAgainWithinTheBackOff()
	{
		ScriptedHorizons server = Horizons(HorizonsSample.MoonJson);
		server.Status = HttpStatusCode.ServiceUnavailable;
		server.Body = "<html>503</html>";
		ManualClock clock = new(Start);
		HorizonsClient client = HorizonsOver(server, clock);
		HorizonsRequest request = HorizonsRequest.Moon(HorizonsStart, HorizonsStart.AddHours(3), TimeSpan.FromHours(1));

		await Assert.ThrowsExactlyAsync<HorizonsException>(() => client.GetVectorsAsync(request)).ConfigureAwait(false);

		clock.Advance(ResponseCache.FailureBackoff - TimeSpan.FromMinutes(1));
		await Assert.ThrowsExactlyAsync<HorizonsException>(() => client.GetVectorsAsync(request)).ConfigureAwait(false);
		Assert.AreEqual(1, server.Requests, "Inside the back-off the failing query must not be sent again.");

		clock.Advance(TimeSpan.FromMinutes(2));
		server.Status = HttpStatusCode.OK;
		server.Body = HorizonsSample.MoonJson;
		Fetched<HorizonsEphemeris> recovered = await client.GetVectorsAsync(request).ConfigureAwait(false);

		Assert.AreEqual(2, server.Requests);
		Assert.IsFalse(recovered.IsStale);
		Assert.HasCount(4, recovered.Value.Vectors);
	}

	[TestMethod]
	public async Task AHorizonsOutageWithACachedCopyServesItMarkedStale()
	{
		ScriptedHorizons server = Horizons(HorizonsSample.MoonJson);
		ManualClock clock = new(Start);
		HorizonsClient client = HorizonsOver(server, clock);
		HorizonsRequest request = HorizonsRequest.Moon(HorizonsStart, HorizonsStart.AddHours(3), TimeSpan.FromHours(1));

		Fetched<HorizonsEphemeris> fresh = await client.GetVectorsAsync(request).ConfigureAwait(false);
		Assert.IsFalse(fresh.IsStale);

		clock.Advance(TimeSpan.FromDays(10));
		server.FailWith = new HttpRequestException("no route to host");

		Fetched<HorizonsEphemeris> stale = await client.GetVectorsAsync(request).ConfigureAwait(false);

		Assert.IsTrue(stale.IsStale);
		Assert.AreEqual(Start, stale.FetchedAt);
		Assert.HasCount(4, stale.Value.Vectors);
	}

	[TestMethod]
	public async Task OverlappingHorizonsCallersForOneTableShareOneRequest()
	{
		ScriptedHorizons server = Horizons(HorizonsSample.MoonJson);
		server.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		HorizonsClient client = HorizonsOver(server, new ManualClock(Start));
		HorizonsRequest request = HorizonsRequest.Moon(HorizonsStart, HorizonsStart.AddHours(3), TimeSpan.FromHours(1));

		Task<Fetched<HorizonsEphemeris>>[] calls = [client.GetVectorsAsync(request), client.GetVectorsAsync(request)];

		server.Hold.SetResult();
		await Task.WhenAll(calls).ConfigureAwait(false);

		Assert.AreEqual(1, server.Requests);
	}

	private static string History() => File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Data", "SpaceTrack", "gp_history_25544.json"));

	private ScriptedSpaceTrack Server(string body)
	{
		ScriptedSpaceTrack server = new() { QueryBody = body };
		owned.Add(server);
		return server;
	}

	private SpaceTrackClient SpaceTrackOver(ScriptedSpaceTrack server, ManualClock clock, SpaceTrackRateLimiter? limiter = null)
	{
		HttpClient http = new(server, disposeHandler: false);
		SpaceTrackClient client = new(
			http,
			new ResponseCache(Path.Join(root, "cache"), TimeSpan.FromHours(1), clock),
			limiter ?? new SpaceTrackRateLimiter(clock),
			new FixedCredentials());

		owned.Add(http);
		owned.Add(client);

		return client;
	}

	private ScriptedHorizons Horizons(string body)
	{
		ScriptedHorizons server = new() { Body = body };
		owned.Add(server);
		return server;
	}

	private HorizonsClient HorizonsOver(ScriptedHorizons server, ManualClock clock)
	{
		HttpClient http = new(server, disposeHandler: false);
		owned.Add(http);

		return new HorizonsClient(http, new ResponseCache(Path.Join(root, "horizons"), TimeSpan.FromDays(1), clock));
	}

	/// <summary>A credential source that always has the one test account.</summary>
	internal sealed class FixedCredentials : ISpaceTrackCredentialSource
	{
		public Task<SpaceTrackCredentials?> GetAsync(CancellationToken cancellationToken) =>
			Task.FromResult<SpaceTrackCredentials?>(new SpaceTrackCredentials("osculator@example.com", "password"));
	}

	/// <summary>Space-Track reduced to a login that always succeeds and a scripted query answer.</summary>
	internal sealed class ScriptedSpaceTrack : HttpMessageHandler
	{
		public string QueryBody { get; set; } = "[]";

		public HttpStatusCode QueryStatus { get; set; } = HttpStatusCode.OK;

		public int Logins { get; private set; }

		public int Queries { get; private set; }

		public int Requests => Logins + Queries;

		public string? LastQueryPath { get; private set; }

		[System.Diagnostics.CodeAnalysis.SuppressMessage(
			"Reliability", "CA2000:Dispose objects before losing scope",
			Justification = "The response is handed to HttpClient, which owns it from here and disposes it.")]
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.Method == HttpMethod.Post)
			{
				Logins++;
				HttpResponseMessage accepted = new(HttpStatusCode.OK) { Content = new StringContent("\"\"") };
				accepted.Headers.Add("Set-Cookie", "chocolatechip=session; path=/; secure; HttpOnly");
				return Task.FromResult(accepted);
			}

			Queries++;
			LastQueryPath = request.RequestUri!.AbsolutePath;

			return Task.FromResult(new HttpResponseMessage(QueryStatus) { Content = new StringContent(QueryBody) });
		}
	}

	/// <summary>Horizons reduced to one scripted answer, a failure, and a hold.</summary>
	private sealed class ScriptedHorizons : HttpMessageHandler
	{
		public string Body { get; set; } = string.Empty;

		public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

		public Exception? FailWith { get; set; }

		public TaskCompletionSource? Hold { get; set; }

		public int Requests { get; private set; }

		[System.Diagnostics.CodeAnalysis.SuppressMessage(
			"Reliability", "CA2000:Dispose objects before losing scope",
			Justification = "The response is handed to HttpClient, which owns it from here and disposes it.")]
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests++;

			if (Hold is not null)
			{
				await Hold.Task.ConfigureAwait(false);
			}

			return FailWith is not null
				? throw FailWith
				: new HttpResponseMessage(Status) { Content = new StringContent(Body) };
		}
	}

	/// <summary>A clock that moves only when told to.</summary>
	/// <param name="start">The starting instant.</param>
	internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset now = start;

		public override DateTimeOffset GetUtcNow() => now;

		public void Advance(TimeSpan by) => now += by;
	}
}
