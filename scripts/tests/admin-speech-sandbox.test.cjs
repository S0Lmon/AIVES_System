const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../AIVES.WebRazor/wwwroot/js/admin-speech-sandbox.js'), 'utf8');

function browser(language, supported = true) {
    const recognizers = [];
    const inputs = new Map([
        ['Input_VietnameseVoice', { value: 'edge:hoaimy' }],
        ['Input_EnglishVoice', { value: 'edge:aria' }],
        ['Input_SpeechRate', { value: '1' }]
    ]);
    const makeElement = () => ({
        value: '',
        textContent: '',
        disabled: false,
        classList: { toggle() {} },
        addEventListener(event, callback) { this[event] = callback; }
    });
    const elements = new Map();
    const card = {
        dataset: { speechLanguage: language },
        querySelector(selector) {
            if (!elements.has(selector)) elements.set(selector, makeElement());
            return elements.get(selector);
        }
    };
    class Recognition {
        constructor() { recognizers.push(this); }
        start() { this.started = true; }
        stop() { this.stopped = true; if (this.onend) this.onend(); }
        abort() { this.aborted = true; }
    }
    const sandbox = {
        dataset: {
            ttsUrl: '/Admin/Settings?handler=TestVoice',
            unsupported: 'Browser speech recognition unavailable',
            listening: 'Listening',
            stopped: 'Stopped',
            sttFailed: 'Speech recognition failed'
        },
        querySelectorAll: () => [card]
    };
    vm.runInNewContext(source, {
        document: { getElementById: id => id === 'speechSandbox' ? sandbox : inputs.get(id) },
        window: { SpeechRecognition: supported ? Recognition : undefined, addEventListener() {} },
        fetch: () => Promise.reject(new Error('unused'))
    });
    return { card, elements, recognizers };
}

const speechResult = (transcript, isFinal) => Object.assign([{ transcript }], { isFinal });

test('admin sandbox uses the browser recognizer with the selected language', () => {
    const b = browser('vi-VN');
    const start = b.card.querySelector('[data-start-stt]');
    assert.equal(start.disabled, false);
    start.click();
    const recognition = b.recognizers[0];
    assert.equal(recognition.lang, 'vi-VN');
    assert.equal(recognition.started, true);
    recognition.onresult({ resultIndex: 0, results: [speechResult('xin chào', true)] });
    assert.equal(b.card.querySelector('[data-stt-result]').value, 'xin chào');
    b.card.querySelector('[data-stop-stt]').click();
    assert.equal(recognition.stopped, true);
    assert.equal(b.card.querySelector('[data-stt-status]').textContent, 'Stopped');
});

test('admin sandbox disables speech testing when the browser API is unavailable', () => {
    const b = browser('en-US', false);
    assert.equal(b.card.querySelector('[data-start-stt]').disabled, true);
    assert.equal(b.card.querySelector('[data-stt-status]').textContent, 'Browser speech recognition unavailable');
    assert.equal(b.recognizers.length, 0);
});
