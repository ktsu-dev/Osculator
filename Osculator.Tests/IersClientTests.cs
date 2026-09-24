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
using ktsu.Osculator.Data.Iers;
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
		// The reason the body is parsed before it is written. A truncated download or a captive
		// portal's login page is a 200 with a body, and writing it would break every later run —
		// including the offline path, which is exactly when it would be needed.
		CountingHandler handler = Transport(IersSample.Csv);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
		IersClient client = ClientOver(handler, clock, TimeSpan.FromDays(1));

		EarthOrientationTable good = await client.GetTableAsync().ConfigureAwait(false);

		Assert.AreEqual(5, good.Count);

		clock.Advance(TimeSpan.FromDays(2));
		handler.Body = "<html>Sign in to continue</html>";

		EarthOrientationTable afterBadFetch = await client.GetTableAsync().ConfigureAwait(false);

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

		EarthOrientationTable offline = await client.GetTableAsync().ConfigureAwait(false);

		Assert.AreEqual(5, offline.Count);
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

	private sealed class CountingHandler(string body) : HttpMessageHandler
	{
		public int Requests { get; private set; }

		public string Body { get; set; } = body;

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

	private sealed class FakeClock(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset now = start;

		public override DateTimeOffset GetUtcNow() => now;

		public void Advance(TimeSpan by) => now += by;
	}
}
