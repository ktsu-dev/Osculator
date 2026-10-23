// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Data.SpaceTrack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers reading Space-Track's public conjunction data messages, and querying them through the
/// client's limiter and cache.
/// </summary>
/// <remarks>
/// <c>Data/SpaceTrack/cdm_public.json</c> is in the shape Space-Track's <c>cdm_public</c> class
/// serves — every value a string, times without a zone, the miss distance in metres — but it was
/// built for these tests rather than captured, because this repository holds no Space-Track account.
/// Replacing it with a captured response needs nothing but the file.
/// </remarks>
[TestClass]
public sealed class ConjunctionDataMessageTests
{
	private static readonly DateTimeOffset Start = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

	private readonly List<IDisposable> owned = [];

	private string root = string.Empty;

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-cdm-").FullName;

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
	public void TheParserReadsTheTimeOfClosestApproachTheMissDistanceAndBothObjects()
	{
		IReadOnlyList<ConjunctionDataMessage> messages = ConjunctionDataMessageJson.Read(Fixture());

		Assert.HasCount(3, messages);

		ConjunctionDataMessage first = messages[0];
		Assert.AreEqual("1100000001", first.Id);
		Assert.AreEqual(new DateTimeOffset(2026, 10, 21, 3, 14, 15, 926, TimeSpan.Zero), first.TimeOfClosestApproach);
		Assert.AreEqual(new DateTimeOffset(2026, 10, 19, 21, 14, 7, TimeSpan.Zero), first.Created);
		Assert.AreEqual(0.412, first.MissDistanceKm, 1e-12, "MIN_RNG is metres; the record holds kilometres.");
		Assert.AreEqual(1.372e-4, first.CollisionProbability!.Value, 1e-18);
		Assert.IsTrue(first.EmergencyReportable);
		Assert.AreEqual(25544, first.Primary);
		Assert.AreEqual("ISS (ZARYA)", first.PrimaryName);
		Assert.AreEqual(49863, first.Secondary);
		Assert.AreEqual("COSMOS 1408 DEB", first.SecondaryName);
	}

	[TestMethod]
	public void AnEmptyProbabilityIsAbsentRatherThanZeroOrMalformed()
	{
		IReadOnlyList<ConjunctionDataMessage> messages = ConjunctionDataMessageJson.Read(Fixture());

		Assert.IsNull(messages[1].CollisionProbability);
		Assert.IsFalse(messages[1].EmergencyReportable);
		Assert.AreEqual(4.1e-7, messages[2].CollisionProbability!.Value, 1e-21, "Exponent form is read too.");
	}

	[TestMethod]
	public void EitherPartyCountsAsInvolved()
	{
		IReadOnlyList<ConjunctionDataMessage> messages = ConjunctionDataMessageJson.Read(Fixture());

		Assert.IsTrue(messages[0].Involves(25544));
		Assert.IsTrue(messages[2].Involves(25544), "The ISS is the second object here.");
		Assert.IsFalse(messages[1].Involves(25544));
	}

	[TestMethod]
	public void NumbersWrittenAsJsonNumbersAreReadToo()
	{
		const string Typed = """
			[{"CDM_ID":7,"CREATED":"2026-10-20T00:00:00","TCA":"2026-10-21T00:00:00","MIN_RNG":250.5,"PC":null,
			  "SAT_1_ID":1,"SAT_2_ID":2}]
			""";

		ConjunctionDataMessage message = ConjunctionDataMessageJson.Read(Typed)[0];

		Assert.AreEqual(0.2505, message.MissDistanceKm, 1e-12);
		Assert.AreEqual(1, message.Primary);
		Assert.AreEqual(2, message.Secondary);
		Assert.IsNull(message.PrimaryName);
	}

	[TestMethod]
	[DataRow("""{"error":"no"}""", DisplayName = "An object, not an array")]
	[DataRow("""[{"CDM_ID":"1","CREATED":"2026-10-20 00:00:00","MIN_RNG":"5","SAT_1_ID":"1","SAT_2_ID":"2"}]""", DisplayName = "No TCA")]
	[DataRow("""[{"CDM_ID":"1","CREATED":"2026-10-20 00:00:00","TCA":"2026-10-21T00:00:00","SAT_1_ID":"1","SAT_2_ID":"2"}]""", DisplayName = "No miss distance")]
	[DataRow("""[{"CDM_ID":"1","CREATED":"2026-10-20 00:00:00","TCA":"2026-10-21T00:00:00","MIN_RNG":"5","SAT_1_ID":"1"}]""", DisplayName = "No second object")]
	[DataRow("""[{"CDM_ID":"1","CREATED":"2026-10-20 00:00:00","TCA":"tomorrow","MIN_RNG":"5","SAT_1_ID":"1","SAT_2_ID":"2"}]""", DisplayName = "A TCA that is not a time")]
	[DataRow("""[{"CDM_ID":"1","CREATED":"2026-10-20 00:00:00","TCA":"2026-10-21T00:00:00","MIN_RNG":"far","SAT_1_ID":"1","SAT_2_ID":"2"}]""", DisplayName = "A miss distance that is not a number")]
	public void ABodyThatIsNotAListOfMessagesIsRefused(string body) =>
		Assert.ThrowsExactly<JsonException>(() => ConjunctionDataMessageJson.Read(body));

	[TestMethod]
	public async Task TheConjunctionQueryGoesThroughTheLimiterAndIsCachedForTheHour()
	{
		SpaceTrackHorizonsCacheTests.ScriptedSpaceTrack server = Server(Fixture());
		SpaceTrackHorizonsCacheTests.ManualClock clock = new(Start);
		SpaceTrackRateLimiter limiter = new(clock, 2, 299);
		SpaceTrackClient client = ClientOver(server, clock, limiter);

		Fetched<IReadOnlyList<ConjunctionDataMessage>> all = await client.GetConjunctionsAsync().ConfigureAwait(false);
		Fetched<IReadOnlyList<ConjunctionDataMessage>> iss = await client.GetConjunctionsAsync(25544).ConfigureAwait(false);

		Assert.HasCount(3, all.Value);
		Assert.HasCount(2, iss.Value);
		Assert.IsFalse(iss.IsStale);
		Assert.AreEqual(
			"/basicspacedata/query/class/cdm_public/TCA/%3Enow/orderby/TCA%20asc/format/json",
			server.LastQueryPath);
		Assert.AreEqual(1, server.Queries, "The per-object read filters the cached response rather than asking again.");
		Assert.IsFalse(limiter.TryAcquire(out _), "The login and the one query both took a slot from the limiter.");

		clock.Advance(TimeSpan.FromMinutes(59));
		_ = await client.GetConjunctionsAsync().ConfigureAwait(false);
		Assert.AreEqual(1, server.Queries, "Inside the hour.");

		clock.Advance(TimeSpan.FromMinutes(2));
		_ = await client.GetConjunctionsAsync().ConfigureAwait(false);
		Assert.AreEqual(2, server.Queries, "Past the hour.");
	}

	[TestMethod]
	public async Task ABodyThatIsNotConjunctionsIsNeverCachedInPlaceOfOne()
	{
		SpaceTrackHorizonsCacheTests.ScriptedSpaceTrack server = Server(Fixture());
		SpaceTrackHorizonsCacheTests.ManualClock clock = new(Start);
		SpaceTrackClient client = ClientOver(server, clock, new SpaceTrackRateLimiter(clock));

		await client.GetConjunctionsAsync().ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(2));
		server.QueryBody = "<html>Maintenance</html>";

		Fetched<IReadOnlyList<ConjunctionDataMessage>> served = await client.GetConjunctionsAsync().ConfigureAwait(false);

		Assert.IsTrue(served.IsStale);
		Assert.HasCount(3, served.Value);
	}

	private static string Fixture() => File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Data", "SpaceTrack", "cdm_public.json"));

	private SpaceTrackHorizonsCacheTests.ScriptedSpaceTrack Server(string body)
	{
		SpaceTrackHorizonsCacheTests.ScriptedSpaceTrack server = new() { QueryBody = body };
		owned.Add(server);
		return server;
	}

	private SpaceTrackClient ClientOver(
		SpaceTrackHorizonsCacheTests.ScriptedSpaceTrack server, SpaceTrackHorizonsCacheTests.ManualClock clock, SpaceTrackRateLimiter limiter)
	{
		HttpClient http = new(server, disposeHandler: false);
		SpaceTrackClient client = new(
			http,
			new ResponseCache(Path.Join(root, "cache"), TimeSpan.FromHours(1), clock),
			limiter,
			new SpaceTrackHorizonsCacheTests.FixedCredentials());

		owned.Add(http);
		owned.Add(client);

		return client;
	}
}
