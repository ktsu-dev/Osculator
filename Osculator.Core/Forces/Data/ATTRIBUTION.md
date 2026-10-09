# EGM96 coefficients

`egm96-to70.txt` holds the fully normalized Stokes coefficients C̄ₙₘ and S̄ₙₘ of the Earth
Gravitational Model 1996, degrees 2 to 70, one pair per line as `n m C̄ S̄`.

- **Source:** the National Geospatial-Intelligence Agency's EGM96 coefficient set (Lemoine et al.,
  *The Development of the Joint NASA GSFC and NIMA Geopotential Model EGM96*, NASA/TP-1998-206861),
  a US Government work. Read from GeographicLib's redistribution of it
  (`gravity-distrib/egm96.tar.bz2`), because the NGA host was not reachable from where this was
  written.
- **Checked:** every value round-trips to the stored binary value exactly, and none carries more than
  the published twelve significant digits, so the text is the published coefficient and not a
  rounding of one. Spot values are asserted against the NGA file's own digits in
  `ForceModelTests.Egm96_BundledCoefficients_AreThePublishedOnes`.
- **Scaling:** μ = 398600.4415 km³/s², a = 6378.1363 km, tide-free C̄₂₀. See `Egm96.cs`.
