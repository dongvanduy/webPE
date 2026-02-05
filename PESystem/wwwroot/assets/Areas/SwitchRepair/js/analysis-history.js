const analysisApiBaseUrl = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Switch";

function normalizeHeader(value) {
    return String(value ?? "")
        .trim()
        .toUpperCase()
        .replace(/\s+/g, "_");
}

function htmlEscape(value) {
    return String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

function buildItemsFromInput() {
    const snInput = document.getElementById("analysis-sn-input").value
        .split(/\r?\n|,/)
        .map(value => value.trim())
        .filter(Boolean);

    const failStation = document.getElementById("analysis-fail-station").value.trim();
    const errorCode = document.getElementById("analysis-error-code").value.trim();
    const fa = document.getElementById("analysis-fa").value.trim();
    const status = document.getElementById("analysis-status").value.trim();
    const owner = document.getElementById("analysis-owner").value.trim();
    const customerOwner = document.getElementById("analysis-customer-owner").value.trim();

    return snInput.map(sn => ({
        serialNumber: sn,
        failStation,
        errorCode,
        fa,
        status,
        owner,
        customerOwner
    }));
}

function buildItemsFromSheet(sheetData) {
    return sheetData
        .map(row => {
            const normalizedRow = {};
            Object.keys(row || {}).forEach(key => {
                normalizedRow[normalizeHeader(key)] = row[key];
            });

            const serialNumber = normalizedRow.SERIAL_NUMBER || normalizedRow.SN || normalizedRow.SERIAL;
            if (!serialNumber) {
                return null;
            }

            return {
                serialNumber: String(serialNumber).trim(),
                failStation: normalizedRow.FAIL_STATION ? String(normalizedRow.FAIL_STATION).trim() : "",
                errorCode: normalizedRow.ERROR_CODE ? String(normalizedRow.ERROR_CODE).trim() : "",
                fa: normalizedRow.FA ? String(normalizedRow.FA).trim() : "",
                status: normalizedRow.STATUS ? String(normalizedRow.STATUS).trim() : "",
                owner: normalizedRow.OWNER ? String(normalizedRow.OWNER).trim() : "",
                customerOwner: normalizedRow.CUSTOMER_OWNER ? String(normalizedRow.CUSTOMER_OWNER).trim() : ""
            };
        })
        .filter(Boolean);
}

function renderResult(data, missingSerials) {
    const resultDiv = document.getElementById("analysis-history-result");
    resultDiv.classList.remove("hidden");

    const header = `
        <tr>
            <th>#</th>
            <th>SERIAL_NUMBER</th>
            <th>MODEL_NAME</th>
            <th>FAIL_STATION</th>
            <th>ERROR_CODE</th>
            <th>WIP_GROUP</th>
            <th>ERROR_CODE (CURRENT)</th>
            <th>ERROR_DESC</th>
            <th>FA</th>
            <th>STATUS</th>
            <th>OWNER</th>
            <th>CUSTOMER_OWNER</th>
            <th>TIME_UPDATE</th>
        </tr>`;

    const rows = (data || []).map((item, index) => `
        <tr>
            <td>${index + 1}</td>
            <td>${htmlEscape(item.serialNumber)}</td>
            <td>${htmlEscape(item.modelName)}</td>
            <td>${htmlEscape(item.failStation)}</td>
            <td>${htmlEscape(item.errorCode)}</td>
            <td>${htmlEscape(item.wipGroup)}</td>
            <td>${htmlEscape(item.currentErrorCode)}</td>
            <td>${htmlEscape(item.errorDesc)}</td>
            <td>${htmlEscape(item.fa)}</td>
            <td>${htmlEscape(item.status)}</td>
            <td>${htmlEscape(item.owner)}</td>
            <td>${htmlEscape(item.customerOwner)}</td>
            <td>${htmlEscape(item.timeUpdate)}</td>
        </tr>
    `).join("");

    const warning = missingSerials && missingSerials.length
        ? `<div class="alert alert-warning mt-3">
                <strong>Warning:</strong> Không tìm thấy dữ liệu R109 cho ${missingSerials.length} SN.
           </div>`
        : "";

    resultDiv.innerHTML = `
        <div class="table-container soft-scroll">
            <table class="stacked-result-table">
                <thead>${header}</thead>
                <tbody>${rows || `<tr><td colspan="13" class="text-center text-muted">No data</td></tr>`}</tbody>
            </table>
        </div>
        ${warning}
    `;
}

async function submitAnalysis(items) {
    const resultDiv = document.getElementById("analysis-history-result");
    resultDiv.classList.remove("hidden");
    resultDiv.innerHTML = `
        <div class="alert alert-info">
            <strong>Thông báo:</strong> Loading data...
        </div>
    `;

    if (!items.length) {
        resultDiv.innerHTML = `
            <div class="alert alert-warning">
                <strong>Warning:</strong> Vui lòng nhập dữ liệu hợp lệ.
            </div>
        `;
        return;
    }

    try {
        const response = await fetch(`${analysisApiBaseUrl}/analysis-history`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ items })
        });

        const result = await response.json();
        if (!response.ok) {
            throw new Error(result.message || "Không thể lưu dữ liệu.");
        }

        renderResult(result.data || [], result.missingR109 || []);
    } catch (error) {
        resultDiv.innerHTML = `
            <div class="alert alert-danger">
                <strong>Error:</strong> ${htmlEscape(error.message)}
            </div>
        `;
    }
}

document.addEventListener("DOMContentLoaded", () => {
    const optionSelect = document.getElementById("analysis-options");
    const inputForm = document.getElementById("analysis-input-form");
    const uploadForm = document.getElementById("analysis-upload-form");

    optionSelect?.addEventListener("change", () => {
        inputForm.classList.add("hidden");
        uploadForm.classList.add("hidden");

        if (optionSelect.value === "INPUT") {
            inputForm.classList.remove("hidden");
        }

        if (optionSelect.value === "UPLOAD") {
            uploadForm.classList.remove("hidden");
        }
    });

    document.getElementById("analysis-input-submit")?.addEventListener("click", () => {
        const items = buildItemsFromInput();
        submitAnalysis(items);
    });

    document.getElementById("analysis-upload-submit")?.addEventListener("click", async () => {
        const fileInput = document.getElementById("analysis-file-input");
        if (!fileInput?.files?.length) {
            submitAnalysis([]);
            return;
        }

        const file = fileInput.files[0];
        const reader = new FileReader();

        reader.onload = event => {
            const data = new Uint8Array(event.target.result);
            const workbook = XLSX.read(data, { type: "array" });
            const sheetName = workbook.SheetNames[0];
            const sheet = workbook.Sheets[sheetName];
            const sheetData = XLSX.utils.sheet_to_json(sheet, { defval: "" });
            const items = buildItemsFromSheet(sheetData);
            submitAnalysis(items);
        };

        reader.onerror = () => {
            const resultDiv = document.getElementById("analysis-history-result");
            resultDiv.classList.remove("hidden");
            resultDiv.innerHTML = `
                <div class="alert alert-danger">
                    <strong>Error:</strong> Không thể đọc file.
                </div>
            `;
        };

        reader.readAsArrayBuffer(file);
    });
});
