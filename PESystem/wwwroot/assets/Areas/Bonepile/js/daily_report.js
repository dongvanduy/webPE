const apiBase = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Bonepile2';

const columnKeys = [
    { key: ['modelName', 'model_name', 'MODEL_NAME', 'model'], label: 'modelName' },
    { key: ['saBpTotalQty', 'bpTotalQty', 'bpTotal', 'saBpQty', 'totalQty'], label: 'bpTotalQty' },
    { key: ['approvedScrap', 'approved_scrap', 'approvedScrapQty', 'scrapApproved'], label: 'approvedScrap' },
    { key: ['fxvOnlineWip', 'fxvOnline', 'fxvWip', 'fxvOnlineQty'], label: 'fxvOnlineWip' },
    { key: ['cbRepairedOnceLt30', 'repairOnceLt30', 'needRepairLt30', 'repairOnceUnder30'], label: 'needRepairLt30' },
    { key: ['cbRepairedOnceGt30', 'repairOnceGt30', 'needRepairGt30', 'repairOnceOver30'], label: 'needRepairGt30' },
    { key: ['cbRepairedTwiceLt30', 'repairTwiceLt30', 'repairedTwiceLt30', 'repairTwiceUnder30'], label: 'repairedTwiceLt30' },
    { key: ['cbRepairedTwiceGt30', 'repairTwiceGt30', 'repairedTwiceGt30', 'repairTwiceOver30'], label: 'repairedTwiceGt30' },
    { key: ['others', 'other', 'otherQty'], label: 'others' },
    { key: ['total', 'totalQty', 'grandTotal'], label: 'total' }
];

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

const renderRows = (rows) => {
    const tableBody = document.querySelector('#reportRepairBeforeTable tbody');
    if (!tableBody) return;
    tableBody.innerHTML = rows.map((row) => {
        const cells = columnKeys.map((config) => {
            const value = pickValue(row, config.key);
            return `<td>${formatNumber(value)}</td>`;
        }).join('');
        return `<tr>${cells}</tr>`;
    }).join('');
};

const appendTotalRow = (payload) => {
    const tableBody = document.querySelector('#reportRepairBeforeTable tbody');
    if (!tableBody) return;
    const totalData = payload?.total || payload?.grandTotal || payload?.summary;
    if (!totalData) return;
    const cells = columnKeys.map((config) => {
        const value = pickValue(totalData, config.key);
        return `<td>${formatNumber(value)}</td>`;
    }).join('');
    tableBody.insertAdjacentHTML('beforeend', `<tr class="table-total-row">${cells}</tr>`);
};

const fetchReportRepairBefore = async () => {
    try {
        showSpinner();
        const response = await axios.get(`${apiBase}/report-repair-before`);
        const payload = response?.data || {};
        const rows = normalizeRows(payload);
        renderRows(rows);
        appendTotalRow(payload);
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
