(function () {
    'use strict';

    var sandbox = document.getElementById('speechSandbox');
    if (!sandbox) return;

    var Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    var active = null;
    var audio = null;

    function setStatus(element, message, isError) {
        element.textContent = message || '';
        element.classList.toggle('text-danger', !!isError);
        element.classList.toggle('text-success', !isError && !!message);
    }

    function testVoice(card) {
        var language = card.dataset.speechLanguage === 'vi-VN' ? 'Vi' : 'En';
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

    function finishTest(session, message, isError) {
        if (active !== session) return;
        active = null;
        session.running = false;
        session.card.querySelector('[data-start-stt]').disabled = !Recognition;
        session.card.querySelector('[data-stop-stt]').disabled = true;
        if (message) setStatus(session.card.querySelector('[data-stt-status]'), message, isError);
    }

    function stopTest(session, message, isError) {
        if (active !== session || session.stopping) return;
        session.stopping = true;
        session.running = false;
        session.stopMessage = message;
        session.stopError = isError;
        try { session.recognition.stop(); }
        catch (_) { finishTest(session, message, isError); }
    }

    function startTest(card) {
        if (active) return;
        if (typeof Recognition !== 'function') {
            setStatus(card.querySelector('[data-stt-status]'), sandbox.dataset.unsupported, true);
            return;
        }

        var recognition;
        try { recognition = new Recognition(); }
        catch (_) {
            setStatus(card.querySelector('[data-stt-status]'), sandbox.dataset.sttFailed, true);
            return;
        }
        var session = {
            card: card,
            recognition: recognition,
            running: true,
            stopping: false,
            finalText: ''
        };
        active = session;
        card.querySelector('[data-start-stt]').disabled = true;
        card.querySelector('[data-stop-stt]').disabled = false;
        card.querySelector('[data-stt-result]').value = '';

        var recognition = session.recognition;
        recognition.lang = card.dataset.speechLanguage;
        recognition.continuous = true;
        recognition.interimResults = true;
        recognition.onresult = function (event) {
            if (active !== session) return;
            var interim = '';
            for (var i = event.resultIndex; i < event.results.length; i++) {
                var phrase = event.results[i][0].transcript.trim();
                if (!phrase) continue;
                if (event.results[i].isFinal) session.finalText = (session.finalText + ' ' + phrase).trim();
                else interim += phrase;
            }
            card.querySelector('[data-stt-result]').value =
                (session.finalText + (interim ? ' ' + interim : '')).trim();
        };
        recognition.onerror = function () {
            if (active === session)
                stopTest(session, sandbox.dataset.sttFailed, true);
        };
        recognition.onend = function () {
            if (active !== session) return;
            if (session.running) {
                setTimeout(function () {
                    if (active === session && session.running) {
                        try { recognition.start(); }
                        catch (_) { stopTest(session, sandbox.dataset.sttFailed, true); }
                    }
                }, 250);
            } else {
                finishTest(session, session.stopMessage || sandbox.dataset.stopped, session.stopError);
            }
        };

        setStatus(card.querySelector('[data-stt-status]'), sandbox.dataset.listening, false);
        try { recognition.start(); }
        catch (_) { stopTest(session, sandbox.dataset.sttFailed, true); }
    }

    sandbox.querySelectorAll('[data-speech-language]').forEach(function (card) {
        var startButton = card.querySelector('[data-start-stt]');
        startButton.disabled = typeof Recognition !== 'function';
        if (!Recognition)
            setStatus(card.querySelector('[data-stt-status]'), sandbox.dataset.unsupported, true);
        card.querySelector('[data-test-tts]').addEventListener('click', function () { testVoice(card); });
        startButton.addEventListener('click', function () { startTest(card); });
        card.querySelector('[data-stop-stt]').addEventListener('click', function () {
            if (active && active.card === card)
                stopTest(active, sandbox.dataset.stopped, false);
        });
    });

    window.addEventListener('pagehide', function () {
        if (active) {
            var session = active;
            active = null;
            session.running = false;
            try { session.recognition.abort(); } catch (_) { }
        }
        if (audio) {
            audio.pause();
            URL.revokeObjectURL(audio.src);
            audio = null;
        }
    });
})();
