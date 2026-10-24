# Physics model - assumptions vs. visualization choices

Required deliverable: the honest split between what is *physical* in this Digital
Twin and what is only a *viewing* choice. Source: project Starter Pack
(XR_DT_ET_Project_8.pdf) and the public LIGO/ET material listed under Sources.

## Model (physical content, explicitly simplified)

| Element | Implementation | Status |
|---|---|---|
| Strain | h(t) = h0 * sin(2*pi*f*t + phi) in Continuous/Burst modes | pedagogical sinusoid - teaching model, **not** a solution of Einstein's field equations |
| Propagation | two geometries: fixed-axis plane wave, or concentric radial emission from the binary; phase at a position uses retarded time tau = t - emissionDelay - distance/v | conceptually correct for a wave at fixed speed; radial mode removes the old planar artefact that produced waves "behind" the source before merger |
| Emission delay | `emissionDelay = inspiralDurationSeconds` | nothing is emitted before the merger flash; inspiral radiation is intentionally excluded - the visualization shows the merger-emitted ringdown only |
| Radial waveform | h = A * exp(-tau / ringdown) * sin(2*pi*f*tau + phi), exactly zero for tau >= 8 * ringdown | finite merger ringdown, no residual floor - it really decays to zero |
| Stretch/squeeze | '+' polarization: transverse displacement dx = +0.5*h*x0, dy = -0.5*h*y0 in the plane transverse to the local propagation direction | qualitative LIGO-style picture of test-mass response |
| Earth deformation | diagonal of F = I + k(u1 u1^T - u2 u2^T) in Earth-local axes; for +X propagation the strain acts in local Y/Z | correct transverse pairing - the old X/Y scale stretched along the propagation axis itself |
| Detector arm response | deltaL/L = 0.5 * h * ex * ((a.u1)^2 - (a.u2)^2) per arm axis a | standard transverse strain projection; one arm stretches while the other squeezes |
| Dark port | phase = 2*pi*(roundTripA - roundTripB) / displayWavelength; intensity I = sin(phase/2)^2 | correct Michelson form: equal arms stay dark |

## Visualization choices (explicitly NOT physical)

| Choice | Where labelled | Why it exists |
|---|---|---|
| Visualization speed ~3.2 m/s | MISSION & HELP drawer + tutor entry | real GWs travel at c; slowed so a human can follow |
| Displayed strain ~0.1-1.0 | amplitude slider says "exaggerated"; tutor entry | real strain is ~1e-21; LIGO public-viz practice of exaggerating for visibility |
| displacementExaggeration multiplier | parameter asset header | extra visibility margin on top of displayed strain |
| Ride-with-the-wave frame | perspective button + tutor entry | pedagogical viewpoint; **not** a relativistic observer transformation |
| Marker rings ahead/behind the front | control-panel legend | scaffolding for "which mass moves next" reasoning |
| Strain colouring (orange/blue) | panel legend | stretch vs squeeze sign cue |
| Audio hum tied to abs(h) | - | attention guidance; no physical meaning |
| Spacetime "bedsheet" | MISSION & HELP drawer (world labels removed) | a 2D surface sagging in a 3rd dimension is an **embedding analogy** for curved spacetime - not a material surface and not a solution of Einstein's equations; dents and ripples are exaggerated |
| ET display scale/behaviour | MISSION & HELP drawer + ET INSPECTOR caption | 12 m triangle instead of ~10 km sides, no Fabry-Perot cavities or suspensions, mirror motion capped and exaggerated, light markers crawl slowly, "display wavelength" - none of these are real laser optics; the LF/HF lane pair shares one qualitative strain |

## Detector: what the ET display is and is not

The observatory at the end of the mission is the **Einstein Telescope model
from the 2020 design baseline**: an equilateral triangle of three corner
stations. Each station's two arms meet at 60 degrees along the triangle edges,
and each station nests **two instruments - a low-frequency (LF) and a
high-frequency (HF) lane** - like the real "xylophone" layout. Every lane
shows the full Michelson principle: light is split at a plate whose normal
bisects the arms, travels each arm to a free end test mass, returns and
recombines at the splitter; the differential round trip decides how much light
reaches the photodetector at the dark port. Each arm's strain is projected
independently through deltaL/L = 0.5 * h * ex * ((a.u1)^2 - (a.u2)^2) - for
60-degree arms under arbitrary incidence the two responses are NOT forced to
be opposite.

The UI reflects the real topology: the ET INSPECTOR selects one station
(D1/D2/D3) and reveals its optics while the other two stay dimmed; the paired
LF/HF lanes are drawn as separate objects offset a small distance vertically.
The inspector console states explicitly that the lane pair shares one
qualitative strain - it is illustrative, not a per-band noise model.

It is **not** a reconstruction of the real instrument: ET is a ~10 km-side
underground observatory with Fabry-Perot arm cavities (hundreds of bounces),
power/signal recycling, cryogenic suspensions and frequency-dependent
squeezing. None of that is simulated; the drawer disclosure and the inspector
caption say so explicitly. The reusable `LaserInterferometer` component still
supports the classic perpendicular LIGO-style layout (arbitrary armAxisA/B),
but the mission's apparatus is the triangular ET display.

## Scope control (per brief)

Not modelled: field equations, tensor calculus, numerical relativity, real
inspiral waveforms, detector sensitivity curves, cavity optics, suspension
dynamics, curved-spacetime ray tracing, relativistic observer transformations.
The ride mode is never described as a literal physical journey.

## Chirp mode (planar variant, kept for the parameter UI)

WaveMode.Chirp without radial propagation still evaluates the original
inspiral-plus-ringdown teaching waveform, including its stylized
postMergerResidual floor - that floor is a visualization convenience for the
parameter-slab demos and is labelled as such. The Main scene uses the radial
mode above, where no residual exists.

## Sources

- Einstein Telescope EMR, "Optics and optical metrology" - arms measure
  minuscule relative length differences; monochromatic laser and mirrors;
  recombined light intensity flickers on the photodiode.
  https://www.einsteintelescope-emr.eu/en/optics-and-optical-metrology/
- LIGO Caltech, "What is an interferometer?" - split beam, two perpendicular
  arms, return and recombination at the dark port.
  https://www.ligo.caltech.edu/page/what-is-interferometer
- LIGO Caltech, "LIGO's interferometer" - Fabry-Perot arm cavities, ~300 bounces.
  https://www.ligo.caltech.edu/page/ligos-ifo
- ET Design Report Update 2020 (ET-0007B-20) - Michelson-with-cavities layout.
  https://www.einsteintelescope-emr.eu/wp-content/uploads/2024/05/ET-0007B-20_ETDesignReportUpdate2020.pdf
- Local brief extract: .tools/pdf_text/XR_DT_ET_Project_8.txt - transverse
  stretch/squeeze language and the requirement to label exaggerations.
