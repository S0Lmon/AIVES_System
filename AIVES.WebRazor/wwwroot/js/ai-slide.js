(() => {
    const panel = document.getElementById('aiPanel');
    const backdrop = document.querySelector('[data-slide-close].slide-backdrop');
    const form = document.getElementById('aiPanelForm');
    const results = document.getElementById('aiResults');
    const submit = document.getElementById('aiSubmit');
    if (!panel || !backdrop || !form || !results || !submit) return;

    let returnFocus = null;
    const open = () => {
        returnFocus = document.activeElement;
        panel.hidden = false;
        backdrop.hidden = false;
        document.body.classList.add('slide-open');
        requestAnimationFrame(() => {
            panel.classList.add('open');
            backdrop.classList.add('show');
        });
        panel.querySelector('[data-slide-close]')?.focus();
    };
    const close = () => {
        panel.classList.remove('open');
        backdrop.classList.remove('show');
        document.body.classList.remove('slide-open');
        window.setTimeout(() => {
            panel.hidden = true;
            backdrop.hidden = true;
        }, 350);
        returnFocus?.focus();
    };

    document.querySelectorAll('[data-slide="aiPanel"]').forEach(trigger => trigger.addEventListener('click', open));
    document.querySelectorAll('[data-slide-close]').forEach(button => button.addEventListener('click', close));
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !panel.hidden) close();
    });
    if (new URLSearchParams(window.location.search).get('slide') === 'aiPanel') open();

    form.addEventListener('submit', async event => {
        event.preventDefault();
        const label = submit.querySelector('.ai-submit-label');
        const original = label?.textContent ?? '';
        submit.disabled = true;
        if (label) label.textContent = submit.dataset.aiBusy || '...';
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok) throw new Error(`Question generation returned ${response.status}.`);
            results.innerHTML = await response.text();
        } catch (error) {
            console.error(error);
            results.textContent = results.dataset.aiError || 'Generation failed.';
        } finally {
            submit.disabled = false;
            if (label) label.textContent = original;
        }
    });

    const subjectPicker = document.getElementById('aiSlideSubject');
    const topicPicker = document.getElementById('aiSlideTopic');
    const syncTopics = () => {
        if (!subjectPicker || !topicPicker) return;
        for (const option of topicPicker.options) {
            if (!option.dataset.subjectId) continue;
            option.hidden = !!subjectPicker.value && option.dataset.subjectId !== subjectPicker.value;
        }
        if (topicPicker.selectedOptions[0]?.hidden) topicPicker.value = '';
    };
    subjectPicker?.addEventListener('change', syncTopics);
    syncTopics();

    results.addEventListener('click', event => {
        const use = event.target.closest('.ai-use');
        const copy = event.target.closest('.ai-copy');
        if (!use && !copy) return;
        const card = (use || copy).closest('.ai-result');
        if (!card) return;

        if (use) {
            const set = (id, value) => {
                const field = document.getElementById(id);
                if (field) field.value = value || '';
                return field;
            };
            set('Input_Content', card.dataset.content);
            set('Input_ExpectedAnswer', card.dataset.answer);
            const bloom = document.getElementById('Input_BloomLevelId');
            if (bloom && card.dataset.bloom) {
                const wanted = card.dataset.bloom.toLowerCase();
                Array.from(bloom.options).some(option => {
                    if (option.text.toLowerCase().startsWith(wanted)) {
                        bloom.value = option.value;
                        return true;
                    }
                    return false;
                });
            }
            const sourceSubject = subjectPicker;
            const sourceTopic = topicPicker;
            const targetSubject = document.getElementById('Input_SubjectId');
            const targetTopic = document.getElementById('Input_TopicId');
            if (sourceSubject && targetSubject) {
                targetSubject.value = sourceSubject.value;
                targetSubject.dispatchEvent(new Event('change', { bubbles: true }));
            }
            if (sourceTopic && targetTopic) targetTopic.value = sourceTopic.value;
            const difficulty = document.getElementById('Input_Difficulty');
            const selectedDifficulty = document.getElementById('aiSlideDifficulty')?.value;
            if (difficulty && selectedDifficulty) difficulty.value = selectedDifficulty;
            use.textContent = results.dataset.done || use.textContent;
            use.classList.add('is-applied');
        } else if (navigator.clipboard) {
            navigator.clipboard.writeText(card.dataset.content || '').catch(error => console.error(error));
        }
    });
})();
