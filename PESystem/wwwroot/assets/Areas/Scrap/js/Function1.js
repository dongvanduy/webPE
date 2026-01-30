// Cấu hình đường dẫn API để dễ quản lý
const API_CONFIG = {
    BASE_URL: "https://pe-vnmbd-nvidia-cns.myfiinet.com/api",
    SFC_URL: "https://sfc-portal.cns.myfiinet.com/SfcSmartRepair/api",
    ENDPOINTS: {
        GET_SCRAP: "/Scrap/get-scrap-status-two-and-four",
        INPUT_SN: "/Scrap/input-sn-wait-spe-approve",
        UPDATE_SCRAP: "/Product/UpdateScrap",
        REPAIR_SCRAP: "/repair_scrap"
    }
};

// Hàm để ẩn tất cả các form và khu vực kết quả
function hideAllElements() {
    const forms = ["input-sn-1-form", "custom-form"];
    const results = ["input-sn-1-result", "sn-wait-approve-result"];

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
    const snInput = document.getElementById("sn-input-1");
    const descriptionInput = document.getElementById("description-input-1");

    if (snInput) snInput.value = "";
    if (descriptionInput) descriptionInput.value = "";
}

// Ẩn tất cả các form và khu vực kết quả ngay lập tức khi trang tải
hideAllElements();
let scrapTableInstance = null; // Biến toàn cục lưu instance

function displayTableWithPagination(data, resultDiv) {
    if (!data || data.length === 0) {
        resultDiv.innerHTML = `<div class="alert alert-warning">Không tìm thấy dữ liệu.</div>`;
        return;
    }

    // Nếu bảng chưa tồn tại trong DOM, tạo khung HTML cho nó
    if (!document.getElementById("scrapTable")) {
        resultDiv.innerHTML = `
            <table id="scrapTable" class="display nowrap compact" style="width:100%">
                <thead>
                    <tr>
                        <th>SERIAL_NUMBER</th>
                        <th>DESCRIPTION</th>
                        <th>CREATE_TIME</th>
                        <th>APPLY_TASK_STATUS</th>
                        <th>TYPE_BONEPILE</th>
                        <th>CREATE_BY</th>
                    </tr>
                </thead>
                <tbody></tbody>
            </table>
        `;
    }

    const tableData = data.map(item => ({
        SERIAL_NUMBER: item.sn || "N/A",
        DESCRIPTION: item.description || "N/A",
        CREATE_TIME: item.createTime || "N/A",
        APPLY_TASK_STATUS: item.applyTaskStatus,
        TYPE_BONEPILE: item.remark || "N/A",
        CREATE_BY: item.createBy || "N/A"
    }));

    // Nếu DataTable đã được khởi tạo, chỉ cần thay data
    if ($.fn.DataTable.isDataTable("#scrapTable")) {
        const table = $("#scrapTable").DataTable();
        table.clear().rows.add(tableData).draw();
    } else {
        // Khởi tạo lần đầu
        $("#scrapTable").DataTable({
            data: tableData,
            columns: [
                { data: "SERIAL_NUMBER" },
                { data: "DESCRIPTION" },
                { data: "CREATE_TIME" },
                { data: "APPLY_TASK_STATUS" },
                { data: "TYPE_BONEPILE" },
                { data: "CREATE_BY" }
            ],
            paging: true,
            searching: true,
            ordering: true,
            scrollX: true,
            responsive: true,
            language: {
                lengthMenu: "Hiển thị _MENU_ dòng",
                search: "Tìm kiếm:",
                info: "Trang _PAGE_ / _PAGES_",
                paginate: {
                    previous: "Trước",
                    next: "Sau"
                }
            }
        });
    }
}

// Hàm tải dữ liệu từ API và hiển thị
async function loadScrapStatusTwo(resultDiv) {
    resultDiv.innerHTML = `
        <div class="alert alert-info">
            <strong>Thông báo:</strong> Đang tải danh sách SN chờ SPE approve...
        </div>
    `;

    try {
        const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/get-scrap-status-two-and-four", {
            method: "GET",
            headers: {
                "Content-Type": "application/json"
            }
        });

        const result = await response.json();

        if (response.ok) {
            displayTableWithPagination(result, resultDiv);
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

// Hàm tải file Excel
function downloadExcel(data) {
    // Chuẩn bị dữ liệu cho file Excel
    const worksheetData = data.map(item => ({
        SN: item.sn || "N/A",
        Description: item.description || "N/A",
        "Create Time": item.createTime || "N/A",
        "Apply Task Status": item.applyTaskStatus,
        "Create By": item.createBy || "N/A"
    }));

    // Tạo worksheet
    const worksheet = XLSX.utils.json_to_sheet(worksheetData);
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, worksheet, "SN_Wait_SPE_Approve");

    // Tải file Excel
    XLSX.writeFile(workbook, "SN_Wait_SPE_Approve.xlsx");
}

// Xử lý sự kiện khi trang tải lần đầu
document.addEventListener("DOMContentLoaded", function () {
    console.log("DOMContentLoaded triggered for Function1");
    hideAllElements();

    // Xử lý sự kiện thay đổi giá trị trong dropdown
    document.getElementById("search-options").addEventListener("change", function () {
        console.log("Dropdown changed to:", this.value);
        hideAllElements();

        const selectedValue = this.value;

        if (selectedValue === "INPUT_SN_1") {
            document.getElementById("input-sn-1-form").classList.remove("hidden");
            document.getElementById("input-sn-1-result").classList.remove("hidden");
        } else if (selectedValue === "SN_WAIT_SPE_APPROVE") {
            const resultDiv = document.getElementById("sn-wait-approve-result");
            document.getElementById("custom-form").classList.remove("hidden");
            document.getElementById("sn-wait-approve-result").classList.remove("hidden");

            // Tải dữ liệu ngay khi form được hiển thị
            loadScrapStatusTwo(resultDiv);
        }
    });

    // Xử lý sự kiện khi nhấn nút "INPUT SN" trong form INPUT_SN_1
    document.getElementById("input-sn-btn").addEventListener("click", async function () {
        const resultDiv = document.getElementById("input-sn-1-result");

        // Lấy danh sách SN từ textarea
        const snInput = document.getElementById("sn-input-1").value.trim();

        // Lọc trùng lặp ngay từ đầu vào
        const sNs = [...new Set(snInput.split(/\r?\n/).map(sn => sn.trim()).filter(sn => sn))];

        // Lấy mô tả từ input
        const description = document.getElementById("description-input-1").value.trim();

        // Lấy thông tin loại Bonepile
        const typeBonepile = document.getElementById("bp-options").value;

        // Lấy thông tin loại Approve
        const typeApprove = document.getElementById("approve-options").value;

        // Lấy thông tin người dùng hiện tại
        const createdBy = $('#analysisPerson').val();

        // Validation
        if (!sNs.length) return showMessage(resultDiv, 'warning', 'Vui lòng nhập ít nhất một Serial Number hợp lệ!');
        if (!description) return showMessage(resultDiv, 'warning', 'Vui lòng nhập mô tả!');
        if (!["BP-10", "BP-20", "B36R"].includes(typeBonepile)) return showMessage(resultDiv, 'warning', 'Vui lòng chọn loại BonePile!');
        if (!["2", "4"].includes(typeApprove)) return showMessage(resultDiv, 'warning', 'Vui lòng chọn loại Approve!');


        // Hiển thị trạng thái đang xử lý
        resultDiv.innerHTML = `<div class="alert alert-info"><strong>Đang xử lý:</strong> Đang kiểm tra hệ thống SFC...</div>`;


        // Định dạng sn_list thành chuỗi cách nhau bởi dấu phẩy
        const snListString = sNs.join(",");

        try {
            // --- 2. GỌI API SMART REPAIR (GATEKEEPER) ---
            // Nếu Approve != 4 thì mới cần check bên SFC
            if (typeApprove !== "4") {
                const repairScrapData = {
                    type: "insert",
                    sn_list: snListString,
                    type_bp: typeBonepile,
                    status: typeApprove,
                    task: "",
                    emp_no: createdBy,
                    reason: "Input Scrap"
                };

                const repairResponse = await fetch(`${API_CONFIG.SFC_URL}${API_CONFIG.ENDPOINTS.REPAIR_SCRAP}`, {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(repairScrapData)
                });

                // Lấy text trả về để so sánh chính xác (API này thường trả về chuỗi "OK" có cả dấu ngoặc kép)
                const repairResultText = await repairResponse.text();

                // Logic kiểm tra: Phải trả về OK hoặc "OK" mới cho đi tiếp

                if (!repairResponse.ok || (repairResultText !== "OK" && repairResultText !== "\"OK\"")) {
                    // NẾU THẤT BẠI -> DỪNG LẠI NGAY, KHÔNG LƯU SQL
                    console.error("SFC Repair Failed:", repairResultText);
                    return showMessage(resultDiv, 'danger', `Lỗi từ hệ thống SFC (Repair Scrap): ${repairResultText}. Dữ liệu chưa được lưu.`);
                }
            }

            // --- 3. NẾU BƯỚC 2 OK -> GỌI API INPUT SQL SERVER ---
            resultDiv.innerHTML = `<div class="alert alert-info"><strong>Đang xử lý:</strong> SFC OK. Đang lưu vào cơ sở dữ liệu...</div>`;

            const requestData = {
                sNs: sNs,
                description: description,
                remark: typeBonepile,
                approve: typeApprove,
                createdBy: createdBy
            };

            const inputSnResponse = await fetch(`${API_CONFIG.BASE_URL}${API_CONFIG.ENDPOINTS.INPUT_SN}`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(requestData)
            });

            const inputSnResult = await inputSnResponse.json();

            if (!inputSnResponse.ok) {
                throw new Error(`Lỗi lưu DB: ${inputSnResult.message}`);
            }

            // --- 4. CẬP NHẬT TRẠNG THÁI SẢN PHẨM (UPDATE SCRAP) ---
            // Bước này giữ lại để đảm bảo logic cũ (đánh dấu trạng thái 'Đã gửi NV xin báo phế')
            // Chỉ chạy nếu Approve != 4 (tức là vừa gọi repair_scrap xong)
            if (typeApprove !== "4") {
                const updateResponse = await fetch(`${API_CONFIG.BASE_URL}${API_CONFIG.ENDPOINTS.UPDATE_SCRAP}`, {
                    method: "PUT",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({
                        serialNumbers: sNs,
                        scrapStatus: "Đã gửi NV xin báo phế"
                    })
                });

                if (!updateResponse.ok) {
                    console.warn("Update status failed but record inserted");
                    // Vẫn coi là thành công nhưng cảnh báo nhẹ
                }
            }

            // --- 5. HOÀN TẤT ---
            showMessage(resultDiv, 'success', `Thành công! ${inputSnResult.message}`);

            // Reset form
            document.getElementById("sn-input-1").value = "";
            document.getElementById("description-input-1").value = "";

        } catch (error) {
            console.error("System Error:", error);
            showMessage(resultDiv, 'danger', `Đã xảy ra lỗi hệ thống: ${error.message}`);
        }
    });

    // Hàm helper hiển thị thông báo nhanh
    function showMessage(container, type, message) {
        container.innerHTML = `
        <div class="alert alert-${type}">
            <strong>${type === 'danger' ? 'Lỗi' : type === 'warning' ? 'Cảnh báo' : 'Thông báo'}:</strong> ${message}
        </div>`;
    }

    // Xử lý sự kiện khi nhấn nút "Download Excel" trong form SN_WAIT_SPE_APPROVE
    document.getElementById("sn-wait-list-btn").addEventListener("click", async function () {
        try {
            // Gọi API để lấy toàn bộ dữ liệu
            const response = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/get-scrap-status-two-and-four", {
                method: "GET",
                headers: {
                    "Content-Type": "application/json"
                }
            });

            const result = await response.json();

            if (response.ok && result && result.length > 0) {
                // Tải file Excel
                downloadExcel(result);
            } else {
                const resultDiv = document.getElementById("sn-wait-approve-result");
                resultDiv.innerHTML = `
                    <div class="alert alert-warning">
                        <strong>Cảnh báo:</strong> Không có dữ liệu để tải xuống.
                    </div>
                `;
            }
        } catch (error) {
            const resultDiv = document.getElementById("sn-wait-approve-result");
            resultDiv.innerHTML = `
                <div class="alert alert-danger">
                    <strong>Lỗi:</strong> Không thể tải dữ liệu để tạo file Excel.
                </div>
            `;
            console.error("Error:", error);
        }
    });
});