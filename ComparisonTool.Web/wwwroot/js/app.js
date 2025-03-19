// This file should be placed in wwwroot/js/app.js

// Save content as a file 
function saveAsFile(filename, contentType, content) {
    // Create a blob with the content
    const blob = new Blob([content], { type: contentType });

    // Create a URL for the blob
    const url = URL.createObjectURL(blob);

    // Create a temporary link element
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;

    // Append the link to the body
    document.body.appendChild(a);

    // Trigger a click on the link
    a.click();

    // Clean up
    setTimeout(() => {
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }, 0);
}

// Render a pie chart (using Chart.js)
function renderPieChart(canvasId, labels, data) {
    const ctx = document.getElementById(canvasId).getContext('2d');

    // Generate colors
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

// Generate colors for charts
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