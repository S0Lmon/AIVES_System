(() => {
    const picker = document.getElementById('examQuestionPicker');
    if (!picker) return;

    const subject = document.getElementById('examSubject');
    const topic = document.getElementById('Input_TopicId');
    const rubric = document.getElementById('examRubric');
    const countInput = document.getElementById('Input_MainQuestionCount');
    const list = document.getElementById('examQuestionList');
    const status = document.getElementById('examQuestionStatus');
    const count = document.getElementById('examQuestionCount');
    const form = picker.closest('form');
    let selectedIds = new Set((picker.dataset.selectedIds ?? '')
        .split(',')
        .filter(Boolean));
    let request;

    const updateCount = () => {
        const selectedCount = list.querySelectorAll('input:checked').length;
        const required = Number.parseInt(countInput?.value ?? '1', 10) || 1;
        count.textContent = `${selectedCount} ${picker.dataset.selectedLabel}. ${picker.dataset.requiredLabel} ${required}.`;
    };

    const load = async () => {
        request?.abort();
        request = new AbortController();
        list.replaceChildren();
        count.textContent = '';
        if (!subject?.value) {
            status.textContent = picker.dataset.selectSubject;
            return;
        }

        status.textContent = '';
        const url = new URL(picker.dataset.url, window.location.href);
        url.searchParams.set('subjectId', subject.value);
        if (topic?.value) url.searchParams.set('topicId', topic.value);
        if (rubric?.value) url.searchParams.set('rubricId', rubric.value);

        try {
            const response = await fetch(url, {
                headers: { Accept: 'application/json' },
                signal: request.signal
            });
            if (!response.ok) {
                const message = await response.text();
                throw new Error(message || picker.dataset.loadError);
            }
            const questions = await response.json();
            const availableIds = new Set(questions.map(question => String(question.id)));
            selectedIds = new Set([...selectedIds].filter(id => availableIds.has(id)));

            if (questions.length === 0) {
                status.textContent = picker.dataset.noQuestions;
                updateCount();
                return;
            }

            for (const question of questions) {
                const id = String(question.id);
                const label = document.createElement('label');
                label.className = 'exam-question-option';
                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.name = picker.dataset.questionName;
                checkbox.value = id;
                checkbox.checked = selectedIds.has(id);
                checkbox.addEventListener('change', () => {
                    if (checkbox.checked) selectedIds.add(id);
                    else selectedIds.delete(id);
                    updateCount();
                });
                const content = document.createElement('span');
                content.textContent = question.content;
                const bloom = document.createElement('small');
                bloom.textContent = question.bloomLevelName;
                label.append(checkbox, content, bloom);
                list.append(label);
            }
            updateCount();
        } catch (error) {
            if (error.name !== 'AbortError') status.textContent = error.message || picker.dataset.loadError;
        }
    };

    subject?.addEventListener('change', load);
    topic?.addEventListener('change', load);
    rubric?.addEventListener('change', load);
    countInput?.addEventListener('input', updateCount);
    form?.addEventListener('submit', event => {
        const selectedCount = list.querySelectorAll('input:checked').length;
        const required = Number.parseInt(countInput?.value ?? '1', 10) || 1;
        if (selectedCount < required) {
            event.preventDefault();
            status.textContent = `${picker.dataset.requiredLabel} ${required} selected questions.`;
            list.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    });

    load();
})();
