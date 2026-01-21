document.addEventListener('DOMContentLoaded', function () {
    const exportExcelBtn = document.getElementById('export-excel-btn');
    const selectAllCheckbox = document.getElementById('selectAllCheckbox');
    const selectTypeBtn = document.getElementById('select-type-btn');
    let selectedRows = [];
    let table = null;

    // Hàm tạo nội dung cho ô với tooltip
    function createTooltipCell(data) {
        return `<span class="tooltip-trigger" data-tooltip="${data || ''}">${data || ''}</span>`;
    }

    // Hàm gắn sự kiện tooltip cho các phần tử
    function attachTooltipEvents() {
        $('.tooltip-trigger').each(function () {
            const $this = $(this);
            // Kiểm tra xem sự kiện đã được gắn chưa để tránh trùng lặp
            if (!$this.data('tooltip-initialized')) {
                $this.on('mouseover', function (e) {
                    const tooltipText = $this.data('tooltip');
                    if (tooltipText) {
                        let tooltip = document.querySelector('.custom-tooltip');
                        if (!tooltip) {
                            tooltip = document.createElement('div');
                            tooltip.className = 'custom-tooltip';
                            document.body.appendChild(tooltip);
                        }
                        tooltip.textContent = tooltipText;
                        tooltip.style.display = 'block';
                        tooltip.style.left = (e.pageX + 10) + 'px';
                        tooltip.style.top = (e.pageY - 20) + 'px';
                    }
                }).on('mousemove', function (e) {
                    const tooltip = document.querySelector('.custom-tooltip');
                    if (tooltip && tooltip.style.display === 'block') {
                        tooltip.style.left = (e.pageX + 10) + 'px';
                        tooltip.style.top = (e.pageY - 20) + 'px';
                    }
                }).on('mouseout', function () {
                    const tooltip = document.querySelector('.custom-tooltip');
                    if (tooltip) {
                        tooltip.style.display = 'none';
                    }
                });
                // Đánh dấu phần tử đã được gắn sự kiện
                $this.data('tooltip-initialized', true);
            }
        });
    }

    // Hàm tải dữ liệu lịch sử dựa trên type
    async function loadHistoryTable(type) {
        try {
            const response = await fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/MaterialSystem/GetHistory', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ type: type }) // Gửi type dưới dạng JSON
            });

            if (!response.ok) {
                throw new Error(`Lỗi khi gọi API: ${response.status} ${response.statusText}`);
            }

            const data = await response.json();
            console.log('Dữ liệu lịch sử:', data);

            // Hủy bảng cũ nếu đã tồn tại
            if ($.fn.DataTable.isDataTable('#historyMaterialsTable')) {
                $('#historyMaterialsTable').DataTable().destroy();
            }

            // Tạo bảng DataTable mới
            table = $('#historyMaterialsTable').DataTable({
                data: data,
                scrollX: true,
                columns: [
                    {
                        data: null,
                        orderable: false,
                        render: function (data, type, row, meta) {
                            return `<input type="checkbox" class="row-checkbox" data-row-index="${meta.row}" />`;
                        }
                    },
                    { data: 'task', defaultContent: '', render: createTooltipCell },
                    { data: 'mA_LIEU', defaultContent: '', render: createTooltipCell },
                    { data: 'mA_ALL', defaultContent: '', render: createTooltipCell },
                    { data: 'nhA_CUNG_UNG', defaultContent: '', render: createTooltipCell },
                    { data: 'datE_CODE', defaultContent: '', render: createTooltipCell },
                    { data: 'loT_CODE', defaultContent: '', render: createTooltipCell },
                    { data: 'type', defaultContent: '', render: createTooltipCell },
                    { data: 'qtY1', defaultContent: 0, render: createTooltipCell },
                    { data: 'qtY2', defaultContent: 0, render: createTooltipCell },
                    { data: 'borroweD_TIME', defaultContent: '', render: createTooltipCell },
                    { data: 'oP1', defaultContent: '', render: createTooltipCell },
                    { data: 'returN_TIME', defaultContent: '', render: createTooltipCell },
                    { data: 'oP2', defaultContent: '', render: createTooltipCell },
                    { data: 'esd', defaultContent: '', render: createTooltipCell },
                    { data: 'location', defaultContent: '', render: createTooltipCell },
                    { data: 'remark', defaultContent: '', render: createTooltipCell },
                    { data: 'qtY3', defaultContent: 0, render: createTooltipCell },
                    { data: 'qtY4', defaultContent: 0, render: createTooltipCell }
                ],
                pageLength: 15,
                responsive: true,
                order: [[10, 'desc']], // Sắp xếp mặc định theo cột TIME (giảm dần)
                drawCallback: function () {
                    attachTooltipEvents();
                }
            });
            // === CLICK ROW TO HIGHLIGHT ===
            $('#historyMaterialsTable tbody').on('click', 'tr', function () {
                // Bỏ highlight cũ
                $('#historyMaterialsTable tbody tr').removeClass('selected-row');

                // Nếu click chính row đó → toggle chọn
                $(this).toggleClass('selected-row');
            });

            // Xóa danh sách hàng được chọn khi bảng được tải lại
            selectedRows = [];

            // Sự kiện chọn checkbox từng hàng
            $('#historyMaterialsTable tbody').on('change', '.row-checkbox', function () {
                const rowIndex = $(this).data('row-index');
                const rowData = table.row(rowIndex).data();

                if ($(this).is(':checked')) {
                    if (!selectedRows.includes(rowData)) {
                        selectedRows.push(rowData);
                    }
                } else {
                    selectedRows = selectedRows.filter(row => row !== rowData);
                }
                console.log('Hàng được chọn:', selectedRows);
            });

            // Sự kiện chọn tất cả checkbox
            if (selectAllCheckbox) {
                selectAllCheckbox.addEventListener('change', function () {
                    const isChecked = this.checked;
                    $('.row-checkbox').prop('checked', isChecked);
                    if (isChecked) {
                        selectedRows = table.rows().data().toArray();
                    } else {
                        selectedRows = [];
                    }
                    console.log('Hàng được chọn:', selectedRows);
                });
            }
        } catch (error) {
            console.error('Lỗi khi tải dữ liệu bảng:', error);
            showError("Có lỗi xảy ra khi tải dữ liệu!");
        }
    }

    let lastSelectedType = ""; // nhớ type đã chọn gần nhất

    // Hàm hiển thị dropdown chọn type
    async function showTypeSelection() {
        const { value: type } = await Swal.fire({
            title: "Chọn Type",
            input: "select",
            inputOptions: {
                "": "Tất cả",
                Input: "Nhập Kho",
                Borrow: "Phát Liệu",
                DefectiveExport: "Xuất Kho"
            },
            inputValue: lastSelectedType, // giữ lựa chọn trước đó
            inputPlaceholder: "Chọn...",
            showCancelButton: true,
            confirmButtonText: "OK",
            cancelButtonText: "Hủy",
            inputValidator: (value) => {
                if (value === undefined) {
                    return "Vui lòng chọn Type!";
                }
                return null;
            }
        });

        if (type !== undefined) {
            lastSelectedType = type; // lưu lại type đã chọn
            Swal.fire({
                icon: "success",
                title: "Đã chọn",
                text: `Bạn đã chọn: ${type || "Tất cả"}`,
                timer: 1000,
                showConfirmButton: false
            });
            loadHistoryTable(type);
        }
    }


    // Sự kiện cho nút "Chọn Loại Giao Dịch"
    if (selectTypeBtn) {
        selectTypeBtn.addEventListener('click', async function () {
            showTypeSelection();
        });
    }

    // Hàm xuất dữ liệu sang Excel
    function exportToExcel() {
        if (!table || !$.fn.DataTable.isDataTable('#historyMaterialsTable')) {
            showWarning("Bảng dữ liệu chưa được tải!");
            return;
        }

        const data = table.rows().data().toArray();
        const wb = XLSX.utils.book_new();
        const ws = XLSX.utils.json_to_sheet(data);
        XLSX.utils.book_append_sheet(wb, ws, "History Materials");
        const fileName = `history_materials_${new Date().toISOString().slice(0, 10)}.xlsx`;
        XLSX.writeFile(wb, fileName);
    }

    // Sự kiện cho nút xuất Excel
    if (exportExcelBtn) {
        exportExcelBtn.addEventListener('click', function () {
            exportToExcel();
        });
    }

    // Tải toàn bộ dữ liệu ngay khi trang được tải
    loadHistoryTable("");
});