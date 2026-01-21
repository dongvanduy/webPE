// =========================================================
// FUNCTION: Ẩn toàn bộ section
// =========================================================
function hideAll() {
    document.querySelectorAll(".form-section, .scrap-card, #sn-wait-approve-result")
        .forEach(x => x.classList.add("hidden"));
}

// =========================================================
// FUNCTION: SweetAlert Loading
// =========================================================
function showLoading(msg) {
    Swal.fire({
        title: msg,
        text: "Please wait...",
        icon: "info",
        showConfirmButton: false,
        allowOutsideClick: false
    });
}

// =========================================================
// API 1: INSERT ORACLE
// =========================================================
async function insertOracle(snListString, typeBonepile, createdBy) {
    let payload = {
        type: "insert",
        sn_list: snListString,
        type_bp: typeBonepile,
        status: "2",
        task: null,
        emp_no: createdBy
    };

    try {
        let res = await fetch("https://sfc-portal.cns.myfiinet.com/SfcSmartRepair/api/repair_scrap", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        let text = await res.text();

        if (text === '"OK"') {
            return { success: true, message: "OK" };
        } else {
            let cleanMsg = text.replace(/^"|"$/g, '').trim();
            return {
                success: false,
                message: cleanMsg || "Oracle insert failed"
            };
        }
    } catch (err) {
        return { success: false, message: "Lỗi kết nối: " + err.message };
    }
}
// =========================================================
// API 2: INSERT SQL SERVER
// =========================================================
async function insertSql(snList, description, typeBonepile, createdBy) {

    let payload = {
        sNs: snList,
        description: description,
        remark: typeBonepile,
        approve: "2",     // FIXED APPROVE = 2
        createdBy: createdBy
    };

    let res = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Switch/input-sn-wait-spe-approve-sw", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    });

    return await res.json();
}

// =========================================================
// LOAD TABLE WAIT SPE APPROVE
// =========================================================
async function loadWaitSpeTable() {

    showLoading("Loading data....");

    try {
        let res = await fetch("https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Switch/get-status-two-four-switch");
        let data = await res.json();
        Swal.close();

        if (!res.ok) {
            Swal.fire("Error", "Failed to load data!", "error");
            return;
        }

        let tableData = data.map(x => ({
            SERIAL_NUMBER: x.sn,
            DESCRIPTION: x.description,
            CREATE_TIME: x.createTime,
            APPLY_TASK_STATUS: x.applyTaskStatus,
            BONEPILE_TYPE: x.remark,
            CREATE_BY: x.createBy
        }));

        // destroy table nếu đã khởi tạo
        if ($.fn.DataTable.isDataTable("#table-sn-wait-approve")) {
            $("#table-sn-wait-approve").DataTable().clear().destroy();
        }

        $("#table-sn-wait-approve").DataTable({
            data: tableData,
            columns: [
                { data: "SERIAL_NUMBER" },
                { data: "DESCRIPTION" },
                { data: "CREATE_TIME" },
                { data: "APPLY_TASK_STATUS" },
                { data: "BONEPILE_TYPE" },
                { data: "CREATE_BY" }
            ],
            pageLength: 10,
            searching: true,
            ordering: true,

            // Enable Export Excel
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

    } catch (err) {
        Swal.fire("Lỗi", "Cannot connect to API!", "error");
    }
}

// =========================================================
// MAIN LOGIC
// =========================================================
document.addEventListener("DOMContentLoaded", function () {

    const selectFunc = document.getElementById("search-options");

    const formInputSN = document.getElementById("input-sn-1-form");
    const formWaitList = document.getElementById("custom-form");
    const resultWait = document.getElementById("sn-wait-approve-result");

    hideAll();

    // =====================================================
    // Khi chọn chức năng
    // =====================================================
    selectFunc.addEventListener("change", function () {
        hideAll();

        if (this.value === "INPUT_SN") {
            formInputSN.classList.remove("hidden");
            return;
        }

        if (this.value === "SN_WAIT_SPE_APPROVE") {
            formWaitList.classList.remove("hidden");
            resultWait.classList.remove("hidden");
            loadWaitSpeTable();
            return;
        }
    });


    // =====================================================
    // INPUT SN - Oracle → SQL Server
    // =====================================================
    document.getElementById("input-sn-btn").addEventListener("click", async function () {

        let raw = document.getElementById("sn-input-1").value.trim();
        let description = document.getElementById("description-input-1").value.trim();
        let typeBonepile = document.getElementById("bp-options").value;
        let createdBy = document.getElementById("analysisPerson").value;
        // --- [FIX] KIỂM TRA PHIÊN ĐĂNG NHẬP / MÃ NHÂN VIÊN ---
        if (!createdBy || createdBy.trim() === "") {
            // Cố gắng lấy dự phòng từ Cookie hoặc SessionStorage nếu có (tùy chọn)
            // createdBy = getCookie("EMP_NO"); 

            // Nếu vẫn không có, chặn lại ngay lập tức
            if (!createdBy) {
                return Swal.fire({
                    icon: "error",
                    title: "Mất thông tin đăng nhập",
                    text: "Không tìm thấy mã nhân viên (Emp No). Vui lòng tải lại trang!",
                    confirmButtonText: "Tải lại trang"
                }).then((result) => {
                    if (result.isConfirmed) {
                        window.location.reload();
                    }
                });
            }
        }
        // -----------------------------------------------------

        let snList = raw.split(/\r?\n/).map(x => x.trim()).filter(x => x);
        let snListString = snList.join(",");

        // validation
        if (snList.length === 0)
            return Swal.fire("Warning", "Serial Number not entered yet!", "warning");

        if (!description)
            return Swal.fire("Warning", "Description not entered yet!", "warning");

        if (!["BP-10", "BP-20", "B36R"].includes(typeBonepile))
            return Swal.fire("Warning", "Bonepile type has not been selected!", "warning");

        // insert Oracle
        showLoading("Inserting into Oracle...");
        let okOracle = await insertOracle(snListString, typeBonepile, createdBy);

        if (!okOracle.success) {
            Swal.close();
            return Swal.fire({
                icon: "error",
                title: "Oracle Error",
                text: okOracle.message,       //hiện đúng message từ server
                footer: "Contact IT/PE if you think this is a mistake"
            });
        }

        // insert SQL
        showLoading("Inserting into SQL Server...");
        let sql = await insertSql(snList, description, typeBonepile, createdBy);

        if (!sql || sql.success !== true) {
            Swal.close();
            return Swal.fire("Error", sql?.message || "Insert SQL failed!", "error");
        }
        // Thành công
        Swal.close();
        Swal.fire("Success", "Inserted into Oracle → SQL Server", "success");

        // Reset form
        document.getElementById("sn-input-1").value = "";
        document.getElementById("description-input-1").value = "";
        document.getElementById("bp-options").selectedIndex = 0;

        document.getElementById("sn-input-1").focus();
    });

});