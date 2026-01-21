// Hàm hiển thị bảng với DataTable + export Excel + checkbox
function renderTableWithDataTable(data, tableId, checkboxName, selectAllId) {
    let selectedTasks = new Set();

    const tableBody = document.querySelector(`#${tableId} tbody`);
    if (!tableBody) return;

    tableBody.innerHTML = "";

    data.forEach(item => {
        const isChecked = selectedTasks.has(item.taskNumber) ? "checked" : "";
        const row = `
            <tr>
                <td class="checkbox-column"><input type="checkbox" name="${checkboxName}" value="${item.taskNumber}" ${isChecked}></td>
                <td>${item.taskNumber || "N/A"}</td>
                <td>${item.applyTime || "N/A"}</td>
                <td>${item.totalQty || "N/A"}</td>
            </tr>
        `;
        tableBody.insertAdjacentHTML("beforeend", row);
    });

    // Hủy DataTable cũ nếu tồn tại
    if ($.fn.DataTable.isDataTable(`#${tableId}`)) {
        $(`#${tableId}`).DataTable().clear().destroy();
    }

    // Khởi tạo DataTable với export Excel
    $(`#${tableId}`).DataTable({
        pageLength: 10,
        lengthMenu: [10, 25, 50, 100],
        order: [],
        columnDefs: [
            { orderable: false, targets: 0 },
            { width: "10px", targets: 0 }
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

        // 🎯 **Thêm export Excel**
        dom: '<"top d-flex align-items-center"flB>rt<"bottom"ip>',
        buttons: [{
            extend: 'excelHtml5',
            text: '<img src="/assets/img/excel.png" class="excel-icon excel-button"/>',
            title: 'SN_Wait_SPE_Approve',
            exportOptions: {
                columns: ':visible',
                modifier: { selected: null },
                format: { header: (data) => data.trim() }
            }
        }],
    });
}

// Hàm gọi API + SweetAlert lỗi
async function fetchAndRenderData(status, resultDivId, tableId, checkboxName, selectAllId) {
    const modelType = "SWITCH";

    Swal.fire({
        title: "Loading data...",
        icon: "info",
        showConfirmButton: false,
        allowOutsideClick: false
    });

    try {
        const response = await fetch(
            `https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Scrap/get-task-by-status?status=${status}&modelType=${encodeURIComponent(modelType.trim())}`
        );

        const result = await response.json();
        Swal.close();

        if (!response.ok) {
            Swal.fire("Error!", result.message, "error");
            return;
        }

        const sortedData = result.data.sort((a, b) => new Date(a.applyTime) - new Date(b.applyTime));

        renderTableWithDataTable(sortedData, tableId, checkboxName, selectAllId);

    } catch (error) {
        Swal.fire("Connection error!", "Unable to connect to the API. Please try again!", "error");
        console.error("Error:", error);
    }
}




// Khi DOM tải xong
document.addEventListener("DOMContentLoaded", function () {

    const searchMrbOptions = document.getElementById("search-mrb-options");

    searchMrbOptions?.addEventListener("change", function () {
        const val = this.value;

        if (val === "5") {
            fetchAndRenderData(5, "wait-re-move-mrb-result", "wait-re-move-mrb-table", "mrb-checkbox-5", "select-all-5");
        }
        else if (val === "6") {
            fetchAndRenderData(6, "wait-mrb-confirm-result", "wait-mrb-confirm-table", "mrb-checkbox-6", "select-all-6");
        }
        else if (val === "7") {
            fetchAndRenderData(7, "moved-mrb-form-result", "moved-mrb-form-table", "mrb-checkbox-7", "select-all-7");
        }
    });

});
