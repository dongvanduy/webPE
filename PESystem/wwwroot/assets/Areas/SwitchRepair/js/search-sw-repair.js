// search-export.js
const defaultApiBaseUrl = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SwitchRepair";

function getApiBaseUrl() {
    const container = document.querySelector(".data-card[data-api-base]");
    return container?.dataset.apiBase || defaultApiBaseUrl;
}

function toStr(v) {
    return String(v ?? "").trim();
}

function parseSerials(text) {
    const raw = (text || "")
        .split(/\r?\n|,|;|\t/)
        .map((x) => x.trim())
        .filter(Boolean);

    const seen = new Set();
    const out = [];
    for (const sn of raw) {
        const key = sn.toUpperCase();
        if (seen.has(key)) continue;
        seen.add(key);
        out.push(sn);
    }
    return out;
}

function showAlert(id, type, msg) {
    const el = document.getElementById(id);
    if (!el) return;
    el.innerHTML = msg ? `<div class="alert ${type} mb-2">${msg}</div>` : "";
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
    return { ok: res.ok, status: res.status, json };
}

function normalizeApiRow(x) {
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

let searchTable = null;

function initSearchTable() {
    const tableEl = $("#search-result-table");
    if (!tableEl.length) return;

    if ($.fn.DataTable.isDataTable(tableEl)) {
        searchTable = tableEl.DataTable();
        return;
    }

    searchTable = tableEl.DataTable({
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
            { data: "timeUpdate", title: "TIME_UPDATE" },
        ],
        pageLength: 25,
        autoWidth: false,
        scrollX: true,
        dom: "Bfrtip",
        buttons: [
            {
                extend: "excelHtml5",
                title: `SwitchRepair_Search_${new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-")}`,
                exportOptions: { columns: ":visible" },
            },
            "copyHtml5",
            "csvHtml5",
            "print",
            "colvis",
        ],
    });
}

async function doSearch() {
    showAlert("search-alert", "alert-info", "Đang tìm kiếm...");

    const serialNumbers = parseSerials(document.getElementById("search-sn-input")?.value);

    if (!serialNumbers.length) {
        showAlert("search-alert", "alert-warning", "Vui lòng nhập SerialNumber.");
        return;
    }

    const base = getApiBaseUrl();
    const url = `${base}/sw-search`;

    const { ok, status, json } = await postJson(url, { serialNumbers });

    if (!ok) {
        showAlert("search-alert", "alert-danger", json?.message || `Lỗi API (${status}).`);
        searchTable.clear().draw();
        return;
    }

    const rows = Array.isArray(json?.data) ? json.data.map(normalizeApiRow) : [];
    showAlert("search-alert", "alert-success", json?.message || `Tìm thấy ${rows.length} SN.`);
    searchTable.clear().rows.add(rows).draw();
}

document.addEventListener("DOMContentLoaded", () => {
    initSearchTable();

    document.getElementById("search-btn")?.addEventListener("click", doSearch);
});
