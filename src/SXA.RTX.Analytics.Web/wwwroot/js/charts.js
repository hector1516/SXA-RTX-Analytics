// Gráficas Chart.js con la paleta minimalista y adaptadas al tema claro/oscuro.
window.__rxVar = function (name, fallback) {
    try {
        const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
        return v || fallback;
    } catch (e) { return fallback; }
};

window.__rxColors = function () {
    return {
        accent: window.__rxVar('--rx-accent', '#2F6BE0'),
        warning: window.__rxVar('--rx-warning', '#B45309'),
        neutral: window.__rxVar('--rx-border-strong', '#D1D5DB'),
        text: window.__rxVar('--rx-text', '#111827'),
        muted: window.__rxVar('--rx-muted', '#6B7280'),
        border: window.__rxVar('--rx-border', '#E5E7EB'),
        surface: window.__rxVar('--rx-surface', '#FFFFFF')
    };
};

function rxBaseOptions() {
    const c = window.__rxColors();
    return {
        responsive: true,
        maintainAspectRatio: false,
        plugins: { legend: { display: false } },
        scales: {
            x: {
                grid: { display: false },
                border: { color: c.border },
                ticks: { color: c.muted, font: { size: 11 } }
            },
            y: {
                beginAtZero: true,
                grid: { color: c.border, drawTicks: false },
                border: { display: false },
                ticks: { color: c.muted, font: { size: 11 }, precision: 0 }
            }
        }
    };
}

// Dona para proporciones.
window.renderDonut = (canvasId, labels, values) => {
    const el = document.getElementById(canvasId);
    if (!el) return;
    if (el._chart) el._chart.destroy();
    const c = window.__rxColors();
    el._chart = new Chart(el, {
        type: 'doughnut',
        data: {
            labels: labels,
            datasets: [{
                data: values,
                backgroundColor: [c.accent, c.muted, c.neutral],
                borderColor: c.surface,
                borderWidth: 2,
                hoverOffset: 2
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            cutout: '70%',
            plugins: { legend: { display: false }, tooltip: { enabled: true } }
        }
    });
};

// Barras para comparaciones.
window.renderBars = (canvasId, labels, values, label) => {
    const el = document.getElementById(canvasId);
    if (!el) return;
    if (el._chart) el._chart.destroy();
    const c = window.__rxColors();
    el._chart = new Chart(el, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: label || '',
                data: values,
                backgroundColor: c.accent,
                borderRadius: 6,
                maxBarThickness: 30
            }]
        },
        options: rxBaseOptions()
    });
};

// Genérico (usado por /query).
window.renderChart = (canvasId, type, labels, values, label) => {
    const el = document.getElementById(canvasId);
    if (!el) return;
    if (el._chart) el._chart.destroy();
    const c = window.__rxColors();
    const isPie = type === 'pastel' || type === 'pie';
    const isArea = type === 'area';
    const chartType = isPie ? 'doughnut' : isArea ? 'line' : (type === 'bar' ? 'bar' : type);

    const options = isPie
        ? { responsive: true, maintainAspectRatio: false, cutout: '60%', plugins: { legend: { position: 'bottom', labels: { color: c.text, boxWidth: 12 } } } }
        : rxBaseOptions();
    if (!isPie) options.plugins = { legend: { display: false } };

    el._chart = new Chart(el, {
        type: chartType,
        data: {
            labels: labels,
            datasets: [{
                label: label || 'Valor',
                data: values,
                backgroundColor: isPie
                    ? [c.accent, '#5AC8FA', c.muted, '#AEAEB2', '#8E8E93']
                    : isArea ? 'rgba(0,113,227,0.16)' : c.accent,
                borderColor: c.accent,
                borderWidth: isArea ? 2 : (isPie ? 2 : 0),
                fill: isArea,
                tension: 0.3,
                borderRadius: isArea ? 0 : 6,
                maxBarThickness: 34
            }]
        },
        options: options
    });
};
