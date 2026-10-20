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
    const uploads = [], requests = [], recorders = [];
    class Recorder {
        static isTypeSupported() { return true; }
        constructor() { this.mimeType = 'video/webm'; recorders.push(this); }
        start() {}
        stop() { this.stopped = true; }
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
        window: { MediaRecorder: Recorder }, MediaRecorder: Recorder, Blob, FormData,
        navigator: { mediaDevices: { getUserMedia: async () => ({ getVideoTracks: () => [{}],
            getTracks: () => [{ stop() {} }] }) } },
        fetch: (url, options) => {
            if (url.includes('/Recording/')) {
                uploads.push({ url, options });
                return Promise.resolve(response({}));
            }
            return new Promise(resolve => requests.push({ url, options, resolve }));
        },
        setInterval: () => 1, clearInterval() {}, setTimeout
    });
    return { uploads, requests, recorders, submit: () => element('[data-action="submit"]').click() };
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
