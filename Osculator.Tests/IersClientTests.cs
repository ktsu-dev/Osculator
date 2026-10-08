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
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Data.Iers;
using CountingHandler = ktsu.Osculator.Tests.CelesTrakClientTests.CountingHandler;
using FakeClock = ktsu.Osculator.Tests.CelesTrakClientTests.FakeClock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers fetching the IERS series.
/// </summary>
/// <remarks>
/// The same obligation as the CelesTrak client — someone else's bandwidth, a series that changes
/// once a day at most — so the same discipline: a counting transport and a clock the test moves by
/// hand, with every assertion about how many times the service was actually asked.
/// </remarks>
[TestClass]
public sealed class IersClientTests
{
	private readonly List<IDisposable> owned = [];

	private string root = string.Empty;

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-iers-").FullName;

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
	public async Task TheSeriesIsFetchedOnceAndThenReadFromDisk()
	{
		CountingHandler handler = Transport(IersSample.Csv);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		IersClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));

		await client.GetTableAsync().ConfigureAwait(false);
		await client.GetTableAsync().ConfigureAwait(false);

		Assert.AreEqual(1, handler.Requests);

		clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
		await client.GetTableAsync().ConfigureAwait(false);

		Assert.AreEqual(2, handler.Requests, "A day later the IERS has published again.");
	}

	[TestMethod]
	public async Task ABadDownloadDoesNotDisplaceAGoodCachedCopy()
	{
		// The reason the body is parsed before it is written. A captive portal's login page is a 200
		// with a body, and writing it would break every later run — including the offline path,
		// which is exactly when it would be needed. A truncated download parses; the test below
		// covers that one.
		CountingHandler handler = Transport(IersSample.Csv);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		IersClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));

		EarthOrientationTable good = (await client.GetTableAsync().ConfigureAwait(false)).Value;

		Assert.AreEqual(5, good.Count);

		clock.Advance(TimeSpan.FromDays(2));
		handler.Body = "<html>Sign in to continue</html>";

		EarthOrientationTable afterBadFetch = (await client.GetTableAsync().ConfigureAwait(false)).Value;

		Assert.AreEqual(5, afterBadFetch.Count, "The cached copy should have survived.");
	}

	[TestMethod]
	public async Task WithTheNetworkGoneItFallsBackToWhateverIsCached()
	{
		CountingHandler handler = Transport(IersSample.Csv);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		IersClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));

		await client.GetTableAsync().ConfigureAwait(false);

		clock.Advance(TimeSpan.FromDays(30));
		handler.FailWith = new HttpRequestException("no route to host");

		EarthOrientationTable offline = (await client.GetTableAsync().ConfigureAwait(false)).Value;

		Assert.AreEqual(5, offline.Count);
	}

	[TestMethod]
	public async Task TheCallersOwnCancellationIsNotAnsweredFromTheStaleCache()
	{
		CountingHandler handler = Transport(IersSample.Csv);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
		IersClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));

		await client.GetTableAsync().ConfigureAwait(false);
		clock.Advance(TimeSpan.FromDays(2));

		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync().ConfigureAwait(false);

		// Stale data from an abandoned call would read as a success. Cancelling is not a timeout.
		await Assert.ThrowsAsync<OperationCanceledException>(
			() => client.GetTableAsync(cancelled.Token)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task WithTheNetworkGoneAndNothingCachedItSaysSo()
	{
		CountingHandler handler = Transport(IersSample.Csv);
		handler.FailWith = new HttpRequestException("no route to host");
		IersClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromDays(1));

		IersException failure = await Assert.ThrowsExactlyAsync<IersException>(
			() => client.GetTableAsync()).ConfigureAwait(false);

		Assert.IsInstanceOfType<HttpRequestException>(failure.InnerException);
	}

	[TestMethod]
	public async Task ATruncatedDownloadDoesNotShrinkTheCachedTable()
	{
		CountingHandler handler = Transport(IersSample.Csv);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		IersClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));

		EarthOrientationTable good = (await client.GetTableAsync().ConfigureAwait(false)).Value;

		// The connection drops partway through a row. What is left keeps the header and two whole
		// rows, so it parses — it just ends twenty years early, in the real file's terms.
		clock.Advance(TimeSpan.FromDays(2));
		handler.Body = IersSample.Csv[..(IersSample.Csv.IndexOf("\n61298;", StringComparison.Ordinal) + 20)];

		Fetched<EarthOrientationTable> online = await client.GetTableAsync().ConfigureAwait(false);

		Assert.AreEqual(good.Count, online.Value.Count);
		Assert.AreEqual(good.LastModifiedJulianDate, online.Value.LastModifiedJulianDate);
		Assert.IsTrue(online.IsStale, "The good copy is standing in for a download that failed.");

		// And it was not written: the offline path still has the whole table.
		clock.Advance(TimeSpan.FromDays(30));
		handler.FailWith = new HttpRequestException("no route to host");

		EarthOrientationTable offline = (await client.GetTableAsync().ConfigureAwait(false)).Value;

		Assert.AreEqual(good.LastModifiedJulianDate, offline.LastModifiedJulianDate);
	}

	[TestMethod]
	public async Task OverlappingCallsShareOneDownload()
	{
		CountingHandler handler = Transport(IersSample.Csv);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		handler.Gate = release.Task;
		IersClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromDays(1));

		Task<Fetched<EarthOrientationTable>>[] calls = [.. Enumerable.Range(0, 8).Select(_ => Task.Run(() => client.GetTableAsync()))];
		await CelesTrakClientTests.WaitForRequestsAsync(handler, 1).ConfigureAwait(false);
		release.SetResult();

		Fetched<EarthOrientationTable>[] results = await Task.WhenAll(calls).ConfigureAwait(false);

		Assert.AreEqual(1, handler.Requests, "Four megabytes, once.");
		Assert.IsTrue(results.All(r => r.Value.Count == 5));
	}

	[TestMethod]
	public async Task AStaleFallbackSaysSo()
	{
		CountingHandler handler = Transport(IersSample.Csv);
		DateTimeOffset start = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
		FakeClock clock = new(start);
		IersClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));

		Fetched<EarthOrientationTable> fresh = await client.GetTableAsync().ConfigureAwait(false);

		clock.Advance(TimeSpan.FromDays(3));
		handler.FailWith = new HttpRequestException("no route to host");

		Fetched<EarthOrientationTable> stale = await client.GetTableAsync().ConfigureAwait(false);

		Assert.IsFalse(fresh.IsStale);
		Assert.IsTrue(stale.IsStale);
		Assert.AreEqual(start, stale.FetchedAt);
	}

	[TestMethod]
	public async Task AFailingServerIsNotAskedOnEveryCall()
	{
		CountingHandler handler = Transport(IersSample.Csv);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		IersClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));

		await client.GetTableAsync().ConfigureAwait(false);
		clock.Advance(TimeSpan.FromDays(2));
		handler.Status = HttpStatusCode.ServiceUnavailable;

		for (int i = 0; i < 5; i++)
		{
			await client.GetTableAsync().ConfigureAwait(false);
		}

		Assert.AreEqual(2, handler.Requests);
	}

	[TestMethod]
	public async Task ACacheThatCannotBeWrittenStillReturnsTheTableAndDownloadsItOnce()
	{
		CountingHandler handler = Transport(IersSample.Csv);
		string blocked = Path.Join(root, "blocked");
		await File.WriteAllTextAsync(blocked, "not a directory").ConfigureAwait(false);
		HttpClient http = new(handler, disposeHandler: false);
		owned.Add(http);
		IersClient client = new(http, new ResponseCache(blocked, TimeSpan.FromDays(1), new FakeClock(DateTimeOffset.UnixEpoch)));

		for (int i = 0; i < 3; i++)
		{
			Assert.AreEqual(5, (await client.GetTableAsync().ConfigureAwait(false)).Value.Count);
		}

		Assert.AreEqual(1, handler.Requests);
	}

	private CountingHandler Transport(string body)
	{
		CountingHandler handler = new(body);
		owned.Add(handler);
		return handler;
	}

	private IersClient ClientOver(CountingHandler handler, FakeClock clock, TimeSpan window)
	{
		HttpClient http = new(handler, disposeHandler: false);
		owned.Add(http);
		return new IersClient(http, new ResponseCache(Path.Join(root, "cache"), window, clock));
	}
}
