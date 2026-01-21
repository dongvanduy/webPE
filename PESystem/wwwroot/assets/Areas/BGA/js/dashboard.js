const API_BASE_URL = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/bga-replace";
const STATUS_TEXT = {
    3: "Return PD line ok",
    4: "Waiting approve replace BGA",
    10: "Check in barking",
    11: "Check out barking",
    12: "VI after barking",
    13: "Remove BGA, clean pad",
    14: "Check VI BGA pad",
    15: "Replace BGA",
    16: "Xray",
    17: "ICT, FT",
    18: "Waiting return PD line ok",
    19: "Return PD line ok"
};

const AXIS_TICK_COLOR = "#f8f9fa";
const GRID_LINE_COLOR = "rgba(255, 255, 255, 0.2)";

const statusValueLabelsPlugin = {
    id: "statusValueLabels",
    afterDatasetsDraw(chart, _args, pluginOptions) {
        const options = pluginOptions || {};
        if (options.display === false) {
            return;
        }

        const dataset = chart.data.datasets?.[0];
        const meta = chart.getDatasetMeta(0);
        if (!dataset || !Array.isArray(dataset.data)) {
            return;
        }

        const ctx = chart.ctx;
        const color = options.color || "#0d6efd";
        const fontOptions = options.font || {};
        const fontSize = fontOptions.size || 12;
        const fontWeight = fontOptions.weight || "600";
        const fontFamily = fontOptions.family || "'Segoe UI', Arial, sans-serif";
        const lineHeight = fontOptions.lineHeight || Math.round(fontSize * 1.3);
        const align = options.align || "center";
        const offset = typeof options.offset === "number" ? options.offset : 8;

        const labels = Array.isArray(chart.data.labels) ? chart.data.labels : [];
        const statusDetails = Array.isArray(dataset.statusDetails) ? dataset.statusDetails : [];

        const buildStatusText = (detail, fallbackLabel) => {
            if (detail && typeof detail === "object") {
                const statusCode = detail.status ?? fallbackLabel ?? "";
                const statusName = detail.statusName;
                let text = statusName ? `${statusCode} - ${statusName}` : `${statusCode}`;
                text = `${text}`.trim();
                if (text.length > 34) {
                    text = `${text.slice(0, 31)}...`;
                }
                return text;
            }

            if (typeof fallbackLabel === "string" && fallbackLabel.length) {
                return fallbackLabel;
            }

            return "";
        };

        ctx.save();
        ctx.fillStyle = color;
        ctx.font = `${fontWeight} ${fontSize}px ${fontFamily}`;
        ctx.textAlign = align;
        ctx.textBaseline = "bottom";

        meta.data.forEach((element, index) => {
            const value = dataset.data[index];
            if (value === undefined || value === null) {
                return;
            }

            const detail = statusDetails[index];
            const fallbackLabel = typeof labels[index] === "string" ? labels[index] : "";
            const statusText = buildStatusText(detail, fallbackLabel);
            const lines = [];

            if (statusText) {
                lines.push(`TT: ${statusText}`);
            }

            lines.push(`SL: ${value}`);

            const totalHeight = (lines.length - 1) * lineHeight;
            const x = element.x;
            const startY = element.y - offset;

            lines.forEach((line, lineIndex) => {
                if (!line) {
                    return;
                }

                const lineY = startY - (totalHeight - (lineIndex * lineHeight));
                ctx.fillText(line, x, lineY);
            });
        });

        ctx.restore();
    }
};

document.addEventListener("DOMContentLoaded", () => {
    const statusChartCanvas = document.getElementById("status-chart");
    const statusChartEmpty = document.getElementById("status-chart-empty");
    const barkingChartCanvas = document.getElementById("barking-age-chart");
    const barkingChartEmpty = document.getElementById("barking-age-empty");
    const dashboardLastUpdated = document.getElementById("dashboard-last-updated");
    const refreshDashboardButton = document.getElementById("refresh-dashboard-btn");
    const statusDownloadSelect = document.getElementById("status-download-select");
    const statusDownloadButton = document.getElementById("status-download-btn");
    const statusDownloadFeedback = document.getElementById("status-download-feedback");
    const barkingDownloadButton = document.getElementById("barking-download-btn");
    const barkingDetailsContainer = document.getElementById("barking-details");

    if (!statusChartCanvas || !statusChartEmpty || !barkingChartCanvas || !barkingChartEmpty) {
        return;
    }

    let statusChartInstance = null;
    let barkingChartInstance = null;
    let statusSummaryState = [];
    let barkingAgingState = [];

    const setButtonLoading = (button, isLoading, loadingText) => {
        if (!button) {
            return;
        }
        if (isLoading) {
            button.disabled = true;
            button.dataset.originalText = button.dataset.originalText || button.innerHTML;
            button.innerHTML = loadingText || '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Đang tải...';
        } else {
            button.disabled = false;
            const original = button.dataset.originalText;
            if (original) {
                button.innerHTML = original;
            }
        }
    };

    const setStatusDownloadFeedback = (message, type = "muted") => {
        if (!statusDownloadFeedback) {
            return;
        }

        const classMap = {
            muted: "text-muted",
            success: "text-success",
            warning: "text-warning",
            danger: "text-danger"
        };

        statusDownloadFeedback.textContent = message || "";
        statusDownloadFeedback.className = "mt-3 small";
        if (message && classMap[type]) {
            statusDownloadFeedback.classList.add(classMap[type]);
        }
    };

    const toCsvValue = (value) => {
        if (value === null || value === undefined) {
            return "";
        }
        const stringValue = String(value).replace(/"/g, '""');
        if (/[",\n]/.test(stringValue)) {
            return `"${stringValue}"`;
        }
        return stringValue;
    };

    const buildCsvContent = (rows, columns) => {
        const headerLine = columns.map(column => toCsvValue(column.label)).join(",");
        const dataLines = rows.map(row => columns.map(column => {
            const rawValue = typeof column.value === "function"
                ? column.value(row)
                : row[column.key];
            return toCsvValue(rawValue);
        }).join(","));

        return [headerLine, ...dataLines].join("\n");
    };

    const triggerCsvDownload = (csvContent, filename) => {
        const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    };

    const populateStatusDownloadOptions = (data) => {
        if (!statusDownloadSelect || !statusDownloadButton) {
            return;
        }

        statusDownloadSelect.innerHTML = "";
        const placeholderOption = document.createElement("option");
        placeholderOption.value = "";
        placeholderOption.textContent = "Chọn trạng thái để tải danh sách SN";
        statusDownloadSelect.appendChild(placeholderOption);
        statusDownloadSelect.value = "";

        if (!Array.isArray(data) || !data.length) {
            statusDownloadSelect.disabled = true;
            statusDownloadButton.disabled = true;
            return;
        }

        data.forEach(item => {
            const option = document.createElement("option");
            option.value = item.status;
            option.textContent = `${item.status} - ${item.statusName} (${item.count ?? 0} SN)`;
            statusDownloadSelect.appendChild(option);
        });

        statusDownloadSelect.disabled = false;
        statusDownloadButton.disabled = true;
    };

    const renderBarkingDetails = (data) => {
        if (!barkingDetailsContainer) {
            return;
        }

        if (!Array.isArray(data) || !data.length) {
            barkingDetailsContainer.innerHTML = `<p class="text-muted small mb-0">Không có dữ liệu chi tiết để hiển thị.</p>`;
            return;
        }

        const topItems = data.slice(0, Math.min(10, data.length));
        const rows = topItems.map(item => {
            const applyTimeText = item.applyTime
                ? new Date(item.applyTime).toLocaleString()
                : "Không xác định";
            const hoursText = typeof item.hours === "number"
                ? `${Math.round(item.hours * 10) / 10} giờ`
                : "N/A";
            const minutesText = typeof item.minutes === "number"
                ? `${Math.round(item.minutes)} phút`
                : "-";
            return `
                <tr>
                    <td>${item.sn ?? ""}</td>
                    <td>${applyTimeText}</td>
                    <td>${hoursText}</td>
                    <td>${minutesText}</td>
                </tr>
            `;
        }).join("");

        barkingDetailsContainer.innerHTML = `
            <div class="table-responsive small">
                <table class="table table-sm table-bordered align-middle mb-0">
                    <thead class="table-light">
                        <tr>
                            <th>SN</th>
                            <th>Apply time</th>
                            <th>Số giờ</th>
                            <th>Số phút (xấp xỉ)</th>
                        </tr>
                    </thead>
                    <tbody>${rows}</tbody>
                </table>
                <p class="text-muted fst-italic mt-2 mb-0">Hiển thị tối đa 10 SN mới nhất ở trạng thái 11.</p>
            </div>
        `;
    };

    const downloadStatusDetails = async () => {
        if (!statusDownloadSelect || !statusDownloadButton) {
            return;
        }

        const selectedValue = statusDownloadSelect.value;
        if (!selectedValue) {
            setStatusDownloadFeedback("Vui lòng chọn trạng thái trước khi tải.", "warning");
            return;
        }

        const status = parseInt(selectedValue, 10);
        if (Number.isNaN(status)) {
            setStatusDownloadFeedback("Giá trị trạng thái không hợp lệ.", "danger");
            return;
        }

        setStatusDownloadFeedback("Đang chuẩn bị dữ liệu...", "muted");
        setButtonLoading(statusDownloadButton, true, "Đang tải...");

        try {
            const response = await fetch(`${API_BASE_URL}/status?status=${status}`);
            const data = await response.json();
            if (!response.ok) {
                const message = data?.message || "Không thể tải danh sách SN.";
                throw new Error(message);
            }

            if (!Array.isArray(data) || !data.length) {
                setStatusDownloadFeedback("Không có dữ liệu SN cho trạng thái đã chọn.", "warning");
                return;
            }

            const csvColumns = [
                { label: "SN", key: "sn" },
                { label: "Internal Task", key: "internalTask" },
                { label: "Task Number", key: "taskNumber" },
                { label: "Description", key: "desc" },
                { label: "Status", key: "applyTaskStatus" },
                { label: "Status name", value: row => row.statusName ?? STATUS_TEXT[row.applyTaskStatus] ?? "" },
                { label: "Remark", key: "findBoardStatus" },
                { label: "Approve person", key: "approveScrapperson" },
                { label: "Apply time", value: row => row.applyTime ? new Date(row.applyTime).toLocaleString() : "" }
            ];

            const csvContent = buildCsvContent(data, csvColumns);
            const statusName = STATUS_TEXT[status] || statusSummaryState.find(item => item.status === status)?.statusName || status;
            const filename = `replace-bga-status-${status}-${(statusName || "").toString().replace(/[^a-z0-9-_]+/gi, "-")}.csv`;
            triggerCsvDownload(csvContent, filename);
            setStatusDownloadFeedback("Đã tải xuống danh sách SN thành công.", "success");
        } catch (error) {
            setStatusDownloadFeedback(error.message || "Đã xảy ra lỗi khi tải dữ liệu.", "danger");
        } finally {
            setButtonLoading(statusDownloadButton, false);
        }
    };

    const downloadBarkingDetails = () => {
        if (!Array.isArray(barkingAgingState) || !barkingAgingState.length) {
            return;
        }

        const csvColumns = [
            { label: "SN", key: "sn" },
            { label: "Apply time", value: row => row.applyTime ? new Date(row.applyTime).toLocaleString() : "" },
            { label: "Giờ kể từ Barking", key: "hours" },
            { label: "Phút kể từ Barking", key: "minutes" }
        ];

        const csvContent = buildCsvContent(barkingAgingState, csvColumns);
        const filename = `replace-bga-barking-aging-${new Date().toISOString().slice(0, 10)}.csv`;
        triggerCsvDownload(csvContent, filename);
    };

    const showStatusLoading = () => {
        statusChartEmpty.textContent = "Đang tải dữ liệu...";
        statusChartEmpty.classList.remove("d-none", "alert-danger");
        statusChartEmpty.classList.add("alert-info");
        statusChartCanvas.classList.add("d-none");
        statusSummaryState = [];
        populateStatusDownloadOptions([]);
        setStatusDownloadFeedback("");
    };

    const showBarkingLoading = () => {
        barkingChartEmpty.textContent = "Đang tải dữ liệu...";
        barkingChartEmpty.classList.remove("d-none", "alert-danger");
        barkingChartEmpty.classList.add("alert-info");
        barkingChartCanvas.classList.add("d-none");
        barkingAgingState = [];
        renderBarkingDetails([]);
        if (barkingDownloadButton) {
            barkingDownloadButton.disabled = true;
        }
    };

    const renderStatusChart = (data, errorMessage) => {
        if (errorMessage) {
            statusChartEmpty.textContent = errorMessage;
            statusChartEmpty.classList.remove("alert-info");
            statusChartEmpty.classList.add("alert-danger");
            statusChartEmpty.classList.remove("d-none");
            statusChartCanvas.classList.add("d-none");
            if (statusChartInstance) {
                statusChartInstance.destroy();
                statusChartInstance = null;
            }
            statusSummaryState = [];
            populateStatusDownloadOptions([]);
            return;
        }

        if (!Array.isArray(data) || !data.length) {
            statusChartEmpty.textContent = "Không có dữ liệu để hiển thị.";
            statusChartEmpty.classList.remove("alert-danger");
            statusChartEmpty.classList.add("alert-info");
            statusChartEmpty.classList.remove("d-none");
            statusChartCanvas.classList.add("d-none");
            if (statusChartInstance) {
                statusChartInstance.destroy();
                statusChartInstance = null;
            }
            statusSummaryState = [];
            populateStatusDownloadOptions([]);
            return;
        }

        const enrichedData = data.map(item => ({
            status: item.status,
            count: item.count ?? 0,
            statusName: item.statusName ?? STATUS_TEXT[item.status] ?? item.status
        }));
        statusSummaryState = enrichedData;
        populateStatusDownloadOptions(enrichedData);
        setStatusDownloadFeedback("");
        const labels = enrichedData.map(item => `${item.status} - ${item.statusName}`);
        const values = enrichedData.map(item => item.count);
        const statusDetails = enrichedData.map(item => ({
            status: item.status,
            statusName: item.statusName
        }));

        if (statusChartInstance) {
            statusChartInstance.destroy();
        }

        statusChartInstance = new Chart(statusChartCanvas, {
            type: "bar",
            data: {
                labels,
                datasets: [{
                    label: "Số lượng SN",
                    data: values,
                    backgroundColor: "rgba(54, 162, 235, 0.6)",
                    borderColor: "rgba(54, 162, 235, 1)",
                    borderWidth: 1,
                    statusDetails
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: false,
                        labels: {
                            color: AXIS_TICK_COLOR
                        }
                    },
                    tooltip: {
                        callbacks: {
                            title: (tooltipItems) => {
                                if (!tooltipItems || !tooltipItems.length) {
                                    return "";
                                }

                                const item = tooltipItems[0];
                                const detail = statusDetails[item.dataIndex];
                                if (detail && detail.statusName) {
                                    return `${detail.status} - ${detail.statusName}`;
                                }
                                return item.label ?? "";
                            },
                            label: context => `Số lượng: ${context.parsed.y ?? context.parsed}`
                        }
                    },
                    statusValueLabels: {
                        display: true,
                        color: "#0d6efd",
                        font: {
                            size: 11,
                            weight: "600",
                            lineHeight: 14
                        }
                    }
                },
                scales: {
                    x: {
                        ticks: {
                            autoSkip: false,
                            maxRotation: 45,
                            minRotation: 0,
                            color: AXIS_TICK_COLOR
                        },
                        grid: {
                            color: GRID_LINE_COLOR
                        }
                    },
                    y: {
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: "Số lượng",
                            color: AXIS_TICK_COLOR
                        },
                        ticks: {
                            color: AXIS_TICK_COLOR
                        },
                        grid: {
                            color: GRID_LINE_COLOR
                        }
                    }
                }
            },
            plugins: [statusValueLabelsPlugin]
        });

        statusChartCanvas.classList.remove("d-none");
        statusChartEmpty.classList.add("d-none");
    };

    const renderBarkingChart = (data, errorMessage) => {
        if (errorMessage) {
            barkingChartEmpty.textContent = errorMessage;
            barkingChartEmpty.classList.remove("alert-info");
            barkingChartEmpty.classList.add("alert-danger");
            barkingChartEmpty.classList.remove("d-none");
            barkingChartCanvas.classList.add("d-none");
            if (barkingChartInstance) {
                barkingChartInstance.destroy();
                barkingChartInstance = null;
            }
            barkingAgingState = [];
            renderBarkingDetails([]);
            if (barkingDownloadButton) {
                barkingDownloadButton.disabled = true;
            }
            return;
        }

        if (!data.length) {
            barkingChartEmpty.textContent = "Không có SN nào ở trạng thái 11.";
            barkingChartEmpty.classList.remove("alert-danger");
            barkingChartEmpty.classList.add("alert-info");
            barkingChartEmpty.classList.remove("d-none");
            barkingChartCanvas.classList.add("d-none");
            if (barkingChartInstance) {
                barkingChartInstance.destroy();
                barkingChartInstance = null;
            }
            barkingAgingState = [];
            renderBarkingDetails([]);
            if (barkingDownloadButton) {
                barkingDownloadButton.disabled = true;
            }
            return;
        }

        const validData = data.filter(item => typeof item.hours === "number" && !Number.isNaN(item.hours));
        const missingCount = data.length - validData.length;

        if (!validData.length) {
            barkingChartEmpty.textContent = missingCount > 0
                ? "Không có dữ liệu giờ hợp lệ cho trạng thái 11. (Một số bản ghi thiếu Apply Time)"
                : "Không có dữ liệu giờ hợp lệ cho trạng thái 11.";
            barkingChartEmpty.classList.remove("alert-danger");
            barkingChartEmpty.classList.add("alert-info");
            barkingChartEmpty.classList.remove("d-none");
            barkingChartCanvas.classList.add("d-none");
            if (barkingChartInstance) {
                barkingChartInstance.destroy();
                barkingChartInstance = null;
            }
            barkingAgingState = [];
            renderBarkingDetails([]);
            if (barkingDownloadButton) {
                barkingDownloadButton.disabled = true;
            }
            return;
        }

        const labels = validData.map(item => item.sn ?? "");
        const values = validData.map(item => item.hours ?? 0);
        const backgroundColors = validData.map(item => {
            const hours = item.hours ?? 0;
            return hours > 12
                ? "rgba(220, 53, 69, 0.7)"
                : "rgba(255, 159, 64, 0.6)";
        });
        const borderColors = validData.map(item => {
            const hours = item.hours ?? 0;
            return hours > 12
                ? "rgba(220, 53, 69, 1)"
                : "rgba(255, 159, 64, 1)";
        });
        const tooltips = validData.map(item => {
            const applyTime = item.applyTime ? new Date(item.applyTime).toLocaleString() : "Không xác định";
            const hours = item.hours ?? 0;
            const minutes = typeof item.minutes === "number" && !Number.isNaN(item.minutes)
                ? item.minutes
                : Math.round(hours * 60);
            return `SN: ${item.sn}\nApply time: ${applyTime}\nSố giờ: ${Math.round(hours * 10) / 10}\n~ ${minutes} phút`;
        });

        if (missingCount > 0) {
            barkingChartEmpty.textContent = `${missingCount} SN không có Apply Time hợp lệ.`;
            barkingChartEmpty.classList.remove("d-none", "alert-danger", "alert-info");
            barkingChartEmpty.classList.add("alert-warning");
        } else {
            barkingChartEmpty.classList.add("d-none");
            barkingChartEmpty.classList.remove("alert-warning", "alert-danger");
            if (!barkingChartEmpty.classList.contains("alert-info")) {
                barkingChartEmpty.classList.add("alert-info");
            }
        }

        barkingAgingState = validData;
        renderBarkingDetails(validData);
        if (barkingDownloadButton) {
            barkingDownloadButton.disabled = false;
        }

        if (barkingChartInstance) {
            barkingChartInstance.destroy();
        }

        barkingChartInstance = new Chart(barkingChartCanvas, {
            type: "bar",
            data: {
                labels,
                datasets: [{
                    label: "Số giờ kể từ khi Barking",
                    data: values,
                    backgroundColor: backgroundColors,
                    borderColor: borderColors,
                    borderWidth: 1
                }]
            },
            options: {
                indexAxis: "y",
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    x: {
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: "Số giờ",
                            color: AXIS_TICK_COLOR
                        },
                        ticks: {
                            color: AXIS_TICK_COLOR
                        },
                        grid: {
                            color: GRID_LINE_COLOR
                        }
                    },
                    y: {
                        ticks: {
                            autoSkip: false,
                            color: AXIS_TICK_COLOR
                        },
                        grid: {
                            color: GRID_LINE_COLOR
                        }
                    }
                },
                plugins: {
                    legend: {
                        display: false,
                        labels: {
                            color: AXIS_TICK_COLOR
                        }
                    },
                    tooltip: {
                        callbacks: {
                            label: context => tooltips[context.dataIndex]
                        }
                    }
                }
            }
        });

        barkingChartCanvas.classList.remove("d-none");
        if (!barkingChartEmpty.classList.contains("alert-warning")) {
            barkingChartEmpty.classList.add("d-none");
        }
    };

    const loadStatusSummary = async () => {
        try {
            const response = await fetch(`${API_BASE_URL}/dashboard/status-summary`);
            const data = await response.json();
            if (!response.ok) {
                throw new Error(data.message || "Không thể tải biểu đồ trạng thái.");
            }
            if (!Array.isArray(data)) {
                throw new Error("Dữ liệu trả về không hợp lệ.");
            }
            renderStatusChart(data);
            return true;
        } catch (error) {
            renderStatusChart([], error.message);
            return false;
        }
    };

    const loadBarkingAging = async () => {
        try {
            const response = await fetch(`${API_BASE_URL}/dashboard/barking-aging`);
            const data = await response.json();
            if (!response.ok) {
                throw new Error(data.message || "Không thể tải dữ liệu Barking.");
            }
            if (!Array.isArray(data)) {
                throw new Error("Dữ liệu trả về không hợp lệ.");
            }
            renderBarkingChart(data);
            return true;
        } catch (error) {
            renderBarkingChart([], error.message);
            return false;
        }
    };

    const refreshDashboard = async () => {
        showStatusLoading();
        showBarkingLoading();

        const [statusSuccess, barkingSuccess] = await Promise.all([
            loadStatusSummary(),
            loadBarkingAging()
        ]);

        if (dashboardLastUpdated) {
            const suffix = (!statusSuccess || !barkingSuccess) ? " (có lỗi)" : "";
            dashboardLastUpdated.textContent = `${new Date().toLocaleString()}${suffix}`;
        }
    };

    statusDownloadSelect?.addEventListener("change", () => {
        if (statusDownloadButton) {
            statusDownloadButton.disabled = !statusDownloadSelect.value;
        }
        setStatusDownloadFeedback("");
    });

    statusDownloadButton?.addEventListener("click", async () => {
        await downloadStatusDetails();
    });

    barkingDownloadButton?.addEventListener("click", () => {
        downloadBarkingDetails();
    });

    refreshDashboardButton?.addEventListener("click", async () => {
        setButtonLoading(refreshDashboardButton, true, "Đang tải...");
        await refreshDashboard();
        setButtonLoading(refreshDashboardButton, false);
    });

    refreshDashboard();
});
