const KhoB28Manager = (() => {
    const API_BASE_URL = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/KhoB28";
    let dataTable = null;

    const Utils = {
        parseSerialNumbers: (value) => value
            .split("\n")
            .map(sn => sn.trim().toUpperCase())
            .filter(sn => sn && /^[A-Za-z0-9-]+$/.test(sn)),
        formatDate: (dateValue) => {
            if (!dateValue) return "";
            const date = new Date(dateValue);
            return Number.isNaN(date.getTime()) ? dateValue : date.toLocaleString("vi-VN");
        },
        showError: (message) => Swal.fire({ icon: "error", title: "Lỗi", text: message }),
        showSuccess: (message) => Swal.fire({ icon: "success", title: "Thành công", text: message })
    };

    const Table = {
        render: (items) => {
            const tableElement = $("#kho-b28-table");
            if (dataTable) {
                dataTable.clear().destroy();
            }

            dataTable = tableElement.DataTable({
                data: items,
                columns: [
                    { data: "id" },
                    { data: "serialNumber" },
                    { data: "modelName" },
                    { data: "location" },
                    { data: "inBy" },
                    {
                        data: "inDate",
                        render: (data) => Utils.formatDate(data)
                    },
                    { data: "borrowBy" },
                    {
                        data: "borrowDate",
                        render: (data) => Utils.formatDate(data)
                    },
                    { data: "status" },
                    { data: "note" },
                    {
                        data: null,
                        orderable: false,
                        render: (data) => {
                            if (data?.status?.toLowerCase() === "borrowed") {
                                return `<button class="btn btn-sm btn-warning return-btn" data-serial="${data.serialNumber}">Trả</button>`;
                            }
                            return "";
                        }
                    }
                ],
                order: [[0, "desc"]],
                dom: '<"top d-flex align-items-center"f>rt<"bottom"ip>',
                language: {
                    search: "Tìm kiếm:",
                    lengthMenu: "Hiển thị _MENU_ dòng",
                    info: "Hiển thị _START_ đến _END_ của _TOTAL_ dòng",
                    infoEmpty: "Không có dữ liệu",
                    emptyTable: "Không có dữ liệu"
                }
            });
        }
    };

    const Api = {
        getAll: async () => {
            const response = await fetch(`${API_BASE_URL}/GetAll`);
            if (!response.ok) throw new Error("Không thể tải dữ liệu.");
            return response.json();
        },
        importItems: async (payload) => {
            const response = await fetch(`${API_BASE_URL}/Import`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            if (!response.ok) throw new Error("Không thể nhập kho.");
            return response.json();
        },
        exportItems: async (payload) => {
            const response = await fetch(`${API_BASE_URL}/Export`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            if (!response.ok) throw new Error("Không thể xuất kho.");
            return response.json();
        },
        borrowItems: async (payload) => {
            const response = await fetch(`${API_BASE_URL}/Borrow`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            if (!response.ok) throw new Error("Không thể mượn kho.");
            return response.json();
        },
        returnItems: async (payload) => {
            const response = await fetch(`${API_BASE_URL}/Return`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            if (!response.ok) throw new Error("Không thể trả kho.");
            return response.json();
        }
    };

    const Actions = {
        refresh: async () => {
            const data = await Api.getAll();
            Table.render(data.data || []);
        },
        handleImport: async () => {
            const { value: formValues } = await Swal.fire({
                title: "Nhập kho B28",
                html: `
                    <input id="swal-location" class="swal2-input" placeholder="Vị trí">
                    <input id="swal-model" class="swal2-input" placeholder="Model Name (tùy chọn)">
                    <textarea id="swal-serials" class="swal2-textarea" placeholder="Serial Number (mỗi dòng 1 SN)"></textarea>
                    <input id="swal-note" class="swal2-input" placeholder="Ghi chú (tùy chọn)">
                `,
                focusConfirm: false,
                showCancelButton: true,
                confirmButtonText: "Nhập",
                cancelButtonText: "Hủy",
                preConfirm: () => {
                    const serials = document.getElementById("swal-serials").value;
                    return {
                        location: document.getElementById("swal-location").value,
                        modelName: document.getElementById("swal-model").value,
                        serials,
                        note: document.getElementById("swal-note").value
                    };
                }
            });

            if (!formValues) return;
            const serialNumbers = Utils.parseSerialNumbers(formValues.serials || "");
            if (serialNumbers.length === 0) {
                Utils.showError("Vui lòng nhập Serial Number hợp lệ.");
                return;
            }

            const payload = {
                serialNumbers,
                modelName: formValues.modelName || null,
                location: formValues.location || null,
                inBy: document.getElementById("current-user")?.value || "Unknown",
                note: formValues.note || null
            };

            const response = await Api.importItems(payload);
            if (response.success) {
                Utils.showSuccess("Nhập kho thành công.");
                await Actions.refresh();
                return;
            }
            Utils.showError("Nhập kho thất bại.");
        },
        handleExport: async () => {
            const { value: serials } = await Swal.fire({
                title: "Xuất kho B28",
                input: "textarea",
                inputPlaceholder: "Serial Number (mỗi dòng 1 SN)",
                showCancelButton: true,
                confirmButtonText: "Xuất",
                cancelButtonText: "Hủy"
            });

            if (!serials) return;
            const serialNumbers = Utils.parseSerialNumbers(serials);
            if (serialNumbers.length === 0) {
                Utils.showError("Vui lòng nhập Serial Number hợp lệ.");
                return;
            }

            const response = await Api.exportItems({ serialNumbers });
            if (response.success) {
                Utils.showSuccess("Xuất kho thành công.");
                await Actions.refresh();
                return;
            }
            Utils.showError("Xuất kho thất bại.");
        },
        handleBorrow: async () => {
            const { value: formValues } = await Swal.fire({
                title: "Mượn kho B28",
                html: `
                    <input id="swal-borrower" class="swal2-input" placeholder="Người mượn">
                    <textarea id="swal-serials" class="swal2-textarea" placeholder="Serial Number (mỗi dòng 1 SN)"></textarea>
                `,
                showCancelButton: true,
                confirmButtonText: "Mượn",
                cancelButtonText: "Hủy",
                preConfirm: () => ({
                    borrower: document.getElementById("swal-borrower").value,
                    serials: document.getElementById("swal-serials").value
                })
            });

            if (!formValues) return;
            const borrower = formValues.borrower?.trim();
            const serialNumbers = Utils.parseSerialNumbers(formValues.serials || "");
            if (!borrower || serialNumbers.length === 0) {
                Utils.showError("Vui lòng nhập đầy đủ thông tin.");
                return;
            }

            const response = await Api.borrowItems({ serialNumbers, borrowBy: borrower });
            if (response.success) {
                Utils.showSuccess("Mượn kho thành công.");
                await Actions.refresh();
                return;
            }
            Utils.showError("Mượn kho thất bại.");
        },
        handleReturn: async (serialNumber) => {
            const { value: location } = await Swal.fire({
                title: `Trả kho ${serialNumber}`,
                input: "text",
                inputPlaceholder: "Vị trí (tùy chọn)",
                showCancelButton: true,
                confirmButtonText: "Trả",
                cancelButtonText: "Hủy"
            });

            const payload = {
                serialNumbers: [serialNumber],
                location: location || null,
                returnBy: document.getElementById("current-user")?.value || null
            };

            const response = await Api.returnItems(payload);
            if (response.success) {
                Utils.showSuccess("Trả kho thành công.");
                await Actions.refresh();
                return;
            }
            Utils.showError("Trả kho thất bại.");
        }
    };

    const init = () => {
        document.addEventListener("DOMContentLoaded", async () => {
            await Actions.refresh();

            document.getElementById("import-btn")?.addEventListener("click", Actions.handleImport);
            document.getElementById("export-btn")?.addEventListener("click", Actions.handleExport);
            document.getElementById("borrow-btn")?.addEventListener("click", Actions.handleBorrow);

            $("#kho-b28-table").on("click", ".return-btn", function () {
                const serialNumber = $(this).data("serial");
                if (serialNumber) {
                    Actions.handleReturn(serialNumber);
                }
            });
        });
    };

    return { init };
})();

KhoB28Manager.init();
