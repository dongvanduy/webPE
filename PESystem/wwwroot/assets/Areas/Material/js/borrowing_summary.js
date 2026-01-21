document.addEventListener('DOMContentLoaded', () => {
    const API_BASE_URL = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/MaterialSystem';
    const tableSelector = '#borrowingSummaryTable';
    const selectAllCheckbox = document.getElementById('selectAllCheckbox');
    const returnBtn = document.getElementById('return-btn');
    const refreshBtn = document.getElementById('refresh-btn');
    const exportXlsxBtn = document.getElementById('export-xlsx-btn');
    const exportCsvBtn = document.getElementById('export-csv-btn');
    const currentUser = document.getElementById('entryPerson')?.value || '';
    const totalBorrowingLabel = document.getElementById('total-borrowing');

    const AGING_BUCKETS = [
        { label: '≤3 ngày', min: 0, max: 3 },
        { label: '4-7 ngày', min: 4, max: 7 },
        { label: '8-14 ngày', min: 8, max: 14 },
        { label: '15-30 ngày', min: 15, max: 30 },
        { label: '>30 ngày', min: 31, max: Infinity }
    ];

    let table = null;
    let rawData = [];
    let agingChart = null;
    let datalabelsRegistered = false;
    let agingModalTable = null;
    const selectedTaskIds = new Set();

    function createTooltipCell(value) {
        const displayValue = value ?? '';
        return `<span class="tooltip-trigger" data-tooltip="${displayValue}">${displayValue}</span>`;
    }

    function formatDateTime(value) {
        if (!value) return '';
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return '';
        return date.toLocaleString('vi-VN', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false
        });
    }

    function attachTooltipEvents() {
        $('.tooltip-trigger').each(function () {
            const $element = $(this);
            if ($element.data('tooltip-initialized')) {
                return;
            }

            $element.on('mouseover', function (event) {
                const tooltipText = $element.data('tooltip');
                if (!tooltipText) return;
                let tooltip = document.querySelector('.custom-tooltip');
                if (!tooltip) {
                    tooltip = document.createElement('div');
                    tooltip.className = 'custom-tooltip';
                    document.body.appendChild(tooltip);
                }
                tooltip.textContent = tooltipText;
                tooltip.style.display = 'block';
                tooltip.style.left = `${event.pageX + 10}px`;
                tooltip.style.top = `${event.pageY - 20}px`;
            });

            $element.on('mousemove', function (event) {
                const tooltip = document.querySelector('.custom-tooltip');
                if (!tooltip || tooltip.style.display !== 'block') return;
                tooltip.style.left = `${event.pageX + 10}px`;
                tooltip.style.top = `${event.pageY - 20}px`;
            });

            $element.on('mouseout', function () {
                const tooltip = document.querySelector('.custom-tooltip');
                if (tooltip) {
                    tooltip.style.display = 'none';
                }
            });

            $element.data('tooltip-initialized', true);
        });
    }

    function updateSelectedRows() {
        const currentData = table ? table.rows().data().toArray() : [];
        const selectedRows = currentData.filter(row => selectedTaskIds.has(row.task));
        return selectedRows;
    }

    function updateSelectAllCheckboxState() {
        if (!selectAllCheckbox || !table) return;
        const totalRows = table.rows().data().toArray().length;
        const selectedRowsCount = Array.from(selectedTaskIds).length;
        selectAllCheckbox.checked = totalRows > 0 && selectedRowsCount === totalRows;
        selectAllCheckbox.indeterminate = selectedRowsCount > 0 && selectedRowsCount < totalRows;
    }

    function renderTable(data) {
        if ($.fn.DataTable.isDataTable(tableSelector)) {
            $(tableSelector).DataTable().destroy();
        }

        table = $(tableSelector).DataTable({
            data,
            scrollX: true,
            columns: [
                {
                    data: null,
                    orderable: false,
                    render: function (_data, _type, row, meta) {
                        const isChecked = selectedTaskIds.has(row.task);
                        return `<input type="checkbox" class="row-checkbox" data-row-index="${meta.row}" ${isChecked ? 'checked' : ''}/>`;
                    }
                },
                { data: 'task', defaultContent: '', render: createTooltipCell },
                { data: 'maLieu', defaultContent: '', render: createTooltipCell },
                { data: 'nhaCungUng', defaultContent: '', render: createTooltipCell },
                { data: 'dateCode', defaultContent: '', render: createTooltipCell },
                { data: 'lotCode', defaultContent: '', render: createTooltipCell },
                { data: 'op1', defaultContent: '', render: createTooltipCell },
                { data: 'op2', defaultContent: '', render: createTooltipCell },
                { data: 'qty1', defaultContent: 0, render: createTooltipCell },
                { data: 'qty2', defaultContent: 0, render: createTooltipCell },
                { data: 'qty3', defaultContent: 0, render: createTooltipCell },
                { data: 'qty4', defaultContent: 0, render: createTooltipCell },
                {
                    data: 'borrowedTime',
                    defaultContent: '',
                    render: value => createTooltipCell(formatDateTime(value))
                },
                {
                    data: null,
                    defaultContent: 'Đang mượn',
                    render: () => createTooltipCell('Đang mượn')
                }
            ],
            pageLength: 10,
            order: [[11, 'desc']],
            drawCallback: function () {
                attachTooltipEvents();
                updateSelectAllCheckboxState();
            }
        });

        // === CLICK ROW TO HIGHLIGHT ===
        $('#borrowingSummaryTable tbody').on('click', 'tr', function () {
            // Bỏ highlight cũ
            $('#borrowingSummaryTable tbody tr').removeClass('selected-row');

            // Nếu click chính row đó → toggle chọn
            $(this).toggleClass('selected-row');
        });


        $(tableSelector + ' tbody').off('change', '.row-checkbox').on('change', '.row-checkbox', function () {
            const rowIndex = $(this).data('row-index');
            const rowData = table.row(rowIndex).data();
            if (!rowData) return;
            if (this.checked) {
                selectedTaskIds.add(rowData.task);
            } else {
                selectedTaskIds.delete(rowData.task);
            }
            updateSelectAllCheckboxState();
        });

        updateSelectAllCheckboxState();
    }

    function getAgingBucketIndex(item) {
        if (!item?.borrowedTime) return -1;
        const borrowedDate = new Date(item.borrowedTime);
        if (Number.isNaN(borrowedDate.getTime())) return -1;
        const now = new Date();
        const diffMs = now.getTime() - borrowedDate.getTime();
        const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

        return AGING_BUCKETS.findIndex(bucket => {
            const min = bucket.min ?? 0;
            const max = bucket.max ?? Infinity;
            return diffDays >= min && diffDays <= max;
        });
    }

    function calculateAgingBuckets(data) {
        const counts = new Array(AGING_BUCKETS.length).fill(0);

        data.forEach(item => {
            const bucketIndex = getAgingBucketIndex(item);
            if (bucketIndex >= 0) {
                counts[bucketIndex] += 1;
            }
        });

        return { buckets: AGING_BUCKETS, counts };
    }

    function filterDataByBucket(bucketIndex) {
        if (bucketIndex < 0) return [];
        return rawData.filter(item => getAgingBucketIndex(item) === bucketIndex);
    }

    function renderAgingModalTable(bucketIndex, data) {
        const modalTitle = document.getElementById('agingModalTitle');
        const bucket = AGING_BUCKETS[bucketIndex];
        if (modalTitle && bucket) {
            modalTitle.textContent = `Chi tiết nhóm: ${bucket.label}`;
        }

        if ($.fn.DataTable.isDataTable('#agingBucketTable')) {
            $('#agingBucketTable').DataTable().destroy();
        }

        const tbody = document.querySelector('#agingBucketTable tbody');
        if (tbody) {
            tbody.innerHTML = '';
        }

        agingModalTable = null;
        agingModalTable = $('#agingBucketTable').DataTable({
            data,
            scrollX: true,
            columns: [
                { data: 'task', defaultContent: '', render: createTooltipCell },
                { data: 'maLieu', defaultContent: '', render: createTooltipCell },
                { data: 'nhaCungUng', defaultContent: '', render: createTooltipCell },
                { data: 'dateCode', defaultContent: '', render: createTooltipCell },
                { data: 'lotCode', defaultContent: '', render: createTooltipCell },
                { data: 'op1', defaultContent: '', render: createTooltipCell },
                { data: 'op2', defaultContent: '', render: createTooltipCell },
                { data: 'qty1', defaultContent: 0, render: createTooltipCell },
                { data: 'qty2', defaultContent: 0, render: createTooltipCell },
                { data: 'qty3', defaultContent: 0, render: createTooltipCell },
                { data: 'qty4', defaultContent: 0, render: createTooltipCell },
                {
                    data: 'borrowedTime',
                    defaultContent: '',
                    render: value => createTooltipCell(formatDateTime(value))
                },
                {
                    data: null,
                    defaultContent: 'Đang mượn',
                    render: () => createTooltipCell('Đang mượn')
                }
            ],
            pageLength: 10,
            order: [[10, 'desc']],
            drawCallback: function () {
                attachTooltipEvents();
            }
        });
    }

    function showAgingModal(bucketIndex) {
        if (bucketIndex < 0) return;
        const bucket = AGING_BUCKETS[bucketIndex];
        if (!bucket) return;

        const bucketData = filterDataByBucket(bucketIndex);
        renderAgingModalTable(bucketIndex, bucketData);

        const modalElement = document.getElementById('agingModal');
        if (!modalElement) return;

        if (typeof bootstrap !== 'undefined' && bootstrap.Modal) {
            const modalInstance = bootstrap.Modal.getOrCreateInstance(modalElement);
            modalInstance.show();
        } else {
            modalElement.style.display = 'block';
            modalElement.classList.add('show');
        }
    }

    function updateAgingChart(data) {
        const chartCanvas = document.getElementById('agingChart');
        if (!chartCanvas || typeof Chart === 'undefined') return;

        const { buckets, counts } = calculateAgingBuckets(data);

        if (agingChart) {
            agingChart.destroy();
        }

        if (typeof ChartDataLabels !== 'undefined' && !datalabelsRegistered) {
            Chart.register(ChartDataLabels);
            datalabelsRegistered = true;
        }

        agingChart = new Chart(chartCanvas, {
            type: 'bar',
            data: {
                labels: buckets.map(bucket => bucket.label),
                datasets: [
                    {
                        label: 'Số lượng đang mượn',
                        data: counts,
                        backgroundColor: '#76b900'
                    }
                ]
            },
            options: {
                onClick: (_event, elements) => {
                    if (!elements?.length) return;
                    const element = elements[0];
                    const bucketIndex = element.index ?? -1;
                    showAgingModal(bucketIndex);
                },
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    datalabels: {
                        anchor: 'end',
                        align: 'top',
                        color: '#333',
                        font: {
                            weight: 'bold'
                        },
                        formatter: value => (value > 0 ? value : '')
                    },
                    tooltip: {
                        callbacks: {
                            label: context => ` ${context.parsed.y} mã liệu`
                        }
                    }
                },
                scales: {
                    x: {
                        ticks: {
                            font: {
                                size: 12
                            }
                        }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: {
                            precision: 0
                        }
                    }
                }
            }
        });
    }

    async function fetchBorrowingSummary() {
        try {
            showSpinner();
            const response = await fetch(`${API_BASE_URL}/GetBorrowingSummary`);
            if (!response.ok) {
                throw new Error(`Lỗi khi gọi API: ${response.status} ${response.statusText}`);
            }

            rawData = await response.json();
            selectedTaskIds.clear();
            renderTable(rawData);
            updateAgingChart(rawData);
            if (totalBorrowingLabel) {
                totalBorrowingLabel.textContent = `Tổng số liệu đang mượn: ${rawData.length}`;
            }
            if (selectAllCheckbox) {
                selectAllCheckbox.checked = false;
                selectAllCheckbox.indeterminate = false;
            }
        } catch (error) {
            console.error('Lỗi tải thống kê mượn:', error);
            showError('Không thể tải dữ liệu thống kê đang mượn.');
        } finally {
            hideSpinner();
        }
    }

    function exportToXlsx() {
        if (!table || !$.fn.DataTable.isDataTable(tableSelector)) {
            showWarning('Bảng dữ liệu chưa được tải!');
            return;
        }
        const exportData = table.rows().data().toArray().map(row => ({
            TASK: row.task || '',
            MA_LIEU: row.maLieu || '',
            NHA_CUNG_UNG: row.nhaCungUng || '',
            DATE_CODE: row.dateCode || '',
            LOT_CODE: row.lotCode || '',
            OP1: row.op1 || '',
            OP2: row.op2 || '',
            QTY1: row.qty1 ?? 0,
            QTY2: row.qty2 ?? 0,
            QTY3: row.qty3 ?? 0,
            QTY4: row.qty4 ?? 0,
            BORROWED_TIME: formatDateTime(row.borrowedTime),
            TRANG_THAI: 'Đang mượn'
        }));

        const workbook = XLSX.utils.book_new();
        const worksheet = XLSX.utils.json_to_sheet(exportData);
        XLSX.utils.book_append_sheet(workbook, worksheet, 'BorrowingSummary');
        const fileName = `borrowing_summary_${new Date().toISOString().slice(0, 10)}.xlsx`;
        XLSX.writeFile(workbook, fileName);
    }

    function exportToCsv() {
        if (!table || !$.fn.DataTable.isDataTable(tableSelector)) {
            showWarning('Bảng dữ liệu chưa được tải!');
            return;
        }
        const exportData = table.rows().data().toArray().map(row => ({
            TASK: row.task || '',
            MA_LIEU: row.maLieu || '',
            NHA_CUNG_UNG: row.nhaCungUng || '',
            DATE_CODE: row.dateCode || '',
            LOT_CODE: row.lotCode || '',
            OP1: row.op1 || '',
            OP2: row.op2 || '',
            QTY1: row.qty1 ?? 0,
            QTY2: row.qty2 ?? 0,
            QTY3: row.qty3 ?? 0,
            QTY4: row.qty4 ?? 0,
            BORROWED_TIME: formatDateTime(row.borrowedTime),
            TRANG_THAI: 'Đang mượn'
        }));
        const worksheet = XLSX.utils.json_to_sheet(exportData);
        const csv = XLSX.utils.sheet_to_csv(worksheet);
        const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.setAttribute('download', `borrowing_summary_${new Date().toISOString().slice(0, 10)}.csv`);
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    }

    function validateReturnQuantities(formValues, rows) {
        return rows.every(row => {
            const borrowedTotal = (row.qty1 ?? 0) + (row.qty2 ?? 0);
            return borrowedTotal === (formValues.qtyOK + formValues.qtyNG);
        });
    }

    async function handleReturn() {
        const selectedRows = updateSelectedRows();
        if (selectedRows.length === 0) {
            showWarning('Vui lòng chọn ít nhất 1 hàng!');
            return;
        }

        const referenceRow = selectedRows[0];
        const suggestedQtyOK = Math.max((referenceRow.qty1 ?? 0) - (referenceRow.qty3 ?? 0), 0);
        const suggestedQtyNG = Math.max((referenceRow.qty2 ?? 0) - (referenceRow.qty4 ?? 0), 0);

        try {
            const { value: formValues } = await Swal.fire({
                title: 'Nhập thông tin thu liệu',
                html: `
                    <style>
                        .swal-form-row {
                            display: flex;
                            align-items: center;
                            justify-content: space-between;
                            gap: 8px;
                            margin-bottom: 6px;
                        }
                        .swal-form-row label {
                            flex: 0 0 140px;
                            font-weight: 500;
                            text-align: left;
                        }
                        .swal-form-row input {
                            flex: 1;
                        }
                        .swal-helper {
                            font-size: 12px;
                            color: #6c757d;
                            margin-bottom: 8px;
                        }
                    </style>
                    <div class="swal-helper">Tổng số lượng cần thu: ${ (referenceRow.qty1 ?? 0) + (referenceRow.qty2 ?? 0) }</div>
                    <div class="swal-form-row">
                        <label for="swal-qtyOK">Số lượng OK:</label>
                        <input type="number" id="swal-qtyOK" class="swal2-input" min="0" value="0" />
                    </div>
                    <div class="swal-form-row">
                        <label for="swal-qtyNG">Số lượng NG:</label>
                        <input type="number" id="swal-qtyNG" class="swal2-input" min="0" value="${suggestedQtyOK}" />
                    </div>
                    <div class="swal-form-row">
                        <label for="swal-remark">Remark (Note):</label>
                        <input type="text" id="swal-remark" class="swal2-input" placeholder="Nhập ghi chú..." />
                    </div>
                    <div class="swal-form-row">
                        <label for="swal-opReturn">Người trả (OP):</label>
                        <input type="text" id="swal-opReturn" class="swal2-input" placeholder="Nhập mã người trả"/>
                    </div>
                `,
                focusConfirm: false,
                preConfirm: () => {
                    const qtyOK = parseInt(document.getElementById('swal-qtyOK').value || '0', 10);
                    const qtyNG = parseInt(document.getElementById('swal-qtyNG').value || '0', 10);
                    const remark = document.getElementById('swal-remark').value.trim();
                    const opReturn = document.getElementById('swal-opReturn').value.trim();

                    if (!opReturn) {
                        Swal.showValidationMessage('Vui lòng nhập mã người trả.');
                        return false;
                    }

                    if (qtyOK < 0 || qtyNG < 0) {
                        Swal.showValidationMessage('Số lượng không hợp lệ.');
                        return false;
                    }

                    return { qtyOK, qtyNG, remark, opReturn };
                },
                showCancelButton: true,
                confirmButtonText: 'Xác nhận',
                cancelButtonText: 'Hủy',
                showLoaderOnConfirm: true,
                allowOutsideClick: () => !Swal.isLoading()
            });

            if (!formValues) return;

            if (!validateReturnQuantities(formValues, selectedRows)) {
                showError('Tổng số lượng OK + NG phải bằng tổng số lượng phát của từng mã.');
                return;
            }

            for (const row of selectedRows) {
                const response = await fetch(`${API_BASE_URL}/ReturnMaterial`, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({
                        task: row.task || '',
                        maLieu: row.maLieu || '',
                        nhaCungUng: row.nhaCungUng || '',
                        dateCode: row.dateCode || '',
                        lotCode: row.lotCode || '',
                        qtyOK: formValues.qtyOK,
                        qtyNG: formValues.qtyNG,
                        remark: formValues.remark || '',
                        op: formValues.opReturn
                    })
                });

                if (!response.ok) {
                    throw new Error(`Lỗi khi gọi API ReturnMaterial: ${response.status} ${response.statusText}`);
                }

                await response.json();
            }

            showSuccess('Thu liệu thành công!');
            await fetchBorrowingSummary();
        } catch (error) {
            if (error?.isDismissed) {
                return;
            }
            console.error('Lỗi khi thu liệu:', error);
            showError('Không thể thu liệu. Vui lòng kiểm tra lại thông tin.');
        }
    }

    if (selectAllCheckbox) {
        selectAllCheckbox.addEventListener('change', function () {
            if (!table) return;
            const isChecked = this.checked;
            selectedTaskIds.clear();
            if (isChecked) {
                table.rows().every(function () {
                    const rowData = this.data();
                    if (rowData?.task) {
                        selectedTaskIds.add(rowData.task);
                    }
                });
            }
            $(tableSelector + ' .row-checkbox').prop('checked', isChecked);
            updateSelectAllCheckboxState();
        });
    }

    if (refreshBtn) {
        refreshBtn.addEventListener('click', () => {
            fetchBorrowingSummary();
        });
    }

    if (exportXlsxBtn) {
        exportXlsxBtn.addEventListener('click', exportToXlsx);
    }

    if (exportCsvBtn) {
        exportCsvBtn.addEventListener('click', exportToCsv);
    }

    if (returnBtn) {
        returnBtn.addEventListener('click', handleReturn);
    }

    const agingModalElement = document.getElementById('agingModal');
    if (agingModalElement) {
        agingModalElement.addEventListener('shown.bs.modal', () => {
            if (agingModalTable) {
                agingModalTable.columns.adjust();
            } else if ($.fn.DataTable.isDataTable('#agingBucketTable')) {
                $('#agingBucketTable').DataTable().columns.adjust();
            }
        });
    }

    fetchBorrowingSummary();
});
