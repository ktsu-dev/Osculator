// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Hexa.NET.ImGui;
using ktsu.ImGui.App;
using ktsu.ImGui.Widgets;
using ktsu.Osculator.App.Globe;
using ktsu.Osculator.Core.Elements;

/// <summary>
/// The Earth with every object on it: ground tracks, and dots coloured by residual magnitude.
/// </summary>
/// <remarks>
/// <para>
/// The Earth is a CPU raster, painted by <see cref="EarthRasterizer"/> on a worker thread and
/// uploaded with <see cref="ImGuiApp.CreateTexture(ReadOnlySpan{byte}, int, int)"/> and
/// <see cref="ImGuiApp.UpdateTexture(ImGuiAppTextureInfo, ReadOnlySpan{byte}, int, int)"/>, then
/// panned and zoomed by <see cref="ImGuiWidgets.ImageCanvas"/>. It is repainted only when the view
/// or the terminator moves, which for a map is every few minutes of simulated time.
/// </para>
/// <para>
/// Everything that moves — the graticule, the tracks, the objects — is drawn over the texture as
/// vectors through the canvas's own image-to-screen mapping, so a track stays a crisp line at any
/// zoom rather than a magnified staircase of texels.
/// </para>
/// <para>
/// Until the catalogue panel supplies objects through <see cref="SetObjects"/> and
/// <see cref="Select"/>, the panel shows an ISS element set that is also committed in the parser
/// tests, and more can be pasted in as two-line element sets.
/// </para>
/// </remarks>
internal static class GlobePanel
{
	/// <summary>The window title, and the id the shell docks the panel under.</summary>
	internal const string Title = "Globe";

	private const string DefaultLine1 = "1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999";
	private const string DefaultLine2 = "2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812";

	/// <summary>The most objects whose ground tracks are drawn; every object still gets its dot.</summary>
	private const int MaximumTracks = 16;

	/// <summary>How far the subsolar point may move, in degrees, before the night side is repainted.</summary>
	private const double TerminatorRepaintDegrees = 0.25;

	private const float LegendHeight = 34f;

	private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
	private static readonly TimeSpan TrackStep = TimeSpan.FromMinutes(1);
	private static readonly float[] Speeds = [1f, 60f, 600f, 3600f];
	private static readonly string[] SpeedLabels = ["1x", "60x", "600x", "3600x"];

	private static readonly ImGuiWidgets.ImageCanvasState Canvas = new() { MinZoom = 0.1f, MaxZoom = 32f };

	private static readonly List<GlobeObject> Objects = [];
	private static readonly Dictionary<GlobeObject, (DateTime Centre, int SpanMinutes, List<List<TrackPoint>> Runs)> Tracks = [];
	private static readonly Dictionary<GlobeObject, (DateTime Instant, GlobeResidual Kind, double? Kilometers)> Residuals = [];

	private static MapProjection projection = MapProjection.Equirectangular;
	private static GlobeResidual residualKind = GlobeResidual.FloatVersusDouble;
	private static DateTime instantUtc = DateTime.UtcNow;
	private static bool playing = true;
	private static int speedIndex;
	private static int trackSpanMinutes = 90;
	private static bool showNight = true;
	private static bool followSelected = true;
	private static float centreLatitude;
	private static float centreLongitude;
	private static int selected;

	private static string pasteLine1 = string.Empty;
	private static string pasteLine2 = string.Empty;
	private static string? pasteError;

	private static ImGuiAppTextureInfo? texture;
	private static (MapView View, int Width, int Height, (double, double)? Sun) shownKey;
	private static (MapView View, int Width, int Height, (double, double)? Sun) pendingKey;
	private static Task<byte[]>? pending;
	private static bool fitPending = true;
	private static bool initialized;

	/// <summary>
	/// Replaces the objects on the globe.
	/// </summary>
	/// <param name="elementSets">The element sets to show.</param>
	internal static void SetObjects(IEnumerable<ElementSet> elementSets)
	{
		Ensure.NotNull(elementSets);

		Objects.Clear();
		Tracks.Clear();
		Residuals.Clear();

		foreach (ElementSet elements in elementSets)
		{
			Objects.Add(new GlobeObject(elements));
		}

		selected = 0;
	}

	/// <summary>
	/// Selects an object, adding it to the globe if it is not already there.
	/// </summary>
	/// <param name="elements">The element set to select.</param>
	internal static void Select(ElementSet elements)
	{
		Ensure.NotNull(elements);

		int index = Objects.FindIndex(o => o.Elements.NoradCatalogId == elements.NoradCatalogId);

		if (index < 0)
		{
			Objects.Add(new GlobeObject(elements));
			index = Objects.Count - 1;
		}
		else if (!ReferenceEquals(Objects[index].Elements, elements))
		{
			Tracks.Remove(Objects[index]);
			Residuals.Remove(Objects[index]);
			Objects[index] = new GlobeObject(elements);
		}

		selected = index;
	}

	/// <summary>
	/// Restores the panel's start-up state.
	/// </summary>
	internal static void ResetState()
	{
		SetObjects([TleParser.Parse(DefaultLine1, DefaultLine2, "ISS (ZARYA)")]);
		projection = MapProjection.Equirectangular;
		residualKind = GlobeResidual.FloatVersusDouble;
		instantUtc = DateTime.UtcNow;
		playing = true;
		speedIndex = 0;
		trackSpanMinutes = 90;
		showNight = true;
		followSelected = true;
		centreLatitude = 0f;
		centreLongitude = 0f;
		pasteLine1 = string.Empty;
		pasteLine2 = string.Empty;
		pasteError = null;
		fitPending = true;
	}

	/// <summary>
	/// Draws the panel's contents for one frame.
	/// </summary>
	internal static void Draw()
	{
		if (!initialized)
		{
			ResetState();
			initialized = true;
		}

		AdvanceClock();
		DrawControls();

		MapView view = CurrentView(out TrackPoint? selectedPoint);
		(int width, int height) = TextureSize(view);
		(double, double)? sun = showNight ? QuantizedSun(instantUtc) : null;
		RequestRaster((view, width, height, sun));
		AdoptRaster();

		Vector2 available = ImGui.GetContentRegionAvail();
		Vector2 canvasSize = new(Math.Max(available.X, 64f), Math.Max(available.Y - LegendHeight, 64f));

		if (texture is null)
		{
			ImGui.Dummy(canvasSize);
			ImGui.TextUnformatted("Painting the Earth…");
			return;
		}

		Vector2 imageSize = new(texture.Width, texture.Height);

		if (fitPending)
		{
			Canvas.FitToViewport(imageSize, canvasSize);
			fitPending = false;
		}

		Vector2 origin = ImGui.GetCursorScreenPos();
		ImGuiWidgets.ImageCanvas("##globe", texture.TextureId, imageSize, Canvas, canvasSize);
		bool canvasHovered = ImGui.IsItemHovered();

		// The overlay is drawn against the view the texture was painted for, not the one just
		// requested, so the tracks never slide across a coastline for the frames a repaint takes.
		DrawOverlay(shownKey.View, origin, canvasSize, imageSize, selectedPoint, canvasHovered);
		DrawLegend();
	}

	private static void AdvanceClock()
	{
		if (playing)
		{
			instantUtc += TimeSpan.FromSeconds(ImGui.GetIO().DeltaTime * Speeds[speedIndex]);
		}
	}

	private static void DrawControls()
	{
		int projectionIndex = (int)projection;
		ImGui.SetNextItemWidth(150f);
		if (ImGui.Combo("Projection", ref projectionIndex, "Map\0Globe\0"))
		{
			projection = (MapProjection)projectionIndex;
			fitPending = true;
		}

		ImGui.SameLine();
		int residualIndex = (int)residualKind;
		ImGui.SetNextItemWidth(170f);
		if (ImGui.Combo("Colour by", ref residualIndex, "|float - double|\0|decimal - double|\0"))
		{
			residualKind = (GlobeResidual)residualIndex;
			Residuals.Clear();
		}

		ImGui.SameLine();
		ImGui.Checkbox("Night", ref showNight);

		if (projection == MapProjection.Orthographic)
		{
			ImGui.SameLine();
			ImGui.Checkbox("Follow selected", ref followSelected);

			if (!followSelected)
			{
				ImGui.SetNextItemWidth(140f);
				ImGui.SliderFloat("Centre lat", ref centreLatitude, -90f, 90f, "%.0f°");
				ImGui.SameLine();
				ImGui.SetNextItemWidth(140f);
				ImGui.SliderFloat("Centre lon", ref centreLongitude, -180f, 180f, "%.0f°");
			}
		}

		ImGui.TextUnformatted(instantUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", Culture));
		ImGui.SameLine();
		ImGui.Checkbox("Play", ref playing);
		ImGui.SameLine();
		ImGui.SetNextItemWidth(80f);
		ImGui.Combo("Speed", ref speedIndex, string.Join('\0', SpeedLabels) + "\0");
		ImGui.SameLine();
		if (ImGui.Button("-1 h"))
		{
			instantUtc -= TimeSpan.FromHours(1);
		}

		ImGui.SameLine();
		if (ImGui.Button("+1 h"))
		{
			instantUtc += TimeSpan.FromHours(1);
		}

		ImGui.SameLine();
		if (ImGui.Button("Now"))
		{
			instantUtc = DateTime.UtcNow;
		}

		ImGui.SameLine();
		ImGui.SetNextItemWidth(160f);
		ImGui.SliderInt("Track ± min", ref trackSpanMinutes, 0, 720);

		DrawObjectList();
	}

	private static void DrawObjectList()
	{
		if (!ImGui.CollapsingHeader("Objects"))
		{
			return;
		}

		for (int i = 0; i < Objects.Count; i++)
		{
			GlobeObject item = Objects[i];
			string label = string.Create(Culture, $"{item.Name} ({item.Elements.NoradCatalogId}), epoch {item.Elements.Epoch:yyyy-MM-dd}##obj{i}");

			if (ImGui.Selectable(label, i == selected))
			{
				selected = i;
			}
		}

		ImGui.SetNextItemWidth(-1f);
		ImGui.InputText("##line1", ref pasteLine1, 80);
		ImGui.SetNextItemWidth(-1f);
		ImGui.InputText("##line2", ref pasteLine2, 80);

		if (ImGui.Button("Add element set"))
		{
			try
			{
				Select(TleParser.Parse(pasteLine1.Trim(), pasteLine2.Trim()));
				pasteLine1 = string.Empty;
				pasteLine2 = string.Empty;
				pasteError = null;
			}
			catch (FormatException exception)
			{
				pasteError = exception.Message;
			}
			catch (ArgumentException exception)
			{
				pasteError = exception.Message;
			}
		}

		if (pasteError is not null)
		{
			ImGui.SameLine();
			ImGui.TextWrapped(pasteError);
		}
	}

	private static MapView CurrentView(out TrackPoint? selectedPoint)
	{
		selectedPoint = null;

		if (selected >= 0 && selected < Objects.Count && Objects[selected].TryLocate(instantUtc, out TrackPoint point))
		{
			selectedPoint = point;
		}

		if (projection == MapProjection.Equirectangular)
		{
			return new MapView(MapProjection.Equirectangular, 0.0, 0.0);
		}

		if (followSelected && selectedPoint is TrackPoint centre)
		{
			// Rounded so the globe turns in steps the repaint can keep up with, rather than
			// asking for a new texture every frame as the object moves.
			return new MapView(MapProjection.Orthographic, Math.Round(centre.LatitudeDegrees), Math.Round(centre.LongitudeDegrees));
		}

		return new MapView(MapProjection.Orthographic, centreLatitude, centreLongitude);
	}

	private static (int Width, int Height) TextureSize(MapView view) =>
		view.Projection == MapProjection.Equirectangular ? (1440, 720) : (900, 900);

	private static (double, double) QuantizedSun(DateTime instant)
	{
		(double latitude, double longitude) = SubsolarPoint.At(instant);
		return (
			Math.Round(latitude / TerminatorRepaintDegrees) * TerminatorRepaintDegrees,
			Math.Round(longitude / TerminatorRepaintDegrees) * TerminatorRepaintDegrees);
	}

	private static void RequestRaster((MapView View, int Width, int Height, (double, double)? Sun) key)
	{
		if (pending is not null || (texture is not null && key == shownKey))
		{
			return;
		}

		pendingKey = key;
		pending = Task.Run(() => EarthRasterizer.Render(key.View, key.Width, key.Height, LandMask.Default, key.Sun));
	}

	private static void AdoptRaster()
	{
		if (pending is null || !pending.IsCompleted)
		{
			return;
		}

		Task<byte[]> finished = pending;
		pending = null;

		if (!finished.IsCompletedSuccessfully)
		{
			return;
		}

		byte[] pixels = finished.Result;
		(MapView _, int width, int height, _) = pendingKey;

		if (texture is not null && texture.Width == width && texture.Height == height)
		{
			ImGuiApp.UpdateTexture(texture, pixels, width, height);
		}
		else
		{
			if (texture is not null)
			{
				ImGuiApp.DeleteTexture(texture);
			}

			texture = ImGuiApp.CreateTexture(pixels, width, height);
			fitPending = true;
		}

		shownKey = pendingKey;
	}

	private static void DrawOverlay(MapView view, Vector2 origin, Vector2 canvasSize, Vector2 imageSize, TrackPoint? selectedPoint, bool canvasHovered)
	{
		(Vector2 min, Vector2 max) = Canvas.ImageRectInViewport(imageSize, canvasSize);
		Vector2 imageOrigin = origin + min;
		Vector2 imageExtent = max - min;
		Vector2 ToScreen(Vector2 normalized) => imageOrigin + (normalized * imageExtent);

		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		drawList.PushClipRect(origin, origin + canvasSize, true);

		DrawGraticule(drawList, view, ToScreen);

		for (int i = 0; i < Objects.Count && i < MaximumTracks; i++)
		{
			uint color = i == selected ? Pack(255, 210, 90, 230) : Pack(200, 200, 200, 120);
			foreach (List<Vector2> line in GroundTrack.Project(view, TrackFor(Objects[i])))
			{
				for (int p = 1; p < line.Count; p++)
				{
					drawList.AddLine(ToScreen(line[p - 1]), ToScreen(line[p]), color, i == selected ? 2f : 1f);
				}
			}
		}

		Vector2 mouse = ImGui.GetIO().MousePos;
		int hovered = -1;
		float hoveredDistance = 8f;
		TrackPoint hoveredPoint = default;

		for (int i = 0; i < Objects.Count; i++)
		{
			if (!Objects[i].TryLocate(instantUtc, out TrackPoint point)
				|| !view.TryProject(point.LatitudeDegrees, point.LongitudeDegrees, out Vector2 at))
			{
				continue;
			}

			Vector2 screen = ToScreen(at);
			(byte r, byte g, byte b) = ResidualColorScale.ColorFor(ResidualFor(Objects[i]));
			drawList.AddCircleFilled(screen, 5f, Pack(r, g, b, 255));
			drawList.AddCircle(screen, 5f, Pack(0, 0, 0, 200), 0, 1f);

			if (i == selected && selectedPoint is not null)
			{
				drawList.AddCircle(screen, 9f, Pack(255, 210, 90, 255), 0, 2f);
			}

			float distance = Vector2.Distance(screen, mouse);
			if (canvasHovered && distance < hoveredDistance)
			{
				hovered = i;
				hoveredDistance = distance;
				hoveredPoint = point;
			}
		}

		drawList.PopClipRect();

		if (hovered >= 0)
		{
			GlobeObject item = Objects[hovered];
			double? residual = ResidualFor(item);
			ImGui.SetTooltip(string.Create(
				Culture,
				$"{item.Name} ({item.Elements.NoradCatalogId})\n" +
				$"lat {hoveredPoint.LatitudeDegrees:F2}°  lon {hoveredPoint.LongitudeDegrees:F2}°  alt {hoveredPoint.AltitudeKilometers:F1} km\n" +
				$"{ResidualLabel(residualKind)}: {(residual is double km ? FormatKilometers(km) : "not available")}"));

			if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
			{
				selected = hovered;
			}
		}
	}

	private static void DrawGraticule(ImDrawListPtr drawList, MapView view, Func<Vector2, Vector2> toScreen)
	{
		uint color = Pack(255, 255, 255, 40);

		for (int latitude = -60; latitude <= 60; latitude += 30)
		{
			List<TrackPoint> parallel = [];
			for (int longitude = -180; longitude <= 180; longitude += 3)
			{
				parallel.Add(new TrackPoint(default, latitude, Math.Min(longitude, 179.999), 0.0));
			}

			DrawLines(drawList, GroundTrack.Project(view, [parallel]), toScreen, color);
		}

		for (int longitude = -180; longitude < 180; longitude += 30)
		{
			List<TrackPoint> meridian = [];
			for (int latitude = -90; latitude <= 90; latitude += 3)
			{
				meridian.Add(new TrackPoint(default, latitude, longitude, 0.0));
			}

			DrawLines(drawList, GroundTrack.Project(view, [meridian]), toScreen, color);
		}
	}

	private static void DrawLines(ImDrawListPtr drawList, List<List<Vector2>> lines, Func<Vector2, Vector2> toScreen, uint color)
	{
		foreach (List<Vector2> line in lines)
		{
			for (int p = 1; p < line.Count; p++)
			{
				drawList.AddLine(toScreen(line[p - 1]), toScreen(line[p]), color, 1f);
			}
		}
	}

	private static void DrawLegend()
	{
		ImGui.TextUnformatted(ResidualLabel(residualKind));
		ImGui.SameLine();

		Vector2 start = ImGui.GetCursorScreenPos();
		const float barWidth = 260f;
		const float barHeight = 12f;
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		const int slices = 64;

		for (int s = 0; s < slices; s++)
		{
			(byte r, byte g, byte b) = ResidualColorScale.Sample((s + 0.5) / slices);
			Vector2 a = start + new Vector2(barWidth * s / slices, 2f);
			Vector2 c = start + new Vector2(barWidth * (s + 1) / slices, 2f + barHeight);
			drawList.AddRectFilled(a, c, Pack(r, g, b, 255));
		}

		ImGui.Dummy(new Vector2(barWidth, barHeight + 4f));
		ImGui.SameLine();
		ImGui.TextUnformatted(string.Create(
			Culture,
			$"1e{ResidualColorScale.MinimumLog10Kilometers:0} km … 1e{ResidualColorScale.MaximumLog10Kilometers:0} km, log scale; grey = no state"));
	}

	private static List<List<TrackPoint>> TrackFor(GlobeObject item)
	{
		DateTime centre = new(instantUtc.Ticks - (instantUtc.Ticks % TrackStep.Ticks), DateTimeKind.Utc);

		if (Tracks.TryGetValue(item, out (DateTime Centre, int SpanMinutes, List<List<TrackPoint>> Runs) cached)
			&& cached.Centre == centre && cached.SpanMinutes == trackSpanMinutes)
		{
			return cached.Runs;
		}

		TimeSpan span = TimeSpan.FromMinutes(trackSpanMinutes);
		List<List<TrackPoint>> runs = GroundTrack.Sample(item.Elements, item.AsDouble, centre, span, span, TrackStep);
		Tracks[item] = (centre, trackSpanMinutes, runs);
		return runs;
	}

	private static double? ResidualFor(GlobeObject item)
	{
		DateTime second = new(instantUtc.Ticks - (instantUtc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

		if (Residuals.TryGetValue(item, out (DateTime Instant, GlobeResidual Kind, double? Kilometers) cached)
			&& cached.Instant == second && cached.Kind == residualKind)
		{
			return cached.Kilometers;
		}

		double? kilometers = item.ResidualKilometers(residualKind, second);
		Residuals[item] = (second, residualKind, kilometers);
		return kilometers;
	}

	private static string ResidualLabel(GlobeResidual kind) => kind switch
	{
		GlobeResidual.FloatVersusDouble => "|r(float) - r(double)|",
		GlobeResidual.DecimalVersusDouble => "|r(decimal) - r(double)|",
		_ => kind.ToString(),
	};

	private static string FormatKilometers(double kilometers) => kilometers switch
	{
		>= 1.0 => string.Create(Culture, $"{kilometers:F3} km"),
		>= 1e-3 => string.Create(Culture, $"{kilometers * 1e3:F3} m"),
		>= 1e-6 => string.Create(Culture, $"{kilometers * 1e6:F3} mm"),
		_ => string.Create(Culture, $"{kilometers:E2} km"),
	};

	private static uint Pack(byte r, byte g, byte b, byte a) =>
		((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
}
