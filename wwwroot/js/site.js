(() => {
    'use strict';
    const inbox = document.querySelector('[data-approval-url]:not([data-approval-url=""])');
    if (!inbox) return;

    const badges = document.querySelectorAll('.approval-count');
    const menuToggle = document.querySelector('.mobile-menu-toggle');
    let checking = false;

    async function refresh() {
        if (document.hidden || checking) return;
        checking = true;
        try {
            const response = await fetch(inbox.dataset.approvalUrl, {
                cache: 'no-store',
                headers: { Accept: 'application/json' }
            });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return;
            const { count } = await response.json();
            if (!Number.isSafeInteger(count) || count < 0) return;

            for (const badge of badges) {
                badge.textContent = count > 99 ? '99+' : String(count);
                badge.hidden = count === 0;
            }
            inbox.setAttribute('aria-label', `Bekleyen onaylar, ${count} talep`);
            menuToggle.setAttribute('aria-label', count > 0 ? `Menüyü aç/kapat, ${count} bekleyen onay` : 'Menüyü aç/kapat');
        } catch {
            // Bağlantı geçici olarak kesilirse sonraki kontrolde tekrar denenir.
        } finally {
            checking = false;
        }
    }

    refresh();
    window.setInterval(refresh, 30000);
    window.addEventListener('focus', refresh);
    document.addEventListener('visibilitychange', refresh);
})();
