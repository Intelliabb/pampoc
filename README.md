# PamPoc — A Fully Local Voice AI Assistant

**PamPoc** is a proof-of-concept, end-to-end **voice assistant** ("Pam") that runs entirely on your own
machine — no cloud API keys, no data leaving the laptop. You talk to a native macOS app, and Pam talks back.

It is built and maintained as a **workshop / training codebase**: the pipeline is small enough to read in an
hour, but real enough to demonstrate speech-to-text, LLM inference, text-to-speech, prompt design, .NET
service composition, and MAUI client development against a live backend.

In the sample scenario, Pam is a **front-desk scheduling assistant for a family health clinic** — the system
prompt in `PamPocApi/PamPocApi/Services/PromptService.cs` is what gives her that persona, and swapping it is
usually the first lab exercise.

---

## What's in the box

| Path | What it is |
|---|---|
| `PamPocApi/` | ASP.NET Core 9 Web API — the "voice gateway". Orchestrates STT → LLM → TTS. |
| `PamPocClient/` | .NET MAUI app (**macOS / Mac Catalyst only**) — chat UI, mic capture, audio playback. |
| `setup_and_start_voice_stack.sh` | One-shot macOS installer: installs and starts Ollama, whisper.cpp and Piper. |
| `sample.wav` | Canned audio clip for testing the pipeline without a microphone. |

Everything else (the models, the STT server, the TTS binary) lives outside the repo and is provisioned by
the setup script.

---

## Architecture

```
┌──────────────────────────┐
│  PamPocClient (MAUI)     │   Mac Catalyst app
│  • record mic (AVFoundation, 16 kHz mono,
│    auto-stops after 3 s of silence)
│  • chat transcript UI
│  • play back Pam's reply
└────────────┬─────────────┘
             │  multipart/form-data  POST /api/voice/json
             ▼
┌──────────────────────────┐
│  PamPocApi (ASP.NET 9)   │   http://localhost:5269
│                          │
│  1. ffmpeg → 16 kHz mono 16-bit PCM WAV
│  2. STT  ──────────────────────────────►  whisper.cpp server   :8001
│  3. LLM  ──────────────────────────────►  Ollama (OpenAI API)  :11434
│  4. TTS  ──────────────────────────────►  Piper (CLI or HTTP)
│                          │
│  returns { transcript, assistantText, audioBase64, usage, timingsMs }
└──────────────────────────┘
```

Three local model services back the gateway:

| Stage | Engine | Default | Endpoint |
|---|---|---|---|
| Speech-to-text | [whisper.cpp](https://github.com/ggml-org/whisper.cpp) server | `ggml-small.en` | `http://127.0.0.1:8001/v1/audio/transcriptions` |
| LLM | [Ollama](https://ollama.com) (OpenAI-compatible) | `mistral:instruct` | `http://localhost:11434/v1/chat/completions` |
| Text-to-speech | [Piper](https://github.com/rhasspy/piper) | `en_US-amy-medium` | CLI subprocess (or HTTP on `:5000`) |

Because whisper.cpp and Ollama both expose **OpenAI-compatible** routes, the gateway code is a good starting
point for pointing the same app at a hosted provider — another common workshop exercise.

---

## Prerequisites

- **macOS on Apple Silicon** (the client is Mac Catalyst; the setup script is Homebrew-based)
- [Homebrew](https://brew.sh)
- [.NET 9 SDK](https://dotnet.microsoft.com/download) + MAUI workload — `dotnet workload install maui`
- Xcode Command Line Tools
- ~5 GB free disk for models (Whisper small.en ≈ 466 MB, `mistral:instruct` ≈ 4 GB, Piper voice ≈ 60 MB)

The setup script installs `git`, `cmake`, `ffmpeg`, `ollama` and `python@3.11` for you. `ffmpeg` is **required
at runtime** — the API shells out to it to normalize incoming audio.

---

## Quick start

### 1. Provision the local model stack

```bash
./setup_and_start_voice_stack.sh
```

This will:
- start Ollama and pull `mistral:instruct` (override with `OLLAMA_MODEL=llama3.2 ./setup_and_start_voice_stack.sh`)
- clone + build whisper.cpp into `~/Library/tools/whisper.cpp` and launch `whisper-server` on port 8001
- create a Python venv at `~/Library/tools/venvs/piper`, install `piper-tts`, download the `en_US-amy-medium` voice
No configuration editing is required — `appsettings.Development.json` already points at the locations the
script installs into, using `~/…` paths that are expanded at startup.

Logs land in `~/Library/Logs/voice-stack/`, PIDs in `~/.run/voice-stack/`.

> ⚠️ The script assumes the repo lives at `~/Projects/pampoc`. Edit `PROJ_ROOT` at the top if yours doesn't.

### 2. Run the API

```bash
cd PamPocApi
dotnet run --project PamPocApi --launch-profile http
```

The API listens on **http://localhost:5269**. Verify the whole stack is reachable:

```bash
curl http://localhost:5269/api/health
# { "ok": true, "services": { "llm": "up", "stt": "up", "tts": "cli" } }
```

Smoke-test the full pipeline with the bundled clip:

```bash
curl -F "file=@sample.wav" http://localhost:5269/api/voice/json | jq '.transcript, .assistantText, .timingsMs'
```

### 3. Run the client

```bash
cd PamPocClient
dotnet build -t:Run -f net9.0-maccatalyst
```

Tap **🎙️** and speak. Recording stops automatically after 3 seconds of silence (or 20 seconds total), the
clip is sent to `/api/voice/json`, and Pam's reply is shown in the transcript and played aloud. You can also
type into the entry box to hit the text-only `/api/chat` route.

macOS will prompt for microphone access on first use; if you deny it, re-enable under
**System Settings → Privacy & Security → Microphone**.

---

## API reference

| Method | Route | Body | Returns |
|---|---|---|---|
| `POST` | `/api/voice` | multipart: `file`, optional `language`, `llm_model`, `system_prompt`, `temperature`, `max_tokens` | `audio/wav` (spoken reply) |
| `POST` | `/api/voice/json` | multipart: `file`, optional `language`, `llm_model`, `system_prompt` | JSON: transcript, assistant text, base64 WAV, token usage, per-stage timings |
| `POST` | `/api/chat` | JSON: `{ model, messages[], temperature, maxTokens }` | `{ text, usage, provider }` |
| `POST` | `/api/speech/stt` | multipart: `file`, optional `language` | `{ text, language }` |
| `POST` | `/api/speech/tts` | JSON: `{ text, voice }` | `audio/wav` |
| `GET` | `/api/health` | — | upstream reachability for LLM / STT / TTS |
| `GET` | `/health` | — | ASP.NET Core health check |

OpenAPI is exposed at `/openapi/v1.json` in the Development environment.

`/api/voice/json` is the interesting one for teaching: it returns `timingsMs` broken out per stage
(`stt`, `llm`, `tts`, `total`), which makes latency budgets concrete during a session.

---

## Configuration

All settings live under the `ServiceConfiguration` section and are bound + validated at startup
(`Configuration/ServiceConfiguration.cs`). `appsettings.Development.json` is the single source of
configuration for local runs.

| Key | Purpose |
|---|---|
| `SttUrl` | whisper.cpp transcription endpoint |
| `LlmUrl` | OpenAI-compatible chat completions endpoint |
| `DefaultLlmModel` | Model name used when a request doesn't specify one |
| `TtsMode` | `cli` (spawn Piper) or `http` (POST to a Piper server) |
| `PiperBin` | Path to the Piper executable (`cli` mode) |
| `TtsVoicePath` | Path to the `.onnx` voice (`cli` mode) |
| `TtsUrl` | Piper HTTP endpoint (`http` mode) |

`PiperBin` and `TtsVoicePath` may be written with a leading `~/`, which is expanded to the current user's
home directory at startup (`ConfigurationExtensions.ExpandHome`). That's what lets one committed config work
on every attendee's machine — the setup script installs to the same `~/Library/…` locations for everyone.

Each key can be overridden with a `PAMPOC__`-prefixed environment variable, e.g.:

```bash
PAMPOC__DEFAULT_LLM_MODEL=llama3.2 dotnet run --project PamPocApi
```

Validation is strict: `http` mode requires `TtsUrl`, `cli` mode requires `TtsVoicePath`, and the app refuses
to start otherwise.

The client's backend address is currently a constant — `BaseUrl` in
`PamPocClient/PamPocClient/Services/VoiceService.cs`. Change it there to point at a different gateway.

---

## Troubleshooting

**`whisper-server` isn't running.** `curl http://127.0.0.1:8001` — if it's down, check
`~/Library/Logs/voice-stack/whisper-server.log`. Re-running the setup script restarts it.

**Ollama unavailable.** `brew services restart ollama`, then `curl http://localhost:11434/api/tags`.

**`Audio conversion failed`.** `ffmpeg` isn't on the API process's `PATH`. Install with `brew install ffmpeg`
and restart the API from a shell where `which ffmpeg` succeeds.

**TTS returns a 502 / Piper CLI failed.** Confirm the voice file exists at
`~/Library/models/piper/en_US-amy-medium.onnx` and that `~/Library/tools/venvs/piper/bin/piper` is
executable. Re-running the setup script restores both.

**Port 5269 is already in use.** An API instance is still running from an earlier session. Find it with
`lsof -nP -iTCP:5269 -sTCP:LISTEN` and kill that PID, or run on another port with
`ASPNETCORE_URLS=http://127.0.0.1:5270 dotnet run --project PamPocApi --no-launch-profile`.

**Env vars aren't taking effect.** Only the seven `PAMPOC__`-prefixed names are read
(`ConfigurationExtensions.cs`). Unprefixed names like `STT_URL` are ignored — including the block in
`launchSettings.json:11-17`, which is inert and shadowed by `appsettings.Development.json`.

**The client's health indicator never goes green.** `CheckHealthAsync` looks for `status: "healthy"`, but
`/api/health` returns `{ ok, services }`. Reconciling the two is a nice five-minute warm-up task.

---

## Status

This is a **proof of concept**, not production code. No auth, no persistence, no streaming, no test suite,
stateless turns, and macOS-only client. That's intentional — the gaps are the curriculum.
