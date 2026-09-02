window.renderChart = (canvasId, type, labels, values, label) => {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;
    if (ctx._chart) ctx._chart.destroy();
    const cfg = {
        type: type === 'pastel' ? 'pie' : type === 'area' ? 'line' : type,
        data: {
            labels: labels,
            datasets: [{
                label: label || 'Valor',
                data: values,
                backgroundColor: type === 'pastel'
                    ? ['#3A7BD5','#FFC84B','#2ECC71','#FF6B35','#8EC6FF','#9AA8BE']
                    : type === 'area' ? 'rgba(58,123,213,0.25)' : '#3A7BD5',
                borderColor: '#3A7BD5',
                fill: type === 'area',
                tension: 0.3
            }]
        },
        options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'bottom' } } }
    };
    ctx._chart = new Chart(ctx, cfg);
};
