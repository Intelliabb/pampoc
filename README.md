# PamPoc — A Fully Local Voice AI Assistant

**PamPoc** is a proof-of-concept, end-to-end **voice assistant** ("Pam") that runs entirely on your own machine — no cloud API keys, no data leaving your device. You talk to a browser app, and Pam talks back.

It is built and maintained as a **workshop / training codebase**: the pipeline is small enough to read in an hour, but real enough to demonstrate speech-to-text, LLM inference, text-to-speech, prompt design, .NET service composition, and web client development against a live backend.

In the sample scenario, Pam is a **front-desk scheduling assistant for a family health clinic** — the system prompt in `PamPocApi/PamPocApi/Services/PromptService.cs` is what gives her that persona, and swapping it is usually the first lab exercise.

---

## What's in the box

| Path | What it is |
|---|---|
| `PamPocApi/` | ASP.NET Core 9 Web API — the "voice gateway". Orchestrates STT → LLM → TTS. |
| `PamPocWebClient/` | Blazor Server web app — chat UI, mic capture, audio playback. Runs in browser. |
| `PamPocClient/` | .NET MAUI app (macOS only) — native app alternative to web client. |
| `setup_and_start_voice_stack.sh` | Automated setup for macOS: installs/starts Ollama, whisper.cpp, Piper. |
| `sample.wav` | Test audio clip for testing the pipeline without a microphone. |

Everything else (the models, the STT server, the TTS binary) lives outside the repo and is provisioned by the setup script or installed manually.

---

## Architecture

```
┌──────────────────────────┐
│  PamPocWebClient         │   Web app (browser)
│  or PamPocClient (MAUI)  │   or native Mac app
│                          │
│  • record/upload audio   │
│  • chat transcript UI    │
│  • play Pam's reply      │
└────────────┬─────────────┘
             │  multipart/form-data  POST /api/voice/json
             ▼
┌──────────────────────────────────────────────┐
│  PamPocApi (ASP.NET 9)                       │  http://localhost:5269
│                                              │
│  1. ffmpeg → 16 kHz mono 16-bit PCM WAV      │
│  2. STT  ────────────────────────────────────┼─►  whisper.cpp server   :8001
│  3. LLM  ────────────────────────────────────┼─►  Ollama (OpenAI API)  :11434
│  4. TTS  ────────────────────────────────────┼─►  Piper HTTP server    :8002
│                                              │
│                                              │
│  Returns: { transcript, assistantText,       │
│             audioBase64, usage, timingsMs }  │
└──────────────────────────────────────────────┘
```

Three local model services back the gateway:

| Stage | Engine | Default | Endpoint |
|---|---|---|---|
| **Speech-to-text** | [whisper.cpp](https://github.com/ggml-org/whisper.cpp) server | `ggml-small.en` | `http://127.0.0.1:8001/inference` |
| **LLM** | [Ollama](https://ollama.com) (OpenAI-compatible) | `gemma:2b` or `mistral:instruct` | `http://localhost:11434/v1/chat/completions` |
| **Text-to-speech** | [Piper](https://github.com/OHF-voice/piper1-gpl) HTTP server | `en_US-amy-medium` | `http://127.0.0.1:8002/synthesize` |

All three services expose **HTTP REST endpoints**, making them interchangeable with hosted providers — another common workshop exercise.

---

## Prerequisites

### System Requirements

- **macOS** (Apple Silicon or Intel x86_64) or **Windows** or **Linux**
- [.NET 9 Runtime or SDK](https://dotnet.microsoft.com/download)
- [Postman](https://www.postman.com/downloads/) (for testing API endpoints)
- Microphone or test audio file (`sample.wav` included)
- ~5 GB free disk space for models:
  - Whisper small.en ≈ 466 MB
  - LLM (gemma:2b) ≈ 1.6 GB
  - Piper voice ≈ 65 MB

### macOS & Linux Prerequisites

**macOS:**
- [Homebrew](https://brew.sh)
- Xcode Command Line Tools: `xcode-select --install`
- Git, CMake, ffmpeg, Python 3.11

**Linux:**
- Git, CMake, ffmpeg, Python 3.11

### Windows Prerequisites

⚠️ **IMPORTANT: Install all tools in `C:\Tools\` (no spaces in path)**

Batch scripts like whisper's model downloader fail with spaces in paths. Use `C:\Tools\` instead of `Program Files`.

- **Git** - Install to `C:\Tools\git`
- **CMake** - Extract to `C:\Tools\cmake`
- **Visual C++ Build Tools** (download from [Visual Studio](https://visualstudio.microsoft.com/downloads/))
  - Required for compiling C/C++ projects like whisper.cpp
  - Select "Desktop development with C++" workload during installation
  - Alternatively, use Ninja: `choco install ninja`
- **ffmpeg** - Download binary from [ffmpeg.org](https://ffmpeg.org/download.html) and extract to `C:\Toolsfmpeg`
- **Python 3.11** - Install from [python.org](https://www.python.org/downloads), add to PATH
- **Ninja** (optional) - Download from [ninja-build.org](https://ninja-build.org/) and extract to `C:\Tools
inja`. This is needed if the C++ build tools don't work.

---

## Quick Start — Automated Setup (macOS Only)

The `setup_and_start_voice_stack.sh` script automates everything on macOS with Apple Silicon.

> ⚠️ **Intel Mac users:** Skip the script. Follow the [Manual Setup](#manual-setup) section instead — Homebrew builds fail on Intel. Use official binaries.

### 1. Provision the Local Model Stack

```bash
./setup_and_start_voice_stack.sh
```

This will:
- ✅ Start Ollama and pull `mistral:instruct` (or override: `OLLAMA_MODEL=mistral:instruct ./setup_and_start_voice_stack.sh`)
- ✅ Clone + build whisper.cpp and launch `whisper-server` on port 8001
- ✅ Create Python venv, install `piper-tts` (http and CLI), download voice models, launch Piper HTTP server on port 8002
- ✅ No configuration editing required — `appsettings.Development.json` points at the installed locations, but verify if errors occur

Logs: `~/Library/Logs/voice-stack/`  
PIDs: `~/.run/voice-stack/`

> If your repo is not at `~/Projects/pampoc`, edit `PROJ_ROOT` in the script.

### 2. Run the API

```bash
cd PamPocApi
dotnet run
```

API listens on **http://localhost:5269**. Verify:

```bash
curl http://localhost:5269/api/health
# { "ok": true, "services": { "llm": "up", "stt": "up", "tts": "http" } }
```

Smoke-test with the bundled audio:

```bash
curl -F "file=@sample.wav" http://localhost:5269/api/voice/json | jq '.transcript, .assistantText, .timingsMs'
```

### 3. Run the Web Client

```bash
cd PamPocWebClient
dotnet run
```

Opens **http://localhost:5021** in your browser. Tap **🎙️** and speak. Recording stops after 3 seconds of silence or 20 seconds total.

---

## Manual Setup (Intel Mac, Windows, Linux)

Follow these sections in order to manually install and start each service.

### 1. Install Ollama

**macOS (direct download, not Homebrew):**

```bash
curl -L https://ollama.ai/download/Ollama-darwin.zip -o /tmp/Ollama.zip
unzip /tmp/Ollama.zip -d /Applications/
rm /tmp/Ollama.zip
export PATH="/Applications/Ollama.app/Contents/MacOS:$PATH"
ollama --version  # verify
```

**Windows:**

Download from [ollama.ai/download](https://ollama.ai/download) and run installer.

**Linux:**

Follow instructions on [ollama.ai/download](https://ollama.ai/download).

**Start Ollama:**

```bash
ollama serve
```

**Download LLM model (in a new terminal):**

```bash
ollama pull mistral:instruct  # or: gemma:2b, llama3, etc.
ollama list           # verify
```

---

### 2. Install Whisper.cpp (Speech-to-Text)

**Prerequisites:**

```bash
# macOS
brew install cmake git

# Windows: CMake is part of Visual C++ Build Tools. If you don't have that installed, you can download CMake from cmake.org, add to PATH

# Linux
sudo apt update && sudo apt install cmake git build-essential
```

**Clone and build:**

**Windows (Command Prompt as Administrator):**

```cmd
REM Install to C:\Tools to avoid spaces in path
mkdir C:\Tools
cd C:\Tools
git clone https://github.com/ggml-org/whisper.cpp
cd whisper.cpp
cmake -B build
cmake --build build -j --config Release
```

**macOS/Linux:**

```bash
cd ~/Library/tools  # or your preferred tools directory
git clone https://github.com/ggml-org/whisper.cpp
cd whisper.cpp
cmake -B build
cmake --build build -j
```

**Windows troubleshooting:**

If you get "CMAKE_C_COMPILER not set" error:

1. **Use Developer Command Prompt (CRITICAL):**
   - Search Windows for **"Developer Command Prompt for VS 2022"**
   - **Run as Administrator**
   - Navigate to whisper.cpp directory
   - Try building again:
     ```cmd
     cmake -B build
     cmake --build build -j --config Release
     ```
   - Developer Command Prompt automatically sets up all compiler environment variables

2. **Ensure no spaces in path:**
   - Must be in `C:\Tools\whisper.cpp` or similar (NO spaces)
   - Batch scripts will fail if path contains spaces
   - If installed in `Program Files`, move it to `C:\Tools\`

3. If still failing, reinstall [Visual C++ Build Tools](https://visualstudio.microsoft.com/downloads/):
   - Download "Build Tools for Visual Studio 2022"
   - Select "Desktop development with C++"
   - Complete installation
   - **Restart Developer Command Prompt**

4. Alternative (if MSVC is problematic): Use MinGW instead:
   ```cmd
   choco install mingw
   cmake -B build -G Ninja -DCMAKE_C_COMPILER=gcc -DCMAKE_CXX_COMPILER=g++
   cmake --build build -j
   ```

**Download model:**

**macOS/Linux:**
```bash
sh ./models/download-ggml-model.sh base.en
```

**Windows (PowerShell):**
```powershell
cd models
.\download-ggml-model.cmd base.en
```

The `.cmd` script is the official Windows batch file included in the whisper.cpp repository.

**Start server:**

```bash
./build/bin/whisper-server \
  --model models/ggml-base.en.bin \
  --host 127.0.0.1 \
  --port 8001 \
  --inference-path "/inference"
```

On Windows, you may need to provide `/Release` directory path and provided extension `.exe`, 

```bash
./build/bin/Release/whisper-server.exe \
  --model models/ggml-base.en.bin \
  --host 127.0.0.1 \
  --port 8001 \
  --inference-path "/inference"

```

**Test with cURL:**

```bash
curl http://127.0.0.1:8001/inference \
  -F file="@sample.wav" \
  -F response_format="json"
# { "result": "Hello!" }
```

---

### 3. Install Piper (Text-to-Speech)

**Prerequisites:**

```bash
# macOS
brew install python@3.11

# Windows: Download from python.org, check "Add to PATH"

# Linux
sudo apt install python3.11 python3.11-venv
```

**Create venv and install:**

**macOS/Linux:**

```bash
python3.11 -m venv ~/Library/tools/venvs/piper
source ~/Library/tools/venvs/piper/bin/activate

# Install Piper with HTTP support
pip install --upgrade pip setuptools
pip install ninja cmake scikit-build-core
pip install piper-tts
```

**Windows (PowerShell):**

```powershell
mkdir C:\Tools\piper-venv -Force
python -m venv C:\Tools\piper-venv
C:\Tools\piper-venv\Scripts\Activate.ps1

# Install Piper with HTTP support
pip install piper-tts

# Optionally, if piper-tts is missing depedencies
pip install --upgrade pip setuptools
pip install ninja cmake scikit-build-core
```

**Download voice model:**

**macOS/Linux:**

```bash
mkdir -p ~/Library/models/piper
cd ~/Library/models/piper

curl -L https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/amy/medium/en_US-amy-medium.onnx \
  -o en_US-amy-medium.onnx

curl -L https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/amy/medium/en_US-amy-medium.onnx.json \
  -o en_US-amy-medium.onnx.json
```

**Windows (PowerShell):**

```powershell
mkdir C:\Tools\piper-models -Force
cd C:\Tools\piper-models

$voiceUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/amy/medium/en_US-amy-medium.onnx"
$configUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/amy/medium/en_US-amy-medium.onnx.json"

Invoke-WebRequest -Uri $voiceUrl -OutFile "en_US-amy-medium.onnx" -UseBasicParsing
Invoke-WebRequest -Uri $configUrl -OutFile "en_US-amy-medium.onnx.json" -UseBasicParsing
```

**Start server:**

```bash
# macOS/Linux (venv activated)
python3 -m piper.http_server \
  --model ~/Library/models/piper/en_US-amy-medium.onnx \
  --port 8002

# Windows (venv activated)
python -m piper.http_server ^
  --model C:\Tools\piper\piper-models\en_US-amy-medium.onnx ^
  --port 8002
```

**Test with cURL:**

```bash
curl -X POST http://localhost:8002/synthesize \
  -H 'Content-Type: application/json' \
  -d '{"text": "Hello world"}' \
  -o output.wav

# Listen
open output.wav  # macOS
start output.wav # Windows
```

---

### 4. Add Tools to PATH (If Not Using Script)

Edit your shell config to include tools directory:

**macOS (zsh):**

```bash
nano ~/.zshrc
# Add:
export PATH="/usr/local/bin:/Applications/Ollama.app/Contents/MacOS:$HOME/Library/tools:$PATH"
# Save: Ctrl+O, Enter, Ctrl+X

source ~/.zshrc
```

**Windows (PowerShell):**

Add to System Environment Variables:
- `Path: C:\Program Files\ollama`
- `Path: C:\Program Files\ffmpeg\bin`

---

## Configuration

All settings live in `appsettings.Development.json` under `ServiceConfiguration`:

```json
{
  "ServiceConfiguration": {
    "SttUrl": "http://127.0.0.1:8001/inference",
    "LlmUrl": "http://localhost:11434/v1/chat/completions",
    "DefaultLlmModel": "mistral:instruct",
    "TtsMode": "http",
    "TtsUrl": "http://127.0.0.1:8002/synthesize"
  }
}
```

| Key | Purpose |
|---|---|
| `SttUrl` | Whisper.cpp inference endpoint |
| `LlmUrl` | Ollama OpenAI-compatible chat endpoint |
| `DefaultLlmModel` | Model name (can be overridden per request) |
| `TtsMode` | `http` (Piper HTTP server) or `cli` (Piper CLI binary) |
| `TtsUrl` | Piper HTTP endpoint (only used if `TtsMode` is `http`) |
| `PiperBin` | Path to Piper binary (only used if `TtsMode` is `cli`) |
| `TtsVoicePath` | Path to Piper voice model (only used if `TtsMode` is `cli`) |

Paths with `~/` are expanded to user home directory at startup.

Override any setting with `PAMPOC__`-prefixed env var:

```bash
PAMPOC__DEFAULT_LLM_MODEL=mistral:instruct dotnet run --project PamPocApi
```

---

## API Reference

| Method | Route | Body | Returns |
|---|---|---|---|
| `POST` | `/api/voice/json` | multipart: `file`, optional `language`, `llm_model`, `system_prompt`, `temperature`, `max_tokens` | JSON: transcript, assistant text, base64 WAV, token usage, timings |
| `POST` | `/api/voice` | multipart: `file`, ... | `audio/wav` (spoken reply only) |
| `POST` | `/api/chat` | JSON: `{ model, messages[], temperature, maxTokens }` | `{ text, usage }` |
| `POST` | `/api/speech/stt` | multipart: `file`, optional `language` | `{ text, language }` |
| `POST` | `/api/speech/tts` | JSON: `{ text, voice }` | `audio/wav` |
| `GET` | `/api/health` | — | upstream reachability (llm, stt, tts) |
| `GET` | `/health` | — | ASP.NET Core health check |

`/api/voice/json` returns `timingsMs` per stage (`stt`, `llm`, `tts`, `total`) — great for latency discussions in a workshop setting.

OpenAPI: `/openapi/v1.json` (Development environment only)

---

## Troubleshooting

### General Issues

**PowerShell: "running scripts is disabled on this system"**

If you get this error when trying to activate a Python venv or run scripts in PowerShell:

```
File ...\Scripts\Activate.ps1 cannot be loaded because running scripts is disabled
```

**Solution:** Allow scripts for this session only:

```powershell
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process
```

Then try activating the venv again:

```powershell
.\Scripts\Activate.ps1
```

This only affects the current PowerShell session and doesn't change system-wide policies.

---

**`ffmpeg: command not found`**

`ffmpeg` is required at runtime to normalize incoming audio. Install:

```bash
# macOS
brew install ffmpeg

# Windows
# Download from ffmpeg.org or: choco install ffmpeg

# Linux
sudo apt install ffmpeg
```

Add to PATH if installed to non-standard location.

**Service health check fails**

Verify each service individually:

```bash
# Ollama
curl http://localhost:11434/api/tags

# Whisper
curl http://127.0.0.1:8001

# Piper
curl http://127.0.0.1:8002/voices
```

### Intel Mac Specific

**⚠️ Setup script fails with Homebrew errors**

The automated script fails on Intel Macs because Homebrew tries to compile llama.cpp. **Use manual setup instead** (see [Manual Setup](#manual-setup) section above).

**Quick checklist:**

1. ✅ Install Ollama directly (not via Homebrew) from [ollama.ai/download](https://ollama.ai/download)
2. ✅ Install Flask: `pip install flask`
3. ✅ Install Ninja: `brew install ninja`
4. ✅ Install CMake: `brew install cmake`
5. ✅ Ensure all tools are in PATH (see [Add Tools to PATH](#add-tools-to-path-if-not-using-script))
6. ✅ Run services manually in separate terminals

### Windows Specific

**Python not found in venv**

Make sure to activate the venv before running Piper:

```bash
C:\path\to\piper-venv\Scripts\activate
python -m piper.http_server ...
```

**Port already in use**

Change ports:

```bash
set ASPNETCORE_URLS=http://127.0.0.1:5270
dotnet run --project PamPocApi --no-launch-profile
```

### API Issues

**"Audio conversion failed: ffmpeg not found"**

API failed to call `ffmpeg`. Verify it's on PATH and restart the API from a shell where `which ffmpeg` succeeds.

**Piper returns 502**

Check Piper server is running:

```bash
curl http://127.0.0.1:8002/voices
```

Check logs for errors. Re-run Piper server.

**Ollama unavailable**

```bash
# Restart Ollama
ollama serve
```

**TTS times out**

Piper model may still be loading. Wait 10 seconds and retry. Logs:

```bash
tail -f ~/Library/Logs/voice-stack/piper-http.log  # macOS
```

---

## Workshop Notes

This codebase is designed for hands-on workshops:

1. **Setup time:** ~15 min (running the setup script)
2. **Understanding the stack:** ~30 min (reading the API and client code)
3. **Lab exercises:**
   - Change the system prompt (clinic → pizza shop, etc.)
   - Swap LLM models (`ollama pull llama3` then update config)
   - Add custom voice model (download different Piper voice)
   - Modify timings/latency display
   - Connect to hosted services (Azure OpenAI, etc.)

The pipeline is intentionally small and readable — no streaming, no persistence, no auth — so attendees can add these features as exercises.

---

## Status

**Proof of concept.** No production-ready features (no auth, no persistence, no streaming, stateless turns). Gaps are intentional — they're the curriculum.
