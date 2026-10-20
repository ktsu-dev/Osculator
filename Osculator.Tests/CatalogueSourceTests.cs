// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data;
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Data.SpaceTrack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The full catalogue and its object-type filter (#128, #122): which source the catalogue comes
/// from, what each source contributes, how the satellite catalogue is joined, and how the type
/// filter combines with the orbit-class filter.
/// </summary>
/// <remarks>
/// Every service is a stub transport routed by URL, so each test can say exactly what CelesTrak,
/// the satellite catalogue and Space-Track answered, and count what was asked of them.
/// </remarks>
[TestClass]
public sealed class CatalogueSourceTests
{
	private const string SatcatHeader =
		"OBJECT_NAME,OBJECT_ID,NORAD_CAT_ID,OBJECT_TYPE,OPS_STATUS_CODE,OWNER,LAUNCH_DATE,LAUNCH_SITE,DECAY_DATE,PERIOD,INCLINATION,APOGEE,PERIGEE,RCS,DATA_STATUS_CODE,ORBIT_CENTER,ORBIT_TYPE";

	/// <summary>Rows for a payload, a rocket body and a fragment. The analyst object has no row at all.</summary>
	private static readonly string Satcat = string.Join('\n',
		SatcatHeader,
		"ISS (ZARYA),1998-067A,25544,PAY,+,ISS,1998-11-20,TYMSC,,92.82,51.63,424,416,399.0524,,EA,ORB",
		"CZ-2C R/B,2026-201B,66001,R/B,,PRC,2026-10-01,TSC,,95.00,97.50,600,500,8.1000,,EA,ORB",
		"FENGYUN 1C DEB,1999-025AAA,29733,DEB,,PRC,1999-05-10,TSC,,101.03,98.65,862,833,0.0420,,EA,ORB",
		"GOES 16,2016-071A,41866,PAY,+,US,2016-11-19,AFETR,,1436.10,0.05,35800,35780,10.0000,,EA,ORB");

	private static readonly int[] CelesTrakIds = [25544, 29733, 41866, 66001];

	private static readonly int[] RocketBodiesAndDebris = [2, 3, 4];

	private static readonly int[] LeoDebris = [3];

	private static readonly DateTime Epoch = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

	private static readonly DateTimeOffset Now = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

	private readonly List<IDisposable> owned = [];

	private string root = string.Empty;

	[TestInitialize]
	public void SetUp() => root = Directory.CreateTempSubdirectory("osculator-catalogue-source-").FullName;

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
	public async Task WithoutAnAccount_TheCatalogueIsCelesTraksGroups_DebrisAndRocketBodiesIncluded_AndSaysItIsPartial()
	{
		FakeServices services = Services();
		CatalogueLoad load = await Loader(services, spaceTrack: false).LoadAsync().ConfigureAwait(false);

		Assert.AreEqual(CatalogueSource.CelesTrak, load.Source);
		Assert.IsTrue(load.IsPartial);
		Assert.IsTrue(load.HasObjectTypes);
		CollectionAssert.AreEqual(CelesTrakIds, load.View.All.Select(e => e.Elements.NoradCatalogId).ToArray());
		Assert.AreEqual(SatcatObjectType.Debris, TypeOf(load, 29733));
		Assert.AreEqual(SatcatObjectType.RocketBody, TypeOf(load, 66001));

		// Every group was asked for exactly once, and nothing was asked of Space-Track.
		foreach (string group in CatalogueLoader.CelesTrakGroups)
		{
			Assert.AreEqual(1, services.Count("GROUP=" + group), group);
		}

		Assert.AreEqual(0, services.SpaceTrackRequests);
	}

	[TestMethod]
	public async Task AnObjectInSeveralGroups_IsKeptOnce_WithItsNewestElementSet()
	{
		FakeServices services = Services();
		services.Groups["last-30-days"] = Omm((25544, "ISS (ZARYA)", 6), (66001, "CZ-2C R/B", 0));

		CatalogueLoad load = await Loader(services, spaceTrack: false).LoadAsync().ConfigureAwait(false);

		CatalogueEntry[] iss = [.. load.View.All.Where(e => e.Elements.NoradCatalogId == 25544)];
		Assert.HasCount(1, iss);
		Assert.AreEqual(Epoch.AddHours(6), iss[0].Elements.Epoch);
	}

	[TestMethod]
	public async Task ADebrisGroupTheServiceNoLongerAnswers_IsANote_NotAFailure()
	{
		FakeServices services = Services();
		_ = services.Groups.Remove("iridium-33-debris");

		CatalogueLoad load = await Loader(services, spaceTrack: false).LoadAsync().ConfigureAwait(false);

		Assert.AreEqual(4, load.View.All.Count);
		Assert.IsTrue(load.Notes.Any(note => note.Contains("iridium-33-debris", StringComparison.Ordinal)), string.Join(" | ", load.Notes));
	}

	[TestMethod]
	public async Task WithoutTheActiveGroup_ThereIsNoCatalogue()
	{
		FakeServices services = Services();
		_ = services.Groups.Remove(CatalogueLoader.RequiredGroup);

		await Assert.ThrowsExactlyAsync<CelesTrakException>(() => Loader(services, spaceTrack: false).LoadAsync()).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task WithoutTheSatelliteCatalogue_EveryObjectIsUnknown_AndThePanelIsTold()
	{
		FakeServices services = Services();
		services.SatcatAvailable = false;

		CatalogueLoad load = await Loader(services, spaceTrack: false).LoadAsync().ConfigureAwait(false);

		Assert.IsFalse(load.HasObjectTypes);
		Assert.IsTrue(load.View.All.All(entry => entry.ObjectType == SatcatObjectType.Unknown));
		Assert.AreEqual(load.View.All.Count, load.View.TypeCounts[(int)SatcatObjectType.Unknown]);
		Assert.IsTrue(load.Notes.Any(note => note.Contains("satellite catalogue", StringComparison.Ordinal)));
	}

	[TestMethod]
	public async Task AnObjectTheSatelliteCatalogueHasNoRowFor_IsUnknown()
	{
		FakeServices services = Services();
		services.Groups["last-30-days"] = Omm((81234, "TBA - TO BE ASSIGNED", 0));

		CatalogueLoad load = await Loader(services, spaceTrack: false).LoadAsync().ConfigureAwait(false);

		Assert.AreEqual(SatcatObjectType.Unknown, TypeOf(load, 81234));
	}

	[TestMethod]
	public async Task OfflineAfterOneLoad_TheSameCatalogueComesBackFromTheCache()
	{
		FakeServices services = Services();
		CatalogueLoad online = await Loader(services, spaceTrack: false).LoadAsync().ConfigureAwait(false);

		// A day later every cached copy is past its window, and the service cannot be reached.
		services.Offline = true;
		CatalogueLoad offline = await Loader(services, spaceTrack: false, Now.AddDays(1)).LoadAsync().ConfigureAwait(false);

		Assert.IsTrue(offline.Notes.Any(note => note.Contains("cache", StringComparison.Ordinal)), string.Join(" | ", offline.Notes));
		Assert.IsTrue(offline.HasObjectTypes);

		CollectionAssert.AreEqual(
			online.View.All.Select(e => (e.Elements.NoradCatalogId, e.ObjectType)).ToArray(),
			offline.View.All.Select(e => (e.Elements.NoradCatalogId, e.ObjectType)).ToArray());
	}

	[TestMethod]
	public async Task WithAnAccount_TheCatalogueIsSpaceTracksGp_AndCelesTrakIsNotAskedForElements()
	{
		FakeServices services = Services();
		services.SpaceTrackBody = Omm((25544, "ISS (ZARYA)", 0), (66001, "CZ-2C R/B", 0), (29733, "FENGYUN 1C DEB", 0), (41866, "GOES 16", 0), (90001, "OBJECT A", 0));

		CatalogueLoad load = await Loader(services, spaceTrack: true).LoadAsync().ConfigureAwait(false);

		Assert.AreEqual(CatalogueSource.SpaceTrack, load.Source);
		Assert.IsFalse(load.IsPartial);
		Assert.AreEqual(5, load.View.All.Count);
		Assert.AreEqual(0, services.Count("GROUP="));
		Assert.AreEqual(1, services.Count("satcat.csv"));
	}

	[TestMethod]
	public async Task WhenSpaceTrackRefusesTheLogin_TheCatalogueFallsBackToCelesTrak_AndSaysWhy()
	{
		FakeServices services = Services();
		services.RejectLogin = true;

		CatalogueLoad load = await Loader(services, spaceTrack: true).LoadAsync().ConfigureAwait(false);

		Assert.AreEqual(CatalogueSource.CelesTrak, load.Source);
		Assert.IsTrue(load.IsPartial);
		Assert.IsTrue(load.Notes.Any(note => note.Contains("Space-Track", StringComparison.Ordinal)));
		Assert.AreEqual(4, load.View.All.Count);
	}

	[TestMethod]
	public async Task SelectingAnObject_SeedsItsHistoryFromSpaceTrack_IntoTheSnapshotStore()
	{
		FakeServices services = Services();
		services.SpaceTrackBody = await File.ReadAllTextAsync(Path.Join(AppContext.BaseDirectory, "Data", "SpaceTrack", "gp_history_25544.json")).ConfigureAwait(false);
		SnapshotStore store = new(Path.Join(root, "snapshots"));
		CatalogueLoader loader = Loader(services, spaceTrack: true);

		Assert.IsTrue(loader.CanSeedHistory);

		int added = await loader.SeedHistoryAsync(25544, store).ConfigureAwait(false);
		int expected = OmmJson.Read(services.SpaceTrackBody).Select(set => set.Epoch).Distinct().Count();

		Assert.IsTrue(added > 0);
		Assert.AreEqual(expected, added);
		Assert.AreEqual(expected, store.History(25544).Count);

		// Asked through the history query, once, and a second seed is answered by the cache.
		Assert.AreEqual(1, services.Count("gp_history/NORAD_CAT_ID/25544"));
		Assert.AreEqual(0, await loader.SeedHistoryAsync(25544, store).ConfigureAwait(false));
		Assert.AreEqual(1, services.Count("gp_history/NORAD_CAT_ID/25544"));
	}

	[TestMethod]
	public async Task WithoutAnAccount_HistoryCannotBeSeeded()
	{
		CatalogueLoader loader = Loader(Services(), spaceTrack: false);

		Assert.IsFalse(loader.CanSeedHistory);
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => loader.SeedHistoryAsync(25544, new SnapshotStore(Path.Join(root, "snapshots")))).ConfigureAwait(false);
	}

	[TestMethod]
	public void View_TypeFilter_ShowsOnlyTheTypesItAllows()
	{
		CatalogueView view = new(TypedCatalogue());

		IReadOnlyList<CatalogueEntry> rows = view.Update(string.Empty, CatalogueView.AllClasses,
			CatalogueView.MaskOf(SatcatObjectType.Debris) | CatalogueView.MaskOf(SatcatObjectType.RocketBody));

		CollectionAssert.AreEquivalent(RocketBodiesAndDebris, rows.Select(row => row.Elements.NoradCatalogId).ToArray());
	}

	[TestMethod]
	public void View_ClassAndTypeFilters_Combine_AsBothMustHold()
	{
		CatalogueView view = new(TypedCatalogue());

		// LEO debris: the LEO fragment, not the GEO-transfer fragment and not the LEO payload.
		IReadOnlyList<CatalogueEntry> rows = view.Update(
			string.Empty, CatalogueView.MaskOf(OrbitClass.Leo), CatalogueView.MaskOf(SatcatObjectType.Debris));

		CollectionAssert.AreEqual(LeoDebris, rows.Select(row => row.Elements.NoradCatalogId).ToArray());

		// And a search ranks only what both filters allow: the payload it names does not come back.
		IReadOnlyList<CatalogueEntry> searched = view.Update("LEO PAYLOAD", CatalogueView.MaskOf(OrbitClass.Leo), CatalogueView.MaskOf(SatcatObjectType.Debris));
		Assert.IsFalse(searched.Any(row => row.Elements.NoradCatalogId == 1));
	}

	[TestMethod]
	public void View_CountsEachType_WhenTheCatalogueLoads()
	{
		CatalogueView view = new(TypedCatalogue());

		Assert.AreEqual(1, view.TypeCounts[(int)SatcatObjectType.Payload]);
		Assert.AreEqual(1, view.TypeCounts[(int)SatcatObjectType.RocketBody]);
		Assert.AreEqual(2, view.TypeCounts[(int)SatcatObjectType.Debris]);
		Assert.AreEqual(1, view.TypeCounts[(int)SatcatObjectType.Unknown]);
	}

	[TestMethod]
	public void View_RebuildsOnlyWhenTheTypeFilterChanges()
	{
		CatalogueView view = new(TypedCatalogue());
		int debris = CatalogueView.MaskOf(SatcatObjectType.Debris);

		IReadOnlyList<CatalogueEntry> first = view.Update(string.Empty, CatalogueView.AllClasses, debris);
		int rebuilds = view.Rebuilds;

		for (int frame = 0; frame < 10; frame++)
		{
			Assert.AreSame(first, view.Update(string.Empty, CatalogueView.AllClasses, debris));
		}

		Assert.AreEqual(rebuilds, view.Rebuilds);

		_ = view.Update(string.Empty, CatalogueView.AllClasses, CatalogueView.AllTypes);
		Assert.AreEqual(rebuilds + 1, view.Rebuilds);
	}

	[TestMethod]
	public void Describe_NamesEveryObjectType()
	{
		foreach (SatcatObjectType type in Enum.GetValues<SatcatObjectType>())
		{
			Assert.IsFalse(string.IsNullOrWhiteSpace(CataloguePanel.Describe(type)));
		}

		Assert.AreEqual("Rocket body", CataloguePanel.Describe(SatcatObjectType.RocketBody));
	}

	private static SatcatObjectType TypeOf(CatalogueLoad load, int norad) =>
		load.View.All.Single(entry => entry.Elements.NoradCatalogId == norad).ObjectType;

	private static CatalogueEntry[] TypedCatalogue() =>
	[
		new(Elements(1, "LEO PAYLOAD", 15.5, 0.001), SatcatObjectType.Payload),
		new(Elements(2, "LEO R/B", 15.0, 0.002), SatcatObjectType.RocketBody),
		new(Elements(3, "LEO DEB", 14.5, 0.003), SatcatObjectType.Debris),
		new(Elements(4, "GTO DEB", 2.27, 0.73), SatcatObjectType.Debris),
		new(Elements(5, "ANALYST OBJECT", 15.2, 0.001)),
	];

	private static ElementSet Elements(int norad, string name, double meanMotion, double eccentricity) => new()
	{
		ObjectName = name,
		ObjectId = string.Create(CultureInfo.InvariantCulture, $"2000-{norad:D3}A"),
		NoradCatalogId = norad,
		Epoch = Epoch,
		EpochJulianDate = JulianDate.FromUtc(Epoch),
		MeanMotion = meanMotion,
		Eccentricity = eccentricity,
		Inclination = 51.6,
		RightAscensionOfAscendingNode = 0.0,
		ArgumentOfPericenter = 0.0,
		MeanAnomaly = 0.0,
		BStar = 0.0,
	};

	/// <summary>An OMM JSON response holding one near-circular LEO element set per object.</summary>
	/// <param name="objects">Catalogue number, name, and hours after the base epoch.</param>
	/// <returns>The response body.</returns>
	private static string Omm(params (int Norad, string Name, int Hours)[] objects) =>
		"[" + string.Join(',', objects.Select(o => string.Create(CultureInfo.InvariantCulture, $$"""
			{"OBJECT_NAME":"{{o.Name}}","OBJECT_ID":"2026-{{o.Norad % 1000:D3}}A","EPOCH":"{{Epoch.AddHours(o.Hours):yyyy-MM-ddTHH:mm:ss.ffffff}}",
			"MEAN_MOTION":15.2,"ECCENTRICITY":0.001,"INCLINATION":51.6,"RA_OF_ASC_NODE":10.0,"ARG_OF_PERICENTER":20.0,
			"MEAN_ANOMALY":30.0,"EPHEMERIS_TYPE":0,"CLASSIFICATION_TYPE":"U","NORAD_CAT_ID":{{o.Norad}},"ELEMENT_SET_NO":999,
			"REV_AT_EPOCH":1000,"BSTAR":0.0001,"MEAN_MOTION_DOT":1e-5,"MEAN_MOTION_DDOT":0}
			"""))) + "]";

	private FakeServices Services()
	{
		FakeServices services = new();
		services.Groups["active"] = Omm((25544, "ISS (ZARYA)", 0), (41866, "GOES 16", 0));
		services.Groups["last-30-days"] = Omm((66001, "CZ-2C R/B", 0));
		services.Groups["cosmos-1408-debris"] = "[]";
		services.Groups["fengyun-1c-debris"] = Omm((29733, "FENGYUN 1C DEB", 0));
		services.Groups["iridium-33-debris"] = "[]";
		services.Groups["cosmos-2251-debris"] = "[]";
		services.Satcat = Satcat;
		owned.Add(services);
		return services;
	}

	private CatalogueLoader Loader(FakeServices services, bool spaceTrack, DateTimeOffset? now = null)
	{
		FixedClock clock = new(now ?? Now);
		HttpClient http = new(services, disposeHandler: false);
		owned.Add(http);

		ResponseCache celesTrakCache = new(Path.Join(root, "celestrak"), TimeSpan.FromHours(2), clock);

		SpaceTrackClient? spaceTrackClient = null;

		if (spaceTrack)
		{
			spaceTrackClient = new SpaceTrackClient(
				http,
				new ResponseCache(Path.Join(root, "spacetrack"), TimeSpan.FromHours(2), clock),
				new SpaceTrackRateLimiter(clock),
				new FixedCredentials(new SpaceTrackCredentials("someone@example.com", "not-a-real-password")));
			owned.Add(spaceTrackClient);
		}

		return new CatalogueLoader(new CelesTrakClient(http, celesTrakCache), new SatcatClient(http, celesTrakCache), spaceTrackClient);
	}

	private sealed class FixedCredentials(SpaceTrackCredentials? account) : ISpaceTrackCredentialSource
	{
		public Task<SpaceTrackCredentials?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(account);
	}

	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	/// <summary>CelesTrak, its satellite catalogue and Space-Track behind one transport, routed by URL.</summary>
	private sealed class FakeServices : HttpMessageHandler
	{
		private readonly List<string> requests = [];

		public Dictionary<string, string> Groups { get; } = [];

		public string Satcat { get; set; } = string.Empty;

		public bool SatcatAvailable { get; set; } = true;

		public string SpaceTrackBody { get; set; } = "[]";

		public bool RejectLogin { get; set; }

		public bool Offline { get; set; }

		public int SpaceTrackRequests => requests.Count(uri => uri.Contains("space-track.org", StringComparison.Ordinal));

		public int Count(string fragment) => requests.Count(uri => uri.Contains(fragment, StringComparison.Ordinal));

		[System.Diagnostics.CodeAnalysis.SuppressMessage(
			"Reliability", "CA2000:Dispose objects before losing scope",
			Justification = "The response is handed to HttpClient, which owns it from here and disposes it.")]
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Uri uri = request.RequestUri!;
			requests.Add(uri.ToString());

			if (Offline)
			{
				return Task.FromException<HttpResponseMessage>(new HttpRequestException("No network."));
			}

			if (uri.Host.Contains("space-track.org", StringComparison.Ordinal))
			{
				if (request.Method == HttpMethod.Post)
				{
					if (RejectLogin)
					{
						return Respond(HttpStatusCode.OK, """{"Login":"Failed"}""");
					}

					HttpResponseMessage accepted = new(HttpStatusCode.OK) { Content = new StringContent("\"\"") };
					accepted.Headers.Add("Set-Cookie", "chocolatechip=session; path=/; secure; HttpOnly");
					return Task.FromResult(accepted);
				}

				return Respond(HttpStatusCode.OK, SpaceTrackBody);
			}

			if (uri.AbsolutePath.EndsWith("satcat.csv", StringComparison.Ordinal))
			{
				return SatcatAvailable
					? Respond(HttpStatusCode.OK, Satcat)
					: Task.FromException<HttpResponseMessage>(new HttpRequestException("SATCAT is down."));
			}

			string group = Uri.UnescapeDataString(uri.Query.Split('&')[0]["?GROUP=".Length..]);

			return Groups.TryGetValue(group, out string? body)
				? Respond(HttpStatusCode.OK, body)
				: Respond(HttpStatusCode.NotFound, "No GP data found");
		}

		private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body) =>
			Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
	}
}
