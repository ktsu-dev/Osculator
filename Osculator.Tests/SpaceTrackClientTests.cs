// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Data.SpaceTrack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the Space-Track client against recorded-shape responses, with no live call.
/// </summary>
/// <remarks>
/// <para>
/// <c>Data/SpaceTrack/gp_history_25544.json</c> is in the shape Space-Track's <c>gp_history</c>
/// class serves — every value a string, <c>DECAY_DATE</c> null, the TLE lines alongside — but it was
/// built for these tests rather than captured, because this repository holds no Space-Track account.
/// The middle record is the same element set the CelesTrak tests use. Replacing it with a captured
/// response needs nothing but the file.
/// </para>
/// <para>
/// Mutation-checked: removing the 401 re-login fails <see cref="AnExpiredSessionLogsInAgainOnce"/>;
/// sending the query before acquiring from the limiter fails
/// <see cref="ABurstOfDistinctQueriesNeverSendsMoreThanTheLimitAllows"/>; and caching a body
/// without parsing it first fails <see cref="AnErrorObjectIsNeverCached"/>.
/// </para>
/// </remarks>
[TestClass]
public sealed class SpaceTrackClientTests
{
	private const string Identity = "osculator@example.com";

	private const string Password = "correct horse battery staple ";

	private static readonly DateTimeOffset Start = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

	private readonly List<IDisposable> owned = [];

	private string root = string.Empty;

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-spacetrack-").FullName;

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
	public async Task TheFirstQueryLogsInAndSendsTheSessionCookie()
	{
		using FakeSpaceTrack server = new() { QueryBody = History() };
		SpaceTrackClient client = ClientOver(server, new ManualClock(Start));

		IReadOnlyList<ElementSet> history = await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.HasCount(3, history);
		Assert.HasCount(2, server.Requests);

		Recorded login = server.Requests[0];
		Assert.AreEqual(HttpMethod.Post, login.Method);
		Assert.AreEqual("/ajaxauth/login", login.Path);
		StringAssert.Contains(login.Body, "identity=osculator%40example.com");

		Recorded query = server.Requests[1];
		Assert.AreEqual(HttpMethod.Get, query.Method);
		Assert.AreEqual("chocolatechip=session-1", query.Cookie);
		Assert.AreEqual("/basicspacedata/query/class/gp_history/NORAD_CAT_ID/25544/orderby/EPOCH%20asc/format/json", query.Path);
	}

	[TestMethod]
	public async Task OneLoginServesEveryQueryInTheSession()
	{
		using FakeSpaceTrack server = new() { QueryBody = History() };
		SpaceTrackClient client = ClientOver(server, new ManualClock(Start));

		await client.GetHistoryAsync(25544).ConfigureAwait(false);
		await client.GetObjectAsync(25544).ConfigureAwait(false);
		await client.GetCatalogueAsync().ConfigureAwait(false);

		Assert.AreEqual(1, server.Logins);
		Assert.AreEqual(3, server.Queries);
	}

	[TestMethod]
	public async Task TheCacheAnswersInsideTheHourWithoutLoggingIn()
	{
		using FakeSpaceTrack server = new() { QueryBody = History() };
		ManualClock clock = new(Start);
		SpaceTrackClient client = ClientOver(server, clock);

		await client.GetHistoryAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromMinutes(59));
		await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.HasCount(2, server.Requests, "Inside the hour: login and one query, nothing more.");

		clock.Advance(TimeSpan.FromMinutes(2));
		await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.AreEqual(2, server.Queries, "Past the hour.");
	}

	[TestMethod]
	public void ACacheShorterThanAnHourIsRefused()
	{
		ResponseCache cache = new(root, TimeSpan.FromMinutes(59), new ManualClock(Start));
		using FakeSpaceTrack server = new();
		using HttpClient http = new(server, disposeHandler: false);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SpaceTrackClient(
			http,
			cache,
			new SpaceTrackRateLimiter(new ManualClock(Start)),
			new FixedCredentials(new SpaceTrackCredentials(Identity, Password))));
	}

	[TestMethod]
	public async Task ABurstOfDistinctQueriesNeverSendsMoreThanTheLimitAllows()
	{
		using FakeSpaceTrack server = new() { QueryBody = "[]" };
		SpaceTrackClient client = ClientOver(server, new ManualClock(Start));

		int refused = 0;
		TimeSpan retryAfter = TimeSpan.Zero;

		// Forty different objects, none cached, in the same instant.
		for (int catalogNumber = 1; catalogNumber <= 40; catalogNumber++)
		{
			try
			{
				await client.GetObjectAsync(catalogNumber).ConfigureAwait(false);
			}
			catch (SpaceTrackRateLimitException refusal)
			{
				refused++;
				retryAfter = refusal.RetryAfter;
			}
		}

		// The login counts: one login and 28 queries is 29 requests, and the rest never left.
		Assert.HasCount(29, server.Requests);
		Assert.AreEqual(1, server.Logins);
		Assert.AreEqual(12, refused);
		Assert.AreEqual(TimeSpan.FromMinutes(1), retryAfter);
	}

	[TestMethod]
	public async Task ARefusedRequestFallsBackToAStaleCopy()
	{
		using FakeSpaceTrack server = new() { QueryBody = History() };
		ManualClock clock = new(Start);
		SpaceTrackClient client = ClientOver(server, clock, new SpaceTrackRateLimiter(clock, 2, 299));

		await client.GetHistoryAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(2));

		// Spend this minute's two requests on two other objects.
		server.QueryBody = "[]";
		await client.GetObjectAsync(1).ConfigureAwait(false);
		await client.GetObjectAsync(2).ConfigureAwait(false);
		await Assert.ThrowsExactlyAsync<SpaceTrackRateLimitException>(() => client.GetObjectAsync(3)).ConfigureAwait(false);

		IReadOnlyList<ElementSet> stale = await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.HasCount(3, stale, "Refused by the limiter, answered from the cache.");
	}

	[TestMethod]
	public async Task AnExpiredSessionLogsInAgainOnce()
	{
		using FakeSpaceTrack server = new() { QueryBody = History() };
		SpaceTrackClient client = ClientOver(server, new ManualClock(Start));

		await client.GetObjectAsync(25544).ConfigureAwait(false);

		server.ExpireSessions();
		await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.AreEqual(2, server.Logins);
		Assert.AreEqual("chocolatechip=session-2", server.Requests[^1].Cookie);
	}

	[TestMethod]
	public async Task AQueryRefusedStraightAfterLoginIsNotRetried()
	{
		using FakeSpaceTrack server = new() { QueryBody = History(), RefuseEverySession = true };
		SpaceTrackClient client = ClientOver(server, new ManualClock(Start));

		await Assert.ThrowsExactlyAsync<SpaceTrackException>(() => client.GetHistoryAsync(25544)).ConfigureAwait(false);

		Assert.AreEqual(1, server.Logins);
		Assert.AreEqual(1, server.Queries);
	}

	[TestMethod]
	public async Task ARejectedLoginSaysSoWithoutThePassword()
	{
		using FakeSpaceTrack server = new() { QueryBody = History(), RejectLogin = true };
		SpaceTrackClient client = ClientOver(server, new ManualClock(Start));

		SpaceTrackException failure = await Assert.ThrowsExactlyAsync<SpaceTrackException>(
			() => client.GetHistoryAsync(25544)).ConfigureAwait(false);

		StringAssert.Contains(failure.Message, Identity);
		Assert.DoesNotContain(Password.Trim(), failure.Message);
		Assert.AreEqual(0, server.Queries);
	}

	[TestMethod]
	public async Task NoStoredAccountMeansNoRequestAtAll()
	{
		using FakeSpaceTrack server = new() { QueryBody = History() };
		SpaceTrackClient client = ClientOver(server, new ManualClock(Start), credentials: new FixedCredentials(null));

		await Assert.ThrowsExactlyAsync<SpaceTrackException>(() => client.GetHistoryAsync(25544)).ConfigureAwait(false);

		Assert.IsEmpty(server.Requests);
	}

	[TestMethod]
	public async Task TheCacheIsAnsweredWithoutReadingTheCredentialStore()
	{
		using FakeSpaceTrack server = new() { QueryBody = History() };
		ManualClock clock = new(Start);
		FixedCredentials credentials = new(new SpaceTrackCredentials(Identity, Password));
		SpaceTrackClient client = ClientOver(server, clock, credentials: credentials);

		await client.GetHistoryAsync(25544).ConfigureAwait(false);
		await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.AreEqual(1, credentials.Reads);
	}

	[TestMethod]
	public async Task AnErrorObjectIsNeverCached()
	{
		using FakeSpaceTrack server = new() { QueryBody = History() };
		ManualClock clock = new(Start);
		SpaceTrackClient client = ClientOver(server, clock);

		await client.GetHistoryAsync(25544).ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(2));

		// How Space-Track reports a broken limit or a bad query: a 200 and an object.
		server.QueryBody = """{"error":"You've violated your query rate limit."}""";
		IReadOnlyList<ElementSet> answer = await client.GetHistoryAsync(25544).ConfigureAwait(false);

		Assert.HasCount(3, answer, "The good copy survives.");
		StringAssert.Contains(client.Cache.ReadAtAnyAge("spacetrack:" + SpaceTrackClient.HistoryQuery(25544, null, null)), "25544");
	}

	[TestMethod]
	public async Task TheHistorySeedsTheSnapshotStore()
	{
		using FakeSpaceTrack server = new() { QueryBody = History() };
		SpaceTrackClient client = ClientOver(server, new ManualClock(Start));
		SnapshotStore store = new(Path.Join(root, "snapshots"));

		int added = await client.SeedHistoryAsync(25544, store).ConfigureAwait(false);
		int again = await client.SeedHistoryAsync(25544, store).ConfigureAwait(false);

		Assert.AreEqual(3, added);
		Assert.AreEqual(0, again, "The same epochs are the same sets.");

		IReadOnlyList<ElementSet> archived = store.History(25544);
		Assert.HasCount(3, archived);
		Assert.AreEqual(15.49234213, archived[1].MeanMotion);
		Assert.AreEqual(0.00047339, archived[1].Eccentricity);
		Assert.AreEqual(58689, archived[1].RevolutionAtEpoch);
		Assert.IsTrue(archived[0].Epoch < archived[1].Epoch && archived[1].Epoch < archived[2].Epoch);
	}

	[TestMethod]
	public async Task NothingWrittenToDiskContainsThePassword()
	{
		using FakeSpaceTrack server = new() { QueryBody = History() };
		SpaceTrackClient client = ClientOver(server, new ManualClock(Start));
		SnapshotStore store = new(Path.Join(root, "snapshots"));

		await client.SeedHistoryAsync(25544, store).ConfigureAwait(false);
		await client.GetCatalogueAsync().ConfigureAwait(false);

		foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
		{
			Assert.DoesNotContain(Password.Trim(), await File.ReadAllTextAsync(file).ConfigureAwait(false), file);
		}
	}

	[TestMethod]
	public void QueriesAreWrittenAsSpaceTrackExpectsThem()
	{
		Assert.AreEqual(
			"/basicspacedata/query/class/gp/NORAD_CAT_ID/25544/format/json",
			SpaceTrackClient.ObjectQuery(25544));
		Assert.AreEqual(
			"/basicspacedata/query/class/gp/decay_date/null-val/epoch/%3Enow-30/orderby/norad_cat_id/format/json",
			SpaceTrackClient.CatalogueQuery());
		Assert.AreEqual(
			"/basicspacedata/query/class/gp_history/NORAD_CAT_ID/25544/EPOCH/2026-09-01--2026-10-01/orderby/EPOCH%20asc/format/json",
			SpaceTrackClient.HistoryQuery(25544, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)));
		Assert.AreEqual(
			"/basicspacedata/query/class/gp_history/NORAD_CAT_ID/25544/EPOCH/%3E2026-09-01/orderby/EPOCH%20asc/format/json",
			SpaceTrackClient.HistoryQuery(25544, new DateOnly(2026, 9, 1), null));
		Assert.ThrowsExactly<ArgumentException>(
			() => SpaceTrackClient.HistoryQuery(25544, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)));
	}

	[TestMethod]
	public void CredentialsNeverPrintThePassword()
	{
		SpaceTrackCredentials credentials = new(Identity, Password);

		Assert.DoesNotContain(Password.Trim(), credentials.ToString());
		StringAssert.Contains(credentials.ToString(), Identity);
	}

	[TestMethod]
	public async Task TheCredentialStoreAsksEachPlatformsOwnTool()
	{
		List<string> commands = [];

		OsCredentialStore linux = new(Identity, OSPlatform.Linux, Record(commands, 0, Password));
		OsCredentialStore mac = new(Identity, OSPlatform.OSX, Record(commands, 0, Password + "\n"));
		OsCredentialStore missing = new(Identity, OSPlatform.Linux, Record(commands, 1, string.Empty));

		SpaceTrackCredentials? fromLinux = await linux.GetAsync(CancellationToken.None).ConfigureAwait(false);
		SpaceTrackCredentials? fromMac = await mac.GetAsync(CancellationToken.None).ConfigureAwait(false);
		SpaceTrackCredentials? none = await missing.GetAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("secret-tool lookup service ktsu.Osculator.SpaceTrack account osculator@example.com", commands[0]);
		Assert.AreEqual("security find-generic-password -s ktsu.Osculator.SpaceTrack -a osculator@example.com -w", commands[1]);

		// The trailing space is part of the password; only the tool's line ending is removed.
		Assert.AreEqual(Password, fromLinux!.Password);
		Assert.AreEqual(Password, fromMac!.Password);
		Assert.IsNull(none);
	}

	[TestMethod]
	public void OnlyTheToolsLineEndingIsRemoved()
	{
		Assert.AreEqual(" pw ", OsCredentialStore.StripLineEnding(" pw \n"));
		Assert.AreEqual(" pw ", OsCredentialStore.StripLineEnding(" pw \r\n"));
		Assert.AreEqual(" pw \n", OsCredentialStore.StripLineEnding(" pw \n\n"));
		Assert.AreEqual(" pw ", OsCredentialStore.StripLineEnding(" pw "));
	}

	private static OsCredentialStore.CommandRunner Record(List<string> commands, int exitCode, string output) =>
		(fileName, arguments, _) =>
		{
			commands.Add(string.Join(' ', [fileName, .. arguments]));
			return Task.FromResult((exitCode, output));
		};

	private static string History() => File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Data", "SpaceTrack", "gp_history_25544.json"));

	private SpaceTrackClient ClientOver(
		FakeSpaceTrack server, ManualClock clock, SpaceTrackRateLimiter? limiter = null, ISpaceTrackCredentialSource? credentials = null)
	{
		HttpClient http = new(server, disposeHandler: false);
		SpaceTrackClient client = new(
			http,
			new ResponseCache(Path.Join(root, "cache"), TimeSpan.FromHours(1), clock),
			limiter ?? new SpaceTrackRateLimiter(clock),
			credentials ?? new FixedCredentials(new SpaceTrackCredentials(Identity, Password)));

		owned.Add(http);
		owned.Add(client);

		return client;
	}

	/// <summary>One request as the fake service saw it.</summary>
	private sealed record Recorded(HttpMethod Method, string Path, string? Cookie, string Body);

	/// <summary>A credential source holding one account, or none, and counting reads.</summary>
	/// <param name="account">The account.</param>
	private sealed class FixedCredentials(SpaceTrackCredentials? account) : ISpaceTrackCredentialSource
	{
		public int Reads { get; private set; }

		public Task<SpaceTrackCredentials?> GetAsync(CancellationToken cancellationToken)
		{
			Reads++;
			return Task.FromResult(account);
		}
	}

	/// <summary>
	/// Space-Track as far as the client can tell: a login that sets a session cookie, and queries
	/// that answer 401 without a live one.
	/// </summary>
	private sealed class FakeSpaceTrack : HttpMessageHandler
	{
		private readonly HashSet<string> liveSessions = [];

		private int sessionsIssued;

		public List<Recorded> Requests { get; } = [];

		public string QueryBody { get; set; } = "[]";

		public bool RejectLogin { get; init; }

		public bool RefuseEverySession { get; init; }

		public int Logins => Requests.Count(request => request.Method == HttpMethod.Post);

		public int Queries => Requests.Count(request => request.Method == HttpMethod.Get);

		public void ExpireSessions() => liveSessions.Clear();

		[System.Diagnostics.CodeAnalysis.SuppressMessage(
			"Reliability", "CA2000:Dispose objects before losing scope",
			Justification = "The response is handed to HttpClient, which owns it from here and disposes it.")]
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			string? cookie = request.Headers.TryGetValues("Cookie", out IEnumerable<string>? values) ? string.Join("; ", values) : null;

			Requests.Add(new Recorded(request.Method, request.RequestUri!.AbsolutePath, cookie, body));

			if (request.Method == HttpMethod.Post)
			{
				if (RejectLogin)
				{
					return new(HttpStatusCode.OK) { Content = new StringContent("""{"Login":"Failed"}""") };
				}

				string session = FormattableString.Invariant($"chocolatechip=session-{++sessionsIssued}");
				liveSessions.Add(session);

				HttpResponseMessage accepted = new(HttpStatusCode.OK) { Content = new StringContent("\"\"") };
				accepted.Headers.Add("Set-Cookie", session + "; expires=Wed, 07-Oct-2026 14:00:00 GMT; path=/; secure; HttpOnly");
				return accepted;
			}

			if (RefuseEverySession || cookie is null || !liveSessions.Contains(cookie))
			{
				return new(HttpStatusCode.Unauthorized) { Content = new StringContent(string.Empty) };
			}

			return new(HttpStatusCode.OK) { Content = new StringContent(QueryBody) };
		}
	}

	/// <summary>A clock that moves only when told to.</summary>
	/// <param name="start">The starting instant.</param>
	private sealed class ManualClock(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset now = start;

		public override DateTimeOffset GetUtcNow() => now;

		public void Advance(TimeSpan by) => now += by;
	}
}
