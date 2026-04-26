// ============================================================
// ОБЩЕЕ СОСТОЯНИЕ
// ============================================================
var state = {
    blocks: [],
    opcData: [],
    knownAddrs: new Set(),
    serverUrl: '',
    pollInterval: 1000,
    urlPollTimer: null,
    nextBlockId: 1,
    nextWidgetId: 1,
};

var GRID = 5;

// ============================================================
// ТЕМА
// ============================================================
function toggleTheme() {
    var next = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', next);
    localStorage.setItem('dashboard-theme', next);
    updateThemeIcon(next);
}

function updateThemeIcon(theme) {
    var icon = document.getElementById('theme-icon');
    if (icon) icon.className = theme === 'dark' ? 'fas fa-sun' : 'fas fa-moon';
}

(function initTheme() {
    var saved = localStorage.getItem('dashboard-theme') || 'dark';
    document.documentElement.setAttribute('data-theme', saved);
    // Отложим обновление иконки до загрузки DOM
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() { updateThemeIcon(saved); });
    } else {
        updateThemeIcon(saved);
    }
})();

// ============================================================
// УТИЛИТЫ
// ============================================================
function snap(val) {
    return Math.round(val / GRID) * GRID;
}

function toast(msg, type) {
    type = type || 'info';
    var c = document.getElementById('toast-container');
    if (!c) return;
    var t = document.createElement('div');
    t.className = 'toast toast-' + type;
    t.textContent = msg;
    c.appendChild(t);
    setTimeout(function() { if (t.parentNode) t.remove(); }, 3000);
}

function escHtml(str) {
    var d = document.createElement('div');
    d.textContent = str;
    return d.innerHTML;
}

// ============================================================
// ФОРМАТИРОВАНИЕ ЗНАЧЕНИЙ
// ============================================================
function formatNum(v, precision) {
    if (typeof v === 'number') {
        if (precision === 0 || precision == null) return String(Math.round(v));
        return v.toFixed(precision);
    }
    return String(v != null ? v : '---');
}

function getOpcValue(addr) {
    if (!addr) return null;
    var item = state.opcData.find(function(d) { return d.addr === addr; });
    return item ? item.v : null;
}

// ============================================================
// ОБНОВЛЕНИЕ СОДЕРЖИМОГО ВИДЖЕТА
// ============================================================
function updateWidgetContent(el, widget) {
    var val = getOpcValue(widget.addr);
    var val2 = getOpcValue(widget.addr2);
    var html = '<div class="widget-label">' + escHtml(widget.label || widget.addr || '—') + '</div>';

    switch (widget.type) {
        case 'value': {
            var v = val !== null ? formatNum(val, widget.precision) : '---';
            html += '<div class="widget-value">' + v;
            if (widget.units) html += '<span class="units">' + escHtml(widget.units) + '</span>';
            html += '</div>';
            break;
        }
        case 'bool': {
            var b = val === true;
            var dotClass = b ? 'bool-on' : 'bool-off';
            html += '<div class="widget-value" style="display:flex;align-items:center;justify-content:center;gap:5px;">' +
                    '<span class="bool-indicator ' + dotClass + '"></span>' +
                    '<span>' + (b ? 'ON' : 'OFF') + '</span>' +
                    '</div>';
            break;
        }
        case 'text': {
            var t = val !== null ? String(val) : '---';
            html += '<div class="widget-value">' + escHtml(t) + '</div>';
            break;
        }
        case 'dual': {
            var v1 = val !== null ? formatNum(val, widget.precision) : '---';
            var v2 = val2 !== null ? formatNum(val2, widget.precision) : '---';
            html += '<div class="widget-value">' + v1;
            if (widget.units) html += '<span class="units">' + escHtml(widget.units) + '</span>';
            html += '<span class="sep">' + escHtml(widget.sep || '/') + '</span>';
            html += v2;
            if (widget.units2) html += '<span class="units">' + escHtml(widget.units2) + '</span>';
            html += '</div>';
            break;
        }
    }
    el.innerHTML = html;
}

// ============================================================
// ПАНЕЛЬ ДАННЫХ
// ============================================================
function toggleDataPanel() {
    var panel = document.getElementById('data-panel');
    if (!panel) return;
    panel.classList.toggle('open');
    var arrow = document.getElementById('dp-arrow');
    if (arrow) arrow.className = panel.classList.contains('open') ? 'fas fa-chevron-down' : 'fas fa-chevron-up';
}

function updateDataPanel() {
    var el = document.getElementById('data-panel-content');
    if (!el) return;
    if (state.opcData.length === 0) { el.textContent = 'Нет данных'; return; }
    el.textContent = JSON.stringify(state.opcData, null, 2);
}

// ============================================================
// ОБРАБОТКА OPC-ДАННЫХ
// ============================================================
function processOpcData(data) {
    if (!Array.isArray(data)) return;
    state.opcData = data;
    var changed = false;
    data.forEach(function(d) {
        if (d.addr && !state.knownAddrs.has(d.addr)) {
            state.knownAddrs.add(d.addr);
            changed = true;
        }
    });
    if (changed) updateAddrDatalist();
    updateDataPanel();
    // updateAllWidgets определяется в editor.js / viewer.js
    if (typeof updateAllWidgets === 'function') updateAllWidgets();
}

function updateAddrDatalist() {
    var dl = document.getElementById('addr-datalist');
    if (!dl) return;
    dl.innerHTML = '';
    
    // Собираем соответствие addr -> id из последних данных
    var addrToId = {};
    if (Array.isArray(state.opcData)) {
        state.opcData.forEach(function(d) { if (d.addr) addrToId[d.addr] = d.id; });
    }

    Array.from(state.knownAddrs).sort().forEach(function(addr) {
        var opt = document.createElement('option');
        opt.value = addr; // value остаётся addr, чтобы сохранялось корректно
        var id = addrToId[addr] !== undefined ? addrToId[addr] : '?';
        opt.textContent = '[' + id + '] ' + addr; // То, что видит пользователь
        dl.appendChild(opt);
    });
}

// ============================================================
// СТАТУС ПОДКЛЮЧЕНИЯ
// ============================================================
function setPollStatus(status, text) {
    var dot = document.getElementById('poll-dot');
    var label = document.getElementById('poll-label');
    if (dot) dot.className = 'poll-dot ' + status;
    if (label) label.textContent = text;
    var btn = document.getElementById('url-btn');
    if (btn) {
        if (status === 'connected') btn.classList.add('active');
        else btn.classList.remove('active');
    }
}

// ============================================================
// ОПРОС СЕРВЕРА
// ============================================================
function createPollTick(url, onTick) {
    return function tick() {
        fetch(url)
            .then(function(r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
            .then(function(data) { processOpcData(data); if (onTick) onTick(); })
            .catch(function(err) { setPollStatus('error', 'ошибка: ' + err.message); });
    };
}

// ============================================================
// УТИЛИТЫ ЦВЕТА
// ============================================================
function getContrastYIQ(hexcolor) {
    hexcolor = hexcolor.replace("#", "");
    var r = parseInt(hexcolor.substr(0, 2), 16);
    var g = parseInt(hexcolor.substr(2, 2), 16);
    var b = parseInt(hexcolor.substr(4, 2), 16);
    var yiq = ((r * 299) + (g * 587) + (b * 114)) / 1000;
    return (yiq >= 128) ? 'dark' : 'light';
}