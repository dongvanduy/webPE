// analysis-history.js
const defaultApiBaseUrl = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SwitchRepair";

function getApiBaseUrl() {
    const container = document.querySelector(".data-card[data-api-base]");
    return container?.dataset.apiBase || defaultApiBaseUrl;
}

function normalizeHeader(value) {
    return String(value ?? "").trim().toUpperCase().replace(/\s+/g, "_");
}

function toStr(v) {
    return String(v ?? "").trim();
}

function uniqueSerials(serials) {
    const seen = new Set();
    const out = [];
    for (const sn of serials) {
        const key = toStr(sn).toUpperCase();
        if (!key) continue;
        if (seen.has(key)) continue;
        seen.add(key);
        out.push(toStr(sn));
    }
    return out;
}

function parseSerials(text) {
    return uniqueSerials(
        (text || "")
            .split(/\r?\n|,|;|\t/)
            .map((x) => x.trim())
            .filter(Boolean)
    );
}

function buildItemsFromInput() {
    const serialNumbers = parseSerials(document.getElementById("sn-input")?.value);

    const enterErrorCode = toStr(document.getElementById("enter-error-code-input")?.value);
    const failStation = toStr(document.getElementById("fail-station-input")?.value);
    const fa = toStr(document.getElementById("fa-input")?.value);
    const status = toStr(document.getElementById("status-input")?.value);
    const ownerPE = toStr(document.getElementById("pe-owner-input")?.value);
    const customer = toStr(document.getElementById("customer-input")?.value);

    return serialNumbers.map((sn) => ({
        serialNumber: sn,
        enterErrorCode,
        fa,
        ownerPE,
        customer,
        failStation,
        status,
    }));
}

function buildItemsFromSheet(sheetData) {
    const rows = Array.isArray(sheetData) ? sheetData : [];
    const items = [];

    for (const row of rows) {
        const normalized = {};
        for (const k of Object.keys(row || {})) normalized[normalizeHeader(k)] = row[k];

        const sn = normalized.SERIAL_NUMBER || normalized.SN || normalized.SERIAL;
        if (!toStr(sn)) continue;

        items.push({
            serialNumber: toStr(sn),
            enterErrorCode: toStr(normalized.ENTERERRORCODE || normalized.ENTER_ERROR_CODE || normalized.ENTER_ERRORCODE),
            fa: toStr(normalized.FA),
            ownerPE: toStr(normalized.OWNERPE || normalized.OWNER_PE || normalized.PE_OWNER),
            customer: toStr(normalized.CUSTOMER),
            failStation: toStr(normalized.FAILSTATION || normalized.FAIL_STATION || normalized.STATION),
            status: toStr(normalized.STATUS),
        });
    }

    // remove duplicate SN keep first
    const dedup = [];
    const seen = new Set();
    for (const it of items) {
        const key = it.serialNumber.toUpperCase();
        if (seen.has(key)) continue;
        seen.add(key);
        dedup.push(it);
    }
    return dedup;
}

function showAlert(containerId, type, message) {
    const el = document.getElementById(containerId);
    if (!el) return;
    el.innerHTML = message
        ? `<div class="alert ${type} mb-2">${message}</div>`
        : "";
}

let inputTable = null;
let lastInputSerialSet = new Set();

function initInputTable() {
    const tableEl = $("#analysis-history-table");
    if (!tableEl.length) return;

    if ($.fn.DataTable.isDataTable(tableEl)) {
        inputTable = tableEl.DataTable();
        return;
    }

    inputTable = tableEl.DataTable({
        data: [],
        columns: [
            { data: null, title: "#", width: "40px", render: (d, t, r, meta) => meta.row + 1 },
            { data: "serialNumber", title: "SERIAL_NUMBER" },
            { data: "enterErrorCode", title: "ENTER_ERROR_CODE" },
            { data: "fa", title: "FA" },
            { data: "ownerPE", title: "OWNER_PE" },
            { data: "customer", title: "CUSTOMER" },
            { data: "failStation", title: "FAIL_STATION" },
            { data: "status", title: "STATUS" },
            { data: "errorCode", title: "ERROR_CODE (CURRENT)" },
            { data: "descCode", title: "DESC_CODE" },
            { data: "wipGroup", title: "WIP_GROUP" },
            { data: "modelName", title: "MODEL_NAME" },
            { data: "errorDesc", title: "ERROR_DESC" },
            {
                data: "timeUpdate",
                title: "TIME_UPDATE",
                render: (v) => (v ? toStr(v) : ""),
            },
        ],
        pageLength: 25,
        autoWidth: false,
        scrollX: true,
        order: [],
        rowCallback: function (row, data) {
            const sn = toStr(data.serialNumber).toUpperCase();
            if (lastInputSerialSet.has(sn)) {
                row.style.background = "#fff8d6";
            }
        },
    });
}

async function postJson(url, payload) {
    const res = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
    });

    const text = await res.text();
    let json = null;
    try {
        json = text ? JSON.parse(text) : null;
    } catch {
        json = null;
    }
    return { ok: res.ok, status: res.status, json, raw: text };
}

function normalizeApiRow(x) {
    // chấp nhận nhiều kiểu key từ backend
    const o = x || {};
    return {
        serialNumber: toStr(o.serialNumber ?? o.SerialNumber),
        enterErrorCode: toStr(o.enterErrorCode ?? o.EnterErrorCode),
        fa: toStr(o.fa ?? o.Fa),
        ownerPE: toStr(o.ownerPE ?? o.OwnerPE ?? o.Owner),
        customer: toStr(o.customer ?? o.Customer ?? o.CustomerOwner),
        failStation: toStr(o.failStation ?? o.FailStation),
        status: toStr(o.status ?? o.Status),
        errorCode: toStr(o.errorCode ?? o.ErrorCode),
        descCode: toStr(o.descCode ?? o.DescCode),
        wipGroup: toStr(o.wipGroup ?? o.WipGroup),
        modelName: toStr(o.modelName ?? o.ModelName),
        errorDesc: toStr(o.errorDesc ?? o.ErrorDesc),
        timeUpdate: toStr(o.timeUpdate ?? o.TimeUpdate),
    };
}

function mergePreferApi(apiRows, inputItems) {
    // bảo đảm luôn hiển thị đúng list SN người dùng vừa nhập
    const apiMap = new Map();
    for (const r of apiRows) {
        apiMap.set(r.serialNumber.toUpperCase(), r);
    }

    return inputItems.map((it) => {
        const key = it.serialNumber.toUpperCase();
        const api = apiMap.get(key);

        // nếu api trả về thì ưu tiên api cho các trường oracle, còn user input giữ theo it
        return {
            serialNumber: it.serialNumber,
            enterErrorCode: it.enterErrorCode,
            fa: it.fa,
            ownerPE: it.ownerPE,
            customer: it.customer,
            failStation: it.failStation,
            status: it.status,

            // oracle/from server (nếu có)
            errorCode: api?.errorCode || "",
            descCode: api?.descCode || "",
            wipGroup: api?.wipGroup || "",
            modelName: api?.modelName || "",
            errorDesc: api?.errorDesc || "",
            timeUpdate: api?.timeUpdate || "",
        };
    });
}

async function submitInput(items) {
    showAlert("analysis-history-alert", "alert-info", "Đang xử lý...");

    if (!items || items.length === 0) {
        showAlert("analysis-history-alert", "alert-warning", "Vui lòng nhập SerialNumber.");
        return;
    }

    lastInputSerialSet = new Set(items.map((x) => x.serialNumber.toUpperCase()));

    const base = getApiBaseUrl();
    const url = `${base}/sw-input`;

    const { ok, status, json, raw } = await postJson(url, { items });

    if (!ok) {
        const msg = json?.message || `Lỗi API (${status}).`;
        showAlert("analysis-history-alert", "alert-danger", msg);

        // vẫn show SN vừa nhập
        const fallback = items.map((it) => ({
            serialNumber: it.serialNumber,
            enterErrorCode: it.enterErrorCode,
            fa: it.fa,
            ownerPE: it.ownerPE,
            customer: it.customer,
            failStation: it.failStation,
            status: it.status,
            errorCode: "",
            descCode: "",
            wipGroup: "",
            modelName: "",
            errorDesc: "",
            timeUpdate: "",
        }));
        inputTable.clear().rows.add(fallback).draw();
        return;
    }

    const apiData = Array.isArray(json?.data) ? json.data.map(normalizeApiRow) : [];
    const merged = mergePreferApi(apiData, items);

    showAlert("analysis-history-alert", "alert-success", json?.message || "Thêm thành công.");
    inputTable.clear().rows.add(merged).draw();
}

document.addEventListener("DOMContentLoaded", () => {
    initInputTable();

    document.getElementById("input-btn")?.addEventListener("click", () => {
        const items = buildItemsFromInput();
        submitInput(items);
    });

    document.getElementById("upload-file-btn")?.addEventListener("click", () => {
        const fileInput = document.createElement("input");
        fileInput.type = "file";
        fileInput.accept = ".xlsx,.xls";

        fileInput.addEventListener("change", () => {
            if (!fileInput.files?.length) return;

            const file = fileInput.files[0];
            const reader = new FileReader();

            reader.onload = (event) => {
                try {
                    const data = new Uint8Array(event.target.result);
                    const workbook = XLSX.read(data, { type: "array" });
                    const sheetName = workbook.SheetNames[0];
                    const sheet = workbook.Sheets[sheetName];
                    const sheetData = XLSX.utils.sheet_to_json(sheet, { defval: "" });
                    const items = buildItemsFromSheet(sheetData);
                    submitInput(items);
                } catch (e) {
                    showAlert("analysis-history-alert", "alert-danger", "Không thể đọc file Excel.");
                }
            };

            reader.onerror = () => {
                showAlert("analysis-history-alert", "alert-danger", "Không thể đọc file Excel.");
            };

            reader.readAsArrayBuffer(file);
        });

        fileInput.click();
    });
});
