(function () {
    'use strict';

    var sandbox = document.getElementById('speechSandbox');
    if (!sandbox) return;

    var active = null;
    var audio = null;

    function setStatus(element, message, isError) {
        element.textContent = message || '';
        element.classList.toggle('text-danger', !!isError);
        element.classList.toggle('text-success', !isError && !!message);
    }

    function testVoice(card) {
        var language = card.dataset.speechLanguage;
        var voiceId = language === 'Vi' ? 'Input_VietnameseVoice' : 'Input_EnglishVoice';
        var voice = document.getElementById(voiceId).value;
        var rate = document.getElementById('Input_SpeechRate').value;
        var endpoint = sandbox.dataset.ttsUrl;
        var separator = endpoint.indexOf('?') >= 0 ? '&' : '?';
        var url = endpoint + separator + 'language=' + encodeURIComponent(language)
            + '&voiceName=' + encodeURIComponent(voice)
            + '&speechRate=' + encodeURIComponent(rate);
        var status = card.querySelector('[data-tts-status]');
        setStatus(status, sandbox.dataset.generating, false);
        if (audio) {
            audio.pause();
            URL.revokeObjectURL(audio.src);
            audio = null;
        }

        fetch(url, { credentials: 'same-origin' })
            .then(function (response) {
                if (!response.ok) {
                    return response.json().catch(function () { return {}; }).then(function (data) {
                        throw new Error(data.error || sandbox.dataset.ttsFailed);
                    });
                }
                return response.blob();
            })
            .then(function (blob) {
                var objectUrl = URL.createObjectURL(blob);
                var player = new Audio(objectUrl);
                audio = player;
                function release() {
                    URL.revokeObjectURL(objectUrl);
                    if (audio === player) audio = null;
                }
                player.onended = function () {
                    release();
                    setStatus(status, sandbox.dataset.finished, false);
                };
                player.onerror = function () {
                    release();
                    setStatus(status, sandbox.dataset.playbackFailed, true);
                };
                setStatus(status, sandbox.dataset.playing, false);
                return player.play().catch(function (error) {
                    release();
                    throw error;
                });
            })
            .catch(function (error) {
                setStatus(status, error.message || sandbox.dataset.ttsFailed, true);
            });
    }

    function cleanup(session) {
        if (!session) return Promise.resolve();
        if (session.processor) {
            session.processor.disconnect();
            session.processor.onaudioprocess = null;
        }
        if (session.source) session.source.disconnect();
        if (session.sink) session.sink.disconnect();
        if (session.stream) session.stream.getTracks().forEach(function (track) { track.stop(); });
        var stopRecognition = session.connection
            ? session.connection.invoke('StopRecognition').catch(function () { })
                .then(function () { return session.connection.stop(); }).catch(function () { })
            : Promise.resolve();
        var closeAudio = session.context ? session.context.close().catch(function () { }) : Promise.resolve();
        return Promise.all([stopRecognition, closeAudio]);
    }

    function stopTest(session, message, isError) {
        if (active !== session) return;
        active = null;
        session.card.querySelector('[data-start-stt]').disabled = session.card.dataset.modelReady !== 'true';
        session.card.querySelector('[data-stop-stt]').disabled = true;
        if (message) setStatus(session.card.querySelector('[data-stt-status]'), message, isError);
        cleanup(session);
    }

    function startCapture(session) {
        var context = session.context;
        session.source = context.createMediaStreamSource(session.stream);
        session.processor = context.createScriptProcessor(2048, 1, 1);
        session.sink = context.createGain();
        session.sink.gain.value = 0;
        session.processor.onaudioprocess = function (event) {
            if (active !== session) return;
            var input = event.inputBuffer.getChannelData(0);
            var ratio = context.sampleRate / 16000;
            var count = Math.floor(input.length / ratio);
            if (!count) return;
            var pcm = new Int16Array(count);
            for (var i = 0; i < count; i++) {
                var start = Math.floor(i * ratio);
                var end = Math.floor((i + 1) * ratio);
                var total = 0;
                for (var j = start; j < end; j++) total += input[j];
                var sample = Math.max(-1, Math.min(1, total / Math.max(1, end - start)));
                pcm[i] = sample < 0 ? Math.round(sample * 32768) : Math.round(sample * 32767);
            }
            session.audioQueue = session.audioQueue.then(function () {
                if (active === session) {
                    var bytes = new Uint8Array(pcm.buffer);
                    var binary = '';
                    for (var i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
                    return session.connection.invoke('SendAudio', window.btoa(binary));
                }
            }).catch(function (error) {
                stopTest(session, error.message || sandbox.dataset.connectionFailed, true);
            });
        };
        session.source.connect(session.processor);
        session.processor.connect(session.sink);
        session.sink.connect(context.destination);
    }

    function startTest(card) {
        if (active) return;
        var session = {
            card: card,
            language: card.dataset.speechLanguage,
            stream: null,
            context: null,
            source: null,
            processor: null,
            sink: null,
            connection: null,
            audioQueue: Promise.resolve(),
            finalText: ''
        };
        active = session;
        card.querySelector('[data-start-stt]').disabled = true;
        card.querySelector('[data-stop-stt]').disabled = false;
        card.querySelector('[data-stt-result]').value = '';
        setStatus(card.querySelector('[data-stt-status]'), sandbox.dataset.micAccess, false);

        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia || !(window.AudioContext || window.webkitAudioContext) || !window.signalR) {
            stopTest(session, sandbox.dataset.unsupported, true);
            return;
        }

        navigator.mediaDevices.getUserMedia({ audio: true })
            .then(function (stream) {
                if (active !== session) {
                    stream.getTracks().forEach(function (track) { track.stop(); });
                    return;
                }
                session.stream = stream;
                var AudioContextType = window.AudioContext || window.webkitAudioContext;
                try {
                    session.context = new AudioContextType({ sampleRate: 16000 });
                } catch (_) {
                    session.context = new AudioContextType();
                }
                return session.context.resume().then(function () {
                    var connection = new window.signalR.HubConnectionBuilder().withUrl(sandbox.dataset.hubUrl).build();
                    session.connection = connection;
                    connection.on('RecognitionResult', function (testId, update) {
                        if (active !== session || testId !== 0) return;
                        if (update.isFinal) session.finalText = (session.finalText + ' ' + update.text).trim();
                        card.querySelector('[data-stt-result]').value =
                            (session.finalText + (update.isFinal ? '' : ' ' + update.text)).trim();
                    });
                    return connection.start()
                        .then(function () { return connection.invoke('StartAdminTestRecognition', session.language); })
                        .then(function () {
                            if (active !== session) return cleanup(session);
                            setStatus(card.querySelector('[data-stt-status]'), sandbox.dataset.listening, false);
                            startCapture(session);
                        });
                });
            })
            .catch(function (error) {
                stopTest(session, error.message || sandbox.dataset.sttFailed, true);
            });
    }

    sandbox.querySelectorAll('[data-speech-language]').forEach(function (card) {
        card.querySelector('[data-test-tts]').addEventListener('click', function () { testVoice(card); });
        card.querySelector('[data-start-stt]').addEventListener('click', function () { startTest(card); });
        card.querySelector('[data-stop-stt]').addEventListener('click', function () {
            if (active && active.card === card) stopTest(active, sandbox.dataset.stopped, false);
        });
    });

    window.addEventListener('pagehide', function () {
        if (active) {
            var session = active;
            active = null;
            cleanup(session);
        }
        if (audio) {
            audio.pause();
            URL.revokeObjectURL(audio.src);
            audio = null;
        }
    });
})();
