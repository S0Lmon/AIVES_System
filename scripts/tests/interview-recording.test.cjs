const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../AIVES.WebRazor/wwwroot/js/interview.js'), 'utf8');
const flush = () => new Promise(resolve => setImmediate(resolve));
const turn = id => ({ turnId: id, kind: 'Main', mainIndex: id, mainCount: 2,
    questionText: 'Question ' + id, askedAtUtc: new Date().toISOString(), timeLimitSeconds: 120 });
const state = (id, options = {}) => ({ status: options.status || 'InProgress', recording: options.recording || 'AudioVideo',
    recordingConsentGiven: true, currentTurn: options.status === 'NotStarted' ? null : turn(id), serverNowUtc: new Date().toISOString() });
const response = (data, ok = true) => ({ ok, status: ok ? 200 : 500, json: async () => data });

function browser(options = {}) {
    const elements = new Map();
    const element = name => {
        if (!elements.has(name)) elements.set(name, {
            value: '', dataset: {}, checked: false, addEventListener(event, callback) { this[event] = callback; }, focus() { }
        });
        return elements.get(name);
    };
    const uploads = [], requests = [], recorders = [], sessionRecorders = [], sessionUploads = [], recognitions = [];
    let microphoneRequests = 0;
    class Stream {
        constructor(tracks) { this.tracks = tracks || []; }
        getAudioTracks() { return this.tracks.filter(track => track.kind === 'audio'); }
        getVideoTracks() { return this.tracks.filter(track => track.kind === 'video'); }
    }
    class Recorder {
        static isTypeSupported() { return true; }
        // The whole-viva recorder records a combined MediaStream; per-answer recorders record the camera stream.
        constructor(stream) { this.mimeType = 'video/webm'; this.state = 'inactive'; (stream instanceof Stream ? sessionRecorders : recorders).push(this); }
        start(timeslice) { this.state = 'recording'; this.timeslice = timeslice; }
        stop() { this.stopped = true; this.state = 'inactive'; }
        finish(text) {
            this.ondataavailable({ data: new Blob([text], { type: this.mimeType }) });
            this.onstop();
        }
    }
    const window = { MediaRecorder: Recorder };
    if (options.speechRecognition) {
        window.SpeechRecognition = class {
            constructor() { recognitions.push(this); }
            start() { this.started = true; }
            stop() { this.stopped = true; }
        };
    }
    const root = {
        dataset: { baseUrl: '/Interview/1', lang: 'vi-VN', initialState: JSON.stringify(options.initialState || state(1)),
            textMain: 'Question {0}/{1}', textError: 'Error', textRecordError: 'Recording failed',
            textTypeAnswer: 'Type your answer below.' },
        querySelector: element,
        querySelectorAll: () => []
    };
    vm.runInNewContext(source, {
        document: { getElementById: () => root, querySelector: () => ({ value: 'test-token' }) },
        window, MediaRecorder: Recorder, MediaStream: Stream, Blob, FormData,
        navigator: { mediaDevices: { getUserMedia: async () => { microphoneRequests++; return { getVideoTracks: () => [{ kind: 'video' }],
            getAudioTracks: () => [{ kind: 'audio' }], getTracks: () => [{ stop() {} }] }; } } },
        fetch: (url, options) => {
            // No server voice in these tests: the question is "read" by the (absent) browser voice at once.
            if (url.includes('/QuestionAudio/')) return Promise.reject(new Error('no server voice'));
            if (url.includes('/SessionRecording')) {
                sessionUploads.push({ url, options });
                return Promise.resolve(response({}));
            }
            if (url.includes('/Recording/')) {
                uploads.push({ url, options });
                return Promise.resolve(response({}));
            }
            return new Promise(resolve => requests.push({ url, options, resolve }));
        },
        setInterval: () => 1, clearInterval() {}, setTimeout
    });
    return {
        uploads, requests, recorders, sessionRecorders, sessionUploads, recognitions,
        answer: () => element('[data-field="answer"]'),
        typing: () => element('[data-field="typing"]'),
        speechConsent: () => element('[data-field="speech-consent"]'),
        start: () => element('[data-action="start"]').click({ target: { disabled: false } }),
        microphoneRequests: () => microphoneRequests,
        submit: () => element('[data-action="submit"]').click()
    };
}

test('camera recording waits for the answer to be saved before uploading', async () => {
    const b = browser();
    await flush();
    b.submit();
    b.recorders[0].finish('first evidence');
    await flush();
    assert.equal(b.uploads.length, 0);
    b.requests[0].resolve(response(state(2)));
    await flush();
    assert.equal(b.uploads.length, 1);
    assert.equal(await b.uploads[0].options.body.get('file').text(), 'first evidence');
    assert.equal(b.uploads[0].options.headers.RequestVerificationToken, 'test-token');
});

test('retrying a failed answer retains its original camera evidence', async () => {
    const b = browser();
    await flush();
    b.submit();
    b.recorders[0].finish('retained evidence');
    b.requests[0].resolve(response({ error: 'Temporary failure' }, false));
    await flush();
    assert.equal(b.uploads.length, 0);
    b.submit();
    b.requests[1].resolve(response(state(2)));
    await flush();
    assert.equal(b.uploads.length, 1);
    assert.equal(await b.uploads[0].options.body.get('file').text(), 'retained evidence');
});

test('a delayed recorder stop cannot mix evidence from adjacent questions', async () => {
    const b = browser();
    await flush();
    b.submit();
    b.requests[0].resolve(response(state(2)));
    await flush();
    assert.equal(b.recorders.length, 2);
    b.recorders[1].ondataavailable({ data: new Blob(['second evidence']) });
    b.recorders[0].finish('first evidence');
    await flush();
    assert.equal(await b.uploads[0].options.body.get('file').text(), 'first evidence');
    assert.equal(b.uploads[0].url, '/Interview/1/Recording/1');
});

test('the whole viva is recorded by one recorder and uploaded piece by piece', async () => {
    const b = browser();
    await flush();
    assert.equal(b.sessionRecorders.length, 1);
    const session = b.sessionRecorders[0];
    assert.equal(session.timeslice, 15000);
    session.ondataavailable({ data: new Blob(['piece 0']) });
    session.ondataavailable({ data: new Blob(['piece 1']) });
    await flush();
    await flush();
    assert.equal(b.sessionUploads.length, 2);
    assert.equal(b.sessionUploads[0].url, '/Interview/1/SessionRecording');
    const first = b.sessionUploads[0].options.body;
    assert.equal(first.get('index'), '0');
    assert.equal(b.sessionUploads[1].options.body.get('index'), '1');
    assert.equal(first.get('segment'), b.sessionUploads[1].options.body.get('segment'));
    assert.equal(await first.get('file').text(), 'piece 0');
    assert.equal(b.sessionUploads[0].options.headers.RequestVerificationToken, 'test-token');

    // Moving to the next question keeps the same whole-viva recorder running.
    b.submit();
    b.requests[0].resolve(response(state(2)));
    await flush();
    assert.equal(b.sessionRecorders.length, 1);
    assert.ok(!session.stopped);

    // The end of the viva stops it, so its last piece is flushed and uploaded.
    b.submit();
    b.requests[1].resolve(response({ status: 'Completed', recording: 'AudioVideo', recordingConsentGiven: true }));
    await flush();
    assert.ok(session.stopped);
});

test('browser speech recognition uses the exam language and submits recognized text as speech', async () => {
    const b = browser({ speechRecognition: true, initialState: state(1, { status: 'NotStarted', recording: 'None' }) });
    b.speechConsent().checked = true;
    b.start();
    await flush();
    b.requests[0].resolve(response(state(1, { recording: 'None' })));
    await flush();
    const recognition = b.recognitions[0];
    assert.ok(recognition);
    assert.equal(recognition.lang, 'vi-VN');
    assert.equal(recognition.continuous, true);
    assert.equal(recognition.interimResults, true);

    const result = [{ transcript: 'Ba lớp gồm giao diện, nghiệp vụ và dữ liệu.' }];
    result.isFinal = true;
    recognition.onresult({ resultIndex: 0, results: [result] });
    b.submit();

    const answer = JSON.parse(b.requests[1].options.body);
    assert.equal(answer.transcript, 'Ba lớp gồm giao diện, nghiệp vụ và dữ liệu.');
    assert.equal(answer.inputMode, 0);
});

test('denied or unavailable browser speech recognition leaves a typing fallback', async () => {
    const denied = browser({ speechRecognition: true, initialState: state(1, { status: 'NotStarted', recording: 'None' }) });
    denied.speechConsent().checked = true;
    denied.start();
    await flush();
    denied.requests[0].resolve(response(state(1, { recording: 'None' })));
    await flush();
    denied.recognitions[0].onerror({ error: 'not-allowed' });
    assert.equal(denied.typing().checked, true);
    assert.equal(denied.answer().readOnly, false);
    denied.answer().value = 'Typed answer';
    denied.submit();
    assert.equal(JSON.parse(denied.requests[1].options.body).inputMode, 1);

    const unsupported = browser();
    await flush();
    assert.equal(unsupported.typing().disabled, true);
    assert.equal(unsupported.answer().readOnly, false);
});

test('browser speech recognition is opt-in and typing does not request the microphone for STT', async () => {
    const b = browser({ speechRecognition: true, initialState: state(1, { status: 'NotStarted', recording: 'None' }) });
    b.start();
    await flush();
    assert.equal(b.microphoneRequests(), 0);
    b.requests[0].resolve(response(state(1, { recording: 'None' })));
    await flush();
    assert.equal(b.recognitions.length, 0);
    assert.equal(b.typing().checked, true);
    assert.equal(b.typing().disabled, true);
    assert.equal(b.answer().readOnly, false);
});
