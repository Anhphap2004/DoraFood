(() => {
    const canvas = document.getElementById("revenue-chart");
    const source = document.getElementById("revenue-chart-data");
    const container = document.getElementById(
        "revenue-chart-container"
    );
    const error = document.getElementById("revenue-chart-error");

    if (!canvas || !source || !container || !error) {
        return;
    }

    const showError = () => {
        container.hidden = true;
        error.hidden = false;
    };

    if (!window.Chart) {
        showError();
        return;
    }

    const moneyFormatter = new Intl.NumberFormat("vi-VN", {
        maximumFractionDigits: 2
    });

    const money = value =>
        moneyFormatter.format(value) + " ₫";

    try {
        const data = JSON.parse(source.textContent);

        const existingChart = window.Chart.getChart(canvas);

        if (existingChart) {
            existingChart.destroy();
        }

        const pointRadius = data.labels.length > 31 ? 0 : 3;

        const maximum = Math.max(
            0,
            ...data.collected,
            ...data.refunded
        );

        const reduceMotion = window.matchMedia(
            "(prefers-reduced-motion: reduce)"
        ).matches;

        const chart = new window.Chart(canvas, {
            type: "line",

            data: {
                labels: data.labels,

                datasets: [
                    {
                        label: "Tiền đã thu",
                        data: data.collected,
                        borderColor: "#198754",
                        backgroundColor: "#198754",
                        borderWidth: 2,
                        pointRadius,
                        pointHoverRadius: 5,
                        pointStyle: "circle",
                        tension: 0,
                        fill: false
                    },
                    {
                        label: "Tiền đã hoàn",
                        data: data.refunded,
                        borderColor: "#dc3545",
                        backgroundColor: "#dc3545",
                        borderWidth: 2,
                        borderDash: [6, 4],
                        pointRadius,
                        pointHoverRadius: 5,
                        pointStyle: "triangle",
                        tension: 0,
                        fill: false
                    }
                ]
            },

            options: {
                responsive: true,
                maintainAspectRatio: false,

                animation: reduceMotion
                    ? false
                    : { duration: 350 },

                font: {
                    family: getComputedStyle(
                        document.body
                    ).fontFamily
                },

                interaction: {
                    mode: "index",
                    intersect: false
                },

                plugins: {
                    legend: {
                        position: "top",

                        labels: {
                            usePointStyle: true,
                            padding: 18
                        }
                    },

                    tooltip: {
                        callbacks: {
                            label: context =>
                                `${context.dataset.label}: `
                                + money(context.parsed.y ?? 0)
                        }
                    }
                },

                scales: {
                    x: {
                        grid: {
                            display: false
                        },

                        ticks: {
                            autoSkip: true,
                            maxTicksLimit: 10,
                            maxRotation: 0
                        }
                    },

                    y: {
                        beginAtZero: true,

                        // Giữ trục dễ đọc khi toàn bộ dữ liệu bằng 0.
                        suggestedMax: maximum === 0
                            ? 1000
                            : undefined,

                        ticks: {
                            maxTicksLimit: 6,
                            callback: value => money(Number(value))
                        }
                    }
                }
            }
        });

        // Đồng bộ màu chữ và đường lưới với giao diện sáng/tối.
        const updateTheme = () => {
            const style = getComputedStyle(
                document.documentElement
            );

            const textColor = style.getPropertyValue(
                "--bs-body-color"
            ).trim() || "#495057";

            const gridColor = style.getPropertyValue(
                "--bs-border-color"
            ).trim() || "#dee2e6";

            chart.options.plugins.legend.labels.color = textColor;
            chart.options.scales.x.ticks.color = textColor;
            chart.options.scales.y.ticks.color = textColor;
            chart.options.scales.y.grid.color = gridColor;

            chart.update("none");
        };

        updateTheme();

        const observer = new MutationObserver(updateTheme);

        observer.observe(document.documentElement, {
            attributes: true,
            attributeFilter: ["data-bs-theme"]
        });
    } catch (exception) {
        console.error("Không dựng được biểu đồ doanh thu.", exception);
        showError();
    }
})();