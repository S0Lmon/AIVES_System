const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../AIVES.WebRazor/wwwroot/js/interview.js'), 'utf8');
const flush = () => new Promise(resolve => setImmediate(resolve));
const turn = id => ({ turnId: id, kind: 'Main', mainIndex: id, mainCount: 2,
    questionText: 'Question ' + id, askedAtUtc: new Date().toISOString(), timeLimitSeconds: 120 });
const state = id => ({ status: 'InProgress', recording: 'AudioVideo', recordingConsentGiven: true,
    currentTurn: turn(id), serverNowUtc: new Date().toISOString() });
const response = (data, ok = true) => ({ ok, status: ok ? 200 : 500, json: async () => data });

function browser() {
    const elements = new Map();
    const element = name => {
        if (!elements.has(name)) elements.set(name, {
            value: '', dataset: {}, checked: true, addEventListener(event, callback) { this[event] = callback; }
        });
        return elements.get(name);
    };
    const uploads = [], requests = [], recorders = [], sessionRecorders = [], sessionUploads = [];
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
    const root = {
        dataset: { baseUrl: '/Interview/1', lang: 'vi-VN', initialState: JSON.stringify(state(1)),
            textMain: 'Question {0}/{1}', textError: 'Error', textRecordError: 'Recording failed' },
        querySelector: element,
        querySelectorAll: () => []
    };
    vm.runInNewContext(source, {
        document: { getElementById: () => root, querySelector: () => ({ value: 'test-token' }) },
        window: { MediaRecorder: Recorder }, MediaRecorder: Recorder, MediaStream: Stream, Blob, FormData,
        navigator: { mediaDevices: { getUserMedia: async () => ({ getVideoTracks: () => [{ kind: 'video' }],
            getAudioTracks: () => [{ kind: 'audio' }], getTracks: () => [{ stop() {} }] }) } },
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
    return { uploads, requests, recorders, sessionRecorders, sessionUploads, submit: () => element('[data-action="submit"]').click() };
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
