// AI viva page: reads each question aloud (speechSynthesis), recognises the spoken answer
// (SpeechRecognition, Chrome/Edge) and exchanges text with the server. Typing is the fallback.
// When the exam is recorded, each answer is captured with MediaRecorder and uploaded after it is
// submitted. Timings (time to first word, speaking time) are sent with the answer as signals.
(function () {
    'use strict';

    var root = document.getElementById('interview');
    if (!root) return;

    var baseUrl = root.dataset.baseUrl;
    var lang = root.dataset.lang;
    var text = root.dataset;
    var token = (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || '';
    var Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;

    var steps = {};
    root.querySelectorAll('[data-step]').forEach(function (el) { steps[el.dataset.step] = el; });
    function field(name) { return root.querySelector('[data-field="' + name + '"]'); }

    var answerBox = field('answer');
    var typingBox = field('typing');
    var statusLine = field('status');
    var timerLabel = field('timer');
    var errorBox = field('error');
    var recLabel = field('rec');
    var preview = field('preview');

    var state = JSON.parse(root.dataset.initialState);
    var clockOffset = 0;          // server time minus browser time, in ms
    var turn = null;              // the question being answered
    var recognizer = null;
    var listening = false;        // we want the recognizer running
    var finalText = '';
    var timerId = null;
    var pollId = null;
    var submitting = false;
    var typedMode = !Recognition;

    // Signals for the current answer.
    var answerOpenedAt = 0;       // question finished being read
    var firstWordAt = 0;
    var lastWordAt = 0;

    // Recording.
    var mediaStream = null;
    var recorder = null;
    var chunks = [];

    function show(step) {
        Object.keys(steps).forEach(function (key) { steps[key].hidden = key !== step; });
    }

    function showError(message) {
        errorBox.textContent = message || text.textError;
        errorBox.hidden = false;
    }

    function clearError() { errorBox.hidden = true; }

    function request(method, path, body) {
        var options = { method: method, headers: { 'RequestVerificationToken': token }, credentials: 'same-origin' };
        if (body) {
            options.headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(body);
        }
        return fetch(baseUrl + path, options).then(function (response) {
            return response.json().catch(function () { return {}; }).then(function (data) {
                if (!response.ok) throw new Error(data.error || text.textError);
                return data;
            });
        });
    }

    function speech() { return state.speech || {}; }
    function recordingMode() { return state.recording || 'None'; }

    // ---- Speech output -------------------------------------------------------
    function pickVoice() {
        if (!window.speechSynthesis) return null;
        var voices = window.speechSynthesis.getVoices();
        var preferred = lang.toLowerCase().indexOf('vi') === 0 ? speech().vietnameseVoice : speech().englishVoice;
        var prefix = lang.split('-')[0].toLowerCase();
        return (preferred && voices.find(function (v) { return v.name === preferred; }))
            || voices.find(function (v) { return v.lang && v.lang.toLowerCase() === lang.toLowerCase(); })
            || voices.find(function (v) { return v.lang && v.lang.toLowerCase().indexOf(prefix) === 0; })
            || null;
    }

    function speak(sentence, done) {
        if (!window.speechSynthesis) { done(); return; }
        window.speechSynthesis.cancel();
        var utterance = new SpeechSynthesisUtterance(sentence);
        utterance.lang = lang;
        var voice = pickVoice();
        if (voice) utterance.voice = voice;
        utterance.rate = speech().speechRate || 0.95;
        var finished = false;
        function finish() { if (!finished) { finished = true; done(); } }
        utterance.onend = finish;
        utterance.onerror = finish;
        // Some browsers never fire onend; do not leave the candidate waiting.
        setTimeout(finish, Math.min(60000, 4000 + sentence.length * 120));
        window.speechSynthesis.speak(utterance);
    }

    // ---- Speech input --------------------------------------------------------
    function applyPhrases(target) {
        // Contextual biasing towards subject terms, where the browser supports it.
        var phrases = state.phrases || [];
        if (!phrases.length || !('phrases' in target) || typeof window.SpeechRecognitionPhrase !== 'function') return;
        try {
            target.phrases = phrases.map(function (phrase) { return new window.SpeechRecognitionPhrase(phrase, 5.0); });
        } catch (e) { /* unsupported for this recogniser */ }
    }

    function startListening() {
        if (typedMode || !Recognition) return;
        listening = true;
        if (!recognizer) {
            recognizer = new Recognition();
            recognizer.lang = lang;
            recognizer.continuous = true;
            recognizer.interimResults = true;
            applyPhrases(recognizer);
            recognizer.onresult = function (event) {
                var now = Date.now();
                if (!firstWordAt) firstWordAt = now;
                lastWordAt = now;
                var interim = '';
                for (var i = event.resultIndex; i < event.results.length; i++) {
                    var piece = event.results[i][0].transcript;
                    if (event.results[i].isFinal) finalText += (finalText ? ' ' : '') + piece.trim();
                    else interim += piece;
                }
                answerBox.value = (finalText + (interim ? ' ' + interim : '')).trim();
            };
            recognizer.onerror = function (event) {
                if (event.error === 'not-allowed' || event.error === 'service-not-allowed' || event.error === 'audio-capture') {
                    listening = false;
                    switchToTyping(text.textMicError);
                }
            };
            // Chrome ends recognition after a pause; keep going while the answer is open.
            recognizer.onend = function () { if (listening) { try { recognizer.start(); } catch (e) { /* already running */ } } };
        }
        statusLine.textContent = text.textListening;
        try { recognizer.start(); } catch (e) { /* already running */ }
    }

    function stopListening() {
        listening = false;
        if (recognizer) { try { recognizer.stop(); } catch (e) { /* not running */ } }
    }

    function switchToTyping(message) {
        typedMode = true;
        typingBox.checked = true;
        typingBox.disabled = !Recognition;
        stopListening();
        answerBox.readOnly = false;
        answerBox.focus();
        statusLine.textContent = message || '';
    }

    answerBox.addEventListener('input', function () {
        if (!typedMode) return;
        var now = Date.now();
        if (!firstWordAt) firstWordAt = now;
        lastWordAt = now;
    });

    // ---- Recording -----------------------------------------------------------
    function ensureMedia() {
        if (recordingMode() === 'None' || !state.recordingConsentGiven && state.status !== 'NotStarted') return Promise.resolve(null);
        if (mediaStream) return Promise.resolve(mediaStream);
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia || !window.MediaRecorder) return Promise.resolve(null);
        var video = recordingMode() === 'AudioVideo' ? { width: 320, height: 240, frameRate: 15 } : false;
        return navigator.mediaDevices.getUserMedia({ audio: true, video: video })
            .then(function (stream) {
                mediaStream = stream;
                if (video && preview) { preview.srcObject = stream; preview.hidden = false; }
                return stream;
            })
            .catch(function () { showError(text.textRecordError); return null; });
    }

    function pickMimeType(withVideo) {
        var options = withVideo
            ? ['video/webm;codecs=vp8,opus', 'video/webm', 'video/mp4']
            : ['audio/webm;codecs=opus', 'audio/webm', 'audio/ogg;codecs=opus', 'audio/mp4'];
        return options.find(function (type) { return window.MediaRecorder.isTypeSupported(type); }) || '';
    }

    function startRecording() {
        if (!mediaStream || recorder) return;
        var withVideo = mediaStream.getVideoTracks().length > 0;
        var mimeType = pickMimeType(withVideo);
        try {
            recorder = new MediaRecorder(mediaStream, {
                mimeType: mimeType || undefined,
                audioBitsPerSecond: 32000,
                videoBitsPerSecond: withVideo ? 250000 : undefined
            });
        } catch (e) { recorder = null; return; }
        chunks = [];
        recorder.ondataavailable = function (event) { if (event.data && event.data.size) chunks.push(event.data); };
        recorder.start(1000);
        recLabel.hidden = false;
    }

    // Stops the recorder and uploads what it captured for the given turn. Never blocks the viva.
    function finishRecording(turnId) {
        var active = recorder;
        recorder = null;
        recLabel.hidden = true;
        if (!active) return;
        active.onstop = function () {
            if (!chunks.length) return;
            var type = (active.mimeType || chunks[0].type || 'audio/webm').split(';')[0];
            var blob = new Blob(chunks, { type: type });
            chunks = [];
            var form = new FormData();
            form.append('file', blob, 'answer-' + turnId + (type.indexOf('mp4') >= 0 ? '.mp4' : '.webm'));
            var upload = function (attempt) {
                fetch(baseUrl + '/Recording/' + turnId, { method: 'POST', body: form, headers: { 'RequestVerificationToken': token }, credentials: 'same-origin' })
                    .then(function (response) { if (!response.ok && response.status >= 500 && attempt < 3) setTimeout(function () { upload(attempt + 1); }, 2000 * attempt); })
                    .catch(function () { if (attempt < 3) setTimeout(function () { upload(attempt + 1); }, 2000 * attempt); });
            };
            upload(1);
        };
        try { active.stop(); } catch (e) { /* already stopped */ }
    }

    function stopMedia() {
        if (mediaStream) mediaStream.getTracks().forEach(function (track) { track.stop(); });
        mediaStream = null;
        if (preview) preview.hidden = true;
    }

    // ---- Flow ----------------------------------------------------------------
    function render(next) {
        state = next;
        if (state.serverNowUtc) clockOffset = Date.parse(state.serverNowUtc) - Date.now();
        clearInterval(pollId);
        pollId = null;

        if (state.status === 'NotStarted') { show('intro'); return; }
        if (state.status === 'Completed') { stopTimer(); stopListening(); stopMedia(); show('done'); return; }

        if (!state.currentTurn) {
            // Answer received, the examiner is still deciding: check back shortly.
            field('processing').textContent = text.textProcessing;
            show('processing');
            pollId = setInterval(function () { request('GET', '/State').then(render).catch(function () { }); }, 2000);
            return;
        }
        if (turn && turn.turnId === state.currentTurn.turnId) return; // same question, nothing new
        ask(state.currentTurn);
    }

    function ask(next) {
        turn = next;
        finalText = '';
        answerBox.value = '';
        submitting = false;
        firstWordAt = 0;
        lastWordAt = 0;
        answerOpenedAt = 0;
        field('position').textContent = turn.kind === 'FollowUp'
            ? text.textFollowup
            : text.textMain.replace('{0}', turn.mainIndex).replace('{1}', turn.mainCount);
        field('question').textContent = turn.questionText;
        show('question');
        startTimer();
        ensureMedia().then(readQuestion);
    }

    function readQuestion() {
        stopListening();
        statusLine.textContent = text.textSpeaking;
        speak(turn.questionText, function () {
            if (!answerOpenedAt) answerOpenedAt = Date.now();
            startRecording();
            if (typedMode) { statusLine.textContent = Recognition ? '' : text.textNoSpeech; answerBox.readOnly = false; }
            else startListening();
        });
    }

    function secondsLeft() {
        var deadline = Date.parse(turn.askedAtUtc) + turn.timeLimitSeconds * 1000;
        return Math.max(0, Math.round((deadline - (Date.now() + clockOffset)) / 1000));
    }

    function startTimer() {
        stopTimer();
        var tick = function () {
            var left = secondsLeft();
            timerLabel.textContent = Math.floor(left / 60) + ':' + String(left % 60).padStart(2, '0');
            timerLabel.className = 'status-label ' + (left <= 15 ? 'missing' : left <= 45 ? 'warning' : 'ready');
            if (left === 0) submit();
        };
        tick();
        timerId = setInterval(tick, 1000);
    }

    function stopTimer() { clearInterval(timerId); timerId = null; }

    function submit() {
        if (!turn || submitting) return;
        submitting = true;
        stopTimer();
        stopListening();
        if (window.speechSynthesis) window.speechSynthesis.cancel();
        field('processing').textContent = text.textProcessing;
        show('processing');
        clearError();
        var answeredTurn = turn.turnId;
        finishRecording(answeredTurn);
        var body = {
            turnId: answeredTurn,
            transcript: answerBox.value,
            inputMode: typedMode ? 1 : 0,
            responseDelayMs: answerOpenedAt && firstWordAt ? Math.max(0, firstWordAt - answerOpenedAt) : null,
            speakingMs: firstWordAt && lastWordAt > firstWordAt ? lastWordAt - firstWordAt : null
        };
        request('POST', '/Answer', body)
            .then(render)
            .catch(function (error) {
                showError(error.message);
                // Let the candidate try again on the same question.
                submitting = false;
                show('question');
                startTimer();
            });
    }

    // ---- Wiring --------------------------------------------------------------
    root.querySelector('[data-action="start"]').addEventListener('click', function (event) {
        var consentBox = field('consent');
        var consent = !!(consentBox && consentBox.checked);
        if (recordingMode() !== 'None' && !consent) { showError(text.textConsent); return; }
        event.target.disabled = true;
        clearError();
        // Ask for the microphone (and camera) up front so the first question is not interrupted by the prompt.
        var ready = recordingMode() !== 'None'
            ? ensureMedia()
            : navigator.mediaDevices && navigator.mediaDevices.getUserMedia && !typedMode
                ? navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) { stream.getTracks().forEach(function (t) { t.stop(); }); }).catch(function () { switchToTyping(text.textMicError); })
                : Promise.resolve();
        ready.then(function () { return request('POST', '/Start', { recordingConsent: consent }); })
            .then(render)
            .catch(function (error) { event.target.disabled = false; showError(error.message); });
    });
    root.querySelector('[data-action="submit"]').addEventListener('click', submit);
    root.querySelector('[data-action="repeat"]').addEventListener('click', function () { if (turn) readQuestion(); });
    typingBox.addEventListener('change', function () {
        if (typingBox.checked) switchToTyping('');
        else if (Recognition) { typedMode = false; answerBox.readOnly = true; startListening(); }
    });
    if (window.speechSynthesis) window.speechSynthesis.onvoiceschanged = function () { };
    if (!Recognition) { typingBox.checked = true; typingBox.disabled = true; }

    render(state);
})();
