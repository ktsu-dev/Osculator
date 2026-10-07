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
/// Covers what the OMM reader's failures mean to the CelesTrak cache: a 200 whose body is not a
/// usable element set must neither replace the good cached copy nor escape as something other than
/// the failure the client falls back on.
/// </summary>
/// <remarks>
/// Both cases used to get through. A record missing its orbital fields parsed, as zeros, and so
/// passed the parse-before-cache check and was written over the good copy. An epoch with three
/// fractional digits raised <see cref="FormatException"/>, which the fallback does not catch, so
/// the call failed outright with a perfectly good set on disk.
/// </remarks>
[TestClass]
public sealed class OmmCacheFallbackTests
{
	/// <summary>One real response, as the live service returned it.</summary>
	private const string IssResponse = """
		[{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-09-22T20:26:37.026816",
		"MEAN_MOTION":15.49234213,"ECCENTRICITY":0.00047339,"INCLINATION":51.6316,
		"RA_OF_ASC_NODE":176.7315,"ARG_OF_PERICENTER":169.8211,"MEAN_ANOMALY":190.2874,
		"EPHEMERIS_TYPE":0,"CLASSIFICATION_TYPE":"U","NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,
		"REV_AT_EPOCH":58689,"BSTAR":0.00014639046,"MEAN_MOTION_DOT":7.689e-5,"MEAN_MOTION_DDOT":0}]
		""";

	private string root = string.Empty;

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-omm-fallback-").FullName;

	[TestCleanup]
	public void TearDown() => Directory.Delete(root, recursive: true);

	[TestMethod]
	public async Task ARecordMissingItsOrbitDoesNotDisplaceTheGoodCachedCopy()
	{
		await AssertFallsBackAsync("""[{"OBJECT_NAME":"X","EPOCH":"2026-09-30T00:00:00.000000","NORAD_CAT_ID":25544}]""").ConfigureAwait(false);
	}

	[TestMethod]
	public async Task AnUnreadableEpochFallsBackRatherThanEscaping()
	{
		await AssertFallsBackAsync(IssResponse.Replace("2026-09-22T20:26:37.026816", "22/09/2026 20:26", StringComparison.Ordinal)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task AnEpochInAnotherIsoSpellingIsAcceptedAndCached()
	{
		using SwitchableHandler handler = new(IssResponse.Replace("2026-09-22T20:26:37.026816", "2026-09-22T20:26:37.027+00:00", StringComparison.Ordinal));
		using HttpClient http = new(handler, disposeHandler: false);
		CelesTrakClient client = new(http, new ResponseCache(Path.Join(root, "cache"), TimeSpan.FromHours(4), TimeProvider.System));

		IReadOnlyList<ElementSet> sets = await client.GetObjectAsync(25544).ConfigureAwait(false);

		Assert.AreEqual(new DateTime(2026, 9, 22, 20, 26, 37, 27, DateTimeKind.Utc), sets[0].Epoch);
	}

	/// <summary>
	/// Caches a good set, moves past the freshness window, serves <paramref name="badBody"/> as a
	/// 200, and checks the good set comes back both from that call and from the cache afterwards.
	/// </summary>
	/// <param name="badBody">A body that is not a usable element set.</param>
	/// <returns>A task that completes when the checks have run.</returns>
	private async Task AssertFallsBackAsync(string badBody)
	{
		using SwitchableHandler handler = new(IssResponse);
		using HttpClient http = new(handler, disposeHandler: false);
		FakeClock clock = new(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
		CelesTrakClient client = new(http, new ResponseCache(Path.Join(root, "cache"), TimeSpan.FromHours(4), clock));

		await client.GetObjectAsync(25544).ConfigureAwait(false);

		clock.Advance(TimeSpan.FromHours(5));
		handler.Body = badBody;

		IReadOnlyList<ElementSet> afterBadBody = await client.GetObjectAsync(25544).ConfigureAwait(false);

		Assert.HasCount(1, afterBadBody);
		Assert.AreEqual(15.49234213, afterBadBody[0].MeanMotion, "The good copy should have been returned.");

		// And the bad body was never written: with the network gone, the cache still holds the good one.
		handler.FailWith = new HttpRequestException("no route to host");
		clock.Advance(TimeSpan.FromHours(5));

		IReadOnlyList<ElementSet> offline = await client.GetObjectAsync(25544).ConfigureAwait(false);

		Assert.AreEqual(15.49234213, offline[0].MeanMotion, "The cache should still hold the good copy.");
	}

	/// <summary>A transport whose answer the test can change between calls.</summary>
	/// <param name="body">The body to answer with.</param>
	private sealed class SwitchableHandler(string body) : HttpMessageHandler
	{
		/// <summary>Gets or sets the body to answer with.</summary>
		public string Body { get; set; } = body;

		/// <summary>Gets or sets a failure to throw instead of answering.</summary>
		public Exception? FailWith { get; set; }

		[System.Diagnostics.CodeAnalysis.SuppressMessage(
			"Reliability", "CA2000:Dispose objects before losing scope",
			Justification = "The response is handed to HttpClient, which owns it from here and disposes it.")]
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			FailWith is not null
				? Task.FromException<HttpResponseMessage>(FailWith)
				: Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
	}

	/// <summary>A clock the test moves by hand.</summary>
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
