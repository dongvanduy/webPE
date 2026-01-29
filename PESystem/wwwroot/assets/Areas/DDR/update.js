const apiContainer = document.querySelector("[data-api-base]");
const apiBaseUrl = apiContainer?.dataset.apiBase?.trim() || "";
const apiConfigBase = apiBaseUrl ? `${apiBaseUrl}/api/Config` : "";

const snForm = document.getElementById("sn-form");
const serialNumberList = document.getElementById("serialNumberList");
const refreshButton = document.getElementById("refreshButton");
const inputButton = document.getElementById("inputButton");
const typeBonepileSelect = document.getElementById("typeBonepile");
const hbMbInput = document.getElementById("hbMbInput");
const resultsBody = document.getElementById("results-body");
const tableWrapper = document.getElementById("sn-table-wrapper");

const updateButtons = document.querySelectorAll(".btn-update");
const updateModal = document.getElementById("updateModal");
const updateModalLabel = document.getElementById("updateModalLabel");
const updateStatus = document.getElementById("updateStatus");
const updateValueGroup = document.getElementById("text-value-group");
const updateValueLabel = document.getElementById("updateValueLabel");
const updateValueInput = document.getElementById("updateValueInput");
const errorDescGroup = document.getElementById("error-desc-group");
const errorDescInput = document.getElementById("errorDescInput");
const submitUpdateBtn = document.getElementById("submitUpdateBtn");

let currentSerialNumbers = [];
let currentUpdateType = "";

function normalizeSerialNumbers(input) {
    return input
        .split(/[\n,;]+/)
        .map(sn => sn.trim())
        .filter(sn => sn.length > 0);
}

function formatDate(value) {
    if (!value) return "";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return value;
    }
    return date.toLocaleString();
}

function getValue(item, ...keys) {
    for (const key of keys) {
        if (item?.[key] !== undefined && item?.[key] !== null) {
            return item[key];
        }
    }
    return "";
}

function renderTableRows(data) {
    resultsBody.innerHTML = data.map(item => `
        <tr>
            <td>${getValue(item, "ID", "id")}</td>
            <td>${getValue(item, "SerialNumber", "serialNumber")}</td>
            <td>${getValue(item, "TypeBonpile", "typeBonpile")}</td>
            <td>${getValue(item, "ModelName", "modelName")}</td>
            <td>${getValue(item, "HB_MB", "hb_MB")}</td>
            <td>${getValue(item, "TYPE", "type")}</td>
            <td>${formatDate(getValue(item, "First_Fail_Time", "first_Fail_Time"))}</td>
            <td>${getValue(item, "DescFirstFail", "descFirstFail")}</td>
            <td>${getValue(item, "DDRToolResult", "ddrToolResult")}</td>
            <td>${getValue(item, "QTY_RAM_FAIL", "qty_RAM_FAIL")}</td>
            <td>${getValue(item, "NV_Instruction", "nV_Instruction")}</td>
            <td>${getValue(item, "ReworkFXV", "reworkFXV")}</td>
            <td>${getValue(item, "CutInBP2", "cutInBP2")}</td>
            <td>${getValue(item, "CurrentStatus", "currentStatus")}</td>
            <td>${getValue(item, "Remark", "remark")}</td>
            <td>${getValue(item, "Remark2", "remark2")}</td>
            <td>${formatDate(getValue(item, "Created_At", "created_At"))}</td>
            <td>${formatDate(getValue(item, "Updated_At", "updated_At"))}</td>
        </tr>
    `).join("");
}

async function fetchDpuManagerData(serialNumbers) {
    const response = await fetch(`${apiConfigBase}/search-dpu-manager`, {
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify(serialNumbers)
    });

    if (!response.ok) {
        const errorMessage = await response.text();
        throw new Error(errorMessage || "Không thể lấy dữ liệu DPU_Manager.");
    }

    const result = await response.json();
    return result.Data || result.data || [];
}

async function submitSerialNumberForm(event) {
    if (event) {
        event.preventDefault();
    }

    if (!apiConfigBase) {
        alert("Không tìm thấy API base URL.");
        return;
    }

    const inputValue = serialNumberList.value.trim();
    if (!inputValue) {
        alert("Vui lòng nhập Serial Number.");
        return;
    }

    const serialNumbers = normalizeSerialNumbers(inputValue);
    if (serialNumbers.length === 0) {
        alert("Danh sách Serial Number không hợp lệ.");
        return;
    }

    try {
        const data = await fetchDpuManagerData(serialNumbers);
        if (!data.length) {
            alert("Không tìm thấy dữ liệu trong DPU_Manager.");
            return;
        }

        currentSerialNumbers = serialNumbers;
        renderTableRows(data);
        tableWrapper.classList.remove("d-none");
    } catch (error) {
        console.error("Lỗi khi tìm kiếm DPU_Manager:", error);
        alert(error.message || "Không thể tìm kiếm dữ liệu.");
    }
}

function resetModalFields() {
    updateStatus.value = "PASS";
    updateValueInput.value = "";
    errorDescInput.value = "";
}

function toggleUpdateFields(updateType) {
    const isFt = updateType === "FT-ONLINE" || updateType === "FT-OFFLINE";
    updateValueGroup.classList.toggle("d-none", isFt);
    errorDescGroup.classList.toggle("d-none", !isFt);
    updateStatus.parentElement?.classList.toggle("d-none", !isFt);
}

function openUpdateModal(updateType) {
    if (!currentSerialNumbers.length) {
        alert("Vui lòng tìm kiếm Serial Number trước.");
        return;
    }

    currentUpdateType = updateType;
    resetModalFields();

    switch (updateType) {
        case "DDR-TOOL":
            updateModalLabel.textContent = "Cập nhật DDR-TOOL";
            updateValueLabel.textContent = "DDR_TOOL_RESULT";
            break;
        case "NV-INSTRUCTION":
            updateModalLabel.textContent = "Cập nhật NV Instruction";
            updateValueLabel.textContent = "NV_INSTRUCTION";
            break;
        case "FT-ONLINE":
            updateModalLabel.textContent = "Cập nhật FT-ONLINE";
            break;
        case "FT-OFFLINE":
            updateModalLabel.textContent = "Cập nhật FT-OFFLINE";
            break;
        default:
            updateModalLabel.textContent = "Cập nhật";
    }

    toggleUpdateFields(updateType);
    const modalInstance = new bootstrap.Modal(updateModal);
    modalInstance.show();
}

async function syncErrorDesc() {
    if (!apiConfigBase) {
        alert("Không tìm thấy API base URL.");
        return;
    }

    const endpoint = currentUpdateType === "FT-ONLINE" ? "sync-error-ft-on" : "sync-error-ft-off";
    const response = await fetch(`${apiConfigBase}/${endpoint}`, {
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify(currentSerialNumbers)
    });

    if (!response.ok) {
        const errorMessage = await response.text();
        throw new Error(errorMessage || "Không thể đồng bộ ERROR_DESC.");
    }

    const result = await response.json();
    const data = result.Data || result.data || [];
    const errorLines = data.map(item => `${item.SerialNumber ?? ""}: ${item.NewRemark2 ?? ""}`);
    errorDescInput.value = errorLines.join("\n");
}

async function handleStatusChange() {
    if (updateStatus.value === "FAIL") {
        try {
            await syncErrorDesc();
        } catch (error) {
            console.error("Lỗi khi sync ERROR_DESC:", error);
            alert(error.message || "Không thể lấy ERROR_DESC.");
        }
    } else {
        errorDescInput.value = "";
    }
}

async function submitUpdate() {
    if (!currentSerialNumbers.length) {
        alert("Vui lòng tìm kiếm Serial Number trước.");
        return;
    }

    if (!apiConfigBase) {
        alert("Không tìm thấy API base URL.");
        return;
    }

    const payload = {
        serialNumbers: currentSerialNumbers
    };

    if (currentUpdateType === "DDR-TOOL") {
        if (!updateValueInput.value.trim()) {
            alert("Vui lòng nhập DDR_TOOL_RESULT.");
            return;
        }
        payload.DDRToolResult = updateValueInput.value.trim();
    }

    if (currentUpdateType === "NV-INSTRUCTION") {
        if (!updateValueInput.value.trim()) {
            alert("Vui lòng nhập NV Instruction.");
            return;
        }
        payload.NV_Instruction = updateValueInput.value.trim();
    }

    if (currentUpdateType === "FT-ONLINE" || currentUpdateType === "FT-OFFLINE") {
        const status = updateStatus.value;
        payload.CurrentStatus = `${currentUpdateType} ${status}`;

        const errorDescValue = errorDescInput.value.trim();
        if (errorDescValue && currentSerialNumbers.length === 1) {
            payload.Remark2 = errorDescValue;
        }
    }

    try {
        const response = await fetch(`${apiConfigBase}/update-dpu-fields`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            const errorMessage = await response.text();
            throw new Error(errorMessage || "Không thể cập nhật dữ liệu.");
        }

        await submitSerialNumberForm();
        alert("Cập nhật thành công.");

        const modalInstance = bootstrap.Modal.getInstance(updateModal);
        modalInstance?.hide();
    } catch (error) {
        console.error("Lỗi khi cập nhật:", error);
        alert(error.message || "Không thể cập nhật dữ liệu.");
    }
}

snForm?.addEventListener("submit", submitSerialNumberForm);
refreshButton?.addEventListener("click", () => {
    serialNumberList.value = "";
    if (typeBonepileSelect) {
        typeBonepileSelect.value = "";
    }
    if (hbMbInput) {
        hbMbInput.value = "";
    }
    resultsBody.innerHTML = "";
    tableWrapper.classList.add("d-none");
    currentSerialNumbers = [];
});

inputButton?.addEventListener("click", async () => {
    if (!apiConfigBase) {
        alert("Không tìm thấy API base URL.");
        return;
    }

    const inputValue = serialNumberList.value.trim();
    if (!inputValue) {
        alert("Vui lòng nhập Serial Number.");
        return;
    }

    const serialNumbers = normalizeSerialNumbers(inputValue);
    if (!serialNumbers.length) {
        alert("Danh sách Serial Number không hợp lệ.");
        return;
    }

    const typeBonepile = typeBonepileSelect?.value?.trim() || "";
    const hbMb = hbMbInput?.value?.trim() || "";

    if (!typeBonepile) {
        alert("Vui lòng chọn typeBonepile.");
        return;
    }

    if (!hbMb) {
        alert("Vui lòng nhập MB-HB.");
        return;
    }

    try {
        const response = await fetch(`${apiConfigBase}/add-dpu-manager`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                serialNumbers,
                hbMb,
                typeBonepile
            })
        });

        if (!response.ok) {
            const errorMessage = await response.text();
            throw new Error(errorMessage || "Không thể input DPU_Manager.");
        }

        const result = await response.json();
        alert(result.message || "Input DPU_Manager thành công.");
        await submitSerialNumberForm();
    } catch (error) {
        console.error("Lỗi khi input DPU_Manager:", error);
        alert(error.message || "Không thể input DPU_Manager.");
    }
});

updateButtons.forEach(button => {
    button.addEventListener("click", () => {
        const updateType = button.dataset.updateType;
        openUpdateModal(updateType);
    });
});

updateStatus?.addEventListener("change", handleStatusChange);
submitUpdateBtn?.addEventListener("click", submitUpdate);
