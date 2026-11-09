(() => {
    'use strict';
    const form = document.getElementById('workflow-editor');
    if (!form) return;
    const steps = document.getElementById('workflow-steps');
    const notice = document.getElementById('editor-notice');
    const renumber = () => {
        [...steps.children].forEach((step, i) => {
            step.querySelector('.step-number').textContent = `${i + 1}. Onay adımı`;
            step.querySelector('[data-action="up"]').disabled = i === 0;
            step.querySelector('[data-action="down"]').disabled = i === steps.children.length - 1;
            step.querySelectorAll('[data-step-field]').forEach(input => { input.name = `Steps[${i}].${input.dataset.stepField}`; });
            step.querySelectorAll('.condition-row').forEach((row, j) => {
                row.querySelectorAll('[data-field]').forEach(input => { input.name = `Steps[${i}].Conditions[${j}].${input.dataset.field}`; });
                const field = row.querySelector('[data-field="Field"]');
                const operator = row.querySelector('[data-field="Operator"]');
                [...operator.options].forEach(option => { option.disabled = field.value === '1' && !['0', '1'].includes(option.value); });
            });
        });
    };
    document.getElementById('add-step').addEventListener('click', () => {
        if (steps.children.length >= 20) { notice.textContent = 'En fazla 20 adım ekleyebilirsiniz.'; return; }
        steps.append(document.getElementById('step-template').content.cloneNode(true));
        notice.textContent = '';
        renumber();
        steps.lastElementChild.querySelector('input').focus();
    });
    steps.addEventListener('click', event => {
        const button = event.target.closest('button[data-action]');
        if (!button) return;
        const step = button.closest('.workflow-step-card');
        notice.textContent = '';
        switch (button.dataset.action) {
            case 'remove-step':
                if (steps.children.length <= 1) { notice.textContent = 'En az bir onay adımı gereklidir.'; return; }
                step.remove(); break;
            case 'up': if (step.previousElementSibling) steps.insertBefore(step, step.previousElementSibling); break;
            case 'down': if (step.nextElementSibling) steps.insertBefore(step.nextElementSibling, step); break;
            case 'add-condition':
                if (step.querySelectorAll('.condition-row').length >= 10) { notice.textContent = 'Bir adımda en fazla 10 koşul olabilir.'; return; }
                step.querySelector('.conditions').append(document.getElementById('condition-template').content.cloneNode(true)); break;
            case 'remove-condition': button.closest('.condition-row').remove(); break;
        }
        renumber();
    });
    steps.addEventListener('change', event => {
        if (event.target.dataset.field === 'Field') {
            const row = event.target.closest('.condition-row');
            if (event.target.value === '1') row.querySelector('[data-field="Operator"]').value = '0';
            renumber();
        }
    });
    form.addEventListener('submit', renumber);
    renumber();
})();
