# Technical architecture - Project 08

Model-driven loop (brief Figure 3): Parameters -> Model state -> XR scene.

## Data flow

    SimulationParameters (ScriptableObject, single source of truth)
            |
            v
    GravitationalWaveModel   h(t)=h0*sin(2*pi*f*t+phi), retarded time tau=t-s/v
            |  StrainAt(pos,t) / DisplacementAt(pos,t)  ('+' polarization)
            v
    SimulationController     sim time, play/pause/0.25x/1x/replay/reset
            |
            +--> SpacetimeSheet    translucent membrane: gaussian wells + wave ripples
            +--> EinsteinTelescope  horizontal triangle, 3 stations x 60-deg LF/HF lanes
            +--> WavefrontMarkers  front s=v*t plus ahead/behind guide rings
            +--> PerspectiveController  observer <-> ride-with-the-wave blend
            +--> WavefrontAudio    spatial hum, volume ~ |h|, pitch ~ f

## UI (world-space ControlPanel)

Transport row: Play/Pause, 0.25x, 1x, Replay, Reset
Perspective:   Ride with the wave / Return to observer (position-blend only,
               yaw never overridden - comfort measure)
Task:          Predict next mass (PredictionTask + ResearchLogger)
Sliders:       h0 amplitude, f frequency -> SimulationParameters -> model refresh
Labels:        disclaimer, legend, status line, floating scene labels

## Research/evaluation

ResearchLogger  -> persistentDataPath/Project8Logs/session_*.csv
PredictionTask  -> pause + select-next-mass scoring, logged
ITQ / Presence / SSQ / VRSUQ-SUS questionnaires live in "Survey Questionnaire/"

## AI tutor (course requirement)

AITutor -> ITutorBackend -> LocalTutorBackend (curated KB, offline default)
                         -> HttpTutorBackend  (OpenAI-compatible endpoint;
                                               key via env var, never committed)

## XR stack

OpenXR loader for Standalone (auto load/run), interaction profiles:
Oculus Touch, HTC Vive, Valve Index, WMR. XRI 3.6.1 Starter Assets:
XR Origin rig + XRI Default Input Actions (enabled by XRInputBootstrap).
TeleportationArea on ground, XRGrabInteractable on the strain handle,
XRUIInputModule on the EventSystem.

## Editor bootstrap

ProjectInstaller.InstallPackages  - UPM packages + XRI Starter Assets sample
SceneBuilder.BuildAll             - materials, prefabs, parameter assets, scene,
                                    XR settings (reflection-based, version tolerant)
