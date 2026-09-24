// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

/// <summary>
/// A handful of real rows from the IERS <c>finals2000A.all.csv</c>, for tests that must not reach
/// the network.
/// </summary>
/// <remarks>
/// Real rows rather than invented ones, because the format's traps are in what the real file
/// actually contains: a <c>final</c> block, a <c>prediction</c> block, and trailing rows that
/// carry a date and nothing else. The last of those is the one that matters — see
/// <c>EarthOrientationTable</c>.
/// </remarks>
internal static class IersSample
{
	/// <summary>Gets the sample CSV.</summary>
	internal static string Csv { get; } = "MJD;Year;Month;Day;Type;x_pole;sigma_x_pole;y_pole;sigma_y_pole;x_rate;sigma_x_rate;y_rate;sigma_y_rate;Type;UT1-UTC;sigma_UT1-UTC;LOD;sigma_LOD;Type;dPsi;sigma_dPsi;dEpsilon;sigma_dEpsilon;dX;sigma_dX;dY;sigma_dY;Type;bulB/x_pole;bulB/y_pole;Type;bulB/UT-UTC;Type;bulB/dPsi;bulB/dEpsilon;bulB/dX;bulB/dY\n61296;2026;09;13;final;0.194510;0.000090;0.331655;0.000091;;;;;final;-0.0050574;0.0000262;1.1743;0.0190;prediction;;;;;0.042;0.128;0.209;0.160;;;;;;;;;;\n61297;2026;09;14;final;0.192933;0.000090;0.330554;0.000090;;;;;final;-0.0061769;0.0000273;1.0465;0.0178;prediction;;;;;0.052;0.128;0.218;0.160;;;;;;;;;;\n61298;2026;09;15;final;0.191693;0.000090;0.329602;0.000090;;;;;final;-0.0071236;0.0000242;0.8382;0.0189;prediction;;;;;0.063;0.128;0.226;0.160;;;;;;;;;;\n61302;2026;09;19;prediction;0.188144;0.000891;0.329014;0.000660;;;;;prediction;-0.0097070;0.0002041;;;prediction;;;;;0.102;0.128;0.236;0.160;;;;;;;;;;\n61303;2026;09;20;prediction;0.187025;0.001123;0.328785;0.000884;;;;;prediction;-0.0102799;0.0003028;;;prediction;;;;;0.109;0.128;0.234;0.160;;;;;;;;;;\n61721;2027;11;12;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;\n61722;2027;11;13;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;\n61723;2027;11;14;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;";
}
