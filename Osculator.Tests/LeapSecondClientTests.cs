// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Data.Iers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers fetching the leap-second file through the shared response cache.
/// </summary>
[TestClass]
public sealed class LeapSecondClientTests
{
	private readonly List<IDisposable> owned = [];

	private string root = string.Empty;

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-leap-").FullName;

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
	public async Task TheFileIsFetchedOnceAndThenReadFromDisk()
	{
		CountingHandler handler = Transport(TimeScaleTests.IersSample);
		FakeClock clock = new(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));
		LeapSecondClient client = ClientOver(handler, clock, TimeSpan.FromDays(30));

		LeapSeconds first = await client.GetTableAsync().ConfigureAwait(false);
		await client.GetTableAsync().ConfigureAwait(false);

		Assert.AreEqual(1, handler.Requests);
		Assert.AreEqual(new DateOnly(2027, 6, 28), first.ExpiresOn);

		clock.Advance(TimeSpan.FromDays(31));
		await client.GetTableAsync().ConfigureAwait(false);

		Assert.AreEqual(2, handler.Requests);
	}

	[TestMethod]
	public async Task ABadDownloadDoesNotDisplaceAGoodCachedCopy()
	{
		CountingHandler handler = Transport(TimeScaleTests.IersSample);
		FakeClock clock = new(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));
		LeapSecondClient client = ClientOver(handler, clock, TimeSpan.FromDays(30));

		await client.GetTableAsync().ConfigureAwait(false);

		clock.Advance(TimeSpan.FromDays(60));
		handler.Body = "<html>Sign in to continue</html>";

		LeapSeconds afterBadFetch = await client.GetTableAsync().ConfigureAwait(false);

		Assert.AreEqual(new DateOnly(2027, 6, 28), afterBadFetch.ExpiresOn, "The cached copy should have survived.");
	}

	[TestMethod]
	public async Task WithNothingFetchedOrCachedTheBuiltInTableAnswers()
	{
		CountingHandler handler = Transport(TimeScaleTests.IersSample);
		handler.FailWith = new HttpRequestException("no route to host");
		LeapSecondClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromDays(30));

		LeapSeconds table = await client.GetTableAsync().ConfigureAwait(false);

		Assert.AreSame(LeapSeconds.BuiltIn, table);
	}

	[TestMethod]
	public async Task TheCallersOwnCancellationIsNotAnsweredFromAFallback()
	{
		CountingHandler handler = Transport(TimeScaleTests.IersSample);
		LeapSecondClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromDays(30));

		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync().ConfigureAwait(false);

		await Assert.ThrowsAsync<OperationCanceledException>(
			() => client.GetTableAsync(cancelled.Token)).ConfigureAwait(false);
	}

	private CountingHandler Transport(string body)
	{
		CountingHandler handler = new(body);
		owned.Add(handler);
		return handler;
	}

	private LeapSecondClient ClientOver(CountingHandler handler, FakeClock clock, TimeSpan window)
	{
		HttpClient http = new(handler, disposeHandler: false);
		owned.Add(http);
		return new LeapSecondClient(http, new ResponseCache(Path.Join(root, "cache"), window, clock));
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
			cancellationToken.ThrowIfCancellationRequested();

			return FailWith is not null
				? Task.FromException<HttpResponseMessage>(FailWith)
				: Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
		}
	}

	private sealed class FakeClock(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset now = start;

		public override DateTimeOffset GetUtcNow() => now;

		public void Advance(TimeSpan by) => now += by;
	}
}
