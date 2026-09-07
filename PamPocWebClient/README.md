# PamPocWebClient

Browser client for the Pam voice assistant — the same experience as the Mac Catalyst
`PamPocClient`, as a Blazor Server app. Type to Pam, or hold a conversation with the
microphone and hear her reply.

## Run

The API gateway must be up first (see the repo root README):

```bash
cd PamPocApi
dotnet run --project PamPocApi --launch-profile http
```

Then, from this folder:

```bash
dotnet run
```

That serves the app on <http://localhost:5021> and opens your browser. Click 🎙️ and
speak — recording stops automatically after 3 seconds of silence (20 seconds max),
exactly like the Mac client. Your browser will ask for microphone permission the first
time.

Point it at a different gateway with `ApiBaseUrl`:

```bash
ApiBaseUrl=http://localhost:5269 dotnet run
```

## How it works

```
Browser                            PamPocWebClient (server)         PamPocApi
  mic → MediaRecorder (webm/opus)
  silence detection (AnalyserNode)
  base64 ──── Blazor circuit ────►  OnRecordingCompleted
                                    POST /api/voice/json ──────────►  STT → LLM → TTS
  <Audio> ◄── base64 WAV ─────────  transcript + reply + audio  ◄───
```

| File | What it does |
|---|---|
| `Components/Pages/Home.razor` | Chat UI and all state — the counterpart to `MainViewModel` |
| `Services/VoiceService.cs` | HTTP calls to `/api/chat`, `/api/voice/json`, `/api/health` |
| `wwwroot/js/pam-audio.js` | Microphone capture, silence detection, playback |
| `wwwroot/app.css` | Styling, matching the Mac client's look |

Two things are worth knowing if you change this:

- Recorded audio crosses the Blazor circuit as base64 and easily exceeds SignalR's 32 KB
  default, so `Program.cs` raises `MaximumReceiveMessageSize`.
- The browser records webm/opus (m4a on Safari), not WAV. The API already normalizes
  whatever it receives through `ffmpeg`, so the filename extension sent with the upload
  is what tells `ffmpeg` how to decode it.
