// ============================================================
// СОСТОЯНИЕ ВЬЮЕРА
// ============================================================
var canvas = document.getElementById('dashboard-canvas');

// ============================================================
// РЕНДЕР БЛОКА (без интерактивности)
// ============================================================
function renderBlock(block) {
    var el = document.createElement('div');
    el.className = 'dash-block viewer-block';
    el.id = block.id;
    el.style.left = block.x + 'px';
    el.style.top = block.y + 'px';
    el.style.width = block.w + 'px';
    el.style.height = block.h + 'px';
    el.style.zIndex = block.z;
   
    el.innerHTML =
        '<div class="block-header">' +
            '<span class="block-title">' + escHtml(block.title) + '</span>' +
        '</div>' +
        '<div class="widgets-container"></div>';
 // Применение пользовательских цветов
    if (block.bodyColor) {
        el.style.backgroundColor = block.bodyColor;
    }
    var headerEl = el.querySelector('.block-header');
    if (block.headerColor) {
        headerEl.style.backgroundColor = block.headerColor;
        var isLightBg = getContrastYIQ(block.headerColor) === 'dark';
        var textColor = isLightBg ? 'var(--bg)' : 'var(--fg-dim)';
        headerEl.querySelector('.block-title').style.color = textColor;
    }
    var wc = el.querySelector('.widgets-container');
    block.widgets.forEach(function(w) { wc.appendChild(renderWidget(w)); });
    canvas.appendChild(el);
}

// ============================================================
// РЕНДЕР ВИДЖЕТА (без интерактивности)
// ============================================================
function renderWidget(widget) {
    var el = document.createElement('div');
    el.className = 'widget viewer-widget';
    el.id = widget.id;
    el.title = (widget.label || '') + '\n' + (widget.addr || '') +
        (widget.type === 'dual' ? ' | ' + (widget.addr2 || '') : '');
    updateWidgetContent(el, widget);
    return el;
}

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
function startPolling(url, interval) {
    stopPolling();
    state.serverUrl = url;
    state.pollInterval = Math.max(200, interval);
    var shortUrl = url.replace(/^https?:\/\//, '');
    setPollStatus('connected', shortUrl);

    state.urlPollTimer = setInterval(createPollTick(url, function() {
        setPollStatus('connected', shortUrl);
    }), state.pollInterval);
    createPollTick(url, function() {
        setPollStatus('connected', shortUrl);
    })();
}

function stopPolling() {
    clearInterval(state.urlPollTimer);
    state.urlPollTimer = null;
    state.serverUrl = '';
    setPollStatus('', 'не подключено');
}

// ============================================================
// ЗАГРУЗКА КОНФИГА ПРИ СТАРТЕ
// ============================================================
fetch('dashboard.json')
    .then(function(r) { if (!r.ok) throw new Error('не найден'); return r.json(); })
    .then(function(config) {
        if (!config || !Array.isArray(config.blocks)) {
            toast('Неверный формат конфигурации', 'error');
            return;
        }

        config.blocks.forEach(function(b) {
            var block = {
                id: b.id, title: b.title || 'Без названия',
                headerColor: b.headerColor || null,
                bodyColor: b.bodyColor || null,
                x: b.x != null ? b.x : 40,
                y: b.y != null ? b.y : 40,
                w: b.w || 320, h: b.h || 200, z: b.z || 1,
                widgets: (b.widgets || []).map(function(w) {
                    return {
                        id: w.id, type: w.type || 'value', addr: w.addr || '',
                        label: w.label || '', units: w.units || '',
                        addr2: w.addr2 || '', units2: w.units2 || '', sep: w.sep || '/',
                        precision: w.precision != null ? w.precision : 0,
                    };
                }),
            };
            state.blocks.push(block);
            renderBlock(block);
        });

        if (config.serverUrl) {
            startPolling(config.serverUrl, config.pollInterval || 1000);
        }

        toast('Загружено: ' + config.blocks.length + ' блоков', 'success');
    })
    .catch(function() {
        setPollStatus('', 'не подключено');
        toast('Файл dashboard.json не найден', 'error');
    });