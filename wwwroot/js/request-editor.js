(() => {
    'use strict';
    const select = document.querySelector('select[name="WorkflowDefinitionId"]');
    const help = document.getElementById('process-help');
    if (!select || !help) return;
    const update = () => {
        const option = select.selectedOptions[0];
        document.querySelectorAll('[data-request-fields]').forEach(group => {
            group.hidden = group.disabled = group.dataset.requestFields !== option?.value;
        });
        if (!option || option.value === '0') { help.textContent = 'Gönderim gereksinimlerini görmek için bir süreç seçin.'; return; }
        const fields = [];
        if (option?.dataset.amount === 'true') fields.push('tutar');
        if (option?.dataset.department === 'true') fields.push('departman');
        help.textContent = fields.length ? `Bu süreçte onaya gönderirken ${fields.join(' ve ')} zorunludur. Taslağı bu alanlar olmadan kaydedebilirsiniz.` : 'Bu süreçte tutar ve departman isteğe bağlıdır.';
    };
    select.addEventListener('change', update);
    update();
})();
