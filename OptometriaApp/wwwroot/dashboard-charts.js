window.initializeDashboardCharts = (labels, activityData, financesData, totalText) => {
    // Destroy existing charts if they exist to prevent memory leaks or canvas reuse errors
    if (window.dashboardChart1) {
        window.dashboardChart1.destroy();
    }
    if (window.dashboardChart2) {
        window.dashboardChart2.destroy();
    }

    // 1. Chart 1: Clientes y Citas (Line/Area Chart)
    const el1 = document.getElementById('chartAppointmentsPatients');
    if (el1) {
        const ctx1 = el1.getContext('2d');
        
        window.dashboardChart1 = new Chart(ctx1, {
            type: 'bar',
            data: {
                labels,
                datasets: [{
                    data: activityData,
                    backgroundColor: ['#7F6951', '#FEBC64', '#5DA181'],
                    borderColor: ['#7F6951', '#E9A84E', '#4B8D70'],
                    borderWidth: 1,
                    borderRadius: 7,
                    maxBarThickness: 62
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: false
                    },
                    tooltip: {
                        mode: 'index',
                        intersect: false,
                        padding: 10,
                        cornerRadius: 8,
                        backgroundColor: 'rgba(255, 250, 244, 0.96)',
                        titleColor: '#3C342E',
                        bodyColor: '#3C342E',
                        borderColor: 'rgba(127, 105, 81, 0.15)',
                        borderWidth: 1,
                        multiKeyBackground: 'transparent',
                        usePointStyle: true
                    }
                },
                scales: {
                    y: {
                        beginAtZero: true,
                        suggestedMax: Math.max(...activityData, 1) + 1,
                        grid: {
                            color: 'rgba(127, 105, 81, 0.08)'
                        },
                        ticks: {
                            precision: 0,
                            color: 'rgba(60, 52, 46, 0.6)',
                            font: { size: 10 }
                        },
                        border: {
                            dash: [5, 5]
                        }
                    },
                    x: {
                        grid: {
                            display: false
                        },
                        ticks: {
                            color: 'rgba(60, 52, 46, 0.6)',
                            font: { size: 10 }
                        }
                    }
                }
            }
        });
    }

    // 2. Chart 2: Cartera y Finanzas (Donut Chart)
    const el2 = document.getElementById('chartFinances');
    if (el2) {
        const ctx2 = el2.getContext('2d');
        window.dashboardChart2 = new Chart(ctx2, {
            type: 'doughnut',
            data: {
                labels: ['Ganancias', 'Gastos', 'Cartera'],
                datasets: [{
                    data: financesData,
                    backgroundColor: ['#5DA181', '#E53935', '#7F6951'],
                    borderWidth: 2,
                    borderColor: '#FFFDF9',
                    hoverOffset: 4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: '70%',
                plugins: {
                    legend: {
                        display: false
                    },
                    tooltip: {
                        padding: 10,
                        cornerRadius: 8,
                        backgroundColor: 'rgba(255, 250, 244, 0.96)',
                        bodyColor: '#3C342E',
                        borderColor: 'rgba(127, 105, 81, 0.15)',
                        borderWidth: 1,
                        usePointStyle: true
                    }
                }
            }
        });

        // Center text update
        const elCenter = document.getElementById('chartFinancesTotal');
        if (elCenter) {
            elCenter.innerText = totalText;
        }
    }
};
