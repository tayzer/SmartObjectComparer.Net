function saveAsFile(filename, contentType, content) {
    const blob = new Blob([content], { type: contentType });
    const url = URL.createObjectURL(blob);

    const a = document.createElement('a');
    a.href = url;
    a.download = filename;

    document.body.appendChild(a);

    a.click();

    setTimeout(() => {
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }, 0);
}

function renderPieChart(canvasId, labels, data) {
    const ctx = document.getElementById(canvasId).getContext('2d');
    const colors = generateColors(labels.length);

    new Chart(ctx, {
        type: 'pie',
        data: {
            labels: labels,
            datasets: [{
                data: data,
                backgroundColor: colors,
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'right',
                }
            }
        }
    });
}

function generateColors(count) {
    const colors = [];
    const baseColors = [
        '#4e73df', '#1cc88a', '#36b9cc', '#f6c23e', '#e74a3b',
        '#fd7e14', '#6f42c1', '#20c9a6', '#27a844', '#e83e8c'
    ];

    for (let i = 0; i < count; i++) {
        colors.push(baseColors[i % baseColors.length]);
    }

    return colors;
}

function processFilesInBatches(files, batchSize, callback) {
    return new Promise((resolve) => {
        const totalFiles = files.length;
        let processed = 0;

        function processNextBatch() {
            const batch = files.slice(processed, processed + batchSize);

            if (batch.length === 0) {
                resolve();
                return;
            }

            Promise.all(batch.map(callback))
                .then(() => {
                    processed += batch.length;
                    setTimeout(processNextBatch, 0); // Allow UI to refresh
                });
        }

        processNextBatch();
    });
}

function optimizedFileRead(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = reject;
        reader.readAsArrayBuffer(file);
    });
}