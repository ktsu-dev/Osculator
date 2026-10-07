// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Data.Cddis;
using ktsu.Osculator.Data.CelesTrak;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers fetching ILRS precise orbits from CDDIS.
/// </summary>
/// <remarks>
/// <para>
/// CDDIS needs an Earthdata login, which no test run has, so every test here goes through a stub
/// transport that serves a listing and the committed LAGEOS-1 excerpt, gzipped as the archive
/// serves it. The excerpt is an <c>ilrsb</c> product for 30–31 December 2021, so it is published
/// under the week ending Saturday 1 January 2022.
/// </para>
/// <para>
/// Three obligations are pinned. The archive is asked as rarely as the files allow: a listing once
/// per refetch window, a versioned file never twice. The cache is enough on its own, with no token
/// and no network. And the token never leaves the <c>Authorization</c> header — not into an
/// exception, not into a <c>ToString</c>, not into the cache when a login page comes back instead
/// of a file.
/// </para>
/// </remarks>
[TestClass]
public sealed class CddisClientTests
{
	private const string Token = "eyJ0eXAiOiJKV1QiLCJvcmlnaW4iOiJFYXJ0aGRhdGEifQ-s3cr3t";

	private const string FileName = "ilrsb.orb.lageos1.220101.v70.sp3.gz";

	private static readonly DateOnly Day = new(2021, 12, 30);

	private static readonly DateTimeOffset Start = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

	private readonly List<IDisposable> owned = [];

	private string root = string.Empty;

	private static string Listing =>
		"ilrsa.orb.lageos1.220101.v70.sp3.gz   412345\n"
		+ $"{FileName}   398765\n"
		+ "ilrsb.orb.lageos1.220101.v69.sp3.Z   498765\n";

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-cddis-").FullName;

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
	public async Task AWeekIsListedAndFetchedOnceThenReadFromDisk()
	{
		ArchiveHandler archive = new();
		CddisClient client = ClientOver(archive, new FakeClock(Start), new FixedToken(Token));

		IlrsOrbit first = await client.GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);
		IlrsOrbit second = await client.GetOrbitAsync("lageos1", Day.AddDays(1), "ilrsb").ConfigureAwait(false);

		Assert.AreEqual(FileName, first.Source.Name);
		Assert.AreEqual(31, first.Orbit.Epochs.Count);
		Assert.AreEqual(first.Orbit.Epochs.Count, second.Orbit.Epochs.Count);
		Assert.AreEqual(1, archive.ListingRequests);
		Assert.AreEqual(1, archive.FileRequests);
	}

	[TestMethod]
	public async Task EveryRequestCarriesTheTokenAsABearer()
	{
		ArchiveHandler archive = new();
		CddisClient client = ClientOver(archive, new FakeClock(Start), new FixedToken(Token));

		await client.GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);

		Assert.AreEqual(2, archive.Authorizations.Count);
		Assert.IsTrue(archive.Authorizations.All(a => a == $"Bearer {Token}"));
	}

	[TestMethod]
	public async Task TheListingIsAskedForAgainAfterTheWindowButTheFileNeverIs()
	{
		ArchiveHandler archive = new();
		FakeClock clock = new(Start);
		CddisClient client = ClientOver(archive, clock, new FixedToken(Token));

		await client.GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);
		clock.Advance(Window + TimeSpan.FromMinutes(1));
		await client.GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);

		Assert.AreEqual(2, archive.ListingRequests);
		Assert.AreEqual(1, archive.FileRequests, "A versioned file is immutable and must never be fetched twice.");
	}

	[TestMethod]
	public async Task AReissuedWeekIsPickedUpAtTheNextListing()
	{
		ArchiveHandler archive = new();
		FakeClock clock = new(Start);
		CddisClient client = ClientOver(archive, clock, new FixedToken(Token));

		await client.GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);

		archive.Listing += "ilrsb.orb.lageos1.220101.v71.sp3.gz   398999\n";
		archive.Files["ilrsb.orb.lageos1.220101.v71.sp3.gz"] = Gzip(Sp3ParserTests.Lageos);
		clock.Advance(Window + TimeSpan.FromMinutes(1));

		IlrsOrbit reissued = await client.GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);

		Assert.AreEqual(71, reissued.Source.Version);
		Assert.AreEqual(2, archive.FileRequests);
	}

	[TestMethod]
	public async Task OnceFetchedAWeekIsReadOfflineWithNoTokenAtAll()
	{
		ArchiveHandler archive = new();
		FakeClock clock = new(Start);
		await ClientOver(archive, clock, new FixedToken(Token)).GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);

		ArchiveHandler offline = new() { FailWith = new HttpRequestException("No route to host.") };
		clock.Advance(Window * 30);
		CddisClient client = ClientOver(offline, clock, new FixedToken(null));

		IlrsOrbit orbit = await client.GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);

		Assert.AreEqual(31, orbit.Orbit.Epochs.Count);
		Assert.AreEqual(0, offline.ListingRequests + offline.FileRequests, "With no token the network is never asked.");
	}

	[TestMethod]
	public async Task OnceFetchedAWeekIsReadOfflineWhenTheArchiveIsUnreachable()
	{
		ArchiveHandler archive = new();
		FakeClock clock = new(Start);
		await ClientOver(archive, clock, new FixedToken(Token)).GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);

		ArchiveHandler offline = new() { FailWith = new HttpRequestException("No route to host.") };
		clock.Advance(Window * 30);

		IlrsOrbit orbit = await ClientOver(offline, clock, new FixedToken(Token)).GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);

		Assert.AreEqual(31, orbit.Orbit.Epochs.Count);
		Assert.AreEqual(1, offline.ListingRequests);
	}

	[TestMethod]
	public async Task WithNoTokenAndNothingCachedTheCallerIsToldWhereToPutOne()
	{
		ArchiveHandler archive = new();
		CddisClient client = ClientOver(archive, new FakeClock(Start), new FixedToken(null));

		CddisException refused = await Assert.ThrowsExactlyAsync<CddisException>(
			() => client.GetOrbitAsync("lageos1", Day, "ilrsb")).ConfigureAwait(false);

		StringAssert.Contains(refused.Message, OsCredentialStore.ServiceName);
		Assert.AreEqual(0, archive.ListingRequests);
	}

	[TestMethod]
	public async Task ALoginPageIsARefusalAndIsNeverCached()
	{
		ArchiveHandler archive = new() { RedirectToLogin = true };
		FakeClock clock = new(Start);
		CddisClient client = ClientOver(archive, clock, new FixedToken(Token));

		CddisException refused = await Assert.ThrowsExactlyAsync<CddisException>(
			() => client.GetOrbitAsync("lageos1", Day, "ilrsb")).ConfigureAwait(false);

		StringAssert.Contains(refused.Message, "did not accept the Earthdata token");

		// The login page answered with a 200. Had it been cached, this call would read it back
		// rather than ask again.
		archive.RedirectToLogin = false;
		IlrsOrbit orbit = await client.GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);

		Assert.AreEqual(31, orbit.Orbit.Epochs.Count);
		Assert.AreEqual(2, archive.ListingRequests);
	}

	[TestMethod]
	public async Task ATruncatedDownloadIsNeverCached()
	{
		ArchiveHandler archive = new();
		byte[] whole = archive.Files[FileName];
		archive.Files[FileName] = whole[..(whole.Length / 2)];
		CddisClient client = ClientOver(archive, new FakeClock(Start), new FixedToken(Token));

		await Assert.ThrowsExactlyAsync<CddisException>(() => client.GetOrbitAsync("lageos1", Day, "ilrsb")).ConfigureAwait(false);

		archive.Files[FileName] = whole;
		IlrsOrbit orbit = await client.GetOrbitAsync("lageos1", Day, "ilrsb").ConfigureAwait(false);

		Assert.AreEqual(31, orbit.Orbit.Epochs.Count);
		Assert.AreEqual(2, archive.FileRequests);
	}

	[TestMethod]
	public async Task AnUnpublishedWeekSaysWhy()
	{
		ArchiveHandler archive = new() { NotFound = true };
		CddisClient client = ClientOver(archive, new FakeClock(Start), new FixedToken(Token));

		CddisException missing = await Assert.ThrowsExactlyAsync<CddisException>(
			() => client.GetOrbitAsync("lageos1", Day, "ilrsb")).ConfigureAwait(false);

		StringAssert.Contains(missing.Message, "ten days");
	}

	[TestMethod]
	public async Task AWeekWithOnlyUnixCompressFilesIsRefusedRatherThanFetched()
	{
		ArchiveHandler archive = new() { Listing = "ilrsb.orb.lageos1.220101.v69.sp3.Z   498765\n" };
		CddisClient client = ClientOver(archive, new FakeClock(Start), new FixedToken(Token));

		CddisException refused = await Assert.ThrowsExactlyAsync<CddisException>(
			() => client.GetOrbitAsync("lageos1", Day, "ilrsb")).ConfigureAwait(false);

		StringAssert.Contains(refused.Message, ".Z");
		Assert.AreEqual(0, archive.FileRequests);
	}

	[TestMethod]
	public async Task TheTokenAppearsInNoMessageOnAnyFailurePath()
	{
		List<Exception> failures = [];

		foreach (ArchiveHandler archive in new ArchiveHandler[]
		{
			new() { RedirectToLogin = true },
			new() { NotFound = true },
			new() { FailWith = new HttpRequestException("Connection reset.") },
			new() { Status = HttpStatusCode.Unauthorized },
			new() { Status = HttpStatusCode.InternalServerError },
		})
		{
			CddisClient client = ClientOver(archive, new FakeClock(Start), new FixedToken(Token));
			failures.Add(await Assert.ThrowsExactlyAsync<CddisException>(
				() => client.GetOrbitAsync("lageos1", Day, "ilrsb")).ConfigureAwait(false));
		}

		foreach (Exception failure in failures)
		{
			Assert.IsFalse(failure.ToString().Contains(Token, StringComparison.Ordinal), failure.ToString());
		}

		Assert.IsFalse(new EarthdataToken(Token).ToString().Contains(Token, StringComparison.Ordinal));
		Assert.IsFalse(
			Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
				.Any(path => File.ReadAllText(path).Contains(Token, StringComparison.Ordinal)),
			"The token was written into the cache.");
	}

	[TestMethod]
	public async Task TheCallersCancellationIsNotTurnedIntoAFallback()
	{
		ArchiveHandler archive = new();
		CddisClient client = ClientOver(archive, new FakeClock(Start), new FixedToken(Token));
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync().ConfigureAwait(false);

		await Assert.ThrowsAsync<OperationCanceledException>(
			() => client.GetOrbitAsync("lageos1", Day, "ilrsb", cancelled.Token)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task AnUnknownSatelliteOrCentreIsRefusedBeforeAnythingIsAsked()
	{
		ArchiveHandler archive = new();
		CddisClient client = ClientOver(archive, new FakeClock(Start), new FixedToken(Token));

		await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.GetOrbitAsync("iss", Day)).ConfigureAwait(false);
		await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.GetOrbitAsync("lageos1", Day, "../x")).ConfigureAwait(false);

		Assert.AreEqual(0, archive.ListingRequests + archive.FileRequests);
	}

	private static TimeSpan Window => TimeSpan.FromHours(12);

	private static byte[] Gzip(string text)
	{
		using MemoryStream compressed = new();

		using (GZipStream gzip = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
		{
			byte[] bytes = Encoding.ASCII.GetBytes(text);
			gzip.Write(bytes, 0, bytes.Length);
		}

		return compressed.ToArray();
	}

	private CddisClient ClientOver(ArchiveHandler archive, FakeClock clock, IEarthdataTokenSource tokens)
	{
		HttpClient http = new(archive, disposeHandler: false);
		owned.Add(http);
		owned.Add(archive);
		return new CddisClient(http, new ResponseCache(Path.Join(root, "cache"), Window, clock), tokens);
	}

	private sealed class FixedToken(string? value) : IEarthdataTokenSource
	{
		public EarthdataToken? GetToken() => value is null ? null : new EarthdataToken(value);
	}

	/// <summary>Serves a week's listing and its files, as CDDIS lays them out, and counts what it is asked.</summary>
	private sealed class ArchiveHandler : HttpMessageHandler
	{
		private const string Directory = "/archive/slr/products/orbits/lageos1/220101/";

		public string Listing { get; set; } = CddisClientTests.Listing;

		public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal) { [FileName] = Gzip(Sp3ParserTests.Lageos) };

		public int ListingRequests { get; private set; }

		public int FileRequests { get; private set; }

		public List<string> Authorizations { get; } = [];

		public Exception? FailWith { get; set; }

		public bool RedirectToLogin { get; set; }

		public bool NotFound { get; set; }

		public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

		[System.Diagnostics.CodeAnalysis.SuppressMessage(
			"Reliability", "CA2000:Dispose objects before losing scope",
			Justification = "The response is handed to HttpClient, which owns it from here and disposes it.")]
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			Uri uri = request.RequestUri!;
			Assert.AreEqual(CddisClient.ArchiveHost, uri.Host);
			Assert.IsTrue(uri.AbsolutePath.StartsWith(Directory, StringComparison.Ordinal), uri.ToString());

			string name = uri.AbsolutePath[Directory.Length..];

			if (name == "*")
			{
				ListingRequests++;
				Assert.AreEqual("?list", uri.Query);
			}
			else
			{
				FileRequests++;
			}

			Authorizations.Add(request.Headers.Authorization?.ToString() ?? string.Empty);

			if (FailWith is not null)
			{
				return Task.FromException<HttpResponseMessage>(FailWith);
			}

			if (RedirectToLogin)
			{
				// What an HttpClient that followed the archive's redirect ends on: a 200 and a form,
				// served by the login host, with the bearer header dropped on the way.
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					RequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri("https://urs.earthdata.nasa.gov/oauth/authorize?client_id=x")),
					Content = new StringContent("<html><form action=\"/login\">Earthdata Login</form></html>"),
				});
			}

			if (NotFound)
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
			}

			HttpContent content = name == "*"
				? new StringContent(Listing)
				: Files.TryGetValue(name, out byte[]? body)
					? new ByteArrayContent(body)
					: throw new AssertFailedException($"Asked for {name}, which the listing never offered.");

			return Task.FromResult(new HttpResponseMessage(Status) { RequestMessage = request, Content = content });
		}
	}

	private sealed class FakeClock(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset now = start;

		public override DateTimeOffset GetUtcNow() => now;

		public void Advance(TimeSpan by) => now += by;
	}
}
