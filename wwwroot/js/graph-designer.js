(() => {
    'use strict';
    const byId = id => document.getElementById(id);
    const form = byId('graph-editor');
    if (!form) return;
    const readOnly = form.dataset.readonly === 'true';
    const serialized = byId('GraphJson');
    const message = byId('graph-message');
    let graph;
    try { graph = JSON.parse(serialized.value); }
    catch { message.textContent = 'Tasarım belgesi okunamadı.'; return; }
    if (![graph?.nodes, graph?.routes, graph?.fields].every(Array.isArray)) {
        message.textContent = 'Tasarım belgesinin yapısı geçersiz.';
        return;
    }

    const canvas = byId('graph-canvas');
    const viewport = byId('graph-viewport');
    const layer = byId('graph-nodes');
    const svg = canvas.querySelector('svg');
    const nodeLabels = { start: 'Başlangıç', task: 'Kullanıcı görevi', approval: 'Onay', decision: 'Karar', end: 'Bitiş' };
    const routeLabels = { next: 'İlerle', complete: 'Tamamla', approve: 'Onay', reject: 'Ret', match: 'Koşul sağlanırsa', otherwise: 'Varsayılan yol' };
    const operatorLabels = { equals: '=', notEquals: '≠', greaterThan: '>', greaterThanOrEqual: '≥', lessThan: '<', lessThanOrEqual: '≤' };
    const nodeRoutes = { start: ['next'], task: ['complete'], approval: ['approve', 'reject'], decision: ['match', 'otherwise'], end: [] };
    const past = [];
    const future = [];
    let selected;
    let editingRoute = null;
    let fieldDrag = null;
    let scale = 1;
    let pickFrom;
    let picking = false;

    function record() {
        past.push(JSON.stringify(graph));
        if (past.length > 60) past.shift();
        future.length = 0;
        message.textContent = '';
    }
    function change(action) { record(); action(); render(); }
    function restore(from, to) {
        if (!from.length) return;
        to.push(JSON.stringify(graph));
        graph = JSON.parse(from.pop());
        editingRoute = null;
        render();
    }
    const button = (label, action, className = 'btn btn-outline-secondary btn-sm') => {
        const el = document.createElement('button');
        el.type = 'button';
        el.className = className;
        el.textContent = label;
        el.addEventListener('click', action);
        return el;
    };
    const nodeById = id => graph.nodes.find(node => node.id === id);
    const position = value => Math.max(0, Math.min(6000, Math.round(Number(value) || 0)));
    const optionList = (select, entries, preferred = select.value) => {
        select.replaceChildren(...entries.map(([value, label]) => new Option(label, value)));
        if (entries.some(([value]) => value === preferred)) select.value = preferred;
    };

    function drawLines() {
        const width = Math.max(720, Math.ceil(viewport.clientWidth / scale), ...graph.nodes.map(node => node.x + 220));
        const height = Math.max(380, Math.ceil(viewport.clientHeight / scale), ...graph.nodes.map(node => node.y + 110));
        canvas.style.width = `${Math.ceil(width * scale)}px`;
        canvas.style.height = `${Math.ceil(height * scale)}px`;
        layer.style.transform = `scale(${scale})`;
        svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
        svg.replaceChildren();
        const element = name => document.createElementNS('http://www.w3.org/2000/svg', name);
        const defs = element('defs');
        const marker = element('marker');
        for (const [key, value] of Object.entries({ id: 'graph-arrow', markerWidth: 8, markerHeight: 8, refX: 7, refY: 4, orient: 'auto' }))
            marker.setAttribute(key, value);
        const arrow = element('path');
        arrow.setAttribute('d', 'M0 0 L8 4 L0 8 Z');
        marker.append(arrow);
        defs.append(marker);
        svg.append(defs);
        graph.routes.forEach(route => {
            const source = nodeById(route.from);
            const target = nodeById(route.to);
            if (!source || !target) return;
            const forward = target.x >= source.x + 170;
            const x1 = source.x + (forward ? 170 : 0);
            const x2 = target.x + (forward ? 0 : 170);
            const y1 = source.y + 30;
            const y2 = target.y + 30;
            const offset = Math.max(28, Math.abs(x2 - x1) / 2) * (forward ? 1 : -1);
            const line = element('path');
            line.setAttribute('d', `M ${x1} ${y1} C ${x1 + offset} ${y1}, ${x2 - offset} ${y2}, ${x2} ${y2}`);
            line.setAttribute('marker-end', 'url(#graph-arrow)');
            svg.append(line);
        });
    }

    function drawNodes() {
        layer.replaceChildren(...graph.nodes.map(node => {
            const item = button('', () => {
                selected = node.id;
                updateSelection();
                if (!picking) return;
                if (!pickFrom) {
                    pickFrom = node.id;
                    message.textContent = 'Hedef düğümü seçin.';
                } else if (pickFrom !== node.id) {
                    byId('route-from').value = pickFrom;
                    byId('route-to').value = node.id;
                    updateRouteKinds();
                    picking = false;
                    message.textContent = 'Geçiş türünü seçip bağlantıyı ekleyin.';
                    byId('route-kind').focus();
                }
            }, 'graph-node');
            item.dataset.kind = node.kind;
            item.dataset.id = node.id;
            item.style.left = `${node.x}px`;
            item.style.top = `${node.y}px`;
            const kind = document.createElement('small');
            kind.textContent = nodeLabels[node.kind];
            const title = document.createElement('strong');
            title.textContent = node.name;
            item.append(kind, title);
            if (!readOnly) {
                item.addEventListener('pointerdown', event => {
                    if (event.button !== 0) return;
                    const startX = event.clientX;
                    const startY = event.clientY;
                    const oldX = node.x;
                    const oldY = node.y;
                    let moved = false;
                    item.setPointerCapture(event.pointerId);
                    const move = current => {
                        const dx = (current.clientX - startX) / scale;
                        const dy = (current.clientY - startY) / scale;
                        if (!moved && Math.abs(dx) + Math.abs(dy) < 4) return;
                        if (!moved) { moved = true; record(); }
                        node.x = position(oldX + dx);
                        node.y = position(oldY + dy);
                        item.style.left = `${node.x}px`;
                        item.style.top = `${node.y}px`;
                        drawLines();
                    };
                    const finish = () => {
                        item.removeEventListener('pointermove', move);
                        selected = node.id;
                        updateSelection();
                        updateHistory();
                    };
                    item.addEventListener('pointermove', move);
                    item.addEventListener('lostpointercapture', finish, { once: true });
                });
                item.addEventListener('keydown', event => {
                    const delta = { ArrowLeft: [-16, 0], ArrowRight: [16, 0], ArrowUp: [0, -16], ArrowDown: [0, 16] }[event.key];
                    if (!delta) return;
                    event.preventDefault();
                    selected = node.id;
                    change(() => { node.x = position(node.x + delta[0]); node.y = position(node.y + delta[1]); });
                    layer.querySelector(`[data-id="${node.id}"]`)?.focus();
                });
            }
            return item;
        }));
    }

    const controls = {
        name: 'node-name', x: 'node-x', y: 'node-y', roleId: 'node-role', departmentId: 'node-department',
        userId: 'node-user', allowReturn: 'node-return', outcome: 'node-outcome'
    };
    function updateSelection() {
        const node = nodeById(selected);
        layer.querySelectorAll('.graph-node').forEach(item => item.setAttribute('aria-pressed', String(item.dataset.id === selected)));
        byId('graph-no-selection').hidden = !!node;
        byId('graph-node-properties').hidden = !node;
        if (!node) return;
        for (const [key, id] of Object.entries(controls)) {
            if (key === 'allowReturn') byId(id).checked = !!node[key];
            else byId(id).value = node[key] ?? (key === 'outcome' ? 'completed' : '');
        }
        byId('node-assignment').hidden = !['task', 'approval'].includes(node.kind);
        byId('node-end').hidden = node.kind !== 'end';
    }
    function updateRouteKinds(preferred) {
        const kinds = nodeRoutes[nodeById(byId('route-from').value)?.kind] ?? [];
        optionList(byId('route-kind'), kinds.map(kind => [kind, routeLabels[kind]]), preferred);
        byId('route-rule').hidden = byId('route-kind').value !== 'match';
    }
    function resetRoute() {
        editingRoute = null;
        byId('route-save').textContent = 'Bağlantı ekle';
        byId('route-cancel').hidden = true;
        byId('route-value').value = '';
    }
    function drawRoutes() {
        if (!readOnly) {
            const entries = graph.nodes.map(node => [node.id, node.name]);
            optionList(byId('route-from'), entries);
            optionList(byId('route-to'), entries);
            const variables = [['amount', 'Tutar'], ['department', 'Departman'], ...graph.fields.map(field => [field.key, field.label])];
            optionList(byId('route-variable'), variables);
            updateRouteKinds();
        }
        byId('graph-routes').replaceChildren(...graph.routes.map((route, index) => {
            const item = document.createElement('li');
            const rule = route.kind === 'match' ? ` · ${route.variable} ${operatorLabels[route.operator]} ${route.value}` : '';
            item.textContent = `${nodeById(route.from)?.name} → ${nodeById(route.to)?.name} · ${routeLabels[route.kind]}${rule}`;
            if (!readOnly) {
                item.append(' ', button('Düzenle', () => {
                    editingRoute = index;
                    byId('route-from').value = route.from;
                    byId('route-to').value = route.to;
                    updateRouteKinds(route.kind);
                    byId('route-variable').value = route.variable ?? 'amount';
                    byId('route-operator').value = route.operator ?? 'equals';
                    byId('route-value').value = route.value ?? '';
                    byId('route-save').textContent = 'Bağlantıyı güncelle';
                    byId('route-cancel').hidden = false;
                    byId('route-from').focus();
                }), ' ', button('Yukarı taşı', () => {
                    if (index === 0) return;
                    change(() => {
                        [graph.routes[index - 1], graph.routes[index]] = [graph.routes[index], graph.routes[index - 1]];
                        resetRoute();
                    });
                }), ' ', button('Sil', () => change(() => { graph.routes.splice(index, 1); resetRoute(); }), 'btn btn-outline-danger btn-sm'));
            }
            return item;
        }));
    }
    function fieldInput(label, value, update, type = 'text') {
        const container = document.createElement('label');
        container.className = 'form-label small';
        const input = document.createElement('input');
        input.type = type;
        input.className = type === 'checkbox' ? 'form-check-input ms-2' : 'form-control';
        input.disabled = readOnly;
        if (type === 'checkbox') input.checked = value;
        else input.value = value;
        input.addEventListener('change', () => update(type === 'checkbox' ? input.checked : input.value.trim()));
        container.append(label, input);
        return container;
    }
    function moveField(from, to) {
        if (from === to || from < 0 || to < 0 || to >= graph.fields.length) return;
        change(() => graph.fields.splice(to, 0, graph.fields.splice(from, 1)[0]));
    }
    function drawFields() {
        byId('graph-fields').replaceChildren(...graph.fields.map((field, index) => {
            const item = document.createElement('div');
            item.className = 'graph-field';
            item.draggable = !readOnly;
            const update = (key, value) => change(() => {
                if (key === 'key') graph.routes.filter(route => route.variable === field.key).forEach(route => { route.variable = value; });
                field[key] = value;
            });
            const heading = document.createElement('strong');
            heading.textContent = `${index + 1}. ${field.label}`;
            item.append(heading, fieldInput('Etiket', field.label, value => update('label', value)),
                fieldInput('Değişken anahtarı', field.key, value => update('key', value)),
                fieldInput('Zorunlu', field.required, value => update('required', value), 'checkbox'));
            if (field.kind === 'choice') item.append(fieldInput('Seçenekler (| ile ayırın)', (field.options ?? []).join(' | '),
                value => update('options', value.split('|').map(part => part.trim()).filter(Boolean))));
            if (!readOnly) {
                const actions = document.createElement('div');
                actions.className = 'd-flex flex-wrap gap-2';
                actions.append(button('Yukarı taşı', () => moveField(index, index - 1)),
                    button('Aşağı taşı', () => moveField(index, index + 1)),
                    button('Alanı sil', () => change(() => graph.fields.splice(index, 1)), 'btn btn-outline-danger btn-sm'));
                item.append(actions);
                item.addEventListener('dragstart', event => {
                    fieldDrag = index;
                    event.dataTransfer.setData('text/plain', field.key);
                    event.stopPropagation();
                });
                item.addEventListener('dragend', () => { fieldDrag = null; });
                item.addEventListener('dragover', event => { event.preventDefault(); });
                item.addEventListener('drop', event => {
                    event.preventDefault();
                    if (fieldDrag !== null) moveField(fieldDrag, index);
                    fieldDrag = null;
                });
            }
            return item;
        }));
    }

    function updateHistory() {
        if (readOnly) return;
        byId('graph-undo').disabled = past.length === 0;
        byId('graph-redo').disabled = future.length === 0;
    }
    function render() {
        drawNodes();
        drawLines();
        updateSelection();
        drawRoutes();
        drawFields();
        updateHistory();
    }
    byId('graph-zoom').addEventListener('input', event => { scale = Number(event.target.value) / 100; drawLines(); });
    window.addEventListener('resize', drawLines);
    if (!readOnly) {
        function addNode(kind, x = 28 + (graph.nodes.length % 3) * 205, y = 25 + Math.floor(graph.nodes.length / 3) * 100) {
            if (graph.nodes.length >= 60) { message.textContent = 'En fazla 60 düğüm eklenebilir.'; return; }
            change(() => {
                selected = crypto.randomUUID();
                graph.nodes.push({ id: selected, kind, name: nodeLabels[kind], x: position(x), y: position(y), outcome: 'completed' });
            });
        }
        form.querySelectorAll('[data-add-node]').forEach(item => {
            item.addEventListener('click', () => addNode(item.dataset.addNode));
            item.addEventListener('dragstart', event => event.dataTransfer.setData('application/x-workflow-node', item.dataset.addNode));
        });
        viewport.addEventListener('dragover', event => {
            if (event.dataTransfer.types.includes('application/x-workflow-node')) event.preventDefault();
        });
        viewport.addEventListener('drop', event => {
            event.preventDefault();
            const kind = event.dataTransfer.getData('application/x-workflow-node');
            if (!Object.hasOwn(nodeLabels, kind)) return;
            const box = canvas.getBoundingClientRect();
            addNode(kind, (event.clientX - box.left) / scale, (event.clientY - box.top) / scale);
        });
        for (const [key, id] of Object.entries(controls)) {
            byId(id).addEventListener('change', event => {
                const node = nodeById(selected);
                if (!node) return;
                const input = event.target;
                change(() => {
                    node[key] = key === 'allowReturn' ? input.checked : ['x', 'y'].includes(key) ? position(input.value) :
                        key === 'departmentId' ? Number(input.value) || null : input.value.trim() || null;
                });
            });
        }
        byId('node-remove').addEventListener('click', () => change(() => {
            graph.nodes = graph.nodes.filter(node => node.id !== selected);
            graph.routes = graph.routes.filter(route => route.from !== selected && route.to !== selected);
            selected = null;
            resetRoute();
        }));
        byId('route-from').addEventListener('change', () => updateRouteKinds());
        byId('route-pick').addEventListener('click', () => {
            resetRoute();
            picking = true;
            pickFrom = null;
            message.textContent = 'Önce kaynak, sonra hedef düğümü seçin.';
            layer.querySelector('.graph-node')?.focus();
        });
        byId('route-kind').addEventListener('change', () => updateRouteKinds());
        byId('route-cancel').addEventListener('click', resetRoute);
        byId('route-save').addEventListener('click', () => {
            const from = byId('route-from').value;
            const to = byId('route-to').value;
            const kind = byId('route-kind').value;
            if (!from || !to || !kind || from === to) { message.textContent = 'Farklı kaynak ve hedef düğümleri seçin.'; return; }
            if (graph.routes.length >= 180 && editingRoute === null) { message.textContent = 'En fazla 180 bağlantı eklenebilir.'; return; }
            const route = { from, to, kind };
            if (kind === 'match') Object.assign(route, { variable: byId('route-variable').value,
                operator: byId('route-operator').value, value: byId('route-value').value.trim() });
            change(() => {
                if (editingRoute === null) graph.routes.push(route);
                else graph.routes[editingRoute] = route;
                resetRoute();
            });
        });
        function addField() {
            if (graph.fields.length >= 40) { message.textContent = 'En fazla 40 alan eklenebilir.'; return; }
            change(() => {
                let number = graph.fields.length + 1;
                while (graph.fields.some(field => field.key === `alan_${number}`)) number++;
                const kind = byId('field-kind').value;
                graph.fields.push({ key: `alan_${number}`, label: `Alan ${number}`, kind, required: false,
                    options: kind === 'choice' ? ['Seçenek 1', 'Seçenek 2'] : null });
            });
        }
        const fieldList = byId('graph-fields');
        byId('field-add').addEventListener('click', addField);
        byId('field-add').addEventListener('dragstart', event => event.dataTransfer.setData('application/x-workflow-field', 'new'));
        fieldList.addEventListener('dragover', event => {
            if (event.dataTransfer.types.includes('application/x-workflow-field')) event.preventDefault();
        });
        fieldList.addEventListener('drop', event => {
            if (event.dataTransfer.getData('application/x-workflow-field') === 'new') { event.preventDefault(); addField(); }
        });
        byId('graph-undo').addEventListener('click', () => restore(past, future));
        byId('graph-redo').addEventListener('click', () => restore(future, past));
        form.addEventListener('submit', () => { serialized.value = JSON.stringify(graph); });
        byId('graph-save').disabled = false;
    }
    render();
})();
