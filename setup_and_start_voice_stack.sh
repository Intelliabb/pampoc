#!/usr/bin/env bash
set -euo pipefail

# LLM model to pull for Ollama
OLLAMA_MODEL="${OLLAMA_MODEL:-mistral:instruct}"

# STT server bind
STT_HOST="127.0.0.1"
STT_PORT="8001"

# Project locations
PROJ_ROOT="$HOME/Projects/pampoc"
API_DIR="$PROJ_ROOT/PamPocApi"

# Tools & models
TOOLS_DIR="$HOME/Library/tools"
LOG_DIR="$HOME/Library/Logs/voice-stack"
RUN_DIR="$HOME/.run/voice-stack"

# whisper.cpp (STT)
WHISPER_DIR="$TOOLS_DIR/whisper.cpp"
WHISPER_BIN="$WHISPER_DIR/build/bin/whisper-server"
WHISPER_MODELS_DIR="$HOME/Library/models/whisper"
WHISPER_MODEL="$WHISPER_MODELS_DIR/ggml-small.en.bin"
WHISPER_MODEL_URL="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin"

# Piper (TTS)
PIPER_VENV="$TOOLS_DIR/venvs/piper"
PIPER_BIN="$PIPER_VENV/bin/piper"
PIPER_MODELS_DIR="$HOME/Library/models/piper"
PIPER_VOICE_NAME="en_US-amy-medium"
PIPER_VOICE_ONNX="$PIPER_MODELS_DIR/${PIPER_VOICE_NAME}.onnx"
PIPER_VOICE_JSON="$PIPER_MODELS_DIR/${PIPER_VOICE_NAME}.onnx.json"
PIPER_VOICE_ONNX_URL="https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/amy/medium/en_US-amy-medium.onnx"
PIPER_VOICE_JSON_URL="https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/amy/medium/en_US-amy-medium.onnx.json"

# Ensure Homebrew bin is in PATH for child procs (ffmpeg, ollama, etc.)
export PATH="/opt/homebrew/bin:$PATH"

mkdir -p "$TOOLS_DIR" "$LOG_DIR" "$RUN_DIR" \
         "$WHISPER_MODELS_DIR" "$PIPER_MODELS_DIR" \
         "$(dirname "$PIPER_VENV")" "$API_DIR"

msg() { printf "\033[1;32m%s\033[0m\n" "$*"; }
warn() { printf "\033[1;33m%s\033[0m\n" "$*"; }
err() { printf "\033[1;31m%s\033[0m\n" "$*"; }
ensure_ollama() {
  local tries=20
  # Already up?
  if curl -sSf http://localhost:11434/api/tags >/dev/null 2>&1; then
    msg "Ollama already running."
    return 0
  fi

  msg "Ollama not running — starting via Homebrew…"
  brew services start ollama >/dev/null || true

  # Wait for API
  for i in $(seq 1 $tries); do
    if curl -sSf http://localhost:11434/api/tags >/dev/null 2>&1; then
      msg "Ollama is up (Homebrew service)."
      return 0
    fi
    sleep 0.5
  done

  warn "Homebrew service didn’t come up; trying direct 'ollama serve' in background…"
  nohup env OLLAMA_HOST=127.0.0.1:11434 ollama serve > "$LOG_DIR/ollama-serve.log" 2>&1 &
  echo $! > "$RUN_DIR/ollama-serve.pid"

  # Wait again
  for i in $(seq 1 $tries); do
    if curl -sSf http://localhost:11434/api/tags >/dev/null 2>&1; then
      msg "Ollama is up (direct serve)."
      return 0
    fi
    sleep 0.5
  done

  err "Ollama failed to start. See: 'brew services log ollama' and $LOG_DIR/ollama-serve.log"
  exit 1
}

# ───────────────────────────── 1) Dependencies ─────────────────────────────
msg "Installing prerequisites (Homebrew + tools)…"
if ! command -v brew >/dev/null; then
  err "Homebrew not found. Install from https://brew.sh and rerun."
  exit 1
fi
xcode-select --install 2>/dev/null || true

brew list git >/dev/null 2>&1 || brew install git
brew list cmake >/dev/null 2>&1 || brew install cmake
brew list ffmpeg >/dev/null 2>&1 || brew install ffmpeg
brew list ollama >/dev/null 2>&1 || brew install ollama
brew list python@3.11 >/dev/null 2>&1 || brew install python@3.11

# ───────────────────────────── 2) LLM (Ollama) ─────────────────────────────
msg "Ensuring Ollama is running…"
ensure_ollama

msg "Ensuring model '$OLLAMA_MODEL' is available…"
if ! ollama list | awk '{print $1}' | grep -qx "$OLLAMA_MODEL"; then
  ollama pull "$OLLAMA_MODEL"
fi
# ───────────────────────────── 3) STT (whisper.cpp HTTP server) ─────────────────────────────
msg "Preparing whisper.cpp STT server…"
if [ ! -f "$WHISPER_MODEL" ]; then
  msg "Downloading Whisper model → $WHISPER_MODEL"
  curl -L "$WHISPER_MODEL_URL" -o "$WHISPER_MODEL"
fi

if [ ! -x "$WHISPER_BIN" ]; then
  msg "Building whisper.cpp from source in $WHISPER_DIR"
  if [ ! -d "$WHISPER_DIR" ]; then
    git clone https://github.com/ggml-org/whisper.cpp "$WHISPER_DIR"
  fi
  (cd "$WHISPER_DIR" && cmake -B build && cmake --build build -j)
fi

# Restart server if already running
if [ -f "$RUN_DIR/whisper-server.pid" ] && ps -p "$(cat "$RUN_DIR/whisper-server.pid")" >/dev/null 2>&1; then
  warn "whisper-server already running (PID $(cat "$RUN_DIR/whisper-server.pid")). Restarting…"
  kill "$(cat "$RUN_DIR/whisper-server.pid")" || true
  sleep 0.4
fi

THREADS="$(sysctl -n hw.logicalcpu 2>/dev/null || echo 8)"
msg "Starting whisper-server @ http://$STT_HOST:$STT_PORT (OpenAI path '/v1/audio/transcriptions')…"
nohup "$WHISPER_BIN" \
  --model "$WHISPER_MODEL" \
  --host "$STT_HOST" \
  --port "$STT_PORT" \
  --inference-path "/v1/audio/transcriptions" \
  --threads "$THREADS" \
  --processors 1 \
  > "$LOG_DIR/whisper-server.log" 2>&1 &

echo $! > "$RUN_DIR/whisper-server.pid"

# Readiness check
for i in {1..20}; do
  if curl -s "http://$STT_HOST:$STT_PORT" >/dev/null; then break; fi
  sleep 0.25
done

# ───────────────────────────── 4) TTS (Piper CLI) ─────────────────────────────
msg "Setting up Piper TTS (Python venv in $PIPER_VENV)…"
if [ ! -d "$PIPER_VENV" ]; then
  "$(brew --prefix)/opt/python@3.11/bin/python3.11" -m venv "$PIPER_VENV"
fi
# shellcheck disable=SC1091
source "$PIPER_VENV/bin/activate"
python -m pip -q install --upgrade pip wheel >/dev/null
python -m pip -q install piper-tts >/dev/null

if [ ! -f "$PIPER_VOICE_ONNX" ]; then
  msg "Downloading Piper voice → $PIPER_VOICE_ONNX"
  curl -L "$PIPER_VOICE_ONNX_URL" -o "$PIPER_VOICE_ONNX"
fi
if [ ! -f "$PIPER_VOICE_JSON" ]; then
  curl -L "$PIPER_VOICE_JSON_URL" -o "$PIPER_VOICE_JSON"
fi

# Optional smoke test (writes /tmp/tts_test.wav)
echo "Hello from Piper on your local stack." \
  | "$PIPER_BIN" --model "$PIPER_VOICE_ONNX" --output_file /tmp/tts_test.wav >/dev/null 2>&1 || true

deactivate || true

# ───────────────────────────── 5) Write env file for your API project ─────────────────────────────
ENV_FILE="$API_DIR/gateway.env"
cat > "$ENV_FILE" <<EOF
# Source this in the PamPocApi project before 'dotnet run'
export STT_URL="http://$STT_HOST:$STT_PORT/v1/audio/transcriptions"
export LLM_URL="http://localhost:11434/v1/chat/completions"
export TTS_MODE="cli"
export PIPER_BIN="$PIPER_BIN"
export TTS_VOICE_PATH="$PIPER_VOICE_ONNX"
export DEFAULT_LLM_MODEL="$OLLAMA_MODEL"
EOF

# ───────────────────────────── 6) Summary ─────────────────────────────
msg "✅ Back-end ready."
echo "• LLM  : http://localhost:11434/v1 (Ollama, model: $OLLAMA_MODEL)"
echo "• STT  : http://$STT_HOST:$STT_PORT/v1/audio/transcriptions (whisper.cpp)"
echo "• TTS  : Piper CLI → $PIPER_BIN  (voice: $PIPER_VOICE_ONNX)"
echo "• Logs : $LOG_DIR"
echo "• Env  : $ENV_FILE  (run: 'source \"$ENV_FILE\"' before starting your API)"

echo
msg "Quick pings:"
curl -s http://localhost:11434/api/tags >/dev/null && echo "  ✓ Ollama up" || echo "  ✗ Ollama unavailable"
curl -s "http://$STT_HOST:$STT_PORT"           >/dev/null && echo "  ✓ whisper-server up" || echo "  ✗ whisper-server unavailable"

exit 0
