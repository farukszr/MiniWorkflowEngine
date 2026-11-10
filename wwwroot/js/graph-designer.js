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
    const tabs = [...form.querySelectorAll('[role="tab"]')];
    const nodeLabels = { start: 'Başlangıç', task: 'Kullanıcı görevi', approval: 'Onay', decision: 'Karar', end: 'Bitiş' };
    const routeLabels = { next: 'İlerle', complete: 'Tamamla', approve: 'Onay', reject: 'Ret', match: 'Koşul sağlanırsa', otherwise: 'Varsayılan yol' };
    const operatorLabels = { equals: '=', notEquals: '≠', greaterThan: '>', greaterThanOrEqual: '≥', lessThan: '<', lessThanOrEqual: '≤' };
    const ruleOperators = [...byId('route-operator').options].map(option => [option.value, option.text]);
    const nodeRoutes = { start: ['next'], task: ['complete'], approval: ['approve', 'reject'], decision: ['match', 'otherwise'], end: [] };
    const past = [];
    const future = [];
    let selected;
    let editingRoute = null;
    let pendingRule;
    let fieldDrag = null;
    let scale = 1;
    let connection;
    const nodeWidth = 170;
    const nodeHeight = node => nodeRoutes[node.kind].length ? 88 : 58;

    function selectTab(tab) {
        tabs.forEach(item => {
            const active = item === tab;
            item.setAttribute('aria-selected', String(active));
            item.tabIndex = active ? 0 : -1;
            byId(item.getAttribute('aria-controls')).hidden = !active;
        });
        if (!readOnly) {
            const testing = tab.id === 'designer-tab-test';
            byId(testing ? 'studio-stage-design' : 'studio-stage-preview').removeAttribute('aria-current');
            byId(testing ? 'studio-stage-preview' : 'studio-stage-design').setAttribute('aria-current', 'step');
        }
        if (tab.id === 'designer-tab-flow') drawLines();
    }
    tabs.forEach((tab, index) => {
        tab.addEventListener('click', () => selectTab(tab));
        tab.addEventListener('keydown', event => {
            const offset = { ArrowLeft: -1, ArrowRight: 1 }[event.key];
            const next = offset ? (index + offset + tabs.length) % tabs.length :
                event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : null;
            if (next === null) return;
            event.preventDefault();
            selectTab(tabs[next]);
            tabs[next].focus();
        });
    });

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
        resetConnection();
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
    const svgElement = name => document.createElementNS('http://www.w3.org/2000/svg', name);
    const position = value => Math.max(0, Math.min(6000, Math.round(Number(value) || 0)));
    const optionList = (select, entries, preferred = select.value) => {
        select.replaceChildren(...entries.map(([value, label]) => new Option(label, value)));
        if (entries.some(([value]) => value === preferred)) select.value = preferred;
    };
    function outputPoint(node, kind) {
        const kinds = nodeRoutes[node.kind];
        return { x: node.x + nodeWidth * (kinds.indexOf(kind) + .5) / kinds.length, y: node.y + nodeHeight(node) };
    }
    function variableField(key) {
        if (key === 'amount') return { label: 'Tutar', kind: 'amount' };
        if (key === 'department') return { label: 'Departman', kind: 'text' };
        return graph.fields.find(field => field.key === key);
    }

    function drawLines() {
        const width = Math.max(720, Math.ceil(viewport.clientWidth / scale), ...graph.nodes.map(node => node.x + 220));
        const height = Math.max(380, Math.ceil(viewport.clientHeight / scale), ...graph.nodes.map(node => node.y + 110));
        canvas.style.width = `${Math.ceil(width * scale)}px`;
        canvas.style.height = `${Math.ceil(height * scale)}px`;
        layer.style.transform = `scale(${scale})`;
        svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
        svg.replaceChildren();
        const defs = svgElement('defs');
        const marker = svgElement('marker');
        for (const [key, value] of Object.entries({ id: 'graph-arrow', markerWidth: 8, markerHeight: 8, refX: 7, refY: 4, orient: 'auto' }))
            marker.setAttribute(key, value);
        const arrow = svgElement('path');
        arrow.setAttribute('d', 'M0 0 L8 4 L0 8 Z');
        marker.append(arrow);
        defs.append(marker);
        svg.append(defs);
        graph.routes.forEach(route => {
            const source = nodeById(route.from);
            const target = nodeById(route.to);
            if (!source || !target || !nodeRoutes[source.kind].includes(route.kind)) return;
            const { x: x1, y: y1 } = outputPoint(source, route.kind);
            const right = target.x >= source.x + nodeWidth + 24;
            const left = target.x + nodeWidth <= source.x - 24;
            const x2 = right ? target.x : target.x + nodeWidth;
            const y2 = target.y + 28;
            const lane = right ? (source.x + nodeWidth + x2) / 2 :
                left ? (source.x + x2) / 2 : Math.max(source.x, target.x) + nodeWidth + 24;
            const exitY = y1 + 14 + nodeRoutes[source.kind].indexOf(route.kind) * 14;
            const offset = right || !left ? 1 : -1;
            const vertical = Math.sign(y2 - exitY);
            const line = svgElement('path');
            const startBend = Math.min(7, Math.abs(lane - x1) / 2);
            const endBend = Math.min(7, Math.abs(y2 - exitY) / 2);
            const towardTarget = Math.sign(x2 - lane);
            line.setAttribute('d', `M ${x1} ${y1} V ${exitY - startBend}
                Q ${x1} ${exitY} ${x1 + offset * startBend} ${exitY}
                H ${lane - offset * endBend} Q ${lane} ${exitY} ${lane} ${exitY + vertical * endBend}
                V ${y2 - vertical * endBend} Q ${lane} ${y2} ${lane + towardTarget * endBend} ${y2} H ${x2}`);
            line.dataset.route = route.kind;
            line.setAttribute('marker-end', 'url(#graph-arrow)');
            svg.append(line);
            if (route.kind === 'match' || route.kind === 'otherwise') {
                const label = svgElement('text');
                label.setAttribute('x', lane);
                label.setAttribute('y', (exitY + y2) / 2 - 6);
                const rule = `${variableField(route.variable)?.label ?? route.variable} ${operatorLabels[route.operator]} ${route.value}`;
                label.textContent = route.kind === 'match' ? rule.length > 27 ? `${rule.slice(0, 26)}…` : rule : 'Diğer durumlar';
                svg.append(label);
            }
        });
    }

    function refreshPorts() {
        layer.querySelectorAll('.graph-port').forEach(port => {
            port.setAttribute('aria-pressed', String(port.dataset.from === connection?.from && port.dataset.route === connection.kind));
        });
        layer.classList.toggle('is-connecting', !!connection);
        layer.querySelectorAll('.graph-node').forEach(item => {
            const target = nodeById(item.dataset.id);
            item.classList.toggle('is-connectable', !!connection && canTarget(connection.from, target, connection.kind));
        });
        if (!readOnly) {
            byId('graph-cancel-link').hidden = !connection;
            const picker = byId('graph-target-picker');
            picker.hidden = !connection;
            if (connection) {
                const targets = graph.nodes.filter(node => canTarget(connection.from, node, connection.kind));
                const target = byId('graph-target');
                optionList(target, [['', targets.length ? 'Hedef adımı seç' : 'Önce uygun bir adım ekle'],
                    ...targets.map(node => [node.id, node.name])]);
                target.disabled = !targets.length;
                byId('graph-target-connect').disabled = !target.value;
            }
        }
    }

    function canTarget(from, target, kind) {
        return target && target.id !== from && target.kind !== 'start' &&
            (kind !== 'reject' || target.kind === 'end' && target.outcome === 'rejected');
    }

    function resetConnection() {
        connection = null;
        pendingRule = null;
        editingRoute = null;
        byId('graph-connection-settings').hidden = true;
        refreshPorts();
    }

    function chooseOutput(node, kind, ruleIndex = null) {
        resetConnection();
        connection = { from: node.id, kind, ruleIndex };
        selected = node.id;
        updateSelection();
        refreshPorts();
        message.textContent = `“${node.name}” · ${routeLabels[kind]} çıkışı için tuvalden veya Adım ayarları listesinden bir hedef seç.`;
    }

    function openRule(route, index = null) {
        pendingRule = route;
        editingRoute = index;
        selected = route.from;
        updateSelection();
        byId('graph-connection-settings').hidden = false;
        byId('route-description').textContent = `${nodeById(route.from).name} → ${nodeById(route.to).name}`;
        byId('route-variable').value = route.variable ?? 'amount';
        updateRuleInputs(route.operator, route.value);
        byId('route-variable').focus();
    }

    function updateRuleInputs(preferredOperator, value = '') {
        const field = variableField(byId('route-variable').value);
        const type = field?.kind;
        const ordered = ['amount', 'number', 'date'].includes(type);
        const allowed = ordered ? ruleOperators : ruleOperators.slice(0, 2);
        optionList(byId('route-operator'), allowed, preferredOperator ?? (ordered ? 'greaterThan' : 'equals'));

        const choices = type === 'checkbox' ? [['true', 'Evet'], ['false', 'Hayır']] :
            type === 'choice' ? (field.options ?? []).map(option => [option, option]) : null;
        const entry = byId('route-value');
        byId('route-value-entry').hidden = choices !== null;
        byId('route-value-selection').hidden = choices === null;
        if (choices) optionList(byId('route-value-options'), [['', 'Değer seçin'], ...choices], value);
        entry.type = type === 'date' ? 'date' : 'text';
        entry.inputMode = ['amount', 'number'].includes(type) ? 'decimal' : 'text';
        entry.placeholder = type === 'amount' ? 'Örn. 10000' : '';
        entry.value = value;
        updateRulePreview();
    }

    function ruleValueControl() {
        return byId('route-value-selection').hidden ? byId('route-value') : byId('route-value-options');
    }

    function updateRulePreview() {
        const variable = byId('route-variable').selectedOptions[0]?.text ?? 'Alan';
        const operator = operatorLabels[byId('route-operator').value] ?? '=';
        const control = ruleValueControl();
        const value = control.value ? control.selectedOptions?.[0]?.text ?? control.value : '…';
        byId('route-preview').textContent = `${variable} ${operator} ${value} ise → ${nodeById(pendingRule?.to)?.name ?? 'hedef adım'}`;
    }

    function connect(from, to, kind, ruleIndex = null) {
        const target = nodeById(to);
        if (!target || from === to || target.kind === 'start') {
            message.textContent = 'Başlangıç dışındaki farklı bir düğümü hedef olarak seç.';
            return;
        }
        if (kind === 'reject' && (target.kind !== 'end' || target.outcome !== 'rejected')) {
            message.textContent = 'Ret çıkışını kütüphanedeki Reddedildi bitişine bağla.';
            return;
        }
        const index = kind === 'match' ? ruleIndex ?? -1 : graph.routes.findIndex(route => route.from === from && route.kind === kind);
        if (index < 0 && graph.routes.length >= 180) {
            message.textContent = 'En fazla 180 bağlantı eklenebilir.';
            return;
        }
        const seen = new Set();
        const reachesSource = id => {
            if (id === from) return true;
            if (seen.has(id)) return false;
            seen.add(id);
            return graph.routes.some((route, i) => i !== index && route.from === id && reachesSource(route.to));
        };
        if (reachesSource(to)) { message.textContent = 'Bu bağlantı döngü oluşturur.'; return; }
        connection = null;
        refreshPorts();
        const route = { from, to, kind };
        if (kind === 'match') { openRule({ ...graph.routes[ruleIndex], ...route }, ruleIndex); return; }
        change(() => {
            if (index < 0) graph.routes.push(route);
            else graph.routes[index] = route;
        });
        message.textContent = `${routeLabels[kind]} → ${target.name} bağlantısı kuruldu.`;
        layer.querySelector(`[data-id="${to}"]`)?.focus({ preventScroll: true });
    }

    function enablePortDrag(port, node, kind) {
        let moved = false;
        port.addEventListener('pointerdown', event => {
            if (event.button !== 0) return;
            moved = false;
            port.setPointerCapture(event.pointerId);
            const preview = svgElement('path');
            preview.setAttribute('class', 'graph-drag-line');
            const { x, y } = outputPoint(node, kind);
            let highlighted;
            const move = current => {
                if (!moved && Math.hypot(current.clientX - event.clientX, current.clientY - event.clientY) < 6) return;
                moved = true;
                const bounds = canvas.getBoundingClientRect();
                const toX = (current.clientX - bounds.left) / scale;
                const toY = (current.clientY - bounds.top) / scale;
                preview.setAttribute('d', `M ${x} ${y} C ${x + 42} ${y}, ${toX - 42} ${toY}, ${toX} ${toY}`);
                if (!preview.isConnected) svg.append(preview);
                const next = document.elementFromPoint(current.clientX, current.clientY)?.closest('.graph-node');
                if (next === highlighted) return;
                highlighted?.classList.remove('is-drop-target');
                highlighted = next;
                if (next && canTarget(node.id, nodeById(next.dataset.id), kind)) next.classList.add('is-drop-target');
            };
            const end = current => {
                port.removeEventListener('pointermove', move);
                port.removeEventListener('pointerup', end);
                port.removeEventListener('lostpointercapture', end);
                preview.remove();
                highlighted?.classList.remove('is-drop-target');
                if (!moved || current.type !== 'pointerup') return;
                const target = document.elementFromPoint(current.clientX, current.clientY)?.closest('.graph-node');
                if (!target || target.dataset.id === node.id || !layer.contains(target)) {
                    message.textContent = 'Bağlantıyı tamamlamak için farklı bir düğümün üzerine bırakın.';
                    return;
                }
                selected = target.dataset.id;
                updateSelection();
                connect(node.id, selected, kind);
            };
            port.addEventListener('pointermove', move);
            port.addEventListener('pointerup', end);
            port.addEventListener('lostpointercapture', end);
        });
        port.addEventListener('click', event => {
            if (!moved || event.detail === 0) return;
            event.stopImmediatePropagation();
        }, true);
    }

    function drawNodes() {
        layer.replaceChildren(...graph.nodes.map(node => {
            const frame = document.createElement('div');
            frame.className = 'graph-node-frame';
            frame.dataset.kind = node.kind;
            frame.style.left = `${node.x}px`;
            frame.style.top = `${node.y}px`;
            let dragged = false;
            const item = button('', event => {
                if (dragged && event.detail !== 0) return;
                selected = node.id;
                updateSelection();
                if (connection) connect(connection.from, node.id, connection.kind, connection.ruleIndex);
            }, 'graph-node');
            item.dataset.kind = node.kind;
            item.dataset.id = node.id;
            item.title = node.name;
            if (node.kind === 'end') item.dataset.outcome = node.outcome;
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
                    dragged = false;
                    item.setPointerCapture(event.pointerId);
                    const move = current => {
                        const dx = (current.clientX - startX) / scale;
                        const dy = (current.clientY - startY) / scale;
                        if (!dragged && Math.abs(dx) + Math.abs(dy) < 4) return;
                        if (!dragged) { dragged = true; record(); }
                        node.x = position(oldX + dx);
                        node.y = position(oldY + dy);
                        frame.style.left = `${node.x}px`;
                        frame.style.top = `${node.y}px`;
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
            frame.append(item);
            if (nodeRoutes[node.kind].length) {
                const outputs = document.createElement('div');
                outputs.className = 'graph-outputs';
                nodeRoutes[node.kind].forEach(route => {
                    const label = route === 'match' ? 'Koşul' : route === 'otherwise' ? 'Varsayılan' : routeLabels[route];
                    const linked = graph.routes.some(edge => edge.from === node.id && edge.kind === route);
                    const port = button(label, () => chooseOutput(node, route), 'graph-port');
                    port.disabled = readOnly;
                    port.dataset.from = node.id;
                    port.dataset.route = route;
                    port.dataset.linked = String(linked);
                    port.setAttribute('aria-label', `${node.name} · ${routeLabels[route]}${linked ? ' · Bağlı' : ''}`);
                    if (!readOnly) enablePortDrag(port, node, route);
                    outputs.append(port);
                });
                frame.append(outputs);
            }
            return frame;
        }));
        refreshPorts();
    }

    const controls = {
        name: 'node-name', x: 'node-x', y: 'node-y', roleId: 'node-role', departmentId: 'node-department',
        userId: 'node-user', allowReturn: 'node-return', outcome: 'node-outcome'
    };
    function appendRoute(node) {
        if (!node || !['start', 'task'].includes(node.kind) || graph.routes.some(route => route.from === node.id)) return null;
        return node.kind === 'start' ? 'next' : 'complete';
    }
    function updateSelection() {
        const node = nodeById(selected);
        layer.querySelectorAll('.graph-node').forEach(item => item.setAttribute('aria-pressed', String(item.dataset.id === selected)));
        byId('graph-no-selection').hidden = !!node;
        byId('graph-node-properties').hidden = !node;
        const decision = node?.kind === 'decision';
        byId('decision-guide').hidden = !decision;
        if (decision) {
            const routes = graph.routes.filter(route => route.from === node.id);
            const matches = routes.filter(route => route.kind === 'match');
            const fallback = routes.find(route => route.kind === 'otherwise');
            byId('decision-match-status').textContent = matches.length === 1 ? `→ ${nodeById(matches[0].to)?.name}` :
                matches.length > 1 ? `${matches.length} koşul bağlı` : 'Koşul yolu henüz bağlı değil.';
            byId('decision-default-status').textContent = fallback ? `→ ${nodeById(fallback.to)?.name}` : 'Varsayılan yol henüz bağlı değil.';
            if (!readOnly) {
                byId('decision-edit-rule').hidden = matches.length !== 1;
                byId('decision-match-target').textContent = matches.length === 1 ? 'Koşul hedefini değiştir' :
                    matches.length > 1 ? 'Yeni koşul yolu ekle' : 'Koşul hedefini seç';
            }
        }
        if (!readOnly) byId('graph-append').hidden = appendRoute(node) === null;
        if (!node) return;
        for (const [key, id] of Object.entries(controls)) {
            if (key === 'allowReturn') byId(id).checked = !!node[key];
            else byId(id).value = node[key] ?? (key === 'outcome' ? 'completed' : '');
        }
        byId('node-assignment').hidden = !['task', 'approval'].includes(node.kind);
        byId('node-end').hidden = node.kind !== 'end';
    }
    function drawRoutes() {
        byId('graph-connection-count').textContent = graph.routes.length;
        byId('graph-summary').textContent = `${graph.nodes.length} düğüm · ${graph.routes.length} bağlantı`;
        if (!readOnly) {
            const variables = [['amount', 'Tutar'], ['department', 'Departman'], ...graph.fields.map(field => [field.key, field.label])];
            optionList(byId('route-variable'), variables);
        }
        byId('graph-routes').replaceChildren(...graph.routes.map((route, index) => {
            const item = document.createElement('li');
            const rule = route.kind === 'match' ? ` · ${route.variable} ${operatorLabels[route.operator]} ${route.value}` : '';
            item.textContent = `${nodeById(route.from)?.name} → ${nodeById(route.to)?.name} · ${routeLabels[route.kind]}${rule}`;
            if (!readOnly) {
                const actions = document.createElement('div');
                actions.className = 'd-flex flex-wrap gap-1 mt-1';
                if (route.kind === 'match') {
                    actions.append(button('Koşulu düzenle', () => openRule(route, index)));
                    const previous = graph.routes.findLastIndex((edge, i) => i < index && edge.from === route.from && edge.kind === 'match');
                    if (previous >= 0) actions.append(button('Önceliği artır', () => {
                        resetConnection();
                        change(() => { [graph.routes[previous], graph.routes[index]] = [graph.routes[index], graph.routes[previous]]; });
                    }));
                }
                actions.append(button('Sil', () => {
                    resetConnection();
                    change(() => graph.routes.splice(index, 1));
                }, 'btn btn-outline-danger btn-sm'));
                item.append(actions);
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
        if (!readOnly) byId('graph-starter').hidden = graph.nodes.length !== 0;
        drawNodes();
        drawLines();
        updateSelection();
        drawRoutes();
        drawFields();
        updateHistory();
    }
    const zoom = byId('graph-zoom');
    function applyZoom() {
        scale = Number(zoom.value) / 100;
        byId('graph-zoom-value').textContent = `%${zoom.value}`;
        drawLines();
    }
    zoom.addEventListener('input', applyZoom);
    byId('graph-fit').addEventListener('click', () => {
        if (!graph.nodes.length) { zoom.value = 100; applyZoom(); return; }
        const left = Math.min(...graph.nodes.map(node => node.x));
        const top = Math.min(...graph.nodes.map(node => node.y));
        const width = Math.max(...graph.nodes.map(node => node.x + nodeWidth)) - left;
        const height = Math.max(...graph.nodes.map(node => node.y + nodeHeight(node))) - top;
        zoom.value = Math.max(5, Math.min(100, Math.floor(100 * Math.min(
            (viewport.clientWidth - 32) / width, (viewport.clientHeight - 32) / height))));
        applyZoom();
        viewport.scrollTo(Math.max(0, left * scale - 16), Math.max(0, top * scale - 16));
    });
    viewport.addEventListener('pointerdown', event => {
        if (event.button !== 0 || !canvas.contains(event.target) || event.target.closest('button')) return;
        const left = viewport.scrollLeft;
        const top = viewport.scrollTop;
        viewport.setPointerCapture(event.pointerId);
        viewport.classList.add('is-panning');
        const move = current => viewport.scrollTo(left + event.clientX - current.clientX, top + event.clientY - current.clientY);
        viewport.addEventListener('pointermove', move);
        viewport.addEventListener('lostpointercapture', () => {
            viewport.removeEventListener('pointermove', move);
            viewport.classList.remove('is-panning');
        }, { once: true });
    });
    window.addEventListener('resize', drawLines);
    if (!readOnly) {
        byId('graph-open-test').addEventListener('click', () => {
            byId('designer-tab-test').click();
            byId('designer-tab-test').focus();
        });
        byId('graph-start-expense').addEventListener('click', event => {
            if (graph.nodes.length) return;
            const node = (kind, name, x, y, properties = {}) => ({ id: crypto.randomUUID(), kind, name, x, y, ...properties });
            const start = node('start', 'Talep gönderildi', 24, 200);
            const decision = node('decision', 'Tutar 10.000 TL üzerinde mi?', 240, 200);
            const approval = node('approval', 'Masrafı incele', 465, 320, { roleId: event.currentTarget.dataset.roleId || null });
            const done = node('end', 'Tamamlandı', 700, 95, { outcome: 'completed' });
            const rejected = node('end', 'Reddedildi', 700, 390, { outcome: 'rejected' });
            change(() => {
                graph.nodes.push(start, decision, approval, done, rejected);
                graph.routes.push({ from: start.id, to: decision.id, kind: 'next' },
                    { from: decision.id, to: approval.id, kind: 'match', variable: 'amount', operator: 'greaterThan', value: '10000' },
                    { from: decision.id, to: done.id, kind: 'otherwise' },
                    { from: approval.id, to: done.id, kind: 'approve' }, { from: approval.id, to: rejected.id, kind: 'reject' });
                selected = decision.id;
            });
            if (!byId('Name').value.trim()) byId('Name').value = 'Koşullu masraf onayı';
            byId('graph-fit').click();
            message.textContent = '10.000 TL üzeri onaya gider. Diğer tutarlarda talep doğrudan tamamlanır.';
        });
        byId('graph-start-approval').addEventListener('click', () => {
            if (graph.nodes.length) return;
            change(() => {
                const start = { id: crypto.randomUUID(), kind: 'start', name: 'Başlangıç', x: 24, y: 120 };
                const approval = { id: crypto.randomUUID(), kind: 'approval', name: 'Onay', x: 240, y: 120 };
                const done = { id: crypto.randomUUID(), kind: 'end', name: 'Tamamlandı', x: 456, y: 62, outcome: 'completed' };
                const rejected = { id: crypto.randomUUID(), kind: 'end', name: 'Reddedildi', x: 456, y: 176, outcome: 'rejected' };
                graph.nodes.push(start, approval, done, rejected);
                graph.routes.push({ from: start.id, to: approval.id, kind: 'next' },
                    { from: approval.id, to: done.id, kind: 'approve' }, { from: approval.id, to: rejected.id, kind: 'reject' });
                selected = approval.id;
            });
            byId('graph-fit').click();
            message.textContent = 'Onay düğümü hazır. Düğüm özelliklerinden sorumlu rolü seçin.';
            byId('node-role').focus({ preventScroll: true });
        });
        function addNode(kind, outcome, x = 28 + (graph.nodes.length % 3) * 225, y = 25 + Math.floor(graph.nodes.length / 3) * 130) {
            if (graph.nodes.length >= 60) { message.textContent = 'En fazla 60 düğüm eklenebilir.'; return; }
            resetConnection();
            change(() => {
                selected = crypto.randomUUID();
                const name = kind === 'end' ? outcome === 'rejected' ? 'Reddedildi' : 'Tamamlandı' : nodeLabels[kind];
                graph.nodes.push({ id: selected, kind, name, x: position(x), y: position(y), outcome });
            });
        }
        byId('graph-start-blank').addEventListener('click', () => {
            if (!graph.nodes.length) addNode('start');
        });
        byId('graph-append-add').addEventListener('click', () => {
            const source = nodeById(selected);
            const route = appendRoute(source);
            if (!route) return;
            if (graph.nodes.length >= 60 || graph.routes.length >= 180) {
                message.textContent = 'Düğüm veya bağlantı sınırına ulaşıldı.';
                return;
            }
            const kind = byId('graph-append-kind').value;
            resetConnection();
            change(() => {
                selected = crypto.randomUUID();
                const x = source.x <= 5780 ? source.x + 220 : Math.max(0, source.x - 220);
                let y = source.y;
                while (y < 5900 && graph.nodes.some(node => Math.abs(node.x - x) < 175 && Math.abs(node.y - y) < 95)) y += 110;
                graph.nodes.push({ id: selected, kind, name: kind === 'end' ? 'Tamamlandı' : nodeLabels[kind], x, y, outcome: 'completed' });
                graph.routes.push({ from: source.id, to: selected, kind: route });
            });
            layer.querySelector(`[data-id="${selected}"]`)?.focus();
        });
        form.querySelectorAll('[data-add-node]').forEach(item => {
            const { addNode: kind, outcome = 'completed' } = item.dataset;
            item.addEventListener('click', () => addNode(kind, outcome));
            item.addEventListener('dragstart', event => event.dataTransfer.setData('application/x-workflow-node', `${kind}|${outcome}`));
        });
        viewport.addEventListener('dragover', event => {
            if (event.dataTransfer.types.includes('application/x-workflow-node')) event.preventDefault();
        });
        viewport.addEventListener('drop', event => {
            event.preventDefault();
            const [kind, outcome] = event.dataTransfer.getData('application/x-workflow-node').split('|');
            if (!Object.hasOwn(nodeLabels, kind)) return;
            const box = canvas.getBoundingClientRect();
            addNode(kind, outcome === 'rejected' ? 'rejected' : 'completed', (event.clientX - box.left) / scale, (event.clientY - box.top) / scale);
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
            resetConnection();
        }));
        function cancelConnection() {
            resetConnection();
            message.textContent = 'Bağlantı işlemi iptal edildi.';
        }
        form.addEventListener('keydown', event => {
            if (event.key === 'Escape' && (connection || pendingRule)) cancelConnection();
        });
        form.querySelectorAll('[data-decision-output]').forEach(item => item.addEventListener('click', () => {
            const node = nodeById(selected);
            if (node?.kind !== 'decision') return;
            const kind = item.dataset.decisionOutput;
            const rules = graph.routes.filter(route => route.from === node.id && route.kind === 'match');
            chooseOutput(node, kind, kind === 'match' && rules.length === 1 ? graph.routes.indexOf(rules[0]) : null);
        }));
        const targetPicker = byId('graph-target');
        targetPicker.addEventListener('change', () => { byId('graph-target-connect').disabled = !targetPicker.value; });
        byId('graph-target-connect').addEventListener('click', () => {
            if (connection && targetPicker.value) connect(connection.from, targetPicker.value, connection.kind, connection.ruleIndex);
        });
        byId('decision-edit-rule').addEventListener('click', () => {
            const index = graph.routes.findIndex(route => route.from === selected && route.kind === 'match');
            if (index < 0) return;
            resetConnection();
            openRule(graph.routes[index], index);
        });
        byId('graph-cancel-link').addEventListener('click', cancelConnection);
        byId('route-cancel').addEventListener('click', cancelConnection);
        byId('route-variable').addEventListener('change', () => updateRuleInputs());
        byId('route-operator').addEventListener('change', updateRulePreview);
        byId('route-value-options').addEventListener('change', updateRulePreview);
        byId('route-value').addEventListener('input', updateRulePreview);
        byId('route-save').addEventListener('click', () => {
            if (!pendingRule) return;
            const control = ruleValueControl();
            const value = control.value.trim();
            if (!value) { control.focus(); message.textContent = 'Koşul için karşılaştırma değeri gir.'; return; }
            const route = { ...pendingRule, variable: byId('route-variable').value, operator: byId('route-operator').value, value };
            const index = editingRoute;
            if (graph.routes.some((edge, i) => i !== index && edge.from === route.from && edge.to === route.to &&
                edge.kind === 'match' && edge.variable === route.variable && edge.operator === route.operator && edge.value === value)) {
                message.textContent = 'Aynı koşul bağlantısı zaten var.';
                return;
            }
            resetConnection();
            change(() => {
                if (index === null) graph.routes.push(route);
                else graph.routes[index] = route;
            });
            message.textContent = 'Koşul bağlantısı kaydedildi.';
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
        form.addEventListener('submit', event => {
            if (pendingRule) {
                event.preventDefault();
                byId('designer-tab-flow').click();
                ruleValueControl().focus();
                message.textContent = 'Devam etmek için koşulu kaydet veya koşul düzenlemesinden vazgeç.';
                return;
            }
            serialized.value = JSON.stringify(graph);
        });
        byId('graph-save').disabled = false;
        byId('graph-test-save').disabled = false;
    }
    render();
    if (!readOnly && byId('Id').value === '0' && new URLSearchParams(location.search).get('ornek') === 'masraf')
        byId('graph-start-expense').click();
})();
