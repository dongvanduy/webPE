// ================== FETCH API ==================
async function fetchData(url, payload) {
    try {
        const response = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });
        if (!response.ok) throw new Error(response.statusText);
        return await response.json();
    } catch (err) {
        console.error("API Error:", url, err);
        return { success: false, data: [] };
    }
}

// ================== TABLE CONFIG ==================
const TABLE_CONFIG = {
    xoaR: {
        headers: ["OWNER", "SERIAL_NUMBER", "MO_NUMBER", "MODEL_NAME", "WIP_GROUP", "ERROR_FLAG",
            "TEST_GROUP", "TEST_CODE", "ERROR_DESC", "REPAIRER", "REPAIR_TIME", "REASON_CODE", "ERROR_ITEM"],
        fields: ["owner", "seriaL_NUMBER", "mO_NUMBER", "modeL_NAME", "wiP_GROUP", "erroR_FLAG",
            "tesT_GROUP", "tesT_CODE", "datA1", "repairer", "repaiR_TIME", "reasoN_CODE", "erroR_ITEM_CODE"]
    },
    XacNhanPhanTich: {
        headers: ["OWNER", "SERIAL_NUMBER", "MO_NUMBER", "MODEL_NAME", "WIP_GROUP", "TEST_GROUP",
            "TEST_CODE", "ERROR_DESC", "TYPE", "TIME_SAVE", "CURRENT_STATUS", "NEXT_STATUS", "REMARK"],
        fields: ["owner", "seriaL_NUMBER", "mO_NUMBER", "modeL_NAME", "wiP_GROUP", "tesT_GROUP",
            "tesT_CODE", "datA1", "type", "timE_SAVE", "currenT_STATUS", "nexT_STATUS", "remark"]
    }
};

// ================== MODAL (SerialNumberModal) ==================
const SerialNumberModal = (() => {
    let modalInstance = null;
    let dataTableInstance = null;

    function ensureModal() {
        if (!modalInstance) {
            const el = document.getElementById("serialNumberModal");
            if (el) modalInstance = new bootstrap.Modal(el);
        }
        return modalInstance;
    }

    function ensureTable() {
        if (dataTableInstance) return dataTableInstance;
        dataTableInstance = $("#serialNumberTable").DataTable({
            data: [],
            columns: [
                { title: "SERIAL_NUMBER", data: "serial" },
                { title: "MODEL_NAME", data: "modeL_NAME" },
                { title: "WIP_GROUP", data: "wiP_GROUP" },
                { title: "ERROR_FLAG", data: "erroR_FLAG" },
                { title: "MO_NUMBER", data: "mO_NUMBER" },
                { title: "TEST_GROUP", data: "tesT_GROUP" },
                { title: "ERROR_CODE", data: "tesT_CODE" },
                { title: "ERROR_DESC", data: "datA1" },
                { title: "ERROR_ITEM", data: "erroR_ITEM_CODE" },
                { title: "REPAIRER", data: "repairer" },
                { title: "REPAIR_TIME", data: "repaiR_TIME" }
            ],
            ordering: false,
            pageLength: 10,
            language: {
                search: "Tìm kiếm:",
                info: "Hiển thị _START_ - _END_ / _TOTAL_ dòng",
                paginate: { previous: "Trước", next: "Sau" }
            }
        });
        return dataTableInstance;
    }

    return {
        show(title, data) {
            document.getElementById("serialNumberModalLabel").innerText = title;
            const tbl = ensureTable();
            tbl.clear().rows.add(data).draw();
            ensureModal()?.show();
        }
    };
})();

// ================== UTILITIES ==================
function parseDateTime(value) {
    const d = new Date(value);
    return isNaN(d) ? null : d;
}

function escapeHtml(value) {
    if (value === null || value === undefined) return "";
    return String(value)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}

function formatDateTimeDisplay(value) {
    const d = parseDateTime(value);
    return d ? d.toLocaleString("vi-VN") : "—";
}
function formatHourLabel(date) {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')} ${String(date.getHours()).padStart(2, '0')}:00`;
}
function normalizeSerialRecord(item) {
    return { ...item, serial: item.seriaL_NUMBER || item.serial || "" };
}

function normalizeVietnameseText(value) {
    if (value === null || value === undefined) return "";
    return String(value).normalize("NFD").replace(/[\u0300-\u036f]/g, "");
}

function isOkStatus(status) {
    if (status === null || status === undefined) return false;
    const normalized = normalizeVietnameseText(status).trim().toUpperCase();
    return normalized === "CHO TRA";
}
function getShiftTimeRange() {
    const now = new Date();
    let s, e;
    const h = now.getHours(), m = now.getMinutes();

    if ((h > 7 || (h === 7 && m >= 30)) && (h < 19 || (h === 19 && m < 30))) {
        s = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 7, 30);
        e = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 19, 30);
    } else if (h >= 19 || (h === 19 && m >= 30)) {
        s = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 19, 30);
        e = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1, 7, 30);
    } else {
        s = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1, 19, 30);
        e = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 7, 30);
    }

    const fmt = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}T${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
    return { startDate: fmt(s), endDate: fmt(e) };
}

// ================== FETCH FULLNAME ==================
async function fetchOwnerFullNamesBatch(ownerCodes) {
    if (!ownerCodes.length) return {};
    try {
        const res = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/get-fullname-batch", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(ownerCodes)
        });
        const json = await res.json();
        const data = json.data || {};
        // chỉ giữ lại những owner có fullname thực sự
        const valid = {};
        for (const [k, v] of Object.entries(data)) {
            if (v && v.trim() !== "") valid[k] = v;
        }
        return valid;
    } catch (e) {
        console.error("Lỗi lấy fullname:", e);
        return {};
    }
}

// ================== TABLE RENDER ==================
function renderTable(containerId, data, config, ownerFullNames) {
    const el = document.getElementById(containerId);
    if (!el) return;

    const filtered = (data || []).filter(i => i?.owner && ownerFullNames[i.owner]);
    if (!filtered.length) {
        el.innerHTML = "<div class='text-muted'>Không có dữ liệu hợp lệ.</div>";
        return;
    }

    const html = `
        <table border="1" style="width:100%;border-collapse:collapse;text-align:center;">
            <thead><tr>${config.headers.map(h => `<th>${h}</th>`).join('')}</tr></thead>
            <tbody>${filtered.map(item => `
                <tr>${config.fields.map(f => {
        let v = item[f] || "";
        if (f === "owner") v = ownerFullNames[item.owner] || "";
        return `<td title="${v}">${v}</td>`;
    }).join('')}</tr>`).join('')}
            </tbody>
        </table>`;
    el.innerHTML = html;
}

// ================== OWNER CHART (ECharts) ==================
function aggregateOwnerCounts(records, ownerFullNames, countField) {
    const counts = {};
    (records || []).forEach(item => {
        if (!item) return;
        const ownerCode = (item.owner || "").trim();
        if (!ownerCode || !ownerFullNames[ownerCode]) return;

        let increment = 1;
        if (countField && typeof item[countField] === "number" && !Number.isNaN(item[countField])) {
            increment = item[countField];
        }

        counts[ownerCode] = (counts[ownerCode] || 0) + increment;
    });
    return counts;
}

function buildOwnerLabel(ownerCode, ownerFullNames) {
    const display = ownerFullNames[ownerCode];
    if (!display) return "";
    return display !== ownerCode ? `${display} (${ownerCode})` : display;
}

function renderOwnerChart(containerId, data, title, ownerFullNames, options = {}) {
    const {
        primaryLabel = "Sản lượng",
        primaryCountField = null,
        secondaryData = null,
        secondaryLabel = "Sản lượng CHECK_LIST",
        secondaryCountField = "count"
    } = options;

    const el = document.getElementById(containerId);
    if (!el) return;

    const primaryCounts = aggregateOwnerCounts(data, ownerFullNames, primaryCountField);
    const hasSecondary = Array.isArray(secondaryData);
    const secondaryCounts = hasSecondary
        ? aggregateOwnerCounts(secondaryData, ownerFullNames, secondaryCountField)
        : {};

    const ownerCodes = new Set([
        ...Object.keys(primaryCounts),
        ...(hasSecondary ? Object.keys(secondaryCounts) : [])
    ]);

    const combined = [...ownerCodes]
        .map(code => ({
            code,
            label: buildOwnerLabel(code, ownerFullNames),
            primary: primaryCounts[code] || 0,
            secondary: hasSecondary ? (secondaryCounts[code] || 0) : 0
        }))
        .filter(item => item.label && (item.primary > 0 || item.secondary > 0));

    if (!combined.length) {
        el.innerHTML = "<div class='text-muted'>Không có dữ liệu.</div>";
        return;
    }

    combined.sort((a, b) => (b.primary + b.secondary) - (a.primary + a.secondary));

    const categories = combined.map(item => item.label);
    const primaryValues = combined.map(item => item.primary);
    const secondaryValues = combined.map(item => item.secondary);

    const series = [{
        name: primaryLabel,
        data: primaryValues,
        type: "bar",
        barWidth: hasSecondary ? 18 : 30,
        itemStyle: {
            borderRadius: [0, 0, 0, 0],
            color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                { offset: 0, color: "#42a5f5" },
                { offset: 1, color: "#1e88e5" }
            ])
        },
        label: { show: true, position: "top", color: "#2c3e50", fontWeight: "bold" }
    }];

    if (hasSecondary) {
        series.push({
            name: secondaryLabel,
            data: secondaryValues,
            type: "bar",
            barWidth: 18,
            itemStyle: {
                borderRadius: [0, 0, 0, 0],
                color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                    { offset: 0, color: "#FFD54F" },
                    { offset: 1, color: "#FFA000" }
                ])
            },
            label: { show: true, position: "top", color: "#8C6D1F", fontWeight: "bold" }
        });
    }

    const chart = echarts.init(el);
    chart.setOption({
        tooltip: { trigger: "axis", axisPointer: { type: "shadow" } },
        legend: hasSecondary ? { data: [primaryLabel, secondaryLabel], top: 0 } : undefined,
        grid: { top: hasSecondary ? 60 : 30, left: 50, right: 30, bottom: 80 },
        xAxis: {
            type: "category",
            data: categories,
            axisLabel: {
                rotate: 25,
                color: "#333",
                fontSize: 10,
                formatter: value => value.length > 18 ? `${value.slice(0, 18)}…` : value
            }
        },
        yAxis: { type: "value", name: "Số lượng" },
        series
    });
    window.addEventListener("resize", () => chart.resize());
}

function renderViReConfirmChart(chartId, ownerChartData) {
    const el = document.getElementById(chartId);
    if (!el) return;

    const filtered = (ownerChartData || []).filter(item => (item.okCount + item.ngCount) > 0);
    if (!filtered.length) {
        el.innerHTML = "<div class='text-muted vi-re-no-data'>Không có dữ liệu hợp lệ để hiển thị biểu đồ.</div>";
        return;
    }

    const categories = filtered.map(item => item.label);
    const okValues = filtered.map(item => item.okCount);
    const ngValues = filtered.map(item => item.ngCount);

    let chart = echarts.getInstanceByDom(el);
    if (!chart) {
        chart = echarts.init(el);
    }

    chart.setOption({
        tooltip: {
            trigger: "axis",
            axisPointer: { type: "shadow" }
        },
        legend: {
            data: ["OK", "NG"],
            top: 0
        },
        grid: { top: 50, left: 50, right: 30, bottom: 80 },
        xAxis: {
            type: "category",
            data: categories,
            axisLabel: {
                interval: 0,
                rotate: 25,
                color: "#333",
                fontSize: 10,
                formatter: value => value.length > 18 ? `${value.slice(0, 18)}…` : value
            }
        },
        yAxis: { type: "value", name: "Số lượng" },
        series: [
            {
                name: "OK",
                type: "bar",
                data: okValues,
                barWidth: 18,
                itemStyle: { color: "#4CAF50" },
                label: { show: true, position: "top", color: "#2e7d32", fontWeight: "bold" }
            },
            {
                name: "NG",
                type: "bar",
                data: ngValues,
                barWidth: 18,
                barGap: 0.2,
                itemStyle: { color: "#F44336" },
                label: { show: true, position: "top", color: "#c62828", fontWeight: "bold" }
            }
        ]
    });

    if (!el.__resizeHandler) {
        el.__resizeHandler = () => {
            const instance = echarts.getInstanceByDom(el);
            instance && instance.resize();
        };
        window.addEventListener("resize", el.__resizeHandler);
    }
}

function renderViReConfirmCard(containerId, response, ownerFullNames, options = {}) {
    const container = document.getElementById(containerId);
    if (!container) return;

    const titleText = options.title || "Sản lượng VI-RE";
    const noDataMessage = options.noDataMessage || "Không có dữ liệu trong khoảng thời gian này.";
    const errorMessage = options.errorMessage || "Không thể tải dữ liệu.";
    const emptyMessage = options.emptyMessage || "Không có dữ liệu hợp lệ.";
    const titleHtml = `<div class="panel-title">${escapeHtml(titleText)}</div>`;

    if (!response || response.success === false) {
        container.innerHTML = `${titleHtml}<div class='text-danger'>${escapeHtml(errorMessage)}</div>`;
        return;
    }

    const records = Array.isArray(response?.data) ? response.data : [];

    if (!records.length) {
        container.innerHTML = `${titleHtml}<div class='text-muted'>${escapeHtml(noDataMessage)}</div>`;
        return;
    }

    const ownerData = records.map(item => {
        if (!item) return null;
        const ownerCode = ((item.owner || "").trim()) || "UNKNOWN";
        const displayName = ownerCode === "UNKNOWN"
            ? "Chưa xác định"
            : (ownerFullNames?.[ownerCode] || ownerCode);
        const details = Array.isArray(item.details) ? item.details : [];
        const okCount = typeof item.okCount === "number"
            ? item.okCount
            : details.filter(detail => isOkStatus(detail.status)).length;
        const totalCount = typeof item.count === "number" ? item.count : details.length;
        const ngCount = typeof item.ngCount === "number"
            ? item.ngCount
            : Math.max(totalCount - okCount, 0);
        const ownerMeta = ownerCode !== "UNKNOWN" && displayName !== ownerCode
            ? ` (${ownerCode})`
            : "";
        const chartLabel = `${displayName}${ownerMeta}`;
        const ownerLabelHtml = ownerCode !== "UNKNOWN" && displayName !== ownerCode
            ? `${escapeHtml(displayName)} <span class="text-muted">(${escapeHtml(ownerCode)})</span>`
            : escapeHtml(displayName);

        return {
            ownerCode,
            ownerLabelHtml,
            chartLabel,
            count: totalCount,
            okCount,
            ngCount,
            details
        };
    }).filter(Boolean);

    const totalCount = typeof response.totalCount === "number"
        ? response.totalCount
        : ownerData.reduce((sum, item) => sum + (item?.count || 0), 0);
    const totalOk = typeof response.totalOk === "number"
        ? response.totalOk
        : ownerData.reduce((sum, item) => sum + (item?.okCount || 0), 0);
    const totalNg = typeof response.totalNg === "number"
        ? response.totalNg
        : ownerData.reduce((sum, item) => sum + (item?.ngCount || 0), 0);

    const itemsHtml = ownerData.map(item => {
        const rows = item.details.map(detail => `
                <tr>
                    <td>${escapeHtml(detail.serialNumber)}</td>
                    <td>${escapeHtml(detail.moNumber)}</td>
                    <td>${escapeHtml(detail.modelName)}</td>
                    <td>${escapeHtml(detail.testGroup)}</td>
                    <td>${escapeHtml(detail.testCode)}</td>
                    <td>${escapeHtml(detail.tester ?? "")}</td>
                    <td>${escapeHtml(detail.errorDesc ?? "")}</td>
                    <td>${escapeHtml(detail.status ?? "")}</td>
                    <td>${escapeHtml(detail.preStatus ?? "")}</td>
                    <td>${escapeHtml(formatDateTimeDisplay(detail.testTime))}</td>
                    <td>${escapeHtml(formatDateTimeDisplay(detail.date3))}</td>
                </tr>`).join("");
        const tableRows = rows || "<tr><td colspan=\"11\" class=\"text-center text-muted\">Không có dữ liệu chi tiết.</td></tr>";

        return `
            <details class="vi-re-owner">
                <summary>
                    <span>${item.ownerLabelHtml}</span>
                    <span class="vi-re-owner-count-group">
                        <span class="vi-re-owner-status ok">OK: ${item.okCount}</span>
                        <span class="vi-re-owner-status ng">NG: ${item.ngCount}</span>
                        <span class="vi-re-owner-count">${item.count}</span>
                    </span>
                </summary>
                <div class="vi-re-owner-table-wrapper">
                    <table class="vi-re-owner-table">
                        <thead>
                            <tr>
                                <th>Serial Number</th>
                                <th>MO Number</th>
                                <th>Model Name</th>
                                <th>Test Group</th>
                                <th>Test Code</th>
                                <th>Tester</th>
                                <th>Error Desc</th>
                                <th>Status</th>
                                <th>Previous Status</th>
                                <th>Test Time</th>
                                <th>Confirm Time</th>
                            </tr>
                        </thead>
                        <tbody>${tableRows}</tbody>
                    </table>
                </div>
            </details>`;
    }).filter(Boolean).join("");

    if (!itemsHtml) {
        container.innerHTML = `${titleHtml}<div class='text-muted'>${escapeHtml(emptyMessage)}</div>`;
        return;
    }

    const chartId = `${containerId}-chart`;

    container.innerHTML = `${titleHtml}
        <div class="vi-re-summary">
            <div class="vi-re-total">Tổng sản lượng: <strong>${totalCount}</strong>
                <span class="vi-re-total-split">(OK: <span class="ok">${totalOk}</span> / NG: <span class="ng">${totalNg}</span>)</span>
            </div>
            <div id="${chartId}" class="echart vi-re-chart"></div>
            ${itemsHtml}
        </div>`;

    renderViReConfirmChart(chartId, ownerData.map(item => ({
        label: item.chartLabel,
        okCount: item.okCount,
        ngCount: item.ngCount
    })));
}

// ================== HOURLY CHART (ECharts) ==================
function renderHourlyChart(containerId, data) {
    const el = document.getElementById(containerId);
    if (!el) return;

    const { startDate, endDate } = getShiftTimeRange();
    const s = new Date(startDate), e = new Date(endDate);
    const filtered = (data || []).filter(i => {
        const d = parseDateTime(i.repaiR_TIME);
        return d && d >= s && d <= e;
    });
    if (!filtered.length) {
        el.innerHTML = "<div class='text-muted'>Không có dữ liệu trong ca làm việc hiện tại.</div>";
        return;
    }

    const agg = {};
    filtered.forEach(i => {
        const d = parseDateTime(i.repaiR_TIME);
        const k = new Date(d.getFullYear(), d.getMonth(), d.getDate(), d.getHours()).getTime();
        if (!agg[k]) agg[k] = { label: formatHourLabel(d), count: 0, records: [] };
        agg[k].count++;
        agg[k].records.push(normalizeSerialRecord(i));
    });

    const keys = Object.keys(agg).map(Number).sort((a, b) => a - b);
    const categories = keys.map(k => agg[k].label);
    const values = keys.map(k => agg[k].count);
    const recordsList = keys.map(k => agg[k].records);

    const chart = echarts.init(el);
    chart.setOption({
        tooltip: { trigger: "axis", formatter: p => `${p[0].name}<br/><b>${p[0].value}</b> SN` },
        grid: { top: 30, left: 50, right: 30, bottom: 60 },
        xAxis: { type: "category", data: categories, axisLabel: { rotate: 35, color: "#333", fontSize: 10 } },
        yAxis: { type: "value", name: "Số lượng" },
        series: [{
            data: values,
            type: "bar",
            barWidth: 30,
            itemStyle: {
                borderRadius: [0, 0, 0, 0],
                color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                    { offset: 0, color: "#FF9800" },
                    { offset: 1, color: "#F57C00" }
                ])
            },
            label: { show: true, position: "top", color: "#2c3e50", fontWeight: "bold" }
        }]
    });
    chart.on("click", p => SerialNumberModal.show(p.name, recordsList[p.dataIndex]));
    window.addEventListener("resize", () => chart.resize());
}

// ================== MAIN LOADER ==================
async function loadData(startDate, endDate) {
    try {
        const [repair, confirm, viReConfirm, checkListConfirm] = await Promise.all([
            fetchData("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/get-repair-owner-data", { startDate, endDate }),
            fetchData("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/get-confirm-save-data", { startDate, endDate }),
            fetchData("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/get-vi-re-confirm-data", { startDate, endDate }),
            fetchData("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/get-check-list-confirm-data", { startDate, endDate })
        ]);

        const allOwners = new Set();
        if (Array.isArray(repair?.data)) {
            repair.data.forEach(item => {
                if (item?.owner) allOwners.add(item.owner);
            });
        }
        if (Array.isArray(confirm?.data)) {
            confirm.data.forEach(item => {
                if (item?.owner) allOwners.add(item.owner);
            });
        }
        if (Array.isArray(viReConfirm?.data)) {
            viReConfirm.data.forEach(item => {
                if (item?.owner && item.owner !== "UNKNOWN") {
                    allOwners.add(item.owner);
                }
            });
        }
        if (Array.isArray(checkListConfirm?.data)) {
            checkListConfirm.data.forEach(item => {
                if (item?.owner && item.owner !== "UNKNOWN") {
                    allOwners.add(item.owner);
                }
            });
        }

        const ownerFullNames = await fetchOwnerFullNamesBatch([...allOwners]);

        const checkListData = checkListConfirm?.success ? (checkListConfirm.data || []) : null;

        if (repair?.success) {
            renderTable("xoaR", repair.data, TABLE_CONFIG.xoaR, ownerFullNames);
            renderOwnerChart("repairChart", repair.data, "Sản lượng xóa R theo Owner", ownerFullNames, {
                primaryLabel: "Sản lượng xóa R",
                secondaryLabel: "Sản lượng CHECK_LIST",
                secondaryData: checkListData,
                secondaryCountField: "count"
            });
            renderHourlyChart("repairHourlyChart", repair.data);
        }
        if (confirm?.success) {
            renderTable("XacNhanPhanTich", confirm.data, TABLE_CONFIG.XacNhanPhanTich, ownerFullNames);
            renderOwnerChart("confirmChart", confirm.data, "Sản lượng xác nhận bản lỗi theo Owner", ownerFullNames, {
                primaryLabel: "Sản lượng"
            });
        }

        renderViReConfirmCard("viReConfirmCard", viReConfirm, ownerFullNames, {
            title: "Sản lượng VI-RE Confirm"
        });
    } catch (e) {
        console.error("Lỗi xử lý:", e);
        renderViReConfirmCard("viReConfirmCard", { success: false }, {}, {
            title: "Sản lượng VI-RE Confirm"
        });
    }
}

// ================== INIT ==================
document.addEventListener("DOMContentLoaded", () => {
    const { startDate, endDate } = getShiftTimeRange();
    document.getElementById("startDate").value = startDate;
    document.getElementById("endDate").value = endDate;
    loadData(startDate, endDate);
});

document.getElementById("searchBtn").addEventListener("click", () => {
    const s = document.getElementById("startDate").value;
    const e = document.getElementById("endDate").value;
    if (!s || !e) return alert("Vui lòng chọn thời gian!");
    loadData(s, e);
});

document.getElementById("PdStock-ExportExcel-btn").addEventListener("click", () => {
    const wb = XLSX.utils.book_new();
    ["xoaR", "XacNhanPhanTich"].forEach(id => {
        const t = document.getElementById(id)?.querySelector("table");
        if (t) {
            const ws = XLSX.utils.table_to_sheet(t);
            XLSX.utils.book_append_sheet(wb, ws, id);
        }
    });
    XLSX.writeFile(wb, "ExportedData.xlsx");
});
