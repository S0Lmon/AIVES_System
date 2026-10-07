// The viva room. Each question is read aloud (WAV synthesised on the server), then the microphone
// is captured with an AudioWorklet, downsampled to 16 kHz 16-bit PCM and streamed to InterviewHub,
// which sends the transcript back while the candidate speaks. Server text is written with
// textContent only.
(() => {
    const root = document.querySelector('[data-viva]');
    if (!root || !window.signalR) return;

    const $ = (selector) => root.querySelector(selector);
    const candidateId = Number(root.dataset.candidateId);
    const canListen = root.dataset.canListen === 'true';
    const canSpeak = root.dataset.canSpeak === 'true';
    const audioUrl = root.dataset.audioUrl;
    const TARGET_RATE = 16000;
    const CHUNK_SAMPLES = TARGET_RATE / 4; // 250 ms per message
    const SPEECH_LEVEL = 0.02;

    const ui = {
        error: $('[data-viva-error]'),
        progress: $('[data-viva-progress]'),
        timer: $('[data-viva-timer]'),
        clock: $('[data-viva-clock]'),
        kind: $('[data-viva-kind]'),
        followUps: $('[data-viva-followups]'),
        question: $('[data-viva-question]'),
        status: $('[data-viva-status]'),
        level: $('[data-viva-level]'),
        live: $('[data-viva-live]'),
        text: $('[data-viva-text]'),
        consent: $('[data-viva-consent]'),
        buttons: {
            start: $('[data-viva-start]'),
            replay: $('[data-viva-replay]'),
            record: $('[data-viva-record]'),
            stop: $('[data-viva-stop]'),
            retry: $('[data-viva-retry]'),
            type: $('[data-viva-type]'),
            submit: $('[data-viva-submit]')
        }
    };

    let state = JSON.parse($('[data-viva-state]').textContent);
    let skewMs = 0;
    let turnId = null;
    let turn = null;
    let phase = 'idle'; // asking | recording | finishing | review | typing | submitting
    let recognized = '';
    let streamDone = null;
    let subject = null;
    let pending = [];
    let pendingLength = 0;
    let recordStartedAt = 0;
    let firstSpeechAt = null;
    let timeUpHandled = false;
    // A reloaded page has no user gesture yet, so it waits for a click before playing audio.
    let autoplay = false;
    let mic = null;
    const player = new Audio();

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(root.dataset.hubUrl)
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    // ---- Rendering -------------------------------------------------------

    const showError = (message) => {
        ui.error.textContent = message || '';
        ui.error.hidden = !message;
    };

    const messageOf = (error) => {
        const text = String(error && error.message || error || '');
        const hub = text.indexOf('HubException: ');
        return hub >= 0 ? text.slice(hub + 14) : 'Đã xảy ra lỗi kết nối. Vui lòng thử lại.';
    };

    const showPanel = (name) => root.querySelectorAll('[data-viva-panel]').forEach((panel) => {
        panel.hidden = panel.dataset.vivaPanel !== name;
    });

    const setPhase = (next, status) => {
        phase = next;
        if (status !== undefined) ui.status.textContent = status;
        const b = ui.buttons;
        const show = (button, visible) => { if (button) button.hidden = !visible; };
        show(b.replay, canSpeak && ['asking', 'review', 'typing'].includes(phase));
        show(b.record, canListen && ['asking', 'typing'].includes(phase));
        show(b.stop, phase === 'recording');
        show(b.retry, canListen && phase === 'review');
        show(b.type, ['asking', 'recording'].includes(phase));
        show(b.submit, ['review', 'typing'].includes(phase));
        ui.text.hidden = !['review', 'typing'].includes(phase);
        ui.live.hidden = !['recording', 'finishing'].includes(phase);
        root.classList.toggle('is-recording', phase === 'recording');
    };

    const render = (next) => {
        state = next;
        skewMs = Date.parse(state.serverNowUtc) - Date.now();
        if (state.status === 'Completed') {
            stopMicrophone();
            ui.timer.hidden = true;
            ui.progress.textContent = '';
            showPanel('done');
            return;
        }
        if (state.status === 'NotStarted' || !state.currentTurn) {
            showPanel('start');
            return;
        }
        showPanel('turn');
        if (state.currentTurn.turnId !== turnId) newTurn(state.currentTurn);
    };

    const newTurn = (next) => {
        turn = next;
        turnId = next.turnId;
        recognized = '';
        timeUpHandled = false;
        ui.text.value = '';
        ui.live.textContent = '';
        showError('');

        const followUp = next.kind === 'FollowUp';
        ui.kind.textContent = followUp ? `Câu hỏi xoáy ${next.followUpIndex}` : `Câu chính ${next.mainIndex}/${next.mainCount}`;
        ui.kind.classList.toggle('is-follow-up', followUp);
        ui.followUps.textContent = next.maxFollowUpsPerQuestion > 0
            ? `Tối đa ${next.maxFollowUpsPerQuestion} lượt hỏi xoáy cho mỗi câu · ${Math.round(next.timeLimitSeconds / 60 * 10) / 10} phút mỗi lượt`
            : '';
        ui.progress.textContent = followUp
            ? 'Giám khảo AI muốn bạn làm rõ thêm câu trả lời vừa rồi.'
            : `Câu hỏi ${next.mainIndex} trên ${next.mainCount}`;
        ui.question.textContent = next.questionText;
        ui.timer.hidden = false;

        if (!autoplay) {
            autoplay = true;
            setPhase('asking', 'Bấm "Nghe lại câu hỏi" hoặc "Bắt đầu trả lời" để tiếp tục.');
            return;
        }
        askQuestion(next).then(() => {
            if (turnId === next.turnId && phase === 'asking') beginAnswer();
        });
    };

    // ---- Timer -----------------------------------------------------------

    const remainingMs = () => turn
        ? Date.parse(turn.askedAtUtc) + turn.timeLimitSeconds * 1000 - (Date.now() + skewMs)
        : 0;

    setInterval(() => {
        if (!turn || state.status !== 'InProgress') return;
        const left = Math.max(0, remainingMs());
        const seconds = Math.ceil(left / 1000);
        ui.clock.textContent = `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
        ui.timer.classList.toggle('is-low', seconds <= 15);
        if (left <= 0 && !timeUpHandled && phase !== 'submitting') {
            timeUpHandled = true;
            timeUp();
        }
    }, 250);

    const timeUp = async () => {
        ui.status.textContent = 'Hết giờ, đang nộp câu trả lời…';
        if (phase === 'recording') await stopRecording();
        if (phase === 'finishing' && streamDone) await streamDone.catch(() => {});
        await submit();
    };

    // ---- Question audio --------------------------------------------------

    const askQuestion = (current) => {
        setPhase('asking', canSpeak ? 'Giám khảo AI đang đọc câu hỏi…' : 'Đọc câu hỏi, rồi bắt đầu trả lời.');
        if (!canSpeak) return Promise.resolve();
        return new Promise((resolve) => {
            player.onended = resolve;
            player.onerror = resolve;
            player.src = `${audioUrl}&turnId=${current.turnId}`;
            player.play().catch(resolve);
        });
    };

    const beginAnswer = () => (canListen ? startRecording() : startTyping());

    // ---- Microphone ------------------------------------------------------

    const ensureMicrophone = async () => {
        if (mic) return mic;
        const stream = await navigator.mediaDevices.getUserMedia({
            audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true }
        });
        const context = new AudioContext();
        await context.audioWorklet.addModule(root.dataset.workletUrl);
        const source = context.createMediaStreamSource(stream);
        const node = new AudioWorkletNode(context, 'pcm-capture');
        const mute = context.createGain();
        mute.gain.value = 0;
        source.connect(node);
        node.connect(mute).connect(context.destination);
        node.port.onmessage = (event) => onSamples(event.data, context.sampleRate);
        mic = { stream, context };
        return mic;
    };

    const stopMicrophone = () => {
        if (!mic) return;
        mic.stream.getTracks().forEach((track) => track.stop());
        mic.context.close();
        mic = null;
    };

    const onSamples = (block, rate) => {
        if (phase !== 'recording' || !subject) return;
        let sum = 0;
        for (const sample of block) sum += sample * sample;
        const rms = Math.sqrt(sum / block.length);
        ui.level.style.width = `${Math.min(100, rms * 400)}%`;
        if (rms >= SPEECH_LEVEL && firstSpeechAt === null) firstSpeechAt = performance.now();

        // Downsample to 16 kHz with linear interpolation, then to 16-bit PCM.
        const ratio = rate / TARGET_RATE;
        const length = Math.floor(block.length / ratio);
        const pcm = new Int16Array(length);
        for (let i = 0; i < length; i++) {
            const position = i * ratio;
            const index = Math.floor(position);
            const fraction = position - index;
            const next = index + 1 < block.length ? block[index + 1] : block[index];
            const value = block[index] * (1 - fraction) + next * fraction;
            pcm[i] = Math.max(-32768, Math.min(32767, Math.round(value * 32767)));
        }
        pending.push(pcm);
        pendingLength += pcm.length;
        if (pendingLength >= CHUNK_SAMPLES) flush();
    };

    const flush = () => {
        if (!pendingLength || !subject) return;
        const all = new Int16Array(pendingLength);
        let offset = 0;
        for (const part of pending) { all.set(part, offset); offset += part.length; }
        pending = [];
        pendingLength = 0;
        const bytes = new Uint8Array(all.buffer);
        let binary = '';
        for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
        subject.next(btoa(binary));
    };

    // ---- Answering -------------------------------------------------------

    const startRecording = async () => {
        player.pause();
        try {
            await ensureMicrophone();
            if (mic.context.state === 'suspended') await mic.context.resume();
        } catch (error) {
            console.warn('[AIVES] microphone unavailable', error);
            showError('Không dùng được micro. Hãy cho phép trình duyệt truy cập micro, hoặc gõ câu trả lời.');
            startTyping();
            return;
        }
        showError('');
        recognized = '';
        ui.live.textContent = '…';
        pending = [];
        pendingLength = 0;
        recordStartedAt = performance.now();
        firstSpeechAt = null;
        subject = new signalR.Subject();
        const streamTurn = turnId;
        setPhase('recording', 'Đang nghe bạn trả lời. Chữ sẽ hiện ngay khi bạn nói.');
        streamDone = connection.invoke('StreamAnswer', candidateId, streamTurn, subject).catch((error) => {
            if (turnId !== streamTurn) return;
            showError(messageOf(error));
            subject = null;
            startTyping();
            throw error;
        });
    };

    const stopRecording = async () => {
        if (phase !== 'recording' || !subject) return;
        flush();
        subject.complete();
        subject = null;
        ui.level.style.width = '0';
        setPhase('finishing', 'Đang nhận dạng phần cuối câu trả lời…');
        try {
            await streamDone;
        } catch {
            return;
        }
        if (phase !== 'finishing') return;
        ui.text.value = recognized;
        setPhase('review', recognized
            ? 'Kiểm tra lại câu trả lời rồi bấm Nộp. Phần bạn sửa sẽ được ghi nhận là gõ tay.'
            : 'Không nghe rõ câu trả lời. Bạn có thể trả lời lại hoặc gõ.');
    };

    const startTyping = () => {
        if (phase === 'recording' && subject) {
            subject.complete();
            subject = null;
        }
        if (!ui.text.value && recognized) ui.text.value = recognized;
        setPhase('typing', 'Gõ câu trả lời của bạn rồi bấm Nộp.');
        ui.text.focus();
    };

    const submit = async () => {
        if (phase === 'submitting') return;
        const answeringTurn = turnId;
        const text = ['review', 'typing'].includes(phase) ? ui.text.value : recognized;
        const delay = firstSpeechAt === null ? null : Math.round(firstSpeechAt - recordStartedAt);
        setPhase('submitting', 'Đang gửi câu trả lời, giám khảo AI đang xem xét…');
        try {
            const next = await connection.invoke('SubmitAnswer', candidateId, answeringTurn, text, delay);
            render(next);
        } catch (error) {
            showError(messageOf(error));
            setPhase('typing', 'Gửi chưa được. Bấm Nộp để thử lại.');
            ui.text.value = text;
        }
    };

    connection.on('Transcript', (forTurn, text, isFinal) => {
        if (forTurn !== turnId) return;
        if (phase === 'recording' || phase === 'finishing') ui.live.textContent = text || '…';
        if (isFinal) recognized = text;
    });

    // ---- Buttons ---------------------------------------------------------

    ui.buttons.start?.addEventListener('click', async () => {
        if (ui.consent && !ui.consent.checked) {
            showError('Bài thi này có ghi âm. Hãy đồng ý ghi âm để bắt đầu.');
            return;
        }
        ui.buttons.start.disabled = true;
        // Ask for the microphone now, while the click still counts as a user gesture.
        if (canListen) await ensureMicrophone().catch(() => {});
        try {
            autoplay = true;
            render(await connection.invoke('Start', candidateId, !!(ui.consent && ui.consent.checked)));
        } catch (error) {
            showError(messageOf(error));
        } finally {
            ui.buttons.start.disabled = false;
        }
    });
    ui.buttons.replay?.addEventListener('click', () => { if (turn) askQuestion(turn); });
    ui.buttons.record?.addEventListener('click', startRecording);
    ui.buttons.retry?.addEventListener('click', startRecording);
    ui.buttons.stop?.addEventListener('click', stopRecording);
    ui.buttons.type?.addEventListener('click', startTyping);
    ui.buttons.submit?.addEventListener('click', submit);

    // ---- Connection ------------------------------------------------------

    connection.onreconnecting(() => showError('Mất kết nối, đang kết nối lại…'));
    connection.onreconnected(async () => {
        showError('');
        const lostAnswer = phase === 'recording' || phase === 'finishing';
        subject = null;
        try {
            render(await connection.invoke('GetState', candidateId));
        } catch (error) {
            showError(messageOf(error));
        }
        if (lostAnswer && state.status === 'InProgress') {
            showError('Kết nối bị gián đoạn trong lúc bạn nói. Vui lòng trả lời lại.');
            setPhase('typing', 'Bấm "Bắt đầu trả lời" để nói lại, hoặc gõ câu trả lời.');
        }
    });
    connection.onclose(() => showError('Mất kết nối tới máy chủ. Hãy tải lại trang để tiếp tục.'));

    render(state);
    connection.start()
        .then(() => connection.invoke('GetState', candidateId))
        .then(render)
        .catch((error) => showError(messageOf(error)));
})();
