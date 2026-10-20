// Browser side of the interview room: microphone, WebRTC audio and live captions.
// Audio flows browser <-> realtime provider directly. The SDP offer goes through our API so the
// provider key stays on the server, and the server persists the transcript on its own connection.
// Captions here are display-only.

let pc = null;
let dc = null;
let micStream = null;
let dotnet = null;
let captionsEl = null;
let meter = null;
const liveCaptions = new Map();

export async function start(sessionId, dotnetRef, audioEl, captionsElement, meterCanvas) {
    dotnet = dotnetRef;
    captionsEl = captionsElement;
    notify('OnStatus', 'requesting-microphone');

    micStream = await openMicrophone();
    startMeter(micStream, meterCanvas);

    pc = new RTCPeerConnection();
    pc.ontrack = e => { audioEl.srcObject = e.streams[0]; };
    pc.addTrack(micStream.getAudioTracks()[0], micStream);
    pc.onconnectionstatechange = () => {
        if (!pc) return;
        if (pc.connectionState === 'disconnected' || pc.connectionState === 'failed') notify('OnStatus', 'connection-lost');
    };

    dc = pc.createDataChannel('oai-events');
    dc.onopen = () => {
        // Ask the interviewer to open once audio is flowing, so the greeting isn't cut off.
        send({ type: 'response.create' });
        notify('OnStatus', 'live');
    };
    dc.onmessage = e => handleEvent(JSON.parse(e.data));

    notify('OnStatus', 'connecting');
    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);

    const response = await fetch(`api/interviews/${sessionId}/realtime-session`, {
        method: 'POST',
        body: offer.sdp,
        headers: { 'Content-Type': 'application/sdp' },
        credentials: 'same-origin'
    });
    if (!response.ok) {
        const problem = await response.json().catch(() => null);
        stop();
        throw new Error(problem?.title ?? `Could not connect (HTTP ${response.status}).`);
    }
    await pc.setRemoteDescription({ type: 'answer', sdp: await response.text() });
}

// Browser errors (DOMException) have no stack, so Blazor would show "...undefined".
// Translate them into plain Errors with a message the candidate can act on.
async function openMicrophone() {
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
        throw new Error('The microphone is only available over HTTPS (or on localhost). Open the site with https://.');
    }
    try {
        return await navigator.mediaDevices.getUserMedia({
            audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true }
        });
    } catch (e) {
        const messages = {
            NotFoundError: 'No microphone was found. Plug in a headset or microphone, check it is enabled in Windows Sound settings, then try again.',
            NotAllowedError: 'Microphone access was blocked. Click the camera/microphone icon in the address bar, allow the microphone, then try again. On Windows also check Settings › Privacy & security › Microphone.',
            NotReadableError: 'The microphone is in use by another app (for example Teams or Zoom), or Windows blocked it. Close the other app and try again.',
            OverconstrainedError: 'Your microphone does not support the required settings. Try a different microphone.',
            SecurityError: 'The browser blocked microphone access on this page.',
            AbortError: 'The microphone could not be started. Try again, or reconnect the device.'
        };
        throw new Error(messages[e?.name] ?? `The microphone could not be opened (${e?.name ?? 'unknown error'}).`);
    }
}

export function setMuted(muted) {
    micStream?.getAudioTracks().forEach(t => t.enabled = !muted);
}

export function askToRepeat() {
    send({ type: 'output_audio_buffer.clear' });
    send({
        type: 'conversation.item.create',
        item: { type: 'message', role: 'user', content: [{ type: 'input_text', text: 'Could you please repeat the question?' }] }
    });
    send({ type: 'response.create' });
}

export function stop() {
    try { dc?.close(); } catch { }
    try { pc?.close(); } catch { }
    micStream?.getTracks().forEach(t => t.stop());
    meter?.stop();
    dc = pc = micStream = meter = null;
}

function handleEvent(evt) {
    switch (evt.type) {
        case 'input_audio_buffer.speech_started':
            notify('OnCandidateSpeaking', true);
            break;
        case 'input_audio_buffer.speech_stopped':
            notify('OnCandidateSpeaking', false);
            break;
        case 'output_audio_buffer.started':
            notify('OnInterviewerSpeaking', true);
            break;
        case 'output_audio_buffer.stopped':
            notify('OnInterviewerSpeaking', false);
            break;
        case 'output_audio_buffer.cleared':
            // The candidate interrupted: the provider stopped playback and truncated the unheard audio.
            notify('OnInterviewerSpeaking', false);
            markInterrupted(evt.response_id);
            break;
        case 'response.output_audio_transcript.delta':
            appendCaption(evt.response_id, 'interviewer', evt.delta, false);
            break;
        case 'response.output_audio_transcript.done':
            appendCaption(evt.response_id, 'interviewer', evt.transcript, true);
            break;
        case 'conversation.item.input_audio_transcription.completed':
            appendCaption(evt.item_id, 'candidate', evt.transcript, true);
            break;
        case 'error':
            console.warn('Realtime error', evt.error);
            break;
    }
}

function appendCaption(id, speaker, text, final) {
    if (!captionsEl || !text) return;
    let line = liveCaptions.get(id);
    if (!line) {
        line = document.createElement('div');
        line.className = `caption caption-${speaker}`;
        line.innerHTML = `<span class="caption-speaker">${speaker === 'candidate' ? 'You' : 'Interviewer'}</span><span class="caption-text"></span>`;
        captionsEl.appendChild(line);
        liveCaptions.set(id, line);
    }
    const textEl = line.querySelector('.caption-text');
    textEl.textContent = final ? text : textEl.textContent + text;
    if (final) liveCaptions.delete(id);
    captionsEl.scrollTop = captionsEl.scrollHeight;
}

function markInterrupted(responseId) {
    const line = liveCaptions.get(responseId);
    if (line) { line.classList.add('caption-interrupted'); liveCaptions.delete(responseId); }
}

function send(event) {
    if (dc?.readyState === 'open') dc.send(JSON.stringify(event));
}

function notify(method, arg) {
    dotnet?.invokeMethodAsync(method, arg).catch(() => { });
}

function startMeter(stream, canvas) {
    if (!canvas) return;
    const ctx = new AudioContext();
    const analyser = ctx.createAnalyser();
    analyser.fftSize = 256;
    ctx.createMediaStreamSource(stream).connect(analyser);
    const data = new Uint8Array(analyser.frequencyBinCount);
    const g = canvas.getContext('2d');
    let raf = 0;
    const draw = () => {
        analyser.getByteFrequencyData(data);
        g.clearRect(0, 0, canvas.width, canvas.height);
        const bars = 32, w = canvas.width / bars;
        g.fillStyle = getComputedStyle(canvas).color;
        for (let i = 0; i < bars; i++) {
            const h = Math.max(2, (data[i * 2] / 255) * canvas.height);
            g.fillRect(i * w + 1, (canvas.height - h) / 2, w - 2, h);
        }
        raf = requestAnimationFrame(draw);
    };
    draw();
    meter = { stop: () => { cancelAnimationFrame(raf); ctx.close(); } };
}
