const apiBase = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/report';
const productLineKeys = ['productLine', 'product_line', 'PRODUCT_LINE'];
const columnFilterMap = {
    1: 'all',
    2: 'approvedScrap',
    3: 'fxvOnlineWip',
    4: 'needRepairLt30',
    5: 'needRepairGt30',
    6: 'repairedTwiceLt30',
    7: 'repairedTwiceGt30',
    8: 'others'
};
const columnOrder = [
    'productLine',
    'bpTotalQty',
    'approvedScrap',
    'fxvOnlineWip',
    'needRepairLt30',
    'needRepairGt30',
    'repairedTwiceLt30',
    'repairedTwiceGt30',
    'others'
];
const statusBuckets = {
    approvedScrap: new Set([
        'scraphastask',
        'scraplacktask'
    ]),
    fxvOnlineWip: new Set(['waiting repair']),
    needRepairLt30: new Set([
        'cb repaired once but aging day <30'
    ]),
    needRepairGt30: new Set([
        'cb repaired once but aging day >30'
    ]),
    repairedTwiceLt30: new Set(['cb repaired twice but aging day <30']),
    repairedTwiceGt30: new Set(['cb repaired twice but aging day >30'])
};
const allBucketStatuses = new Set(
    Object.values(statusBuckets).flatMap((set) => Array.from(set))
);
let cachedRecords = [];
let modalTable = null;
const pickValue = (row, keys) => {
    if (!row) return '';
    for (const key of keys) {
        if (row[key] !== undefined && row[key] !== null) {
            return row[key];
        }
    }
    return '';
};
const formatNumber = (value) => {
    if (value === null || value === undefined || value === '') return '';
    const numeric = Number(value);
    if (Number.isNaN(numeric)) return value;
    return numeric.toLocaleString();
};
const formatDateTime = (value) => {
    if (!value) return '';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
};
const normalizeRows = (payload) => {
    if (!payload) return [];
    if (Array.isArray(payload)) return payload;
    if (Array.isArray(payload.data)) return payload.data;
    if (Array.isArray(payload.Data)) return payload.Data; // Handle potential PascalCase
    if (Array.isArray(payload.items)) return payload.items;
    if (Array.isArray(payload.rows)) return payload.rows;
    return [];
};
const sumNumber = (value) => {
    const numeric = Number(value);
    return Number.isNaN(numeric) ? 0 : numeric;
};
const normalizeStatus = (value) => (value || '').toString().trim().toLowerCase();
const buildGroupedRows = (rows) => {
    const grouped = new Map();
    rows.forEach((row) => {
        const productLine = (pickValue(row, productLineKeys) || '').toString().trim();
        if (!productLine) return;
        if (!grouped.has(productLine)) {
            grouped.set(productLine, {
                productLine,
                bpTotalQty: 0,
                approvedScrap: 0,
                fxvOnlineWip: 0,
                needRepairLt30: 0,
                needRepairGt30: 0,
                repairedTwiceLt30: 0,
                repairedTwiceGt30: 0,
                others: 0,
                total: 0
            });
        }
        const target = grouped.get(productLine);
        const status = normalizeStatus(row.status || row.Status);
        target.bpTotalQty += 1;
        target.total += 1;
        if (statusBuckets.approvedScrap.has(status)) {
            target.approvedScrap += 1;
        } else if (statusBuckets.fxvOnlineWip.has(status)) {
            target.fxvOnlineWip += 1;
        } else if (statusBuckets.needRepairLt30.has(status)) {
            target.needRepairLt30 += 1;
        } else if (statusBuckets.needRepairGt30.has(status)) {
            target.needRepairGt30 += 1;
        } else if (statusBuckets.repairedTwiceLt30.has(status)) {
            target.repairedTwiceLt30 += 1;
        } else if (statusBuckets.repairedTwiceGt30.has(status)) {
            target.repairedTwiceGt30 += 1;
        } else {
            target.others += 1;
        }
    });
    return Array.from(grouped.values());
};
const renderRows = (rows) => {
    const tableBody = document.querySelector('#reportRepairBeforeTable tbody');
    if (!tableBody) return [];
    const groupedRows = buildGroupedRows(rows);
    tableBody.innerHTML = groupedRows.map((row) => {
        const cells = columnOrder.map((key) => {
            const value = row[key];
            return `<td>${formatNumber(value)}</td>`;
        }).join('');
        return `<tr>${cells}</tr>`;
    }).join('');
    return groupedRows;
};
const appendTotalRow = (groupedRows) => {
    const tableBody = document.querySelector('#reportRepairBeforeTable tbody');
    if (!tableBody) return;
    const totals = {
        productLine: 'Total',
        bpTotalQty: 0,
        approvedScrap: 0,
        fxvOnlineWip: 0,
        needRepairLt30: 0,
        needRepairGt30: 0,
        repairedTwiceLt30: 0,
        repairedTwiceGt30: 0,
        others: 0
    };
    groupedRows.forEach((row) => {
        totals.bpTotalQty += sumNumber(row.bpTotalQty);
        totals.approvedScrap += sumNumber(row.approvedScrap);
        totals.fxvOnlineWip += sumNumber(row.fxvOnlineWip);
        totals.needRepairLt30 += sumNumber(row.needRepairLt30);
        totals.needRepairGt30 += sumNumber(row.needRepairGt30);
        totals.repairedTwiceLt30 += sumNumber(row.repairedTwiceLt30);
        totals.repairedTwiceGt30 += sumNumber(row.repairedTwiceGt30);
        totals.others += sumNumber(row.others);
    });
    const cells = columnOrder.map((key) => `<td>${formatNumber(totals[key])}</td>`).join('');
    tableBody.insertAdjacentHTML('beforeend', `<tr class="table-total-row">${cells}</tr>`);
};
const getProductLineValue = (row) => (pickValue(row, productLineKeys) || '').toString().trim();
const getStatusValue = (row) => normalizeStatus(row.status || row.Status);
const filterRecords = (productLine, filterKey) => {
    if (!productLine) return [];
    return cachedRecords.filter((record) => {
        if (getProductLineValue(record) !== productLine) return false;
        if (filterKey === 'all') return true;
        const status = getStatusValue(record);
        if (filterKey === 'others') return status && !allBucketStatuses.has(status);
        return statusBuckets[filterKey]?.has(status);
    });
};
const buildModalRows = (records) => records.map((record) => ({
    serialNumber: record.sn || record.Sn || record.SERIAL_NUMBER || '',
    modelName: record.modelName || record.ModelName || record.MODEL_NAME || '',
    productLine: getProductLineValue(record),
    status: record.status || record.Status || '',
    agingDay: record.agingDay || record.AgingDay || record.AGING_DAY || '',
    moNumber: record.moNumber || record.MoNumber || record.MO_NUMBER || '',
    wipGroup: record.wipGroup || record.WipGroup || record.WIP_GROUP || '',
    testGroup: record.testGroup || record.TestGroup || record.TEST_GROUP || '',
    testTime: record.testTime || record.TestTime || record.TEST_TIME || '',
    testCode: record.testCode || record.TestCode || record.TEST_CODE || '',
    errorItem: record.errorCodeItem || record.ErrorCodeItem || record.ERROR_ITEM_CODE || '',
    errorDesc: record.errorDesc || record.ErrorDesc || record.ERROR_DESC || '',
    checkInDate: record.checkInDate || record.CheckInDate || record.CHECKIN_DATE || ''
}));
const openDetailModal = (productLine, filterKey) => {
    if (!productLine || productLine === 'Total') return;
    const records = filterRecords(productLine, filterKey);
    const rows = buildModalRows(records);
    const modalTitle = document.getElementById('reportDetailTitle');
    if (modalTitle) {
        modalTitle.textContent = `Product Line: ${productLine} (${rows.length})`;
    }
    if (modalTable) {
        modalTable.clear();
        modalTable.rows.add(rows);
        modalTable.draw();
    } else {
        modalTable = $('#reportDetailTable').DataTable({
            data: rows,
            columns: [
                { data: 'serialNumber' },
                { data: 'modelName' },
                { data: 'productLine' },
                { data: 'status' },
                { data: 'agingDay' },
                { data: 'moNumber' },
                { data: 'wipGroup' },
                { data: 'testGroup' },
                {
                    data: 'testTime',
                    render: (data) => formatDateTime(data)
                },
                { data: 'testCode' },
                { data: 'errorItem' },
                { data: 'errorDesc' },
                {
                    data: 'checkInDate',
                    render: (data) => formatDateTime(data)
                }
            ],
            pageLength: 15,
            dom: 'B<"top d-flex align-items-center gap-2"f>rt<"bottom d-flex justify-content-between"ip>',
            info: false
        });
    }
    $('#reportDetailModal').modal('show');
};
const bindTableClick = () => {
    const tableBody = document.querySelector('#reportRepairBeforeTable tbody');
    if (!tableBody) return;
    tableBody.addEventListener('click', (event) => {
        const cell = event.target.closest('td');
        if (!cell) return;
        const row = cell.parentElement;
        if (!row) return;
        const productLine = row.firstElementChild?.textContent?.trim();
        const filterKey = columnFilterMap[cell.cellIndex];
        if (!filterKey || !productLine || productLine === 'Total') return;
        openDetailModal(productLine, filterKey);
    });
};
const fetchReportRepairBefore = async () => {
    try {
        showSpinner();
        const response = await axios.get(`${apiBase}/report-repair-before`);
        const payload = response?.data || {};
        const rows = normalizeRows(payload);
        cachedRecords = rows;
        const groupedRows = renderRows(rows);
        appendTotalRow(groupedRows);
    } catch (error) {
        console.error('Không thể tải report-repair-before', error);
        alert('Không thể tải dữ liệu report-repair-before');
    } finally {
        hideSpinner();
    }
};
document.addEventListener('DOMContentLoaded', () => {
    fetchReportRepairBefore();
    bindTableClick();
});