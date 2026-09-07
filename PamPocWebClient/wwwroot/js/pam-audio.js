// Microphone capture + playback for the Pam web client.
// Mirrors the Mac client: auto-stops after 3s of silence, 20s hard cap.

const SILENCE_TIMEOUT_MS = 3000;   // stop after this much quiet
const MAX_RECORDING_MS = 20000;    // hard cap
const SILENCE_THRESHOLD_DB = -30;  // peak level treated as silence
const POLL_MS = 200;

let recorder = null;
let stream = null;
let audioContext = null;
let analyser = null;
let pollTimer = null;
let chunks = [];
let dotNetRef = null;

// A single element, primed during the (user-initiated) mic click. Reusing an
// element that has already played keeps browser autoplay policy out of the way
// when the reply arrives seconds later.
const player = new Audio();
const SILENT_WAV = 'data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAgD4AAAB9AAACABAAZGF0YQAAAAA=';

function unlockPlayback() {
    try {
        player.src = SILENT_WAV;
        player.play().catch(() => { });
    } catch { /* best effort */ }
}

function pickMimeType() {
    const candidates = [
        { mime: 'audio/webm;codecs=opus', ext: 'webm' },
        { mime: 'audio/webm', ext: 'webm' },
        { mime: 'audio/ogg;codecs=opus', ext: 'ogg' },
        { mime: 'audio/mp4', ext: 'm4a' },     // Safari
    ];
    for (const c of candidates) {
        if (window.MediaRecorder && MediaRecorder.isTypeSupported(c.mime)) return c;
    }
    return { mime: '', ext: 'webm' };
}

function toBase64(arrayBuffer) {
    const bytes = new Uint8Array(arrayBuffer);
    let binary = '';
    const chunkSize = 0x8000;
    for (let i = 0; i < bytes.length; i += chunkSize) {
        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
    }
    return btoa(binary);
}

function peakDb() {
    if (!analyser) return 0;
    const buffer = new Float32Array(analyser.fftSize);
    analyser.getFloatTimeDomainData(buffer);
    let peak = 0;
    for (let i = 0; i < buffer.length; i++) {
        const v = Math.abs(buffer[i]);
        if (v > peak) peak = v;
    }
    return peak > 0 ? 20 * Math.log10(peak) : -160;
}

function cleanup() {
    if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
    if (stream) { stream.getTracks().forEach(t => t.stop()); stream = null; }
    if (audioContext) { audioContext.close().catch(() => { }); audioContext = null; }
    analyser = null;
    recorder = null;
}

export async function startRecording(ref) {
    if (recorder) return { ok: false, error: 'Recording already in progress' };

    dotNetRef = ref;
    chunks = [];
    unlockPlayback();

    try {
        stream = await navigator.mediaDevices.getUserMedia({
            audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true }
        });
    } catch (err) {
        cleanup();
        const denied = err && (err.name === 'NotAllowedError' || err.name === 'SecurityError');
        return {
            ok: false,
            error: denied
                ? 'Microphone access denied. Allow it for this site in your browser settings and reload.'
                : `Could not open the microphone: ${err.message || err}`
        };
    }

    const { mime, ext } = pickMimeType();

    try {
        recorder = mime ? new MediaRecorder(stream, { mimeType: mime }) : new MediaRecorder(stream);
    } catch (err) {
        cleanup();
        return { ok: false, error: `MediaRecorder unavailable: ${err.message || err}` };
    }

    recorder.ondataavailable = e => { if (e.data && e.data.size > 0) chunks.push(e.data); };

    recorder.onstop = async () => {
        const type = recorder && recorder.mimeType ? recorder.mimeType : (mime || 'audio/webm');
        const blob = new Blob(chunks, { type });
        cleanup();

        let base64 = '';
        if (blob.size > 0) {
            base64 = toBase64(await blob.arrayBuffer());
        }
        // Strip any codec parameters — the API only needs the container type.
        const contentType = type.split(';')[0];
        if (dotNetRef) {
            await dotNetRef.invokeMethodAsync('OnRecordingCompleted', base64, contentType, `recording.${ext}`);
        }
    };

    // Level metering for silence detection.
    audioContext = new (window.AudioContext || window.webkitAudioContext)();
    analyser = audioContext.createAnalyser();
    analyser.fftSize = 2048;
    audioContext.createMediaStreamSource(stream).connect(analyser);

    recorder.start();

    const startedAt = Date.now();
    let lastActivityAt = Date.now();

    pollTimer = setInterval(() => {
        if (!recorder || recorder.state !== 'recording') return;

        const now = Date.now();
        const elapsed = now - startedAt;

        // Grace period: assume the user is still getting started.
        if (elapsed < SILENCE_TIMEOUT_MS || peakDb() > SILENCE_THRESHOLD_DB) {
            lastActivityAt = now;
        }

        const silentFor = now - lastActivityAt;
        if ((silentFor >= SILENCE_TIMEOUT_MS && elapsed > SILENCE_TIMEOUT_MS) || elapsed > MAX_RECORDING_MS) {
            stopRecording();
        }
    }, POLL_MS);

    return { ok: true };
}

export function stopRecording() {
    if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
    if (recorder && recorder.state !== 'inactive') {
        recorder.stop();   // onstop delivers the audio
    }
}

export function playAudio(base64, mimeType) {
    return new Promise((resolve, reject) => {
        if (!base64) { resolve(); return; }

        let url;
        try {
            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            url = URL.createObjectURL(new Blob([bytes], { type: mimeType || 'audio/wav' }));
        } catch (err) {
            reject(err.message || String(err));
            return;
        }

        const done = () => { URL.revokeObjectURL(url); resolve(); };
        player.onended = done;
        player.onerror = () => { URL.revokeObjectURL(url); reject('Audio playback failed'); };

        player.src = url;
        player.play().catch(err => {
            URL.revokeObjectURL(url);
            reject(err.message || String(err));
        });
    });
}

export function scrollToBottom(selector) {
    const el = document.querySelector(selector);
    if (el) el.scrollTop = el.scrollHeight;
}
