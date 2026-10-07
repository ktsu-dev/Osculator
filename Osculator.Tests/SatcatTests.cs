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
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers reading CelesTrak's satellite catalogue and fetching it without asking too often.
/// </summary>
/// <remarks>
/// The fetch tests follow the CelesTrak and IERS client tests: a counting transport and a clock
/// the test moves by hand, so every assertion is about how many times the service was asked.
/// </remarks>
[TestClass]
public sealed class SatcatTests
{
	/// <summary>
	/// Rows in the live file's column layout: two decayed objects from the first launch, two in
	/// orbit, a debris fragment, and an analyst object with no launch date, cross section or
	/// designator. The header is the one CelesTrak documents.
	/// </summary>
	private const string Sample = """
		OBJECT_NAME,OBJECT_ID,NORAD_CAT_ID,OBJECT_TYPE,OPS_STATUS_CODE,OWNER,LAUNCH_DATE,LAUNCH_SITE,DECAY_DATE,PERIOD,INCLINATION,APOGEE,PERIGEE,RCS,DATA_STATUS_CODE,ORBIT_CENTER,ORBIT_TYPE
		SL-1 R/B,1957-001A,1,R/B,D,CIS,1957-10-04,TYMSC,1957-12-01,96.19,65.10,938,214,20.4200,,EA,IMP
		SPUTNIK 1,1957-001B,2,PAY,D,CIS,1957-10-04,TYMSC,1958-01-03,96.10,65.00,1080,64,,,EA,IMP
		VANGUARD 1,1958-002B,5,PAY,,US,1958-03-17,AFETR,,132.71,34.25,3833,654,0.1220,,EA,ORB
		ISS (ZARYA),1998-067A,25544,PAY,+,ISS,1998-11-20,TYMSC,,92.82,51.63,424,416,399.0524,,EA,ORB
		FENGYUN 1C DEB,1999-025AAA,29733,DEB,,PRC,1999-05-10,TSC,,101.03,98.65,862,833,0.0420,,EA,ORB
		TBA - TO BE ASSIGNED,,81234,UNK,,TBD,,,,,,,,,,EA,ORB
		""";

	private readonly List<IDisposable> owned = [];

	private string root = string.Empty;

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-satcat-").FullName;

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
	public void ADecayedObjectHasADecayDateAndAnActiveOneHasNone()
	{
		IReadOnlyList<SatcatRecord> records = SatcatRecord.ParseCsv(Sample);
		SatcatRecord sputnik = records.Single(r => r.NoradCatalogId == 2);
		SatcatRecord iss = records.Single(r => r.NoradCatalogId == 25544);

		Assert.AreEqual(new DateOnly(1958, 1, 3), sputnik.DecayDate);
		Assert.IsTrue(sputnik.HasDecayed);

		// No decay date, not a default one: 0001-01-01 would file the ISS as decayed long ago.
		Assert.IsNull(iss.DecayDate);
		Assert.IsFalse(iss.HasDecayed);
	}

	[TestMethod]
	public void EveryFieldIsReadFromTheRowItBelongsTo()
	{
		SatcatRecord iss = SatcatRecord.ParseCsv(Sample).Single(r => r.NoradCatalogId == 25544);

		Assert.AreEqual("ISS (ZARYA)", iss.ObjectName);
		Assert.AreEqual("1998-067A", iss.ObjectId);
		Assert.AreEqual(SatcatObjectType.Payload, iss.ObjectType);
		Assert.AreEqual(new DateOnly(1998, 11, 20), iss.LaunchDate);
		Assert.AreEqual(399.0524, iss.RadarCrossSection);
		Assert.AreEqual(RcsSize.Large, iss.RcsSize);
	}

	[TestMethod]
	public void ObjectTypeIsSomethingAFilterCanSelectOn()
	{
		IReadOnlyList<SatcatRecord> records = SatcatRecord.ParseCsv(Sample);

		int[] payloads = [.. records.Where(r => r.ObjectType == SatcatObjectType.Payload).Select(r => r.NoradCatalogId)];

		Assert.AreEqual("2,5,25544", string.Join(',', payloads));
		Assert.AreEqual(SatcatObjectType.RocketBody, records.Single(r => r.NoradCatalogId == 1).ObjectType);
		Assert.AreEqual(SatcatObjectType.Debris, records.Single(r => r.NoradCatalogId == 29733).ObjectType);
		Assert.AreEqual(SatcatObjectType.Unknown, records.Single(r => r.NoradCatalogId == 81234).ObjectType);
	}

	[TestMethod]
	public void EmptyFieldsAreAbsentRatherThanZero()
	{
		IReadOnlyList<SatcatRecord> records = SatcatRecord.ParseCsv(Sample);
		SatcatRecord analyst = records.Single(r => r.NoradCatalogId == 81234);

		Assert.IsNull(analyst.ObjectId);
		Assert.IsNull(analyst.LaunchDate);
		Assert.IsNull(analyst.DecayDate);

		// Sputnik has no cross section on file. Zero would class it as the smallest thing in orbit.
		SatcatRecord sputnik = records.Single(r => r.NoradCatalogId == 2);
		Assert.IsNull(sputnik.RadarCrossSection);
		Assert.IsNull(sputnik.RcsSize);
	}

	[TestMethod]
	public void CrossSectionsAreClassedOnSpaceTracksThresholds()
	{
		IReadOnlyList<SatcatRecord> records = SatcatRecord.ParseCsv(Sample);

		Assert.AreEqual(RcsSize.Small, records.Single(r => r.NoradCatalogId == 29733).RcsSize);
		Assert.AreEqual(RcsSize.Medium, records.Single(r => r.NoradCatalogId == 5).RcsSize);
		Assert.AreEqual(RcsSize.Large, records.Single(r => r.NoradCatalogId == 1).RcsSize);
	}

	[TestMethod]
	public void AQuotedNameKeepsItsComma()
	{
		string csv = Header() + "\n\"OBJECT, WITH COMMA\",2000-001A,26000,PAY,,US,2000-01-01,AFETR,,,,,,,,EA,ORB\n";

		Assert.AreEqual("OBJECT, WITH COMMA", SatcatRecord.ParseCsv(csv).Single().ObjectName);
	}

	[TestMethod]
	public void ColumnsAreFoundByNameNotPosition()
	{
		string csv = "NORAD_CAT_ID,OBJECT_TYPE,OBJECT_NAME,OBJECT_ID,LAUNCH_DATE,DECAY_DATE,RCS\n25544,PAY,ISS (ZARYA),1998-067A,1998-11-20,,399.0524\n";
		SatcatRecord iss = SatcatRecord.ParseCsv(csv).Single();

		Assert.AreEqual(25544, iss.NoradCatalogId);
		Assert.AreEqual("ISS (ZARYA)", iss.ObjectName);
	}

	[TestMethod]
	public void ATruncatedDownloadFailsRatherThanLosingItsLastRows()
	{
		// Cut mid-row, as a dropped connection leaves it. Skipping the short row would cache a
		// catalogue that is quietly missing objects for the whole refetch window.
		string truncated = Sample[..Sample.IndexOf("TSC,,101.03", StringComparison.Ordinal)];

		Assert.ThrowsExactly<FormatException>(() => SatcatRecord.ParseCsv(truncated));
	}

	[TestMethod]
	[DataRow("ISS (ZARYA),1998-067A,25544,SAT,+,ISS,1998-11-20,TYMSC,,92.82,51.63,424,416,399.0524,,EA,ORB", DisplayName = "unknown object type")]
	[DataRow("ISS (ZARYA),1998-067A,25544,PAY,+,ISS,20/11/1998,TYMSC,,92.82,51.63,424,416,399.0524,,EA,ORB", DisplayName = "malformed launch date")]
	[DataRow("ISS (ZARYA),1998-067A,25544,PAY,+,ISS,1998-11-20,TYMSC,never,92.82,51.63,424,416,399.0524,,EA,ORB", DisplayName = "malformed decay date")]
	[DataRow("ISS (ZARYA),1998-067A,25544,PAY,+,ISS,1998-11-20,TYMSC,,92.82,51.63,424,416,big,,EA,ORB", DisplayName = "malformed cross section")]
	[DataRow("ISS (ZARYA),1998-067A,,PAY,+,ISS,1998-11-20,TYMSC,,92.82,51.63,424,416,399.0524,,EA,ORB", DisplayName = "no catalogue number")]
	[DataRow(",1998-067A,25544,PAY,+,ISS,1998-11-20,TYMSC,,92.82,51.63,424,416,399.0524,,EA,ORB", DisplayName = "no name")]
	[DataRow("ISS (ZARYA),1998-067A,25544,,+,ISS,1998-11-20,TYMSC,,92.82,51.63,424,416,399.0524,,EA,ORB", DisplayName = "no object type")]
	[DataRow("\"ISS (ZARYA),1998-067A,25544,PAY,+,ISS,1998-11-20,TYMSC,,92.82,51.63,424,416,399.0524,,EA,ORB", DisplayName = "unterminated quote")]
	public void AMalformedRowIsRejectedRatherThanDefaulted(string row) =>
		Assert.ThrowsExactly<FormatException>(() => SatcatRecord.ParseCsv(Header() + "\n" + row + "\n"));

	[TestMethod]
	public void ARepeatedCatalogueNumberIsRejected()
	{
		string row = "ISS (ZARYA),1998-067A,25544,PAY,+,ISS,1998-11-20,TYMSC,,92.82,51.63,424,416,399.0524,,EA,ORB";

		Assert.ThrowsExactly<FormatException>(() => SatcatRecord.ParseCsv(Header() + "\n" + row + "\n" + row + "\n"));
	}

	[TestMethod]
	[DataRow("", DisplayName = "empty body")]
	[DataRow("<html>Sign in to continue</html>", DisplayName = "captive portal")]
	[DataRow("OBJECT_NAME,OBJECT_ID,NORAD_CAT_ID,OBJECT_TYPE,OPS_STATUS_CODE,OWNER,LAUNCH_DATE,LAUNCH_SITE,DECAY_DATE,PERIOD,INCLINATION,APOGEE,PERIGEE,RCS,DATA_STATUS_CODE,ORBIT_CENTER,ORBIT_TYPE\n", DisplayName = "header only")]
	public void SomethingThatIsNotTheCatalogueIsRejected(string body) =>
		Assert.ThrowsExactly<FormatException>(() => SatcatRecord.ParseCsv(body));

	[TestMethod]
	public async Task TheCatalogueIsNotRefetchedInsideTheWindow()
	{
		CountingHandler handler = Transport(Sample);
		FakeClock clock = new(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));
		SatcatClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetCatalogueAsync().ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(3, 59));
		IReadOnlyList<SatcatRecord> cached = await client.GetCatalogueAsync().ConfigureAwait(false);

		Assert.AreEqual(1, handler.Requests, "Inside the window the service is not asked again.");
		Assert.HasCount(6, cached);

		clock.Advance(TimeSpan.FromMinutes(2));
		await client.GetCatalogueAsync().ConfigureAwait(false);

		Assert.AreEqual(2, handler.Requests, "Past the window.");
	}

	[TestMethod]
	public async Task AnUnparseableResponseDoesNotDisplaceAGoodCachedCopy()
	{
		CountingHandler handler = Transport(Sample);
		FakeClock clock = new(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));
		SatcatClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetCatalogueAsync().ConfigureAwait(false);

		clock.Advance(TimeSpan.FromHours(5));
		handler.Body = "<html>Sign in to continue</html>";
		IReadOnlyList<SatcatRecord> afterBadFetch = await client.GetCatalogueAsync().ConfigureAwait(false);

		Assert.AreEqual(2, handler.Requests);
		Assert.HasCount(6, afterBadFetch, "The bad body is answered from the cached copy.");

		// And the copy on disk is still the good one, which is what every later run reads.
		clock.Advance(TimeSpan.FromHours(5));
		handler.FailWith = new HttpRequestException("no route to host");
		IReadOnlyList<SatcatRecord> offline = await client.GetCatalogueAsync().ConfigureAwait(false);

		Assert.HasCount(6, offline);
	}

	[TestMethod]
	public async Task WithTheNetworkGoneItFallsBackToWhateverIsCached()
	{
		CountingHandler handler = Transport(Sample);
		FakeClock clock = new(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));
		SatcatClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetCatalogueAsync().ConfigureAwait(false);

		clock.Advance(TimeSpan.FromDays(30));
		handler.FailWith = new HttpRequestException("no route to host");

		IReadOnlyList<SatcatRecord> offline = await client.GetCatalogueAsync().ConfigureAwait(false);

		Assert.HasCount(6, offline);
	}

	[TestMethod]
	public async Task WithTheNetworkGoneAndNothingCachedItSaysSo()
	{
		CountingHandler handler = Transport(Sample);
		handler.FailWith = new HttpRequestException("no route to host");
		SatcatClient client = ClientOver(handler, new FakeClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(4));

		CelesTrakException failure = await Assert.ThrowsExactlyAsync<CelesTrakException>(
			() => client.GetCatalogueAsync()).ConfigureAwait(false);

		Assert.IsInstanceOfType<HttpRequestException>(failure.InnerException);
	}

	[TestMethod]
	public async Task TheCallersOwnCancellationIsNotAnsweredFromTheStaleCache()
	{
		CountingHandler handler = Transport(Sample);
		FakeClock clock = new(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));
		SatcatClient client = ClientOver(handler, clock, TimeSpan.FromHours(4));

		await client.GetCatalogueAsync().ConfigureAwait(false);
		clock.Advance(TimeSpan.FromHours(5));

		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync().ConfigureAwait(false);

		await Assert.ThrowsAsync<OperationCanceledException>(
			() => client.GetCatalogueAsync(cancelled.Token)).ConfigureAwait(false);
	}

	private static string Header() => Sample[..Sample.IndexOf('\n', StringComparison.Ordinal)];

	private CountingHandler Transport(string body)
	{
		CountingHandler handler = new(body);
		owned.Add(handler);
		return handler;
	}

	private SatcatClient ClientOver(CountingHandler handler, FakeClock clock, TimeSpan window)
	{
		HttpClient http = new(handler, disposeHandler: false);
		owned.Add(http);
		return new SatcatClient(http, new ResponseCache(Path.Join(root, "cache"), window, clock));
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
