// ============================================================
// СОСТОЯНИЕ РЕДАКТОРА
// ============================================================
var canvas = document.getElementById('dashboard-canvas');

var modalBlockId = null;
var modalWidgetBlockId = null;
var modalWidgetId = null;
var blockDrag = null;
var blockResize = null;
var widgetDragData = null;

// ============================================================
// ГЕНЕРАТОРЫ ID
// ============================================================
function genBlockId() { return 'block-' + (state.nextBlockId++); }
function genWidgetId() { return 'widget-' + (state.nextWidgetId++); }

// ============================================================
// МОДАЛКИ
// ============================================================
function openModal(id) { document.getElementById(id).classList.add('active'); }
function closeModal(id) {
    document.getElementById(id).classList.remove('active');
    modalBlockId = null;
    modalWidgetBlockId = null;
    modalWidgetId = null;
}

document.querySelectorAll('.modal-overlay').forEach(function(el) {
    el.addEventListener('mousedown', function(e) { if (e.target === el) closeModal(el.id); });
});
document.addEventListener('keydown', function(e) {
    if (e.key === 'Escape') {
        document.querySelectorAll('.modal-overlay.active').forEach(function(m) { closeModal(m.id); });
    }
});

// ============================================================
// БЛОКИ
// ============================================================
function addBlock(title, x, y, w, h) {
    var block = {
        id: genBlockId(),
        title: title || 'Новый блок',
        x: snap(x != null ? x : 40 + Math.random() * 200),
        y: snap(y != null ? y : 40 + Math.random() * 200),
        w: snap(w || 320),
        h: snap(h || 200),
        widgets: [],
        z: state.blocks.length + 1,
    };
    state.blocks.push(block);
    renderBlock(block);
    return block;
}

function removeBlock(blockId) {
    state.blocks = state.blocks.filter(function(b) { return b.id !== blockId; });
    var el = document.getElementById(blockId);
    if (el) el.remove();
}

function renderBlock(block) {
    var old = document.getElementById(block.id);
    if (old) old.remove();

    var el = document.createElement('div');
    el.className = 'dash-block';
    el.id = block.id;
    el.style.left = block.x + 'px';
    el.style.top = block.y + 'px';
    el.style.width = block.w + 'px';
    el.style.height = block.h + 'px';
    el.style.zIndex = block.z;
    
    el.innerHTML =
        '<div class="block-header" data-drag-handle>' +
            '<span class="block-title">' + escHtml(block.title) + '</span>' +
            '<div class="block-actions">' +
                '<button class="btn-edit" title="Настройки" data-action="edit"><i class="fas fa-cog"></i></button>' +
                '<button class="btn-del" title="Удалить" data-action="delete"><i class="fas fa-times"></i></button>' +
            '</div>' +
        '</div>' +
        '<div class="widgets-container" data-widgets-drop></div>' +
        '<div class="resize-handle" data-resize-handle></div>';
    // Применение пользовательских цветов
    if (block.bodyColor) {
        el.style.backgroundColor = block.bodyColor;
    }
    var headerEl = el.querySelector('.block-header');
    if (block.headerColor) {
        headerEl.style.backgroundColor = block.headerColor;
        // Автоматический контраст текста в зависимости от цвета заголовка
        var isLightBg = getContrastYIQ(block.headerColor) === 'dark';
        var textColor = isLightBg ? 'var(--bg)' : 'var(--fg-dim)';
        var btnColor = isLightBg ? 'var(--fg)' : 'var(--fg-dim)';
        headerEl.querySelector('.block-title').style.color = textColor;
        headerEl.querySelectorAll('.block-actions button').forEach(function(b) {
            b.style.color = btnColor;
        });
    }
    var wc = el.querySelector('[data-widgets-drop]');
    block.widgets.forEach(function(w) { wc.appendChild(renderWidget(block, w)); });

    var addBtn = document.createElement('button');
    addBtn.className = 'add-widget-btn';
    addBtn.innerHTML = '<i class="fas fa-plus"></i>';
    addBtn.addEventListener('click', function(e) {
        e.stopPropagation();
        openWidgetModal(block.id, null);
    });
    wc.appendChild(addBtn);

    // Двойной клик по заголовку
    el.querySelector('[data-drag-handle]').addEventListener('dblclick', function(e) {
        if (e.target.closest('[data-action]')) return;
        openBlockModal(block.id);
    });
    el.querySelector('[data-action="edit"]').addEventListener('click', function(e) {
        e.stopPropagation();
        openBlockModal(block.id);
    });
    el.querySelector('[data-action="delete"]').addEventListener('click', function(e) {
        e.stopPropagation();
        removeBlock(block.id);
        toast('Блок удалён', 'info');
    });

    // Drag блока
    el.querySelector('[data-drag-handle]').addEventListener('mousedown', function(e) {
        if (e.target.closest('[data-action]')) return;
        if (e.button !== 0) return;
        block.z = Math.max.apply(null, state.blocks.map(function(b) { return b.z; })) + 1;
        el.style.zIndex = block.z;
        blockDrag = { blockId: block.id, startX: e.clientX, startY: e.clientY, origX: block.x, origY: block.y };
        el.classList.add('dragging-block');
        e.preventDefault();
    });

    // Resize блока
    el.querySelector('[data-resize-handle]').addEventListener('mousedown', function(e) {
        if (e.button !== 0) return;
        blockResize = { blockId: block.id, startX: e.clientX, startY: e.clientY, origW: block.w, origH: block.h };
        e.preventDefault();
        e.stopPropagation();
    });

    // Drop виджетов
    wc.addEventListener('dragover', function(e) {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';
        wc.querySelectorAll('.insert-before,.insert-after').forEach(function(w) {
            w.classList.remove('insert-before', 'insert-after');
        });
        var idx = getWidgetInsertIndex(wc, e.clientX);
        var widgets = Array.from(wc.querySelectorAll('.widget'));
        if (idx < widgets.length) widgets[idx].classList.add('insert-before');
        else if (widgets.length > 0) widgets[widgets.length - 1].classList.add('insert-after');
    });
    wc.addEventListener('dragleave', function(e) {
        if (!wc.contains(e.relatedTarget)) {
            wc.querySelectorAll('.insert-before,.insert-after').forEach(function(w) {
                w.classList.remove('insert-before', 'insert-after');
            });
        }
    });
    wc.addEventListener('drop', function(e) {
        e.preventDefault();
        wc.querySelectorAll('.insert-before,.insert-after').forEach(function(w) {
            w.classList.remove('insert-before', 'insert-after');
        });
        if (!widgetDragData) return;
        var srcBlock = state.blocks.find(function(b) { return b.id === widgetDragData.srcBlockId; });
        if (!srcBlock) return;
        var wIdx = srcBlock.widgets.findIndex(function(w) { return w.id === widgetDragData.widgetId; });
        if (wIdx === -1) return;
        var widget = srcBlock.widgets.splice(wIdx, 1)[0];
        var dstBlock = state.blocks.find(function(b) { return b.id === block.id; });
        if (!dstBlock) return;
        var insertIdx = getWidgetInsertIndex(wc, e.clientX);
        if (widgetDragData.srcBlockId === block.id && wIdx < insertIdx) insertIdx--;
        insertIdx = Math.max(0, Math.min(insertIdx, dstBlock.widgets.length));
        dstBlock.widgets.splice(insertIdx, 0, widget);
        if (widgetDragData.srcBlockId !== block.id) renderBlock(srcBlock);
        renderBlock(block);
        widgetDragData = null;
    });

    canvas.appendChild(el);
}

// ============================================================
// МОДАЛКА БЛОКА
// ============================================================
function openBlockModal(blockId) {
    var block = state.blocks.find(function(b) { return b.id === blockId; });
    if (!block) return;
    modalBlockId = blockId;
    document.getElementById('bm-title').value = block.title;
    var hInput = document.getElementById('bm-header-color');
    var bInput = document.getElementById('bm-body-color');
    hInput.value = block.headerColor || '#0e1512';
    bInput.value = block.bodyColor || '#141e19';
    hInput.dataset.active = block.headerColor ? 'true' : 'false';
    bInput.dataset.active = block.bodyColor ? 'true' : 'false';
    hInput.style.opacity = block.headerColor ? '1' : '0.5';
    bInput.style.opacity = block.bodyColor ? '1' : '0.5';
    openModal('block-modal');
    setTimeout(function() { document.getElementById('bm-title').focus(); }, 100);
}

function resetBlockColor(type) {
    var input = document.getElementById('bm-' + type + '-color');
    input.dataset.active = 'false';
    input.style.opacity = '0.5';
}

function saveBlockModal() {
    var block = state.blocks.find(function(b) { return b.id === modalBlockId; });
    if (!block) return;
    block.title = document.getElementById('bm-title').value.trim() || 'Без названия';
    var hInput = document.getElementById('bm-header-color');
    var bInput = document.getElementById('bm-body-color');
    block.headerColor = hInput.dataset.active === 'true' ? hInput.value : null;
    block.bodyColor = bInput.dataset.active === 'true' ? bInput.value : null;
    renderBlock(block);
    closeModal('block-modal');
}

function deleteBlockFromModal() {
    removeBlock(modalBlockId);
    toast('Блок удалён', 'info');
    closeModal('block-modal');
}

function copyBlockFromModal() {
    var block = state.blocks.find(function(b) { return b.id === modalBlockId; });
    if (!block) return;
    
    // Глубокое копирование конфига блока
    var copy = JSON.parse(JSON.stringify(block));
    
    // Генерируем новые ID
    copy.id = genBlockId();
    copy.title = block.title + ' (копия)';
    copy.x = snap(block.x + 30);
    copy.y = snap(block.y + 30);
    copy.z = Math.max.apply(null, state.blocks.map(function(b) { return b.z; })) + 1;
    
    // Генерируем новые ID для всех виджетов внутри копии
    copy.widgets.forEach(function(w) {
        w.id = genWidgetId();
    });
    
    state.blocks.push(copy);
    renderBlock(copy);
    toast('Блок скопирован', 'info');
    closeModal('block-modal');
}
// ============================================================
// ВИДЖЕТЫ
// ============================================================
function createWidgetConfig(type, addr, label, units, addr2, units2, sep, precision) {
    return {
        id: genWidgetId(), type: type || 'value', addr: addr || '',
        label: label || '', units: units || '',
        addr2: addr2 || '', units2: units2 || '', sep: sep || '/',
        precision: precision != null ? precision : 0,
    };
}

function renderWidget(block, widget) {
    var el = document.createElement('div');
    el.className = 'widget';
    el.id = widget.id;
    el.draggable = true;
    el.title = (widget.label || '') + '\n' + (widget.addr || '') + (widget.type === 'dual' ? ' | ' + (widget.addr2 || '') : '');
    updateWidgetContent(el, widget);

    el.addEventListener('click', function(e) {
        e.stopPropagation();
        openWidgetModal(block.id, widget.id);
    });
    el.addEventListener('dragstart', function(e) {
        widgetDragData = { srcBlockId: block.id, widgetId: widget.id };
        el.classList.add('widget-dragging');
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', widget.id);
    });
    el.addEventListener('dragend', function() {
        el.classList.remove('widget-dragging');
        document.querySelectorAll('.insert-before,.insert-after').forEach(function(w) {
            w.classList.remove('insert-before', 'insert-after');
        });
        setTimeout(function() { widgetDragData = null; }, 50);
    });
    return el;
}

function getWidgetInsertIndex(container, clientX) {
    var widgets = Array.from(container.querySelectorAll('.widget:not(.widget-dragging)'));
    if (widgets.length === 0) return 0;
    for (var i = 0; i < widgets.length; i++) {
        var rect = widgets[i].getBoundingClientRect();
        if (clientX < rect.left + rect.width / 2) return i;
    }
    return widgets.length;
}

// ============================================================
// МОДАЛКА ВИДЖЕТА
// ============================================================
function openWidgetModal(blockId, widgetId) {
    var block = state.blocks.find(function(b) { return b.id === blockId; });
    if (!block) return;
    modalWidgetBlockId = blockId;
    var widget = widgetId ? block.widgets.find(function(w) { return w.id === widgetId; }) : null;
    modalWidgetId = widget ? widget.id : null;

    document.getElementById('wm-type').value = widget ? widget.type : 'value';
    document.getElementById('wm-label').value = widget ? widget.label : '';
    document.getElementById('wm-addr').value = widget ? widget.addr : '';
    document.getElementById('wm-addr2').value = widget ? widget.addr2 : '';
    document.getElementById('wm-units').value = widget ? widget.units : '';
    document.getElementById('wm-precision').value = widget ? (widget.precision != null ? widget.precision : 0) : 0;
    document.getElementById('wm-units2').value = widget ? widget.units2 : '';
    document.getElementById('wm-sep').value = widget ? widget.sep : '/';
    onWidgetTypeChange();
    openModal('widget-modal');
    setTimeout(function() { document.getElementById('wm-label').focus(); }, 100);
}

function onWidgetTypeChange() {
    var type = document.getElementById('wm-type').value;
    var isDual = type === 'dual';
    var isNumeric = (type === 'value' || type === 'dual');
    document.getElementById('wm-addr2-field').style.display = isDual ? '' : 'none';
    document.getElementById('wm-units2-field').style.display = isDual ? '' : 'none';
    document.getElementById('wm-sep-field').style.display = isDual ? '' : 'none';
    document.getElementById('wm-units-field').style.display = isNumeric ? '' : 'none';
    document.getElementById('wm-precision-field').style.display = isNumeric ? '' : 'none';
}

function saveWidgetModal() {
    var block = state.blocks.find(function(b) { return b.id === modalWidgetBlockId; });
    if (!block) return;
    var config = createWidgetConfig(
        document.getElementById('wm-type').value,
        document.getElementById('wm-addr').value.trim(),
        document.getElementById('wm-label').value.trim(),
        document.getElementById('wm-units').value.trim(),
        document.getElementById('wm-addr2').value.trim(),
        document.getElementById('wm-units2').value.trim(),
        document.getElementById('wm-sep').value.trim(),
        parseInt(document.getElementById('wm-precision').value) || 0
    );
    if (modalWidgetId) {
        var idx = block.widgets.findIndex(function(w) { return w.id === modalWidgetId; });
        if (idx !== -1) { config.id = modalWidgetId; block.widgets[idx] = config; }
    } else {
        block.widgets.push(config);
    }
    renderBlock(block);
    closeModal('widget-modal');
}

function deleteWidgetFromModal() {
    var block = state.blocks.find(function(b) { return b.id === modalWidgetBlockId; });
    if (!block || !modalWidgetId) return;
    block.widgets = block.widgets.filter(function(w) { return w.id !== modalWidgetId; });
    renderBlock(block);
    toast('Виджет удалён', 'info');
    closeModal('widget-modal');
}

function copyWidgetFromModal() {
    var block = state.blocks.find(function(b) { return b.id === modalWidgetBlockId; });
    if (!block || !modalWidgetId) return;
    
    var wIdx = block.widgets.findIndex(function(w) { return w.id === modalWidgetId; });
    if (wIdx === -1) return;
    
    // Глубокое копирование конфига виджета
    var copy = JSON.parse(JSON.stringify(block.widgets[wIdx]));
    copy.id = genWidgetId();
    
    // Вставляем копию сразу после оригинала
    block.widgets.splice(wIdx + 1, 0, copy);
    renderBlock(block);
    toast('Виджет скопирован', 'info');
    closeModal('widget-modal');
}
// ============================================================
// МЫШЬ: drag / resize с привязкой к сетке
// ============================================================
document.addEventListener('mousemove', function(e) {
    if (blockDrag) {
        var block = state.blocks.find(function(b) { return b.id === blockDrag.blockId; });
        if (!block) return;
        block.x = snap(Math.max(0, blockDrag.origX + e.clientX - blockDrag.startX));
        block.y = snap(Math.max(0, blockDrag.origY + e.clientY - blockDrag.startY));
        var el = document.getElementById(block.id);
        if (el) { el.style.left = block.x + 'px'; el.style.top = block.y + 'px'; }
    }
    if (blockResize) {
        var block = state.blocks.find(function(b) { return b.id === blockResize.blockId; });
        if (!block) return;
        block.w = snap(Math.max(140, blockResize.origW + e.clientX - blockResize.startX));
        block.h = snap(Math.max(80, blockResize.origH + e.clientY - blockResize.startY));
        var el = document.getElementById(block.id);
        if (el) { el.style.width = block.w + 'px'; el.style.height = block.h + 'px'; }
    }
});

document.addEventListener('mouseup', function() {
    if (blockDrag) {
        var el = document.getElementById(blockDrag.blockId);
        if (el) el.classList.remove('dragging-block');
        blockDrag = null;
    }
    if (blockResize) { blockResize = null; }
});

// ============================================================
// ОБНОВЛЕНИЕ ВСЕХ ВИДЖЕТОВ (используется из common.js)
// ============================================================
function updateAllWidgets() {
    state.blocks.forEach(function(block) {
        block.widgets.forEach(function(widget) {
            var el = document.getElementById(widget.id);
            if (el) updateWidgetContent(el, widget);
        });
    });
}

// ============================================================
// ОПРОС СЕРВЕРА
// ============================================================
function openUrlModal() {
    document.getElementById('um-url').value = state.serverUrl;
    document.getElementById('um-interval').value = state.pollInterval;
    openModal('url-modal');
    setTimeout(function() { document.getElementById('um-url').focus(); }, 100);
}
function openDataModal() {
    var el = document.getElementById('data-modal-content');
    if (state.opcData.length === 0) {
        el.textContent = 'Нет данных';
    } else {
        el.textContent = JSON.stringify(state.opcData, null, 2);
    }
    openModal('data-modal');
}
function startUrlPolling() {
    var url = document.getElementById('um-url').value.trim();
    var interval = parseInt(document.getElementById('um-interval').value) || 1000;
    if (!url) { toast('Укажите URL', 'error'); return; }

    stopUrlPolling();
    state.serverUrl = url;
    state.pollInterval = Math.max(200, interval);
    setPollStatus('connected', url.replace(/^https?:\/\//, ''));

    var shortUrl = url.replace(/^https?:\/\//, '');
    state.urlPollTimer = setInterval(createPollTick(url, function() {
        setPollStatus('connected', shortUrl);
    }), state.pollInterval);
    // Первый тик сразу
    createPollTick(url, function() {
        setPollStatus('connected', shortUrl);
    })();

    closeModal('url-modal');
    toast('Опрос запущен: ' + url, 'success');
}

function stopUrlPolling() {
    clearInterval(state.urlPollTimer);
    state.urlPollTimer = null;
    state.serverUrl = '';
    setPollStatus('', 'не подключено');
}

// ============================================================
// КОНФИГУРАЦИЯ
// ============================================================
function buildConfig() {
    return {
        version: 1,
        serverUrl: state.serverUrl,
        pollInterval: state.pollInterval,
        blocks: state.blocks.map(function(b) {
            return {
                id: b.id, title: b.title,
                headerColor: b.headerColor || null,
                bodyColor: b.bodyColor || null,
                x: b.x, y: b.y, w: b.w, h: b.h, z: b.z,
                widgets: b.widgets.map(function(w) {
                    return {
                        id: w.id, type: w.type, addr: w.addr, label: w.label,
                        units: w.units, addr2: w.addr2, units2: w.units2, sep: w.sep,
                        precision: w.precision != null ? w.precision : 0,
                    };
                }),
            };
        }),
    };
}

function exportDashboard() {
    var blob = new Blob([JSON.stringify(buildConfig(), null, 2)], { type: 'application/json' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = 'dashboard.json';
    a.click();
    URL.revokeObjectURL(url);
    toast('Конфиг экспортирован', 'success');
}

function deployDashboard() {
    if (!state.serverUrl) {
        toast('Сначала подключитесь к источнику данных', 'error');
        return;
    }
    var url = state.serverUrl.replace(/\/[^/]*$/, '') + '/save';

    fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json; charset=utf-8' },
        body: JSON.stringify(buildConfig(), null, 2),
    })
        .then(function(r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.text(); })
        .then(function() { toast('Деплой выполнен: ' + url, 'success'); })
        .catch(function(err) { toast('Ошибка деплоя: ' + err.message, 'error'); });
}

function handleImport(event) {
    var file = event.target.files[0];
    if (!file) return;
    var reader = new FileReader();
    reader.onload = function(e) {
        try { importDashboard(JSON.parse(e.target.result)); }
        catch (err) { toast('Ошибка парсинга JSON: ' + err.message, 'error'); }
    };
    reader.readAsText(file);
    event.target.value = '';
}

function parseWidgetConfig(w) {
    return {
        id: w.id, type: w.type || 'value', addr: w.addr || '',
        label: w.label || '', units: w.units || '',
        addr2: w.addr2 || '', units2: w.units2 || '', sep: w.sep || '/',
        precision: w.precision != null ? w.precision : 0,
    };
}

function importDashboard(config) {
    if (!config || !Array.isArray(config.blocks)) {
        toast('Неверный формат конфигурации', 'error');
        return;
    }
    state.blocks.forEach(function(b) { var el = document.getElementById(b.id); if (el) el.remove(); });
    state.blocks = [];
    var maxBN = 0, maxWN = 0;

    config.blocks.forEach(function(b) {
        var block = {
            id: b.id, title: b.title || 'Без названия',
            headerColor: b.headerColor || null,
            bodyColor: b.bodyColor || null,
            x: snap(b.x != null ? b.x : 40), y: snap(b.y != null ? b.y : 40),
            w: snap(b.w || 320), h: snap(b.h || 200), z: b.z || 1,
            widgets: (b.widgets || []).map(function(w) {
                if (w.addr) state.knownAddrs.add(w.addr);
                if (w.addr2) state.knownAddrs.add(w.addr2);
                var n = parseInt(w.id.replace('widget-', ''));
                if (n > maxWN) maxWN = n;
                return parseWidgetConfig(w);
            }),
        };
        var n = parseInt(block.id.replace('block-', ''));
        if (n > maxBN) maxBN = n;
        state.blocks.push(block);
        renderBlock(block);
    });

    state.nextBlockId = maxBN + 1;
    state.nextWidgetId = maxWN + 1;
    updateAddrDatalist();
    updateAllWidgets();

    if (config.serverUrl) {
        state.serverUrl = config.serverUrl;
        state.pollInterval = config.pollInterval || 1000;
        var shortUrl = config.serverUrl.replace(/^https?:\/\//, '');
        setPollStatus('connected', shortUrl);
        state.urlPollTimer = setInterval(createPollTick(config.serverUrl, function() {
            setPollStatus('connected', shortUrl);
        }), state.pollInterval);
        createPollTick(config.serverUrl, function() {
            setPollStatus('connected', shortUrl);
        })();
    }
    toast('Загружено: ' + config.blocks.length + ' блоков', 'success');
}

// ============================================================
// ЗАГРУЗКА КОНФИГА ПРИ СТАРТЕ
// ============================================================
fetch('dashboard.json')
    .then(function(r) { if (!r.ok) throw new Error('не найден'); return r.json(); })
    .then(function(config) { importDashboard(config); })
    .catch(function() { setPollStatus('', 'не подключено'); });