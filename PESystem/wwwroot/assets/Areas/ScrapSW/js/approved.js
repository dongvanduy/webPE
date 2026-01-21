// ==============================
// SweetAlert Helpers
// ==============================
function showLoading(msg = "Processing...") {
    Swal.fire({
        title: msg,
        allowOutsideClick: false,
        didOpen: () => Swal.showLoading()
    });
}

function showSuccess(msg) {
    Swal.fire({ icon: "success", title: "Success", html: msg });
}

function showError(msg) {
    Swal.fire({ icon: "error", title: "Error", html: msg });
}

function showWarning(msg) {
    Swal.fire({ icon: "warning", title: "Warning", html: msg });
}

const SCRAP_API_BASE = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap";
const removeFlowState = {
    snKey: "",
    reasonKey: "",
    unblock: { attempted: false, success: false, message: "" },
    delete: { attempted: false, success: false, message: "" },
    sql: { attempted: false, success: false, message: "" },
    inProgress: false
};

function parseSerialInput(textareaId) {
    return document.getElementById(textareaId).value.trim()
        .split(/\r?\n/)
        .map(x => x.trim())
        .filter(Boolean);
}

// ==============================
// CALL SMARTREPAIR API update
// ==============================
async function callSmartRepair(snList, status, task = "", createdBy) {
    const payload = {
        type: "update",
        sn_list: snList.join(","),
        status,
        task,
        emp_no: createdBy,
        reason: "Update data scrap"
    };

    try {
        const res = await fetch("https://sfc-portal.cns.myfiinet.com/SfcSmartRepair/api/repair_scrap", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        const text = await res.text();

        // CHỈ CHẤP NHẬN DUY NHẤT CHUỖI '"OK"' (có dấu nháy kép) LÀ THÀNH CÔNG
        if (text === '"OK"') {
            return { success: true, raw: text };
        }

        // MỌI TRƯỜNG HỢP KHÁC (kể cả HTTP 200) ĐỀU LÀ LỖI
        let cleanMsg = text
            .replace(/^"|"$/g, '')   // bỏ dấu " ở đầu/cuối
            .trim();
        if (!cleanMsg) cleanMsg = "SmartRepair rejected the request";

        return { success: false, message: cleanMsg, raw: text };

    } catch (err) {
        return { success: false, message: "Không kết nối được SmartRepair system!" };
    }
}


// ==============================
// INPUT SN
// ==============================
async function handleInputSN() {

    const snLines = document.getElementById("sn-input").value.trim().split(/\r?\n/);
    const sNs = snLines.map(x => x.trim()).filter(Boolean);

    const description = document.getElementById("description-input").value.trim();
    const approveScrapPerson = document.getElementById("NVmember-input").value.trim();
    const purpose = document.getElementById("Scrap-options").value;
    const speApproveTime = document.getElementById("speApproveTime-input").value;
    const createdBy = document.getElementById("analysisPerson").value;
    const reasonRemove = document.getElementById("reason-remove").value;

    // =======================
    // VALIDATION
    // =======================
    if (!sNs.length) return showWarning("Please enter SN!");
    if (!description) return showWarning("Please enter description!");
    if (!approveScrapPerson) return showWarning("Please enter the approver!");
    if (!["0", "1", "2", "3", "4"].includes(purpose)) return showWarning("Please select the scrap type!");
    if (!speApproveTime) return showWarning("Please enter the approval time!");
    if (!reasonRemove) return showWarning("Please enter the reason remove!");

    // =======================
    // CALL SMARTREPAIR FIRST
    // =======================
    showLoading("Synchronizing SmartRepair...");

    const smart = await callSmartRepair(sNs, "0", "", createdBy);

    if (!smart.success) {
        return showError("SmartRepair error:<br>" + smart.message);
    }

    // =======================
    // CALL INPUT-SN SQL SERVER
    // =======================
    showLoading("Saving information to SQL Server...");

    const payload = {
        sNs,
        createdBy,
        description,
        approveScrapPerson,
        purpose,
        speApproveTime
    };

    try {
        const res = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/input-sn", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        const result = await res.json();

        if (!res.ok) {
            return showError("Input-SN error:<br>" + result.message);
        }

        // =======================
        // DONE — NO UPDATE PRODUCT CALL
        // =======================
        showSuccess(`
            <b>Input SN success!</b><br>
            SmartRepair: OK<br>
            SQL Input-SN: ${result.message}
        `);

    } catch (err) {
        showError("Cannot connect to SQL Server!");
    }
}

// ==============================
// UPDATE TASK PO
// ==============================
async function handleUpdateTaskPO() {

    const snList = document.getElementById("sn-input-update").value.trim().split(/\r?\n/).map(x => x.trim()).filter(Boolean);
    const task = document.getElementById("task-input").value.trim();
    const po = document.getElementById("po-input").value.trim();

    if (!snList.length) return showWarning("Please enter SN.");
    if (!task) return showWarning("Please enter Task.");
    if (!po) return showWarning("Please enter PO");

    // =======================
    // CALL SMARTREPAIR FIRST
    // =======================
    showLoading("Checking ApplyTaskStatus...");
    try {
        const statusRes = await fetch(`${SCRAP_API_BASE}/detail-task-status`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ SNs: snList })
        });

        const statusResult = await statusRes.json();

        if (!statusRes.ok) {
            return showError("Error:<br>" + (statusResult.message || ""));
        }

        const invalidSNs = (statusResult.data || [])
            .filter(item => item.applyTaskStatus !== 9)
            .map(item => item.sn || item.SN)
            .filter(Boolean);

        if (invalidSNs.length) {
            return showError(`Only update when Task/PO khi PM approved (ApplyTaskStatus = 9)<br>Invalid SN: ${invalidSNs.join(", ")}`);
        }
    } catch (err) {
        return showError("Cannot check ApplyTaskStatus");
    }

    const smart = await callSmartRepair(snList, "5", task, createdBy);

    if (!smart.success) {
        return showError("SmartRepair error:<br>" + smart.message);
    }

    // =======================
    // CALL SQL UPDATE TASK PO
    // =======================
    showLoading("Updating Task/PO...");

    try {
        const payload = { snList, task, po };

        const res = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Switch/update-task-po-switch", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        const result = await res.json();

        if (!res.ok) {
            return showError("SQL UpdateTaskPO error:<br>" + result.message);
        }

        showSuccess(`
            <b>Update Task PO success!</b><br>
            SmartRepair: OK<br>
            SQL Update: ${result.message}
        `);

    } catch (err) {
        showError("Cannot connect to SQL Server!");
    }
}

// ==============================
// UPDATE APPLY TASK STATUS (PM/COST)
// ==============================
async function callUpdateApplyStatus(snList, targetStatus, successTitle) {
    // =======================
    // CALL SMARTREPAIR FIRST
    // =======================
    showLoading("Synchronizing SmartRepair...");

    const smart = await callSmartRepair(snList, String(targetStatus), "", createdBy);

    if (!smart.success) {
        return showError("SmartRepair error:<br>" + smart.message);
    }

    // =======================
    // CALL UPDATE-APPLY-STATUS SQL SERVER
    // =======================
    showLoading("Updating status...");

    let result = {};

    try {
        const payload = { sNs: snList, targetStatus };

        const res = await fetch(`${SCRAP_API_BASE}/update-apply-status`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        try {
            result = await res.json();
        } catch {
            result = {};
        }

        if (!res.ok) {
            return showError("Failed to update status:<br>" + (result.message || ""));
        }

        showSuccess(`
            <b>${successTitle}</b><br>
            SmartRepair: OK<br>
            ${result.message}
        `);
    } catch (err) {
        showError("Cannot connect to SQL Server!");
    }
}

async function callSmartRepairDelete(snList, createdBy, reasonRemove) {
    const payload = {
        type: "delete",
        sn_list: snList.join(","),
        emp_no: createdBy,
        reason: reasonRemove
    };

    try {
        const res = await fetch("https://sfc-portal.cns.myfiinet.com/SfcSmartRepair/api/repair_scrap", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        const text = await res.text();
        const cleanText = text.replace(/^"|"$/g, '').trim();

        // Delete thành công thường trả về "Ok delete ..." hoặc "OK"
        if (cleanText.toLowerCase().includes("ok delete") || text === '"OK"') {
            return { success: true, message: cleanText || "OK" };
        }

        return {
            success: false,
            message: cleanText || "SmartRepair delete failed"
        };

    } catch (err) {
        return { success: false, message: "Không kết nối được SmartRepair system!" };
    }
}

async function callSmartRepairUnblock(snList, createdBy, reasonRemove) {
    const payload = {
        type: "unblock",
        sn_list: snList.join(","),
        emp_no: createdBy,
        reason: reasonRemove
    };

    try {
        const res = await fetch("https://sfc-portal.cns.myfiinet.com/SfcSmartRepair/api/repair_scrap", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        const text = await res.text();
        //const cleanText = text.replace(/^"|"$/g, '').trim();

        // Unblock thành công trả về "OK"
        if (text === '"OK"') {
            return { success: true, message:"OK" };
        }

        return {
            success: false,
            message: "SmartRepair unblock failed!"
        };

    } catch (err) {
        return { success: false, message: "Không kết nối được SmartRepair system!" };
    }
}


async function handlePmUpdate() {
    const snList = parseSerialInput("sn-input-pm");

    if (!snList.length) return showWarning("Please enter SN!");
    try {
        const statusRes = await fetch(`${SCRAP_API_BASE}/detail-task-status`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ SNs: snList })
        });

        const statusResult = await statusRes.json();

        if (!statusRes.ok) {
            return showError("Check status error:<br>" + (statusResult.message || ""));
        }

        const invalidSNs = (statusResult.data || [])
            .filter(item => item.applyTaskStatus !== 20)
            .map(item => item.sn || item.SN)
            .filter(Boolean);

        if (invalidSNs.length) {
            return showError(`Only update when ApplyTaskStatus = 20.<br>Invalid SN: ${invalidSNs.join(", ")}`);
        }
    } catch (err) {
        return showError("Cannot check ApplyTaskStatus!");
    }
    await callUpdateApplyStatus(snList, 9, "PM Update success!");
}

async function handleCostUpdate() {
    const snList = parseSerialInput("sn-input-cost");

    if (!snList.length) return showWarning("Please enter SN!");
    try {
        const statusRes = await fetch(`${SCRAP_API_BASE}/detail-task-status`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ SNs: snList })
        });

        const statusResult = await statusRes.json();

        if (!statusRes.ok) {
            return showError("Check status error:<br>" + (statusResult.message || ""));
        }

        const invalidSNs = (statusResult.data || [])
            .filter(item => item.applyTaskStatus !== 0)
            .map(item => item.sn || item.SN)
            .filter(Boolean);

        if (invalidSNs.length) {
            return showError(`Only update when ApplyTaskStatus = 0.<br>Invalid SN: ${invalidSNs.join(", ")}`);
        }
    } catch (err) {
        return showError("Cannot check ApplyTaskStatus!");
    }
    await callUpdateApplyStatus(snList, 20, "Cost Update success!");
}

// ==============================
// REMOVE APPROVED SN FLOW (UNBLOCK -> DELETE -> SQL REMOVE)
// ==============================
function renderRemoveStatus() {
    const statusBox = document.getElementById("remove-status");
    if (!statusBox) return;

    const rows = [
        `<b>UNBLOCK:</b> ${removeFlowState.unblock.attempted ? (removeFlowState.unblock.success ? "OK" : "FAILED") : "Not started"}`,
        removeFlowState.unblock.message ? `<div class="mt-1">Message: ${removeFlowState.unblock.message}</div>` : "",
        `<b>DELETE:</b> ${removeFlowState.delete.attempted ? (removeFlowState.delete.success ? "OK" : "FAILED") : "Not started"}`,
        removeFlowState.delete.message ? `<div class="mt-1">Message: ${removeFlowState.delete.message}</div>` : "",
        `<b>SQL remove:</b> ${removeFlowState.sql.attempted ? (removeFlowState.sql.success ? "OK" : "FAILED") : "Not started"}`,
        removeFlowState.sql.message ? `<div class="mt-1">Message: ${removeFlowState.sql.message}</div>` : ""
    ].filter(Boolean);

    statusBox.innerHTML = rows.join("<br>");
    statusBox.classList.remove("hidden");
}

function resetRemoveFlow() {
    removeFlowState.snKey = "";
    removeFlowState.reasonKey = "";
    removeFlowState.unblock = { attempted: false, success: false, message: "" };
    removeFlowState.delete = { attempted: false, success: false, message: "" };
    removeFlowState.sql = { attempted: false, success: false, message: "" };
    removeFlowState.inProgress = false;

    const deleteBtn = document.getElementById("delete-btn");
    if (deleteBtn) deleteBtn.disabled = true;

    const statusBox = document.getElementById("remove-status");
    if (statusBox) {
        statusBox.classList.add("hidden");
        statusBox.innerHTML = "";
    }
}

function buildRemoveKey(snList, reason) {
    return `${snList.join("|")}::${reason}`;
}

function lockRemoveButtons(isLocked) {
    document.getElementById("unblock-btn")?.toggleAttribute("disabled", isLocked);
    document.getElementById("delete-btn")?.toggleAttribute("disabled", isLocked || !removeFlowState.unblock.attempted);
}

function enableDeleteButton() {
    const deleteBtn = document.getElementById("delete-btn");
    if (deleteBtn) deleteBtn.disabled = false;
}

function validateRemoveInputs() {
    const snList = parseSerialInput("sn-input-remove");
    const createdBy = document.getElementById("analysisPerson").value;
    const reasonRemove = document.getElementById("reason-remove").value.trim();

    if (!snList.length) {
        showWarning("Please enter SN!");
        return null;
    }
    if (!reasonRemove.length) {
        showWarning("Please enter reason unblock!");
        return null;
    }

    return { snList, createdBy, reasonRemove };
}

async function handleUnblockSN() {
    const inputs = validateRemoveInputs();
    if (!inputs) return;
    const { snList, createdBy, reasonRemove } = inputs;

    const newKey = buildRemoveKey(snList, reasonRemove);
    if (removeFlowState.snKey && newKey !== removeFlowState.snKey) {
        resetRemoveFlow();
    }

    removeFlowState.snKey = buildRemoveKey(snList, reasonRemove);
    removeFlowState.reasonKey = reasonRemove;
    removeFlowState.inProgress = true;
    lockRemoveButtons(true);
    showLoading("Calling SmartRepair UNBLOCK...");

    const smartUnblock = await callSmartRepairUnblock(snList, createdBy, reasonRemove);

    removeFlowState.unblock.attempted = true;
    removeFlowState.unblock.success = smartUnblock.success;
    removeFlowState.unblock.message = smartUnblock.message || "";

    Swal.close();
    renderRemoveStatus();
    enableDeleteButton();
    removeFlowState.inProgress = false;
    lockRemoveButtons(false);

    if (smartUnblock.success) {
        showSuccess("UNBLOCK success. You can proceed to DELETE.");
    } else {
        showWarning("UNBLOCK failed. You may still attempt DELETE.");
    }
}

async function handleDeleteSN() {
    if (!removeFlowState.unblock.attempted) {
        return showWarning("Please click UNBLOCK before DELETE.");
    }
    if (removeFlowState.inProgress) return;

    const inputs = validateRemoveInputs();
    if (!inputs) return;
    const { snList, createdBy, reasonRemove } = inputs;

    const currentKey = buildRemoveKey(snList, reasonRemove);
    if (removeFlowState.snKey && currentKey !== removeFlowState.snKey) {
        resetRemoveFlow();
        return showWarning("SN list or reason changed. Please UNBLOCK again before DELETE.");
    }

    removeFlowState.inProgress = true;
    lockRemoveButtons(true);

    // =======================
    // 1) CALL SMARTREPAIR DELETE
    // =======================
    showLoading("Calling SmartRepair DELETE...");
    const smartDelete = await callSmartRepairDelete(snList, createdBy, reasonRemove);
    removeFlowState.delete.attempted = true;
    removeFlowState.delete.success = smartDelete.success;
    removeFlowState.delete.message = smartDelete.message || "";

    if (!smartDelete.success) {
        Swal.close();
        renderRemoveStatus();
        showError("DELETE failed. SQL remove was not executed.");
        removeFlowState.inProgress = false;
        lockRemoveButtons(false);
        return;
    }

    // =======================
    // 2) CALL REMOVE SQL SERVER AFTER DELETE SUCCESS
    // =======================
    showLoading("Deleting SN on SQL Server...");

    let result = {};
    try {
        const payload = { snList };

        const res = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Switch/remove-switch-sn-list", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        result = await res.json().catch(() => ({}));

        if (!res.ok) {
            removeFlowState.sql = {
                attempted: true,
                success: false,
                message: result.message || "Unknown error"
            };
            Swal.close();
            renderRemoveStatus();
            showError("Delete SN failed:<br>" + (result.message || "Unknown error"));
            removeFlowState.inProgress = false;
            lockRemoveButtons(false);
            return;
        }
        
    } catch (err) {
        removeFlowState.sql = { attempted: true, success: false, message: "Cannot connect to SQL Server!" };
        Swal.close();
        renderRemoveStatus();
        showError("Cannot connect to SQL Server!");
        removeFlowState.inProgress = false;
        lockRemoveButtons(false);
        return;
    }
    // Lấy danh sách SN thực tế đã xóa từ Backend trả về
    const actualDeletedSns = result.deletedSns || [];

    if (actualDeletedSns.length === 0) {
        removeFlowState.sql = { attempted: true, success: false, message: "No SNs were updated on SQL Server." };
        Swal.close();
        renderRemoveStatus();
        showError("No SNs were updated on SQL Server.");
        removeFlowState.inProgress = false;
        lockRemoveButtons(false);
        return;
    }

    removeFlowState.sql = { attempted: true, success: true, message: result.message || "OK" };

    // =======================
    // DONE
    // =======================
    Swal.close();
    renderRemoveStatus();
    showSuccess(`
        <b>Remove SN success!</b><br>
        UNBLOCK: ${removeFlowState.unblock.success ? "OK" : "FAILED"}<br>
        DELETE: OK<br>
        SQL remove: OK<br>
        Count: ${actualDeletedSns.length} SN(s)
    `);
    removeFlowState.inProgress = false;
    lockRemoveButtons(false);
}

// ==============================
// EVENT LISTENERS
// ==============================
document.addEventListener("DOMContentLoaded", () => {
    document.querySelector("#input-sn-form button")?.addEventListener("click", handleInputSN);
    document.getElementById("update-task-btn")?.addEventListener("click", handleUpdateTaskPO);
    document.getElementById("pm-update-btn")?.addEventListener("click", handlePmUpdate);
    document.getElementById("cost-update-btn")?.addEventListener("click", handleCostUpdate);
    document.getElementById("unblock-btn")?.addEventListener("click", handleUnblockSN);
    document.getElementById("delete-btn")?.addEventListener("click", handleDeleteSN);

    const resetInputs = ["sn-input-remove", "reason-remove"];
    resetInputs.forEach(id => {
        document.getElementById(id)?.addEventListener("input", () => {
            resetRemoveFlow();
        });
    });
});
