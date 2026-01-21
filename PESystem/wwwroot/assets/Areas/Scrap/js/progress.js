let selectedInternalTasks = new Set(); // Lưu các InternalTask đã chọn (cho TASK_STOCK_STATUS)
let selectedSNs = new Set(); // Lưu các SN đã chọn (cho SEARCH_STATUS)

function htmlEscape(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function displayValue(value) {
    const normalized = value === undefined || value === null || value === '' ? 'N/A' : value;
    return htmlEscape(normalized);
}

// Hàm để ẩn tất cả các form và khu vực kết quả
function hideAllElements() {
    const forms = ["task-stock-status-form", "search-status-form", "search-history-form"]; // bo  "update-status-form",
    const results = ["task-stock-status-result", "search-status-result", "history-search-result"]; // "update-status-result",

    forms.forEach(formId => {
        const form = document.getElementById(formId);
        if (form) {
            form.classList.add("hidden");
        } else {
            console.warn(`Form with ID ${formId} not found.`);
        }
    });

    results.forEach(resultId => {
        const result = document.getElementById(resultId);
        if (result) {
            result.classList.add("hidden");
        } else {
            console.warn(`Result with ID ${resultId} not found.`);
        }
    });

    // Xóa giá trị của các trường nhập liệu khi ẩn form
    const snStatusUpdate = document.getElementById("sn-status-update");
    const statusOptions = document.getElementById("status-options");
    const searchStatusUpdate = document.getElementById("search-status-update");
    const searchStatusOptions = document.getElementById("search-status-options");
    const historySearchUpdate = document.getElementById("history-search-update");

    if (snStatusUpdate) snStatusUpdate.value = "";
    if (statusOptions) statusOptions.selectedIndex = 0;
    if (searchStatusUpdate) searchStatusUpdate.value = "";
    if (searchStatusOptions) searchStatusOptions.selectedIndex = 0;
    if (historySearchUpdate) historySearchUpdate.value = "";
}

// Hàm tạo và tải xuống file Excel
function exportToExcel(noInternalTaskData, hasInternalTaskNoTaskNumberData, hasTaskNumberData, filename) {
    const workbook = XLSX.utils.book_new();

    // Thêm sheet cho dữ liệu chưa có Internal Task
    if (noInternalTaskData.length > 0) {
        const noInternalTaskSheet = XLSX.utils.json_to_sheet(noInternalTaskData);
        XLSX.utils.book_append_sheet(workbook, noInternalTaskSheet, "NoInternalTask");
    }

    // Thêm sheet cho dữ liệu có Internal Task nhưng chưa có Task Number
    if (hasInternalTaskNoTaskNumberData.length > 0) {
        const hasInternalTaskNoTaskNumberSheet = XLSX.utils.json_to_sheet(hasInternalTaskNoTaskNumberData);
        XLSX.utils.book_append_sheet(workbook, hasInternalTaskNoTaskNumberSheet, "HasInternalNoTaskNumber");
    }

    // Thêm sheet cho dữ liệu đã có Task Number
    if (hasTaskNumberData.length > 0) {
        const hasTaskNumberSheet = XLSX.utils.json_to_sheet(hasTaskNumberData);
        XLSX.utils.book_append_sheet(workbook, hasTaskNumberSheet, "HasTaskNumber");
    }

    XLSX.writeFile(workbook, filename);
}

function exportHistoryToExcel(historyData, filename) {
    if (!Array.isArray(historyData) || !historyData.length) {
        throw new Error("No history data to export");
    }

    const workbook = XLSX.utils.book_new();
    const sheetData = historyData.map(item => ({
        "#": item.rowNumber ?? "",
        "History ID": item.id ?? "",
        "SN": item.sn ?? "",
        "Internal Task": item.internalTask ?? "",
        "Description": item.description ?? "",
        "KanBan Status": item.kanBanStatus ?? "",
        "Sloc": item.sloc ?? "",
        "Task Number": item.taskNumber ?? "",
        "PO": item.po ?? "",
        "Cost": item.cost ?? "",
        "Created By": item.createdBy ?? "",
        "Create Time": item.createTime ?? "",
        "Approve Scrap Person": item.approveScrapPerson ?? "",
        "Apply Task Status": item.applyTaskStatus ?? "",
        "Find Board Status": item.findBoardStatus ?? "",
        "Remark": item.remark ?? "",
        "Purpose": item.purpose ?? "",
        "Category": item.category ?? "",
        "Apply Time": item.applyTime ?? "",
        "Spe Approve Time": item.speApproveTime ?? ""
    }));

    const historySheet = XLSX.utils.json_to_sheet(sheetData);
    XLSX.utils.book_append_sheet(workbook, historySheet, "History");

    XLSX.writeFile(workbook, filename);
}
// Hàm hiển thị bảng với phân trang
function renderTableWithPagination(data, resultDiv, tableHeaders, rowTemplate, extraHtml = "", tableIdPrefix = "") {
    const rowsPerPage = 10; // Số dòng mỗi trang
    let currentPage = 1; // Trang hiện tại
    const totalRows = data.length; // Tổng số dòng
    const totalPages = Math.ceil(totalRows / rowsPerPage); // Tổng số trang

    // Hàm hiển thị dữ liệu cho trang hiện tại
    function displayPage(page) {
        const start = (page - 1) * rowsPerPage;
        const end = start + rowsPerPage;
        const paginatedData = data.slice(start, end);

        let tableHtml = `
            <div class="table-container soft-scroll">
                <table class="stacked-result-table">
                    <thead>
                        <tr>
                            ${tableHeaders}
                        </tr>
                    </thead>
                    <tbody>
        `;

        paginatedData.forEach(item => {
            tableHtml += rowTemplate(item);
        });

        if (!paginatedData.length) {
            const columnCount = (tableHeaders.match(/<th/g) || []).length || 1;
            tableHtml += `
                <tr>
                    <td colspan="${columnCount}" class="text-center text-muted">Không có dữ liệu để hiển thị</td>
                </tr>
            `;
        }

        tableHtml += `
                    </tbody>
                </table>
            </div>
        `;

        // Thêm nút phân trang nếu cần
        if (totalRows > rowsPerPage) {
            const prevButtonId = tableIdPrefix ? `${tableIdPrefix}-prev-page` : "prev-page";
            const nextButtonId = tableIdPrefix ? `${tableIdPrefix}-next-page` : "next-page";
            tableHtml += `
                <div class="pagination" style="margin-top: 10px; text-align: center;">
                    <button id="${prevButtonId}" class="btn btn-sm btn-secondary" ${currentPage === 1 ? 'disabled' : ''}>Previous</button>
                    <span style="margin: 0 10px;">Page ${currentPage} of ${totalPages}</span>
                    <button id="${nextButtonId}" class="btn btn-sm btn-secondary" ${currentPage === totalPages ? 'disabled' : ''}>Next</button>
                </div>
            `;
        }

        // Thêm HTML bổ sung (nếu có, ví dụ: thông báo SN không tìm thấy)
        tableHtml += extraHtml;

        resultDiv.innerHTML = tableHtml;

        // Thêm sự kiện cho nút phân trang (sau khi HTML được render)
        if (totalRows > rowsPerPage) {
            const prevButtonId = tableIdPrefix ? `${tableIdPrefix}-prev-page` : "prev-page";
            const nextButtonId = tableIdPrefix ? `${tableIdPrefix}-next-page` : "next-page";
            const prevButton = document.getElementById(prevButtonId);
            const nextButton = document.getElementById(nextButtonId);

            if (prevButton) {
                prevButton.addEventListener("click", () => {
                    if (currentPage > 1) {
                        currentPage--;
                        displayPage(currentPage);
                    }
                });
            }

            if (nextButton) {
                nextButton.addEventListener("click", () => {
                    if (currentPage < totalPages) {
                        currentPage++;
                        displayPage(currentPage);
                    }
                });
            }
        }
    }

    // Hiển thị trang đầu tiên
    displayPage(currentPage);
}

// Hàm gọi API và hiển thị bảng dữ liệu cho TASK_STOCK_STATUS
async function loadFindBoardStatus() {
    const resultDiv = document.getElementById("task-stock-status-result");

    // Hiển thị thông báo "đang tải dữ liệu"
    resultDiv.innerHTML = `
        <div class="alert alert-info">
            <strong>Thông báo:</strong> Đang tải dữ liệu...
        </div>
    `;

    try {
        // Gọi API để lấy dữ liệu FindBoardStatus
        const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/checking-scrap-quarterly", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                type: "summary"
            })
        });

        const result = await response.json();

        if (response.ok) {
            // Sắp xếp dữ liệu theo CreateTime (từ cũ đến mới)
            //const sortedData = result.sort((a, b) => new Date(a.createTime) - new Date(b.createTime));

            const tableHeaders = `
                <th>Year</th>
                <th>Quater</th>
                <th>Total q'ty</th>
                <th>Waiting Approve scrap</th>
                <th>Approved scrap but haven't Task</th>
                <th>Approved scrap and have Task</th>
                <th>Moved Scrap Stock</th>
            `;

            const rowTemplate = (item) => {
                let statusBackgroundColor = "";
                if (item.status === "close") {
                    statusBackgroundColor = "#28a745"; // Màu xanh lá cây (Bootstrap success)
                } else if (item.status === "on-going") {
                    statusBackgroundColor = "#ffc107"; // Màu vàng (Bootstrap warning)
                }

                return `
                    <tr>
                        <td>${item.year}</td>
                        <td>${item.quarter}</td>
                        <td>${item.total}</td>
                        <td>${item.waitingApproved}</td>
                        <td>${item.haventTask}</td>
                        <td>${item.haveTask}</td>
                        <td>${item.movedScrap}</td>
                `;
            };

            renderTableWithPagination(result.data, resultDiv, tableHeaders, rowTemplate, "", "task-stock");
        } else {
            resultDiv.innerHTML = `
                <div class="alert alert-danger">
                    <strong>Lỗi:</strong> ${result.message}
                </div>
            `;
        }
    } catch (error) {
        resultDiv.innerHTML = `
            <div class="alert alert-danger">
                <strong>Lỗi:</strong> Không thể kết nối đến API. Vui lòng kiểm tra lại.
            </div>
        `;
        console.error("Error:", error);
    }
}

// Hàm gọi API và hiển thị bảng dữ liệu cho SEARCH_STATUS
// Hàm gọi API và hiển thị bảng dữ liệu cho SEARCH_STATUS
async function searchStatus(searchType, searchValues) {
    const resultDiv = document.getElementById("search-status-result");

    // Hiển thị thông báo "đang tải dữ liệu"
    resultDiv.innerHTML = `
        <div class="alert alert-info">
            <strong>Thông báo:</strong> Đang tìm kiếm dữ liệu...
        </div>
    `;

    const requestData = {
        internalTasks: searchType === "2" ? searchValues : [],
        sNs: searchType === "1" ? searchValues : [],
        taskNumber: searchType === "3" ? searchValues : []
    };

    try {
        // Gọi API /api/Scrap/detail-task-status
        const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/detail-task-status", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(requestData)
        });

        const result = await response.json();

        if (response.ok) {
            // Sắp xếp dữ liệu theo CreateTime (từ cũ đến mới)
            const sortedData = result.data.sort((a, b) => new Date(a.createTime) - new Date(b.createTime));

            // Nếu tìm kiếm theo SN (searchType === "1"), so sánh danh sách SN gửi vào với SN trả về
            let unmatchedSNs = [];
            if (searchType === "1") {
                const returnedSNs = sortedData.map(item => item.sn);
                unmatchedSNs = searchValues.filter(sn => !returnedSNs.includes(sn));
            }

            // Chia dữ liệu thành 3 nhóm
            const noInternalTaskData = sortedData.filter(item => !item.internalTask || item.internalTask === "N/A");
            const hasInternalTaskNoTaskNumberData = sortedData.filter(item => item.internalTask && item.internalTask !== "N/A" && (!item.taskNumber || item.taskNumber === "N/A"));
            const hasTaskNumberData = sortedData.filter(item => item.taskNumber && item.taskNumber !== "N/A");

            // Định nghĩa tableHeaders không có cột checkbox
            const tableHeaders = `
                <th>SN</th>
                <th>Internal Task</th>
                <th>Description</th>
                <th>Approve Scrap Person</th>
                <th>KanBan Status</th>
                <th>Sloc</th>
                <th>Task Number</th>
                <th>PO</th>
                <th>Cost</th>
                <th>Remark</th>
                <th>Created By</th>
                <th>Create Time</th>
                <th>Apply Time</th>
                <th>Apply Task Status</th>
                <th>Find Board Status</th>
                <th>Purpose</th>
                <th>Category</th>
                <th>SpeApproveTime</th>
            `;

            // Định nghĩa rowTemplate không có cột checkbox
            const rowTemplate = (item) => `
                <tr>
                    <td>${displayValue(item.sn)}</td>
                    <td>${displayValue(item.internalTask)}</td>
                    <td>${displayValue(item.description)}</td>
                    <td>${displayValue(item.approveScrapPerson)}</td>
                    <td>${displayValue(item.kanBanStatus)}</td>
                    <td>${displayValue(item.sloc)}</td>
                    <td>${displayValue(item.taskNumber)}</td>
                    <td>${displayValue(item.po)}</td>
                    <td>${displayValue(item.cost)}</td>
                    <td>${displayValue(item.remark)}</td>
                    <td>${displayValue(item.createdBy)}</td>
                    <td>${displayValue(item.createTime)}</td>
                    <td>${displayValue(item.applyTime)}</td>
                    <td>${displayValue(item.applyTaskStatus)}</td>
                    <td>${displayValue(item.findBoardStatus)}</td>
                    <td>${displayValue(item.purpose)}</td>
                    <td>${displayValue(item.category)}</td>
                    <td>${displayValue(item.speApproveTime)}</td>
                </tr>
            `;

            // Tạo HTML cho thông báo SN không tìm thấy (nếu có)
            let unmatchedSNsHtml = "";
            if (unmatchedSNs.length > 0) {
                const escapedSNs = unmatchedSNs.map(htmlEscape).join(', ');
                unmatchedSNsHtml = `
                    <div class="alert alert-warning mt-3">
                        <strong>Cảnh báo:</strong> Có ${unmatchedSNs.length} SN không tồn tại trong ScrapList: ${escapedSNs}
                    </div>
                `;
            }

            // Xóa nội dung cũ và thêm các div con với ID duy nhất, bao gồm số lượng
            resultDiv.innerHTML = `
                <div class="grouped-result-grid">
                    <div class="scrap-card grouped-result">
                        <div class="grouped-result-header">
                            <h6>Chưa có Internal Task</h6>
                            <span class="badge bg-success-subtle">${noInternalTaskData.length}</span>
                        </div>
                        <div id="search-status-result-no-internal-task" class="grouped-result-table"></div>
                    </div>
                    <div class="scrap-card grouped-result">
                        <div class="grouped-result-header">
                            <h6>Đã có Internal Task nhưng chưa có Task Number</h6>
                            <span class="badge bg-success-subtle">${hasInternalTaskNoTaskNumberData.length}</span>
                        </div>
                        <div id="search-status-result-has-internal-no-task-number" class="grouped-result-table"></div>
                    </div>
                    <div class="scrap-card grouped-result">
                        <div class="grouped-result-header">
                            <h6>Đã có Task Number</h6>
                            <span class="badge bg-success-subtle">${hasTaskNumberData.length}</span>
                        </div>
                        <div id="search-status-result-has-task-number" class="grouped-result-table"></div>
                    </div>
                </div>
                ${unmatchedSNsHtml}
            `;

            // Render từng bảng vào các div con
            const noInternalTaskDiv = document.getElementById("search-status-result-no-internal-task");
            const hasInternalTaskNoTaskNumberDiv = document.getElementById("search-status-result-has-internal-no-task-number");
            const hasTaskNumberDiv = document.getElementById("search-status-result-has-task-number");

            renderTableWithPagination(noInternalTaskData, noInternalTaskDiv, tableHeaders, rowTemplate, "", "no-internal-task");
            renderTableWithPagination(hasInternalTaskNoTaskNumberData, hasInternalTaskNoTaskNumberDiv, tableHeaders, rowTemplate, "", "has-internal-no-task-number");
            renderTableWithPagination(hasTaskNumberData, hasTaskNumberDiv, tableHeaders, rowTemplate, "", "has-task-number");

            // Lưu dữ liệu đã chia nhóm vào một biến toàn cục để sử dụng khi xuất Excel
            window.searchStatusData = {
                noInternalTaskData: noInternalTaskData.map(item => ({
                    SN: item.sn ?? "N/A",
                    InternalTask: item.internalTask ?? "N/A",
                    Description: item.description ?? "N/A",
                    ApproveScrapPerson: item.approveScrapPerson ?? "N/A",
                    KanBanStatus: item.kanBanStatus ?? "N/A",
                    Sloc: item.sloc ?? "N/A",
                    TaskNumber: item.taskNumber ?? "N/A",
                    PO: item.po ?? "N/A",
                    Cost: item.cost ?? "N/A",
                    Remark: item.remark ?? "N/A",
                    CreatedBy: item.createdBy ?? "N/A",
                    CreateTime: item.createTime ?? "N/A",
                    ApplyTime: item.applyTime ?? "N/A",
                    ApplyTaskStatus: item.applyTaskStatus ?? "N/A",
                    FindBoardStatus: item.findBoardStatus ?? "N/A",
                    Purpose: item.purpose ?? "N/A",
                    Category: item.category ?? "N/A",
                    SpeApproveTime: item.speApproveTime ?? "N/A"
                })),
                hasInternalTaskNoTaskNumberData: hasInternalTaskNoTaskNumberData.map(item => ({
                    SN: item.sn ?? "N/A",
                    InternalTask: item.internalTask ?? "N/A",
                    Description: item.description ?? "N/A",
                    ApproveScrapPerson: item.approveScrapPerson ?? "N/A",
                    KanBanStatus: item.kanBanStatus ?? "N/A",
                    Sloc: item.sloc ?? "N/A",
                    TaskNumber: item.taskNumber ?? "N/A",
                    PO: item.po ?? "N/A",
                    Cost: item.cost ?? "N/A",
                    Remark: item.remark ?? "N/A",
                    CreatedBy: item.createdBy ?? "N/A",
                    CreateTime: item.createTime ?? "N/A",
                    ApplyTime: item.applyTime ?? "N/A",
                    ApplyTaskStatus: item.applyTaskStatus ?? "N/A",
                    FindBoardStatus: item.findBoardStatus ?? "N/A",
                    Purpose: item.purpose ?? "N/A",
                    SpeApproveTime: item.speApproveTime ?? "N/A"
                })),
                hasTaskNumberData: hasTaskNumberData.map(item => ({
                    SN: item.sn ?? "N/A",
                    InternalTask: item.internalTask ?? "N/A",
                    Description: item.description ?? "N/A",
                    ApproveScrapPerson: item.approveScrapPerson ?? "N/A",
                    KanBanStatus: item.kanBanStatus ?? "N/A",
                    Sloc: item.sloc ?? "N/A",
                    TaskNumber: item.taskNumber ?? "N/A",
                    PO: item.po ?? "N/A",
                    Cost: item.cost ?? "N/A",
                    Remark: item.remark ?? "N/A",
                    CreatedBy: item.createdBy ?? "N/A",
                    CreateTime: item.createTime ?? "N/A",
                    ApplyTime: item.applyTime ?? "N/A",
                    ApplyTaskStatus: item.applyTaskStatus ?? "N/A",
                    FindBoardStatus: item.findBoardStatus ?? "N/A",
                    Purpose: item.purpose ?? "N/A",
                    Category: item.category ?? "N/A",
                    SpeApproveTime: item.speApproveTime ?? "N/A"
                }))
            };
        } else {
            resultDiv.innerHTML = `
                <div class="alert alert-danger">
                    <strong>Lỗi:</strong> ${result.message}
                </div>
            `;
        }
    } catch (error) {
        resultDiv.innerHTML = `
            <div class="alert alert-danger">
                <strong>Lỗi:</strong> Không thể kết nối đến API. Vui lòng kiểm tra lại.
            </div>
        `;
        console.error("Error:", error);
    }
}

// Hàm gọi API và hiển thị bảng lịch sử cho HistoryScrapList theo danh sách SN
async function searchHistoryBySN(snValues) {
    const resultDiv = document.getElementById("history-search-result");

    resultDiv.innerHTML = `
        <div class="alert alert-info">
            <strong>Thông báo:</strong> Đang tải dữ liệu lịch sử...
        </div>
    `;

    const requestData = {
        sNs: snValues
    };

    window.historySearchData = null;

    try {
        const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/history-by-sn", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(requestData)
        });

        const result = await response.json();

        if (response.ok) {
            const historyData = Array.isArray(result.data) ? result.data : [];
            const missingSNs = Array.isArray(result.missingSNs) ? result.missingSNs : [];

            const normalizedHistory = historyData.map((item, index) => ({
                ...item,
                rowNumber: index + 1
            }));

            const tableHeaders = `
                <th>#</th>
                <th>History ID</th>
                <th>SN</th>
                <th>Internal Task</th>
                <th>Description</th>
                <th>KanBan Status</th>
                <th>Sloc</th>
                <th>Task Number</th>
                <th>PO</th>
                <th>Cost</th>
                <th>Created By</th>
                <th>Create Time</th>
                <th>Approve Scrap Person</th>
                <th>Apply Task Status</th>
                <th>Find Board Status</th>
                <th>Remark</th>
                <th>Purpose</th>
                <th>Category</th>
                <th>Apply Time</th>
                <th>Spe Approve Time</th>
            `;

            const rowTemplate = (item) => `
                <tr>
                    <td>${displayValue(item.rowNumber)}</td>
                    <td>${displayValue(item.id)}</td>
                    <td>${displayValue(item.sn)}</td>
                    <td>${displayValue(item.internalTask)}</td>
                    <td>${displayValue(item.description)}</td>
                    <td>${displayValue(item.kanBanStatus)}</td>
                    <td>${displayValue(item.sloc)}</td>
                    <td>${displayValue(item.taskNumber)}</td>
                    <td>${displayValue(item.po)}</td>
                    <td>${displayValue(item.cost)}</td>
                    <td>${displayValue(item.createdBy)}</td>
                    <td>${displayValue(item.createTime)}</td>
                    <td>${displayValue(item.approveScrapPerson)}</td>
                    <td>${displayValue(item.applyTaskStatus)}</td>
                    <td>${displayValue(item.findBoardStatus)}</td>
                    <td>${displayValue(item.remark)}</td>
                    <td>${displayValue(item.purpose)}</td>
                    <td>${displayValue(item.category)}</td>
                    <td>${displayValue(item.applyTime)}</td>
                    <td>${displayValue(item.speApproveTime)}</td>
                </tr>
            `;

            let extraHtml = "";
            if (missingSNs.length > 0) {
                const escapedMissing = missingSNs.map(htmlEscape).join(', ');
                extraHtml = `
                    <div class="alert alert-warning mt-3">
                        <strong>Cảnh báo:</strong> Không tìm thấy lịch sử cho ${missingSNs.length} SN: ${escapedMissing}
                    </div>
                `;
            }

            if (normalizedHistory.length === 0) {
                resultDiv.innerHTML = `
                    <div class="alert alert-warning">
                        <strong>Thông báo:</strong> Không tìm thấy dữ liệu lịch sử cho các SN đã nhập.
                    </div>
                    ${extraHtml}
                `;
                window.historySearchData = null;
                return;
            }

            window.historySearchData = {
                history: normalizedHistory,
                missingSNs,
                requestSNs: snValues
            };

            renderTableWithPagination(normalizedHistory, resultDiv, tableHeaders, rowTemplate, extraHtml, "history-search");
        } else {
            resultDiv.innerHTML = `
                <div class="alert alert-danger">
                    <strong>Lỗi:</strong> ${result.message ?? "Không thể lấy dữ liệu lịch sử."}
                </div>
            `;
            window.historySearchData = null;
        }
    } catch (error) {
        resultDiv.innerHTML = `
            <div class="alert alert-danger">
                <strong>Lỗi:</strong> Không thể kết nối đến API. Vui lòng kiểm tra lại.
            </div>
        `;
        console.error("Error:", error);
        window.historySearchData = null;
    }
}
// Ẩn tất cả các form và khu vực kết quả ngay lập tức khi trang tải
hideAllElements();

// Xử lý sự kiện khi trang tải lần đầu
document.addEventListener("DOMContentLoaded", function () {
    console.log("DOMContentLoaded triggered");
    hideAllElements();

    // Xử lý sự kiện thay đổi giá trị trong dropdown
    document.getElementById("search-options").addEventListener("change", async function () {
        console.log("Dropdown changed to:", this.value);
        hideAllElements();

        // Xóa các Set khi chuyển đổi form để tránh trạng thái cũ
        selectedInternalTasks.clear();
        selectedSNs.clear();

        const selectedValue = this.value;

        if (selectedValue === "TASK_STOCK_STATUS") {
            document.getElementById("task-stock-status-form").classList.remove("hidden");
            document.getElementById("task-stock-status-result").classList.remove("hidden");

            // Gọi API và hiển thị bảng ngay khi chọn TASK_STOCK_STATUS
            await loadFindBoardStatus();
        } /*else if (selectedValue === "UPDATE_STATUS") {
            document.getElementById("update-status-form").classList.remove("hidden");
            document.getElementById("update-status-result").classList.remove("hidden");
        }*/ else if (selectedValue === "SEARCH_STATUS") {
            document.getElementById("search-status-form").classList.remove("hidden");
            document.getElementById("search-status-result").classList.remove("hidden");
        } else if (selectedValue === "SEARCH_HISTORY") {
            document.getElementById("search-history-form").classList.remove("hidden");
            document.getElementById("history-search-result").classList.remove("hidden");
        }
    });

    // Xử lý sự kiện khi nhấn nút "Load List" trong TASK_STOCK_STATUS
    document.getElementById("task-stock-status-btn").addEventListener("click", async function () {
        const resultDiv = document.getElementById("task-stock-status-result");

        // Hiển thị thông báo "đang tải dữ liệu"
        resultDiv.innerHTML = `
        <div class="alert alert-info">
            <strong>Thông báo:</strong> Đang tải dữ liệu để xuất Excel...
        </div>
    `;

        try {
            // Gọi API để lấy dữ liệu FindBoardStatus
            const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/checking-scrap-quarterly", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    type: "detail"
                })
            });

            const result = await response.json();

            if (response.ok) {
                // Sắp xếp dữ liệu theo CreateTime (từ cũ đến mới)
                const sortedData = result.data.sort((a, b) => new Date(a.createTime) - new Date(b.createTime));

                // Chuyển dữ liệu thành định dạng cho file Excel
                const excelData = sortedData.map(item => ({
                    "SN": item.sn ?? "N/A",
                    "KanBan Status": item.kanBanStatus ?? "N/A",
                    "Task Number": item.taskNumber ?? "N/A",
                    "PO": item.po ?? "N/A",
                    "Created By": item.createdBy ?? "N/A",
                    "Description": item.description ?? "N/A",
                    "Create Time": item.createTime ?? "N/A",
                    "Apply Task Status": item.applyTaskStatus ?? "N/A",
                    "Remark": item.remark ?? "N/A",
                    "Purpose": item.purpose ?? "N/A"
                }));

                // Tạo và tải xuống file Excel
                const now = new Date();
                const filename = `TaskStockStatus_${now.toISOString().replace(/[:.]/g, '-')}.xlsx`;
                const workbook = XLSX.utils.book_new();
                const worksheet = XLSX.utils.json_to_sheet(excelData);
                XLSX.utils.book_append_sheet(workbook, worksheet, "TaskStockStatus");
                XLSX.writeFile(workbook, filename);

                // Hiển thị thông báo thành công
                resultDiv.innerHTML = `
                <div class="alert alert-success">
                    <strong>Thành công:</strong> File Excel đã được tải xuống.
                </div>
            `;
            } else {
                resultDiv.innerHTML = `
                <div class="alert alert-danger">
                    <strong>Lỗi:</strong> ${result.message}
                </div>
            `;
            }
        } catch (error) {
            resultDiv.innerHTML = `
            <div class="alert alert-danger">
                <strong>Lỗi:</strong> Không thể kết nối đến API. Vui lòng kiểm tra lại.
            </div>
        `;
            console.error("Error:", error);
        }
    });

    // Xử lý sự kiện khi nhấn nút "Search" trong form SEARCH_STATUS
    document.getElementById("search-status-btn").addEventListener("click", async function () {
        const resultDiv = document.getElementById("search-status-result");

        // Lấy danh sách SN hoặc InternalTask từ textarea
        const searchInput = document.getElementById("search-status-update").value.trim();
        const searchValues = searchInput.split(/\r?\n/).map(value => value.trim()).filter(value => value);

        // Lấy loại tìm kiếm từ dropdown
        const searchType = document.getElementById("search-status-options").value;

        // Kiểm tra dữ liệu đầu vào
        if (!searchValues.length) {
            resultDiv.innerHTML = `
                <div class="alert alert-warning">
                    <strong>Cảnh báo:</strong> Vui lòng nhập ít nhất một SN hoặc Internal Task hợp lệ.
                </div>
            `;
            return;
        }

        if (!searchType || (searchType !== "1" && searchType !== "2" && searchType !== "3")) {
            resultDiv.innerHTML = `
                <div class="alert alert-warning">
                    <strong>Cảnh báo:</strong> Vui lòng chọn loại tìm kiếm hợp lệ (Tìm kiếm theo SN hoặc Internal Task).
                </div>
            `;
            return;
        }

        // Gọi hàm tìm kiếm
        await searchStatus(searchType, searchValues);
    });

    // Xử lý sự kiện khi nhấn nút "Load List" trong form SEARCH_STATUS
    document.getElementById("load-status-btn").addEventListener("click", function () {
        const resultDiv = document.getElementById("search-status-result");

        // Kiểm tra xem dữ liệu đã được lưu từ searchStatus chưa
        if (!window.searchStatusData) {
            resultDiv.innerHTML = `
                <div class="alert alert-warning">
                    <strong>Cảnh báo:</strong> Vui lòng thực hiện tìm kiếm trước khi xuất Excel.
                </div>
            `;
            return;
        }

        // Hiển thị thông báo "đang tải dữ liệu"
        resultDiv.innerHTML = `
            <div class="alert alert-info">
                <strong>Thông báo:</strong> Đang tạo file Excel...
            </div>
        `;

        try {
            const { noInternalTaskData, hasInternalTaskNoTaskNumberData, hasTaskNumberData } = window.searchStatusData;

            // Tạo và tải xuống file Excel
            const now = new Date();
            const filename = `SearchStatusDetails_${now.toISOString().replace(/[:.]/g, '-')}.xlsx`;
            exportToExcel(noInternalTaskData, hasInternalTaskNoTaskNumberData, hasTaskNumberData, filename);

            // Hiển thị thông báo thành công
            resultDiv.innerHTML = `
                <div class="alert alert-success">
                    <strong>Thành công:</strong> File Excel đã được tải xuống với 3 sheet: Chưa có Internal Task, Đã có Internal Task nhưng chưa có Task Number, Đã có Task Number.
                </div>
            `;
        } catch (error) {
            resultDiv.innerHTML = `
                <div class="alert alert-danger">
                    <strong>Lỗi:</strong> Không thể tạo file Excel. Vui lòng thử lại.
                </div>
            `;
            console.error("Error:", error);
        }
    });

    // Xử lý sự kiện khi nhấn nút "Search history" trong form SEARCH_HISTORY
    document.getElementById("history-search-btn").addEventListener("click", async function () {
        const resultDiv = document.getElementById("history-search-result");

        const searchInput = document.getElementById("history-search-update").value.trim();
        const searchValues = searchInput.split(/\r?\n/).map(value => value.trim()).filter(value => value);

        if (!searchValues.length) {
            resultDiv.innerHTML = `
                <div class="alert alert-warning">
                    <strong>Cảnh báo:</strong> Vui lòng nhập ít nhất một SN hợp lệ để tra lịch sử.
                </div>
            `;
            return;
        }

        await searchHistoryBySN(searchValues);
    });

    document.getElementById("history-download-btn").addEventListener("click", function () {
        const resultDiv = document.getElementById("history-search-result");

        if (!window.historySearchData || !Array.isArray(window.historySearchData.history) || !window.historySearchData.history.length) {
            resultDiv.innerHTML = `
                <div class="alert alert-warning">
                    <strong>Cảnh báo:</strong> Vui lòng tra cứu lịch sử SN trước khi tải Excel.
                </div>
            `;
            return;
        }

        const existingMessage = document.getElementById("history-export-message");
        if (existingMessage) {
            existingMessage.remove();
        }

        try {
            const now = new Date();
            const filename = `HistorySearch_${now.toISOString().replace(/[:.]/g, '-')}.xlsx`;
            exportHistoryToExcel(window.historySearchData.history, filename);

            const successMessage = document.createElement("div");
            successMessage.className = "alert alert-success mt-3";
            successMessage.id = "history-export-message";

            const missingInfo = window.historySearchData.missingSNs && window.historySearchData.missingSNs.length
                ? ` (Không tìm thấy lịch sử cho ${window.historySearchData.missingSNs.length} SN.)`
                : "";

            successMessage.innerHTML = `
                <strong>Thành công:</strong> File Excel đã được tải xuống.${missingInfo}
            `;

            resultDiv.prepend(successMessage);
        } catch (error) {
            console.error("Error exporting history:", error);

            const errorMessage = document.createElement("div");
            errorMessage.className = "alert alert-danger mt-3";
            errorMessage.id = "history-export-message";
            errorMessage.innerHTML = `
                <strong>Lỗi:</strong> Không thể tạo file Excel. Vui lòng thử lại.
            `;

            resultDiv.prepend(errorMessage);
        }
    });
});
