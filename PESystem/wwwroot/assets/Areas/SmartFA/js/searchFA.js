var SearchFaNormalizer = (function () {
    const existing = typeof window !== 'undefined' ? window.SearchFaNormalizer : undefined;
    if (existing) {
        return existing;
    }
    const hasValue = (value) => value !== undefined && value !== null && value !== '';
    const fieldMappings = {
        seriaL_NUMBER: ['SERIAL_NUMBER', 'serialNumber', 'SerialNumber', 'serial_number'],
        productLine: ['PRODUCT_LINE', 'productLine', 'ProductLine'],
        modeL_NAME: ['MODEL_NAME', 'modelName', 'ModelName'],
        wiP_GROUP: ['WIP_GROUP', 'wipGroup', 'WipGroup'],
        tesT_GROUP: ['TEST_GROUP', 'testGroup', 'TestGroup'],
        tesT_CODE: ['TEST_CODE', 'testCode', 'ERROR_CODE', 'errorCode'],
        datA1: ['DATA1', 'data1', 'ERROR_DESC', 'errorDesc'],
        datA11: ['DATA11', 'data11', 'STATUS', 'status'],
        datA12: ['DATA12', 'data12', 'PR_STATUS', 'prStatus'],
        datA13: ['DATA13', 'data13', 'HANDOVER', 'handover'],
        datA14: ['DATA14', 'data14'],
        datA15: ['DATA15', 'data15'],
        datA17: ['DATA17', 'data17', 'ACTION', 'action'],
        datA18: ['DATA18', 'data18', 'POSITION', 'position', 'LOCATION', 'location'],
        datA19: ['DATA19', 'data19', 'KANBAN_WIP', 'kanbanWip'],
        datE3: ['DATE3', 'date3'],
        tester: ['TESTER', 'tester'],
        mO_NUMBER: ['MO_NUMBER', 'moNumber'],
        mo_NUMBER: ['MO_NUMBER', 'moNumber']
    };

    function normalizeItem(item = {}) {
        if (!item || typeof item !== 'object') {
            return {};
        }
        const normalized = { ...item };
        Object.entries(fieldMappings).forEach(([targetKey, sourceKeys]) => {
            if (hasValue(normalized[targetKey])) {
                return;
            }
            for (const sourceKey of sourceKeys) {
                if (hasValue(item[sourceKey])) {
                    normalized[targetKey] = item[sourceKey];
                    break;
                }
            }
        });
        return normalized;
    }

    function normalizeData(data) {
        if (!Array.isArray(data)) {
            return [];
        }
        return data.map(normalizeItem);
    }

    function normalizeResponse(response) {
        if (!response || typeof response !== 'object') {
            return { success: false, data: [] };
        }
        return { ...response, data: normalizeData(response.data) };
    }

    const api = { normalizeItem, normalizeData, normalizeResponse };
    if (typeof window !== 'undefined') {
        window.SearchFaNormalizer = api;
    }
    return api;
})();


document.addEventListener("DOMContentLoaded", () => {
    loadModelNames();
    loadStatusOptions();
});

async function loadModelNames() {
    try {
        const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/get-unique-modelnames");
        const data = await response.json();
        if (data.success) {
            const modelNameDropdown = document.getElementById("modelName");
            data.data.forEach(modelName => {
                const option = document.createElement("option");
                option.value = modelName;
                option.textContent = modelName;
                modelNameDropdown.appendChild(option);
            });
        } else {
            console.error("Failed to load model names:", data.message);
        }
    } catch (error) {
        console.error("Error fetching model names:", error);
    }
}

async function loadStatusOptions() {
    try {
        const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/get-unique-status");
        const data = await response.json();
        if (data.success) {
            const statusDropdown = document.getElementById("status");
            data.data.forEach(status => {
                const option = document.createElement("option");
                option.value = status;
                option.textContent = status;
                statusDropdown.appendChild(option);
            });
        } else {
            console.error("Failed to load status options:", data.message);
        }
    } catch (error) {
        console.error("Error fetching status options:", error);
    }
}


document.addEventListener("DOMContentLoaded", function () {
    // Các phần tử
    const typeSearch = document.getElementById("typeSearch");
    const serialNumbersInput = document.getElementById("sn-input");
    const statusSelect = document.getElementById("status");
    const testCodeInput = document.getElementById("testCode");
    const data1Input = document.getElementById("data1");
    const modelNameSelect = document.getElementById("modelName");
    const resultsBody = document.getElementById("results-body");
    const searchResults = document.getElementById("search-results");

    // Gửi yêu cầu tìm kiếm
    async function submitSearch() {
        const type = document.getElementById("typeSearch").value;
        const serialNumbers = document.getElementById("sn-input").value
            .split("\n")
            .map(sn => sn.trim())
            .filter(sn => sn); // Loại bỏ khoảng trắng thừa và các giá trị rỗng

        let url, payload;

        if (type === "history") {
            if (serialNumbers.length === 0) {
                showWarning("Vui lòng nhập SN!");
                return;
            }
            url = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/search-history-by-list";
            payload = serialNumbers; // Gửi danh sách SN
        } else {
            url = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/search";
            payload = {
                SerialNumbers: serialNumbers, // Gửi danh sách SN
                Status: document.getElementById("status").value,
                TestCode: document.getElementById("testCode").value,
                Data1: document.getElementById("data1").value,
                ModelName: document.getElementById("modelName").value,
                HandoverStatus: "",
                Location: ""
            };
        }

        try {
            const response = await fetch(url, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            if (!response.ok) throw new Error(`Lỗi HTTP! Trạng thái: ${response.status}`);

            const result = await response.json();

            if (result.success) {
                const normalizedData = SearchFaNormalizer.normalizeData(result.data);
                displayResults(normalizedData);
            } else {
                showError("Error!");
            }
        } catch (error) {
            console.error("Lỗi:", error);
            showError("Error!");
        }
    }

    function displayResults(data) {
        resultsBody.innerHTML = "";

        // Sắp xếp dữ liệu: Serial Number -> Date
        data.sort((a, b) => {
            // So sánh Serial Number
            const snCompare = (a.seriaL_NUMBER || "").localeCompare(b.seriaL_NUMBER || "");
            if (snCompare !== 0) return snCompare;

            // Nếu SN giống nhau, so sánh theo thời gian (datE3)
            const dateA = new Date(a.datE3 || "1970-01-01");
            const dateB = new Date(b.datE3 || "1970-01-01");
            return dateA - dateB;
        });

        // Hiển thị số lượng kết quả
        const resultsInfo = document.getElementById("results-info");
        if (data.length === 0) {
            resultsInfo.textContent = "Không tìm thấy kết quả nào.";
            searchResults.style.display = "none";
            return;
        } else {
            resultsInfo.textContent = `Kết quả: ${data.length}`;
        }

        // Hiển thị dữ liệu đã sắp xếp
        data.forEach(item => {
            const row = document.createElement("tr");
            row.innerHTML = `
            <td>${item.seriaL_NUMBER || ""}</td>
            <td title="${item.modeL_NAME || ""}">${item.modeL_NAME || ""}</td>
            <td>${item.tesT_GROUP || ""}</td>
            <td class="testcode-cell" title="${item.tesT_CODE || ""}">${item.tesT_CODE || ""}</td>
            <td class="data1-cell" title="${item.datA1 || ""}">${item.datA1 || ""}</td>
            <td>${item.datA12 || ""}</td>
            <td>${item.datA17 || ""}</td>
            <td class="data11-cell" title="${item.datA11 || ""}">${item.datA11 || ""}</td>
            <td>${item.tester || ""}</td>
            <td title="${item.datE3 || ""}">${item.datE3 || ""}</td>
            <td>${item.datA18 || ""}</td>
            <td>${item.datA19 || ""}</td>
        `;

            // Sự kiện click để hiển thị nội dung chi tiết
            row.addEventListener("click", () => {
                showFullContent({
                    data1: item.datA1 || "Không có nội dung",
                    testCode: item.tesT_CODE || "Không có nội dung"
                });
            });

            resultsBody.appendChild(row);
        });

        searchResults.style.display = "block";
    }


    function showFullContent(content) {
        // Kiểm tra và xóa modal trước đó (nếu có)
        const existingModal = document.getElementById("modal-content");
        if (existingModal) {
            existingModal.remove();
        }

        // Tạo modal mới
        const modal = document.createElement("div");
        modal.id = "modal-content"; // Gắn ID để kiểm soát modal
        modal.style.position = "fixed";
        modal.style.top = "50%";
        modal.style.left = "50%";
        modal.style.transform = "translate(-50%, -50%)";
        modal.style.background = "#fff";
        modal.style.padding = "20px";
        modal.style.boxShadow = "0 4px 8px rgba(0, 0, 0, 0.2)";
        modal.style.zIndex = "10000";
        modal.style.width = "400px";

        modal.innerHTML = `
        <p><strong>Data1:</strong> ${content.data1}</p>
        <p><strong>TestCode:</strong> ${content.testCode}</p>
        <button id="close-modal" style="border-radius:10px;margin-top: 10px;">Đóng</button>
    `;

        document.body.appendChild(modal);

        // Đóng modal khi nhấn nút
        document.getElementById("close-modal").addEventListener("click", () => {
            modal.remove();
        });
    }

    // Gắn chức năng tìm kiếm vào nút
    window.submitSearch = submitSearch;
});

//Xuất ra file excel?
document.getElementById("exportExcelBtn").addEventListener("click", () => {
    const tableRows = document.querySelectorAll("#results-body tr");
    if (!tableRows || tableRows.length === 0) {
        showError("Không có dữ liệu để xuất!");
        return;
    }

    const excelData = [["Serial Number", "Model Name", "Wip", "Test Group", "Test Code", "Data1", "PreStatus", "Status", "Confirm Person", "Confirm Time", "Handover status", "Location", "Type", "Note"]];

    tableRows.forEach(row => {
        const rowData = Array.from(row.children).map(cell => cell.textContent);
        excelData.push(rowData);
    });

    // Sử dụng thư viện SheetJS (xlsx) để tạo file Excel
    const ws = XLSX.utils.aoa_to_sheet(excelData); // Chuyển mảng thành sheet
    const wb = XLSX.utils.book_new(); // Tạo workbook
    XLSX.utils.book_append_sheet(wb, ws, "Kết Quả"); // Thêm sheet vào workbook
    XLSX.writeFile(wb, "KetQua_TimKiem.xlsx"); // Lưu file Excel
});
function showWarning(message) {
    Swal.fire({
        title: message,
        icon: "warning",
        draggable: true
    });
}