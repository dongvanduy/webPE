const apiBase = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Bonepile2';

const productLineKeys = ['productLine', 'product_line', 'PRODUCT_LINE', 'modelName', 'model_name', 'MODEL_NAME', 'model'];

const columnOrder = [
    'productLine',
    'bpTotalQty',
    'approvedScrap',
    'fxvOnlineWip',
    'needRepairLt30',
    'needRepairGt30',
    'repairedTwiceLt30',
    'repairedTwiceGt30',
    'others',
    'total'
];

const statusBuckets = {
    approvedScrap: new Set([
        'scraphastask',
        'scraplacktask',
        'waitingapprovalscrap',
        'waitingapprovalbga',
        'approvedbga',
        'waitingscrap'
    ]),
    fxvOnlineWip: new Set(['reworkfg']),
    needRepairLt30: new Set([
        'waiting repair aging day <30',
        'cb repaired once but aging day <30'
    ]),
    needRepairGt30: new Set([
        'waiting repair aging day >30',
        'cb repaired once but aging day >30'
    ]),
    repairedTwiceLt30: new Set(['cb repaired twice but aging day <30']),
    repairedTwiceGt30: new Set(['cb repaired twice but aging day >30'])
};

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

const normalizeRows = (payload) => {
    if (!payload) return [];
    if (Array.isArray(payload)) return payload;
    if (Array.isArray(payload.data)) return payload.data;
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

const appendTotalRow = (payload, groupedRows) => {
    const tableBody = document.querySelector('#reportRepairBeforeTable tbody');
    if (!tableBody) return;
    const totalData = payload?.total || payload?.grandTotal || payload?.summary;
    const totals = {
        productLine: 'Total',
        bpTotalQty: 0,
        approvedScrap: 0,
        fxvOnlineWip: 0,
        needRepairLt30: 0,
        needRepairGt30: 0,
        repairedTwiceLt30: 0,
        repairedTwiceGt30: 0,
        others: 0,
        total: 0
    };

    if (totalData) {
        totals.productLine = totalData.productLine || totalData.label || totals.productLine;
        totals.bpTotalQty = sumNumber(totalData.bpTotalQty || totalData.totalQty || totalData.total);
        totals.approvedScrap = sumNumber(totalData.approvedScrap);
        totals.fxvOnlineWip = sumNumber(totalData.fxvOnlineWip);
        totals.needRepairLt30 = sumNumber(totalData.needRepairLt30);
        totals.needRepairGt30 = sumNumber(totalData.needRepairGt30);
        totals.repairedTwiceLt30 = sumNumber(totalData.repairedTwiceLt30);
        totals.repairedTwiceGt30 = sumNumber(totalData.repairedTwiceGt30);
        totals.others = sumNumber(totalData.others);
        totals.total = sumNumber(totalData.total || totalData.totalQty);
    } else if (Array.isArray(groupedRows)) {
        groupedRows.forEach((row) => {
            totals.bpTotalQty += sumNumber(row.bpTotalQty);
            totals.approvedScrap += sumNumber(row.approvedScrap);
            totals.fxvOnlineWip += sumNumber(row.fxvOnlineWip);
            totals.needRepairLt30 += sumNumber(row.needRepairLt30);
            totals.needRepairGt30 += sumNumber(row.needRepairGt30);
            totals.repairedTwiceLt30 += sumNumber(row.repairedTwiceLt30);
            totals.repairedTwiceGt30 += sumNumber(row.repairedTwiceGt30);
            totals.others += sumNumber(row.others);
            totals.total += sumNumber(row.total);
        });
    } else {
        return;
    }

    const cells = columnOrder.map((key) => `<td>${formatNumber(totals[key])}</td>`).join('');
    tableBody.insertAdjacentHTML('beforeend', `<tr class="table-total-row">${cells}</tr>`);
};

const fetchReportRepairBefore = async () => {
    try {
        showSpinner();
        const response = await axios.get(`${apiBase}/report-repair-before`);
        const payload = response?.data || {};
        const rows = normalizeRows(payload);
        const groupedRows = renderRows(rows);
        appendTotalRow(payload, groupedRows);
    } catch (error) {
        console.error('Không thể tải report-repair-before', error);
        alert('Không thể tải dữ liệu report-repair-before');
    } finally {
        hideSpinner();
    }
};

document.addEventListener('DOMContentLoaded', () => {
    fetchReportRepairBefore();
});
