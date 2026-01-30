let selectedInternalTasks = new Set(); // Cho CREATE_TASK_FORM
let selectedHistoryInternalTasks = new Set(); // Cho HISTORY_APPLY

const LOCATION_API_URL = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Search/FindLocations';

function normalizeSerialNumber(value) {
    return (value ?? '').toString().trim().toUpperCase();
}

async function fetchLocationMap(serialNumbers) {
    try {
        const uniqueSerials = Array.from(new Set(
            (serialNumbers || [])
                .map(normalizeSerialNumber)
                .filter(sn => sn)
        ));

        if (!uniqueSerials.length) {
            return {};
        }

        const response = await fetch(LOCATION_API_URL, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(uniqueSerials)
        });

        if (!response.ok) {
            throw new Error(`Location API responded with status ${response.status}`);
        }

        const json = await response.json();
        if (!json?.success || !Array.isArray(json?.data)) {
            throw new Error('Location API returned unexpected payload');
        }

        return json.data.reduce((acc, item) => {
            const sn = normalizeSerialNumber(item?.serialNumber || item?.sn);
            if (!sn) {
                return acc;
            }
            const location = (item?.location || '').toString().trim();
            acc[sn] = location;
            return acc;
        }, {});
    } catch (error) {
        console.error('fetchLocationMap error:', error);
        return {};
    }
}

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function formatPurpose(purpose) {
    if (purpose === null || purpose === undefined) {
        return "N/A";
    }

    const normalized = String(purpose).trim();
    if (!normalized) {
        return "N/A";
    }

    const purposeMap = {
        "0": "SPE approve to scrap",
        "1": "Scrap to quarterly",
        "2": "Approved to engineer sample",
        "3": "Approved to master board",
        "4": "Approved to BGA"
    };

    return purposeMap[normalized] ?? normalized;
}

async function callSmartRepairApi(snList, status, task = "", currentUsername) {
    const normalizedSnList = Array.isArray(snList)
        ? snList.map(sn => sn.trim()).filter(Boolean)
        : [];

    if (!normalizedSnList.length) {
        return { success: false, message: "Không có Serial Number hợp lệ để đồng bộ SmartRepair." };
    }

    const payload = {
        type: "update",
        sn_list: normalizedSnList.join(","),
        status,
        task: task || "",
        emp_no: currentUsername,
        reason: "Update data scrap"
    };

    try {
        const response = await fetch("https://sfc-portal.cns.myfiinet.com/SfcSmartRepair/api/repair_scrap", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        const result = await response.json().catch(() => ({}));
        if (!response.ok) {
            return {
                success: false,
                message: result?.message || `SmartRepair trả về lỗi HTTP ${response.status}`
            };
        }

        return {
            success: true,
            message: result?.message || "Đồng bộ SmartRepair thành công."
        };
    } catch (error) {
        console.error("callSmartRepairApi error:", error);
        return {
            success: false,
            message: "Không thể kết nối đến SmartRepair."
        };
    }
}

// Hàm ẩn tất cả form và khu vực kết quả
function hideAllElements() {
    const forms = ["input-sn-form", "custom-form", "custom-form-sn", "update-data-form", "history-apply-form", "custom-form-bp2"];
    const results = ["input-sn-result", "create-task-result", "update-data-result", "history-apply-result", "create-task-result-sn", "create-task-result-bp2"];

    forms.forEach(formId => {
        const form = document.getElementById(formId);
        if (form) form.classList.add("hidden");
    });

    results.forEach(resultId => {
        const result = document.getElementById(resultId);
        if (result) result.classList.add("hidden");
    });

    ["sn-input", "sn-input-update", "description-input", "NVmember-input", "speApproveTime-input", "task-input", "po-input", "cost-input", "file-input"].forEach(id => {
        const input = document.getElementById(id);
        if (input) input.value = "";
    });

    const scrapOptions = document.getElementById("Scrap-options");
    if (scrapOptions) scrapOptions.selectedIndex = 0;
}

// Xuất dữ liệu ra Excel
function exportToExcel(data, filename) {
    const worksheet = XLSX.utils.json_to_sheet(data);
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, worksheet, "ScrapData");
    XLSX.writeFile(workbook, filename);
}

// Xử lý tạo task
async function processCreateTask(internalTasks, resultDivId) {
    const resultDiv = document.getElementById(resultDivId);
    let statusContainer = resultDiv.querySelector('.action-feedback');
    if (!statusContainer) {
        statusContainer = document.createElement('div');
        statusContainer.className = 'action-feedback mb-2';
        resultDiv.prepend(statusContainer);
    }
    statusContainer.innerHTML = `<div class="alert alert-info"><strong>Thông báo:</strong> Đang tải xuống dữ liệu...</div>`;

    const requestData = { internalTasks };

    try {
        const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/create-task", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(requestData)
        });

        const result = await response.json();
        if (response.ok) {
            const payload = Array.isArray(result.data) ? result.data : [];
            if (!payload.length) {
                statusContainer.innerHTML = `<div class="alert alert-warning"><strong>Thông báo:</strong> Không có dữ liệu để tải xuống.</div>`;
                return;
            }

            const locationMap = await fetchLocationMap(payload.map(item => item.boardSN));

            const normalizedPayload = payload.map(item => {
                const consigICs = Array.isArray(item.consigICs) ? item.consigICs : [];
                const sortedConsigICs = consigICs.slice().sort((a, b) => {
                    const aIsMLX = typeof a.icPn === "string" && a.icPn.toUpperCase().startsWith("MLX");
                    const bIsMLX = typeof b.icPn === "string" && b.icPn.toUpperCase().startsWith("MLX");
                    if (aIsMLX !== bIsMLX) {
                        return aIsMLX ? -1 : 1;
                    }
                    return 0;
                });
                return { ...item, sortedConsigICs };
            });

            const maxConsigCount = normalizedPayload.reduce(
                (max, item) => Math.max(max, item.sortedConsigICs.length),
                0
            );

            const excelData = normalizedPayload.map(item => {
                const row = {
                    InternalTask: item.internalTask ?? "N/A",
                    Item: item.item ?? "N/A",
                    Project: item.project ?? "N/A",
                    OPN: item.opn ?? "N/A",
                    Purpose: item.purpose ?? "N/A",
                    "IC PN": item.icPn ?? "N/A",
                    "IC Detail PN": item.icDetailPn ?? "N/A",
                    "Board SN": item.boardSN ?? "N/A",
                    "After/Before Kanban": item.afterBeforeKanban ?? "N/A",
                    Category: item.category ?? "N/A",
                    CM: item.cm ?? "N/A",
                    Plant: item.plant ?? "N/A",
                    Sloc: item.sloc ?? "N/A",
                    "Task Number": item.taskNumber ?? "N/A",
                    "PO Number": item.poNumber ?? "N/A",
                    "Create By": item.createBy ?? "N/A",
                    "Create Date": item.createDate ?? "N/A",
                    Cost: item.cost ?? "N/A",
                    Remark: item.smtTime ?? "N/A",
                    Description: item.description ?? "N/A",
                    SpeApproveTime: item.speApproveTime ?? "N/A",
                    Location: locationMap[normalizeSerialNumber(item.boardSN)] ?? "N/A"
                };

                for (let index = 0; index < maxConsigCount; index += 1) {
                    const consig = item.sortedConsigICs[index];
                    const consigLabel = `ConsigIC${index + 1}`;
                    const qtyLabel = `Qty ${index + 1}`;
                    row[consigLabel] = consig?.icDetailPn ?? consig?.icPn ?? "N/A";
                    row[qtyLabel] = consig?.qty ?? "N/A";
                }

                return row;
            });

            // Sắp xếp dữ liệu theo InternalTask
            excelData.sort((a, b) => a.InternalTask.localeCompare(b.InternalTask));

            // Filter data into Before and After based on "After/Before Kanban"
            const beforeData = excelData.filter(item => item["After/Before Kanban"] === "Before");
            const afterData = excelData.filter(item => item["After/Before Kanban"] === "After");

            // Create workbook and append two sheets
            const workbook = XLSX.utils.book_new();
            if (beforeData.length > 0) {
                const beforeWorksheet = XLSX.utils.json_to_sheet(beforeData);
                XLSX.utils.book_append_sheet(workbook, beforeWorksheet, "BeforeKanban");
            }
            if (afterData.length > 0) {
                const afterWorksheet = XLSX.utils.json_to_sheet(afterData);
                XLSX.utils.book_append_sheet(workbook, afterWorksheet, "AfterKanban");
            }

            // Generate filename with timestamp
            const now = new Date();
            const filename = `ScrapData_${now.toISOString().replace(/[:.]/g, '-')}.xlsx`;

            // Write the workbook to file
            XLSX.writeFile(workbook, filename);

            const successMessage = result.message
                ? `<div class="alert alert-warning">${escapeHtml(result.message)}</div>`
                : `<div class="alert alert-success"><strong>Thành công:</strong> Dữ liệu đã được tải xuống.</div>`;

            statusContainer.innerHTML = successMessage;
        } else {
            statusContainer.innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> ${result.message}</div>`;
        }
    } catch (error) {
        statusContainer.innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> Không thể kết nối đến API. Vui lòng kiểm tra lại.</div>`;
        console.error("Error:", error);
    }
}

// Xử lý tạo task bằng SN
async function processCreateTaskBySN(sNs, resultDivId) {
    const resultDiv = document.getElementById(resultDivId);
    let statusContainer = resultDiv.querySelector('.action-feedback');
    if (!statusContainer) {
        statusContainer = document.createElement('div');
        statusContainer.className = 'action-feedback mb-2';
        resultDiv.prepend(statusContainer);
    }
    statusContainer.innerHTML = `<div class="alert alert-info"><strong>Thông báo:</strong> Đang tải xuống dữ liệu...</div>`;

    const requestData = { sNs };

    try {
        const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/create-task-sn", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(requestData)
        });

        const result = await response.json();
        if (response.ok) {
            const payload = Array.isArray(result.data) ? result.data : [];
            if (!payload.length) {
                statusContainer.innerHTML = `<div class="alert alert-warning"><strong>Thông báo:</strong> Không có dữ liệu để tải xuống.</div>`;
                return;
            }

            const locationMap = await fetchLocationMap(payload.map(item => item.boardSN));

            const normalizedPayload = payload.map(item => {
                const consigICs = Array.isArray(item.consigICs) ? item.consigICs : [];
                const sortedConsigICs = consigICs.slice().sort((a, b) => {
                    const aIsMLX = typeof a.icPn === "string" && a.icPn.toUpperCase().startsWith("MLX");
                    const bIsMLX = typeof b.icPn === "string" && b.icPn.toUpperCase().startsWith("MLX");
                    if (aIsMLX !== bIsMLX) {
                        return aIsMLX ? -1 : 1;
                    }
                    return 0;
                });
                return { ...item, sortedConsigICs };
            });

            const maxConsigCount = normalizedPayload.reduce(
                (max, item) => Math.max(max, item.sortedConsigICs.length),
                0
            );

            const excelData = normalizedPayload.map(item => {
                const row = {
                    InternalTask: item.internalTask ?? "N/A",
                    Item: item.item ?? "N/A",
                    Project: item.project ?? "N/A",
                    OPN: item.opn ?? "N/A",
                    Purpose: item.purpose ?? "N/A",
                    "IC PN": item.icPn ?? "N/A",
                    "IC Detail PN": item.icDetailPn ?? "N/A",
                    "Board SN": item.boardSN ?? "N/A",
                    "After/Before Kanban": item.afterBeforeKanban ?? "N/A",
                    Category: item.category ?? "N/A",
                    CM: item.cm ?? "N/A",
                    Plant: item.plant ?? "N/A",
                    Sloc: item.sloc ?? "N/A",
                    "Task Number": item.taskNumber ?? "N/A",
                    "PO Number": item.poNumber ?? "N/A",
                    "Create By": item.createBy ?? "N/A",
                    "Create Date": item.createDate ?? "N/A",
                    Cost: item.cost ?? "N/A",
                    Remark: item.smtTime ?? "N/A",
                    Description: item.description ?? "N/A",
                    SpeApproveTime: item.speApproveTime ?? "N/A",
                    Location: locationMap[normalizeSerialNumber(item.boardSN)] ?? "N/A"
                };

                for (let index = 0; index < maxConsigCount; index += 1) {
                    const consig = item.sortedConsigICs[index];
                    const consigLabel = `ConsigIC${index + 1}`;
                    const qtyLabel = `Qty ${index + 1}`;
                    row[consigLabel] = consig?.icDetailPn ?? consig?.icPn ?? "N/A";
                    row[qtyLabel] = consig?.qty ?? "N/A";
                }

                return row;
            });

            // Sắp xếp dữ liệu theo InternalTask
            excelData.sort((a, b) => a.InternalTask.localeCompare(b.InternalTask));

            // Filter data into Before and After based on "After/Before Kanban"
            const beforeData = excelData.filter(item => item["After/Before Kanban"] === "Before");
            const afterData = excelData.filter(item => item["After/Before Kanban"] === "After");

            // Create workbook and append two sheets
            const workbook = XLSX.utils.book_new();
            if (beforeData.length > 0) {
                const beforeWorksheet = XLSX.utils.json_to_sheet(beforeData);
                XLSX.utils.book_append_sheet(workbook, beforeWorksheet, "BeforeKanban");
            }
            if (afterData.length > 0) {
                const afterWorksheet = XLSX.utils.json_to_sheet(afterData);
                XLSX.utils.book_append_sheet(workbook, afterWorksheet, "AfterKanban");
            }

            // Generate filename with timestamp
            const now = new Date();
            const filename = `ScrapData_${now.toISOString().replace(/[:.]/g, '-')}.xlsx`;

            // Write the workbook to file
            XLSX.writeFile(workbook, filename);

            const successMessage = result.message
                ? `<div class="alert alert-warning">${escapeHtml(result.message)}</div>`
                : `<div class="alert alert-success"><strong>Thành công:</strong> Dữ liệu đã được tải xuống.</div>`;

            statusContainer.innerHTML = successMessage;
        } else {
            statusContainer.innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> ${escapeHtml(result.message)}</div>`;
        }
    } catch (error) {
        statusContainer.innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> Không thể kết nối đến API. Vui lòng kiểm tra lại.</div>`;
        console.error("Error:", error);
    }
}


// Xử lý tạo task bonepile 2
async function processCreateTaskBonepile(sNs, resultDivId) {
    const resultDiv = document.getElementById(resultDivId);
    let statusContainer = resultDiv.querySelector('.action-feedback');
    if (!statusContainer) {
        statusContainer = document.createElement('div');
        statusContainer.className = 'action-feedback mb-2';
        resultDiv.prepend(statusContainer);
    }
    statusContainer.innerHTML = `<div class="alert alert-info"><strong>Thông báo:</strong> Đang tải xuống dữ liệu...</div>`;

    const requestData = { sNs };

    try {
        const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/create-task-bonepile", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(requestData)
        });

        const result = await response.json();
        if (response.ok) {
            const payload = Array.isArray(result.data) ? result.data : [];
            if (!payload.length) {
                statusContainer.innerHTML = `<div class="alert alert-warning"><strong>Thông báo:</strong> Không có dữ liệu để tải xuống.</div>`;
                return;
            }

            const locationMap = await fetchLocationMap(payload.map(item => item.boardSN));

            const excelData = payload.map(item => ({
                InternalTask: item.internalTask ?? "N/A",
                Item: item.item ?? "N/A",
                Project: item.project ?? "N/A",
                OPN: item.opn ?? "N/A",
                Purpose: item.purpose ?? "N/A",
                "IC PN": item.icPn ?? "N/A",
                "IC Detail PN": item.icDetailPn ?? "N/A",
                "Board SN": item.boardSN ?? "N/A",
                "After/Before Kanban": item.afterBeforeKanban ?? "N/A",
                Category: item.category ?? "N/A",
                CM: item.cm ?? "N/A",
                Plant: item.plant ?? "N/A",
                Sloc: item.sloc ?? "N/A",
                "Task Number": item.taskNumber ?? "N/A",
                "PO Number": item.poNumber ?? "N/A",
                "Create By": item.createBy ?? "N/A",
                "Create Date": item.createDate ?? "N/A",
                Cost: item.cost ?? "N/A",
                Remark: item.smtTime ?? "N/A",
                Description: item.description ?? "N/A",
                SpeApproveTime: item.speApproveTime ?? "N/A",
                Location: locationMap[normalizeSerialNumber(item.boardSN)] ?? "N/A",
                ConsigIC1: item.icDetailPn ?? "N/A",
                Qty: item.qty ?? "N/A"
            }));

            // Sắp xếp dữ liệu theo InternalTask
            excelData.sort((a, b) => a.InternalTask.localeCompare(b.InternalTask));

            // Filter data into Before and After based on "After/Before Kanban"
            const beforeData = excelData.filter(item => item["After/Before Kanban"] === "Before");
            const afterData = excelData.filter(item => item["After/Before Kanban"] === "After");

            // Create workbook and append two sheets
            const workbook = XLSX.utils.book_new();
            if (beforeData.length > 0) {
                const beforeWorksheet = XLSX.utils.json_to_sheet(beforeData);
                XLSX.utils.book_append_sheet(workbook, beforeWorksheet, "BeforeKanban");
            }
            if (afterData.length > 0) {
                const afterWorksheet = XLSX.utils.json_to_sheet(afterData);
                XLSX.utils.book_append_sheet(workbook, afterWorksheet, "AfterKanban");
            }

            // Generate filename with timestamp
            const now = new Date();
            const filename = `ScrapData_${now.toISOString().replace(/[:.]/g, '-')}.xlsx`;

            // Write the workbook to file
            XLSX.writeFile(workbook, filename);

            const successMessage = result.message
                ? `<div class="alert alert-warning">${escapeHtml(result.message)}</div>`
                : `<div class="alert alert-success"><strong>Thành công:</strong> Dữ liệu đã được tải xuống.</div>`;

            statusContainer.innerHTML = successMessage;
        } else {
            statusContainer.innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> ${escapeHtml(result.message)}</div>`;
        }
    } catch (error) {
        statusContainer.innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> Không thể kết nối đến API. Vui lòng kiểm tra lại.</div>`;
        console.error("Error:", error);
    }
}

// Hiển thị bảng với DataTables
function renderTableWithDataTable(data, tableId, checkboxName, selectAllId) {
    const currentSelectedSet = checkboxName === "task-checkbox" ? selectedInternalTasks : selectedHistoryInternalTasks;
    const tableBody = document.querySelector(`#${tableId} tbody`);
    tableBody.innerHTML = ""; // Xóa nội dung cũ

    data.forEach(item => {
        const isChecked = currentSelectedSet.has(item.internalTask) ? 'checked' : '';
        const isHistory = checkboxName === "history-task-checkbox";
        const purposeText = formatPurpose(item.purpose);
        const timelineValue = isHistory ? (item.applyTime || "N/A") : (item.applyTaskStatus || "N/A");
        const statusOrQty = isHistory ? (item.applyTaskStatus || "N/A") : (item.totalQty || "N/A");

        const safeInternalTask = escapeHtml(item.internalTask ?? "N/A");
        const safeDescription = escapeHtml(item.description ?? "N/A");
        const safeApprove = escapeHtml(item.approveScrapPerson ?? "N/A");
        const safeKanban = escapeHtml(item.kanBanStatus ?? "N/A");
        const safeCategory = escapeHtml(item.category ?? "N/A");
        const safeRemark = escapeHtml(item.remark ?? "N/A");
        const safePurpose = escapeHtml(purposeText);
        const safeCreateTime = escapeHtml(item.createTime ?? "N/A");
        const safeCreateBy = escapeHtml(item.createBy ?? "N/A");
        const safeTimeline = escapeHtml(timelineValue ?? "N/A");
        const safeStatusOrQty = escapeHtml(statusOrQty ?? "N/A");
        const safeTotalQty = escapeHtml(item.totalQty ?? "N/A");

        const purposeColumn = isHistory
            ? ""
            : `<td data-bs-toggle="tooltip" data-bs-title="${safePurpose}">${safePurpose}</td>`;
        const extraQtyColumn = isHistory
            ? `<td data-bs-toggle="tooltip" data-bs-title="${safeTotalQty}">${safeTotalQty}</td>`
            : "";

        const row = `
            <tr>
                <td class="checkbox-column"><input type="checkbox" name="${checkboxName}" value="${escapeHtml(item.internalTask ?? '')}" ${isChecked}></td>
                <td data-bs-toggle="tooltip" data-bs-title="${safeInternalTask}">${safeInternalTask}</td>
                <td data-bs-toggle="tooltip" data-bs-title="${safeDescription}">${safeDescription}</td>
                <td data-bs-toggle="tooltip" data-bs-title="${safeApprove}">${safeApprove}</td>
                <td data-bs-toggle="tooltip" data-bs-title="${safeKanban}">${safeKanban}</td>
                <td data-bs-toggle="tooltip" data-bs-title="${safeCategory}">${safeCategory}</td>
                <td data-bs-toggle="tooltip" data-bs-title="${safeRemark}">${safeRemark}</td>
                ${purposeColumn}
                <td data-bs-toggle="tooltip" data-bs-title="${safeCreateTime}">${safeCreateTime}</td>
                <td data-bs-toggle="tooltip" data-bs-title="${safeCreateBy}">${safeCreateBy}</td>
                <td data-bs-toggle="tooltip" data-bs-title="${safeTimeline}">${safeTimeline}</td>
                <td data-bs-toggle="tooltip" data-bs-title="${safeStatusOrQty}">${safeStatusOrQty}</td>
                ${extraQtyColumn}
            </tr>
        `;
        tableBody.insertAdjacentHTML('beforeend', row);
    });

    // Khởi tạo DataTables
    const dataTable = $(`#${tableId}`).DataTable({
        pageLength: 10,
        lengthMenu: [10, 25, 50, 100],
        order: [],
        columnDefs: [
            { orderable: false, targets: 0 },
            { width: "40px", targets: 0 },
            { width: "300px", targets: [2] }
        ],
        language: {
            search: "Tìm kiếm:",
            lengthMenu: "Hiển thị _MENU_ dòng mỗi trang",
            info: "Hiển thị _START_ đến _END_ của _TOTAL_ dòng",
            paginate: {
                first: "Đầu",
                last: "Cuối",
                next: "Tiếp",
                previous: "Trước"
            }
        },
        destroy: true,
        drawCallback: function () {
            // Khởi tạo Bootstrap Tooltip sau khi bảng được vẽ
            const tooltipTriggerList = document.querySelectorAll(`#${tableId} [data-bs-toggle="tooltip"]`);
            tooltipTriggerList.forEach(tooltipTriggerEl => {
                new bootstrap.Tooltip(tooltipTriggerEl, {
                    placement: 'top', // Vị trí tooltip (top, bottom, left, right)
                    trigger: 'hover' // Kích hoạt khi hover
                });
            });
        }
    });

    // Xử lý checkbox "Select All"
    const selectAllCheckbox = document.getElementById(selectAllId);
    if (selectAllCheckbox) {
        selectAllCheckbox.checked = data.length > 0 && data.every(item => currentSelectedSet.has(item.internalTask));
        selectAllCheckbox.addEventListener("change", function () {
            const isChecked = this.checked;
            data.forEach(item => {
                if (isChecked) currentSelectedSet.add(item.internalTask);
                else currentSelectedSet.delete(item.internalTask);
            });
            $(`input[name="${checkboxName}"]`).prop("checked", isChecked);
        });
    }

    // Xử lý checkbox riêng lẻ
    $(document).off("change", `input[name="${checkboxName}"]`).on("change", `input[name="${checkboxName}"]`, function () {
        const internalTask = this.value;
        if (this.checked) currentSelectedSet.add(internalTask);
        else currentSelectedSet.delete(internalTask);
        selectAllCheckbox.checked = data.every(item => currentSelectedSet.has(item.internalTask));
    });
}

// Xử lý khi trang tải
document.addEventListener("DOMContentLoaded", function () {
    console.log("DOMContentLoaded triggered");
    hideAllElements();

    // Xử lý nút "NHẬP SN"
    document.getElementById("input-sn-btn").addEventListener("click", async function () {
        const snInput = document.getElementById("sn-input").value.trim();
        const description = document.getElementById("description-input").value.trim();
        const approveScrapPerson = document.getElementById("NVmember-input").value.trim();
        const purpose = document.getElementById("Scrap-options").value;
        const speApproveTime = document.getElementById("speApproveTime-input").value;
        const currentUsername = $('#analysisPerson').val();

        if (!currentUsername) {
            alert("Không thể lấy tên người dùng hiện tại.");
            return;
        }

        const sNs = snInput.split(/\r?\n/).map(sn => sn.trim()).filter(sn => sn);
        if (!sNs.length) {
            alert("Vui lòng nhập ít nhất một Serial Number hợp lệ.");
            return;
        }
        if (!description) {
            alert("Vui lòng nhập mô tả.");
            return;
        }
        if (!approveScrapPerson) {
            alert("Vui lòng nhập tên người phê duyệt.");
            return;
        }
        if (!["0", "1", "2", "3", "4"].includes(purpose)) {
            alert("Vui lòng chọn loại scrap.");
            return;
        }

        if (!speApproveTime) {
            alert("Vui lòng nhập thời gian NV phê duyệt.");
            return;
        }

        const resultDiv = document.getElementById("input-sn-result");
        resultDiv.innerHTML = `<div class="alert alert-info"><strong>Thông báo:</strong> Đang chờ xử lý...</div>`;

        try {
            const requestData = { sNs, createdBy: currentUsername, description, approveScrapPerson, purpose, speApproveTime };
            const inputSnResponse = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/input-sn", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(requestData)
            });

            const inputSnResult = await inputSnResponse.json();
            if (inputSnResponse.ok) {
                // Gọi API UpdateScrap nếu input-sn thành công
                let scrapStatus;
                switch (purpose) {
                    case "0": scrapStatus = "SPE approve to scrap"; break;
                    case "1": scrapStatus = "Scrap to quarterly"; break;
                    case "2": scrapStatus = "Approved to engineer sample"; break;
                    case "3": scrapStatus = "Approved to master board"; break;
                    case "4": scrapStatus = "SPE approve to BGA"; break;
                    default: scrapStatus = "Unknown"; break;
                }

                let smartRepairResult = null;
                if (["0", "1", "2", "3"].includes(purpose)) {
                    smartRepairResult = await callSmartRepairApi(sNs, "0", currentUsername);
                }

                const updateProductRequest = { serialNumbers: sNs, scrapStatus };
                const updateProductResponse = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Product/UpdateScrap", {
                    method: "PUT",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(updateProductRequest)
                });

                const updateProductResult = await updateProductResponse.json();
                const messageParts = [
                    `<strong>${inputSnResult.message}</strong>`,
                    `Internal Task: ${inputSnResult.internalTask}`
                ];

                if (smartRepairResult) {
                    const smartRepairMessage = smartRepairResult.success
                        ? `SmartRepair: ${smartRepairResult.message}`
                        : `<strong>SmartRepair lỗi:</strong> ${smartRepairResult.message}`;
                    messageParts.push(smartRepairMessage);
                }

                if (updateProductResponse.ok && updateProductResult.success) {
                    messageParts.push(`Update Product: ${updateProductResult.message}`);
                    resultDiv.innerHTML = `<div class="alert alert-success">${messageParts.join("<br>")}</div>`;
                } else {
                    messageParts.push(`<strong>Lỗi khi cập nhật Product:</strong> ${updateProductResult.message || "Không có thông tin lỗi"}`);
                    resultDiv.innerHTML = `<div class="alert alert-warning">${messageParts.join("<br>")}</div>`;
                }
            } else {
                resultDiv.innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> ${inputSnResult.message}</div>`;
            }
        } catch (error) {
            resultDiv.innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> Không thể kết nối đến API. Vui lòng kiểm tra lại.</div>`;
            console.error("Error:", error);
        }
    });

    // Xử lý nút "Tạo Task"
    document.getElementById("create-task-btn").addEventListener("click", async function () {
        const selectedTasksArray = Array.from(selectedInternalTasks);
        if (selectedTasksArray.length === 0) {
            alert("Vui lòng chọn ít nhất một Internal Task.");
            return;
        }
        await processCreateTask(selectedTasksArray, "create-task-result");
        selectedInternalTasks.clear();
        document.querySelectorAll('input[name="task-checkbox"]').forEach(cb => cb.checked = false);
        const selectAll = document.getElementById('select-all');
        if (selectAll) {
            selectAll.checked = false;
        }
    });

    // Xử lý nút "Tạo Task bằng SN"
    document.getElementById("create-task-btn-sn").addEventListener("click", async function () {
        const snInput = document.getElementById("create-task-btn-sn-box").value.trim();
        const sNs = snInput.split(/\r?\n/).map(sn => sn.trim()).filter(sn => sn);
        if (sNs.length === 0) {
            alert("Vui lòng nhập ít nhất một Serial Number hợp lệ.");
            return;
        }
        await processCreateTaskBySN(sNs, "create-task-result-sn");
        document.getElementById("create-task-btn-sn-box").value = "";
    });

    // Xử lý nút "Tạo Task Bonepile 2.0"
    document.getElementById("create-task-btn-bp2").addEventListener("click", async function () {
        const snInput = document.getElementById("creat-task-bp2").value.trim();
        const sNs = snInput.split(/\r?\n/).map(sn => sn.trim()).filter(sn => sn);
        if (sNs.length === 0) {
            alert("Vui lòng nhập ít nhất một Serial Number hợp lệ.");
            return;
        }
        await processCreateTaskBonepile(sNs, "create-task-result-bp2");
        document.getElementById("create-task-btn-sn-box").value = "";
    });

    // Xử lý nút "Cập nhật Task PO"
    document.getElementById("update-task-btn").addEventListener("click", async function () {
        const snInput = document.getElementById("sn-input-update").value.trim();
        const task = document.getElementById("task-input").value.trim();
        const po = document.getElementById("po-input").value.trim();
        const snList = snInput.split(/\r?\n/).map(sn => sn.trim()).filter(sn => sn);
        const currentUsername = $('#analysisPerson').val();

        if (!currentUsername) {
            alert("Không thể lấy tên người dùng hiện tại.");
            return;
        }
        const resultDiv = document.getElementById("update-data-result");
        resultDiv.innerHTML = `<div class="alert alert-info"><strong>Thông báo:</strong> Đang chờ xử lý...</div>`;

        if (!snList.length) {
            alert("Vui lòng nhập ít nhất một Serial Number hợp lệ.");
            return;
        }
        if (!task) {
            alert("Vui lòng nhập Task.");
            return;
        }
        if (!po) {
            alert("Vui lòng nhập PO.");
            return;
        }

        try {
            const updateTaskRequest = { snList, task, po };
            const updateResponse = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/update-task-po", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(updateTaskRequest)
            });

            const updateResult = await updateResponse.json();
            if (updateResponse.ok) {
                const smartRepairResult = await callSmartRepairApi(snList, "5", task, currentUsername);
                const messages = [
                    `<strong>Thành công:</strong> ${updateResult.message}`
                ];

                if (smartRepairResult.success) {
                    messages.push(`SmartRepair: ${smartRepairResult.message}`);
                    resultDiv.innerHTML = `<div class="alert alert-success">${messages.join("<br>")}</div>`;
                } else {
                    messages.push(`<strong>SmartRepair lỗi:</strong> ${smartRepairResult.message}`);
                    resultDiv.innerHTML = `<div class="alert alert-warning">${messages.join("<br>")}</div>`;
                }
            } else {
                resultDiv.innerHTML = `<div class="alert alert-warning"><strong>Lỗi khi cập nhật Task PO:</strong> ${updateResult.message}</div>`;
            }
        } catch (error) {
            resultDiv.innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> Không thể kết nối đến API. Vui lòng kiểm tra lại.</div>`;
            console.error("Error:", error);
        }
    });

    // Xử lý nút "Cập nhật COST"
    document.getElementById("update-cost-btn").addEventListener("click", async function () {
        const fileInput = document.getElementById("file-input");
        const resultDiv = document.getElementById("update-data-result");

        if (!fileInput.files || fileInput.files.length === 0) {
            alert("Vui lòng chọn một file Excel để tải lên.");
            return;
        }

        const file = fileInput.files[0];
        const reader = new FileReader();

        reader.onload = async function (e) {
            try {
                const data = new Uint8Array(e.target.result);
                const workbook = XLSX.read(data, { type: "array" });
                const firstSheetName = workbook.SheetNames[0];
                const worksheet = workbook.Sheets[firstSheetName];
                const jsonData = XLSX.utils.sheet_to_json(worksheet);

                if (!jsonData || jsonData.length === 0) {
                    alert("File Excel không chứa dữ liệu.");
                    return;
                }

                const requiredColumns = ["Board SN", "Cost"];
                const columns = Object.keys(jsonData[0]);
                const missingColumns = requiredColumns.filter(col => !columns.includes(col));
                if (missingColumns.length > 0) {
                    alert(`File Excel thiếu các cột: ${missingColumns.join(", ")}.`);
                    return;
                }

                const boardSNs = [];
                const costs = [];
                jsonData.forEach(row => {
                    const boardSN = row["Board SN"] ? String(row["Board SN"]).trim() : null;
                    const cost = row["Cost"] != null ? Number(row["Cost"]) : null;
                    if (boardSN && cost != null) {
                        boardSNs.push(boardSN);
                        costs.push(cost);
                    }
                });

                if (boardSNs.length === 0 || costs.length === 0 || boardSNs.length !== costs.length) {
                    alert("Dữ liệu trong file Excel không hợp lệ. Vui lòng kiểm tra lại cột 'Board SN' và 'Cost'.");
                    return;
                }

                const requestData = { boardSNs, costs };
                resultDiv.innerHTML = `<div class="alert alert-info"><strong>Thông báo:</strong> Đang chờ xử lý...</div>`;

                const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/update-cost", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(requestData)
                });

                const result = await response.json();
                resultDiv.innerHTML = response.ok
                    ? `<div class="alert alert-success"><strong>${result.message}</strong></div>`
                    : `<div class="alert alert-danger"><strong>Lỗi:</strong> ${result.message}</div>`;
            } catch (error) {
                resultDiv.innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> Không thể xử lý file Excel hoặc kết nối đến API.</div>`;
                console.error("Error:", error);
            }
        };

        reader.onerror = function () {
            alert("Không thể đọc file Excel. Vui lòng kiểm tra lại file.");
        };
        reader.readAsArrayBuffer(file);
    });

    // Xử lý thay đổi dropdown
    document.getElementById("search-options").addEventListener("change", async function () {
        console.log("Dropdown changed to:", this.value);
        hideAllElements();
        selectedInternalTasks.clear();
        selectedHistoryInternalTasks.clear();

        const selectedValue = this.value;
        if (selectedValue === "INPUT_SN") {
            document.getElementById("input-sn-form").classList.remove("hidden");
            document.getElementById("input-sn-result").classList.remove("hidden");
        }
        else if (selectedValue === "CREATE_TASK_FORM") {
            document.getElementById("custom-form").classList.remove("hidden");
            document.getElementById("create-task-result").classList.remove("hidden");

            try {
                const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/get-scrap-status-zero", {
                    method: "GET",
                    headers: { "Content-Type": "application/json" }
                });

                const result = await response.json();
                if (response.ok) {
                    const sortedData = result.sort((a, b) => new Date(a.createTime) - new Date(b.createTime));
                    renderTableWithDataTable(sortedData, "task-checkbox-table", "task-checkbox", "select-all");
                } else {
                    document.getElementById("create-task-result").innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> ${result.message}</div>`;
                }
            } catch (error) {
                document.getElementById("create-task-result").innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> Không thể kết nối đến API.</div>`;
                console.error("Error:", error);
            }
        }
        else if (selectedValue === "CREATE_TASK_FORM_SN") {
            document.getElementById("custom-form-sn").classList.remove("hidden");
            document.getElementById("create-task-result-sn").classList.remove("hidden");
        }
        else if (selectedValue === "CREATE_TASK_BP2") {
            document.getElementById("custom-form-bp2").classList.remove("hidden");
            document.getElementById("create-task-result-bp2").classList.remove("hidden");
        }
        else if (selectedValue === "UPDATE_DATA") {
            document.getElementById("update-data-form").classList.remove("hidden");
            document.getElementById("update-data-result").classList.remove("hidden");
        } else if (selectedValue === "HISTORY_APPLY") {
            document.getElementById("history-apply-form").classList.remove("hidden");
            document.getElementById("history-apply-result").classList.remove("hidden");

            try {
                const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/get-history-apply", {
                    method: "GET",
                    headers: { "Content-Type": "application/json" }
                });

                const result = await response.json();
                if (response.ok) {
                    const sortedData = result.sort((a, b) => new Date(b.applyTime) - new Date(a.applyTime));
                    renderTableWithDataTable(sortedData, "history-task-checkbox-table", "history-task-checkbox", "select-all-history");
                } else {
                    document.getElementById("history-apply-result").innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> ${result.message}</div>`;
                }
            } catch (error) {
                document.getElementById("history-apply-result").innerHTML = `<div class="alert alert-danger"><strong>Lỗi:</strong> Không thể kết nối đến API.</div>`;
                console.error("Error:", error);
            }
        }
    });
});
