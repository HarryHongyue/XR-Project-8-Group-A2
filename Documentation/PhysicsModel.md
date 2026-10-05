# Physics model - assumptions vs. visualization choices

Required deliverable: the honest split between what is *physical* in this Digital Twin
and what is only a *viewing* choice. Source: project Starter Pack
(XR_DT_ET_Project_8.pdf).

## Model (physical content, explicitly simplified)

| Element | Implementation | Status |
|---|---|---|
| Strain | h(t) = h0 * sin(2*pi*f*t + phi) | pedagogical sinusoid - teaching model, **not** a solution of Einstein's field equations |
| Propagation | wavefront advances along a fixed axis; phase at a position uses retarded time tau = t - s/v | conceptually correct for a plane wave at fixed speed |
| Stretch/squeeze | '+' polarization: transverse displacement dx = +0.5*h*x0, dy = -0.5*h*y0 | qualitative LIGO-style picture of test-mass response |
| Wave packet | Burst mode = sinusoid inside a Gaussian envelope | convenience for a single observable pass |
| Frequency / amplitude / phase | user-adjustable model parameters | physical parameters of the model |

## Visualization choices (explicitly NOT physical)

| Choice | Where labelled | Why it exists |
|---|---|---|
| Visualization speed ~2.2 m/s | floating control-panel disclaimer and tutor entry | real GWs travel at c; slowed so a human can follow |
| Displayed strain ~0.1-1.0 | amplitude slider says "exaggerated"; tutor entry | real strain is ~1e-21; LIGO public-viz practice of exaggerating for visibility |
| displacementExaggeration multiplier | parameter asset header | extra visibility margin on top of displayed strain |
| Ride-with-the-wave frame | perspective button + tutor entry | pedagogical viewpoint; **not** a relativistic observer transformation |
| Marker rings ahead/behind the front | control-panel legend | scaffolding for "which mass moves next" reasoning |
| Strain colouring (orange/blue) | panel legend | stretch vs squeeze sign cue |
| Audio hum tied to abs(h) | - | attention guidance; no physical meaning |
| Ship-centred lattice and black-hole wells | cockpit teaching display | finite 3D volume follows the ship; visual potential wells curve its test-mass links near the binary; neither depicts a literal curved-spacetime embedding nor solves Einstein's equations |

## Scope control (per brief)

Not modelled: field equations, tensor calculus, numerical relativity, detector
sensitivity, astrophysical waveforms, curved-spacetime ray tracing, relativistic
observer transformations. The ride mode is never described as a literal physical journey.


## Chirp mode (v2 spaceship scene)

The spaceship scene uses WaveMode.Chirp: a qualitative binary-inspiral waveform.
Instantaneous frequency sweeps from 0.25 x f_peak up to frequencyHz over
inspiralDurationSeconds while amplitude grows with the square root of progress,
followed by an exponential ringdown of time constant ringdownSeconds. This mirrors
the GW150914-like chirp shape that the binary black-hole visual shows orbiting and
merging. It is still a teaching approximation, not a numerical-relativity template.
