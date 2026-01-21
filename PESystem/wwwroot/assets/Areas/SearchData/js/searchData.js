document.addEventListener('DOMContentLoaded', () => {
    const scanEl = document.getElementById('scan-log');
    const foundEl = document.getElementById('repairCount');
    const fromInput = document.getElementById('fromTime');
    const toInput = document.getElementById('toTime');
    const applyBtn = document.getElementById('applyTime');
    const spinner = document.getElementById('spinner-overlay');
    const listTitle = document.getElementById('listDetailTitle');
    const detailModalEl = document.getElementById('listDetailModal');
    const summaryModalEl = document.getElementById('scanSummaryModal');
    const summaryTitleEl = document.getElementById('scanSummaryTitle');
    const totalScanBtn = document.getElementById('view-total-scan');
    const foundScanBtn = document.getElementById('view-found-scan');
    const searchBtn = document.getElementById('search-btn');
    const textarea = document.getElementById('serialNumber');
    const scanInput = document.getElementById('scanSerialInput');
    const scanHint = document.getElementById('scanSerialHint');
    const modal = new bootstrap.Modal(document.getElementById('serialModal'));

    let listsTable;
    let itemsTable;
    let summaryTable;
    let activeSummaryFilter = 'total';
    let currentListData = null;
    let currentListItems = [];
    let lastMatchedSerial = null;
    let highlightTimerId = null;
    let isProcessingScan = false;
    const detailModal = detailModalEl && typeof bootstrap !== 'undefined'
        ? new bootstrap.Modal(detailModalEl)
        : null;
    const summaryModal = summaryModalEl && typeof bootstrap !== 'undefined'
        ? new bootstrap.Modal(summaryModalEl)
        : null;
    const summaryApiUrl = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/scan/summary';
    let summaryCache = { key: '', items: [], total: 0, found: 0, loaded: false };
    let summaryPromise = null;

    async function fetchSerialInfo(serialNumber) {
        if (!serialNumber) return null;
        const encoded = encodeURIComponent(serialNumber);
        const url = `https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Product/GetSNInfo?serialNumber=${encoded}`;
        try {
            const response = await fetch(url);
            if (!response.ok) return null;
            const json = await response.json();

            if (json && json.success && json.data) {
                return {
                    modelName: json.data.modelName || '',
                    productLine: json.data.productLine || '',
                    wipGroup: json.data.wipGroup || ''
                };
            }
            return null;
        } catch (error) {
            console.error('Lỗi khi gọi GetSNInfo:', error);
            return null;
        }
    }
    function extractWipGroup(info) {
        if (!info || typeof info !== 'object') return '';
        if (info.wipGroup) return info.wipGroup.trim();
        if (info.data && typeof info.data === 'object' && info.data.wipGroup)
            return info.data.wipGroup.trim();
        return '';
    }

    function toggleSpinner(show) {
        if (!spinner) return;
        spinner.style.display = show ? 'flex' : 'none';
    }


    function normalizeSerial(value) {
        return (value ?? '').toString().trim().toLowerCase();
    }

    function getListId(list) {
        if (!list || typeof list !== 'object') {
            return null;
        }
        const candidate = list.searchListId ?? list.searchListID ?? list.id ?? list.Id ?? list.SearchListId;
        if (candidate === undefined || candidate === null) {
            return null;
        }
        const numeric = Number(candidate);
        return Number.isNaN(numeric) ? candidate : numeric;
    }

    function updateScanHint(listData) {
        if (!scanHint) {
            return;
        }
        if (!listData) {
            scanHint.textContent = 'Chọn danh sách cần tra cứu và quét Serial Number để kiểm tra.';
            return;
        }
        const total = listData.totalItems ?? listData.TotalItems ?? currentListItems.length;
        const found = listData.foundItems ?? listData.FoundItems ?? currentListItems.filter(item => isFoundValue(item?.isFound)).length;
        const name = listData.listName ?? listData.ListName ?? '';
        const parts = [];
        if (name) {
            parts.push(`Danh sách "${name}"`);
        }
        if (total || total === 0) {
            parts.push(`(${found}/${total} đã tìm thấy)`);
        }
        parts.push('Quét Serial Number để kiểm tra và cập nhật trạng thái.');
        scanHint.textContent = parts.join(' ');
    }

    function syncCurrentListItem(updatedItem) {
        if (!updatedItem) {
            return;
        }
        const normalized = normalizeSerial(updatedItem.serialNumber ?? updatedItem.SerialNumber);
        currentListItems = currentListItems.map(item => {
            const itemSerial = normalizeSerial(item?.serialNumber ?? item?.SerialNumber);
            if (normalized && normalized === itemSerial) {
                return { ...item, ...updatedItem };
            }
            return item;
        });
    }

    function incrementFoundCount(listData) {
        if (!listData || typeof listData !== 'object') {
            return;
        }
        const rawValue = listData.foundItems ?? listData.FoundItems ?? 0;
        const currentValue = Number(rawValue);
        const nextValue = Number.isNaN(currentValue) ? 1 : currentValue + 1;
        if ('foundItems' in listData) {
            listData.foundItems = nextValue;
        } else if ('FoundItems' in listData) {
            listData.FoundItems = nextValue;
        } else {
            listData.foundItems = nextValue;
        }
    }

    function clearHighlight() {
        lastMatchedSerial = null;
        if (highlightTimerId) {
            clearTimeout(highlightTimerId);
            highlightTimerId = null;
        }
        if (itemsTable) {
            itemsTable.rows().invalidate().draw(false);
        }
    }


    function formatLocalDateTime(date) {
        if (!(date instanceof Date) || Number.isNaN(date.getTime())) return '';
        const pad = n => String(n).padStart(2, '0');
        const Y = date.getFullYear();
        const M = pad(date.getMonth() + 1);
        const D = pad(date.getDate());
        const h = pad(date.getHours());
        const m = pad(date.getMinutes());
        // datetime-local expects "YYYY-MM-DDTHH:MM"
        return `${Y}-${M}-${D}T${h}:${m}`;
    }


    function buildRangeParams() {
        const params = new URLSearchParams();
        if (fromInput?.value) {
            // Gửi đúng định dạng local, không timezone
            const fromStr = `${fromInput.value}:00`;
            params.append('from', fromStr);
        }
        if (toInput?.value) {
            const toStr = `${toInput.value}:00`;
            params.append('to', toStr);
        }
        console.log("⏰ Sending params (no timezone):", params.toString());
        return params;
    }




    function setDefaultRange() {
        if (!fromInput || !toInput) return;
        const now = new Date();
        const start = new Date(now.getTime());
        start.setDate(start.getDate() - 1);
        start.setHours(7, 30, 0, 0);
        fromInput.value = formatLocalDateTime(start);
        toInput.value = formatLocalDateTime(now);
    }

    function getRangeKey(params) {
        return params.toString();
    }

    function extractNumber(sources, keys) {
        for (const source of sources) {
            if (!source || typeof source !== 'object' || Array.isArray(source)) {
                continue;
            }
            for (const key of keys) {
                if (!(key in source)) {
                    continue;
                }
                const raw = source[key];
                if (raw === undefined || raw === null || raw === '') {
                    continue;
                }
                const rawValue = typeof raw === 'string' ? raw.replace(/,/g, '').trim() : raw;
                const num = Number(rawValue);
                if (!Number.isNaN(num)) {
                    return num;
                }
            }
        }
        return null;
    }

    function extractSummaryData(payload) {
        if (!payload || typeof payload !== 'object') {
            return { items: [], total: 0, found: 0 };
        }

        let items = [];

        // Nếu API có trả foundItems / scannedItems
        if (Array.isArray(payload.foundItems) || Array.isArray(payload.scannedItems)) {
            const foundItems = (payload.foundItems || []).map(r => ({
                ...r,
                isFound: true
            }));
            const scannedItems = (payload.scannedItems || []).map(r => ({
                ...r,
                isFound: false
            }));
            items = [...foundItems, ...scannedItems].map(normalizeSummaryRecord);
        } else {
            // fallback: dò các key cũ
            const collectionKeys = [
                'items', 'data', 'list', 'lists', 'rows',
                'records', 'result', 'results'
            ];
            let rawItems = Array.isArray(payload) ? payload : [];
            if (!rawItems.length) {
                for (const key of collectionKeys) {
                    const value = payload[key];
                    if (Array.isArray(value)) {
                        rawItems = value;
                        break;
                    }
                }
            }
            items = Array.isArray(rawItems) ? rawItems.map(normalizeSummaryRecord) : [];
        }

        // Tính tổng số và số đã tìm thấy
        const total = payload.scanCount ?? payload.total ?? items.length;
        const found = payload.foundCount ?? payload.found ?? items.filter(x => x.isFound).length;

        return { items, total, found };
    }

    function renderTable(data) {
        const tbody = document.querySelector('#serialTable tbody');
        tbody.innerHTML = '';
        currentData = data; // lưu lại dữ liệu

        if (!data.length) {
            const toast = new bootstrap.Toast(document.getElementById('noDataToast'));
            toast.show();
            return;
        }

        data.forEach(item => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
            <td>${item.serialNumber || ''}</td>
            <td>${item.createdAt || ''}</td>
        `;
            tbody.appendChild(tr);
        });

        modal.show();
    }

    // Xuất Excel
    document.getElementById('exportExcelBtn')?.addEventListener('click', () => {
        if (!currentData.length) {
            alert("Không có dữ liệu để xuất!");
            return;
        }

        // Chuyển dữ liệu thành worksheet
        const wsData = currentData.map(item => ({
            "Serial Number": item.serialNumber,
            "Created At": item.createdAt
        }));

        const ws = XLSX.utils.json_to_sheet(wsData);
        const wb = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(wb, ws, "Serials");

        // Xuất file
        XLSX.writeFile(wb, "serial_numbers.xlsx");
    });

    async function fetchSummary(params) {
        const query = params.toString();
        const url = query ? `${summaryApiUrl}?${query}` : summaryApiUrl;
        const response = await fetch(url);
        if (!response.ok) {
            throw new Error('Failed to fetch summary');
        }
        const payload = await response.json();
        return extractSummaryData(payload);
    }

    async function ensureSummary(forceRefresh = false) {
        const params = buildRangeParams();
        const key = getRangeKey(params);
        if (!forceRefresh && summaryCache.loaded && summaryCache.key === key) {
            return summaryCache;
        }
        if (!summaryPromise || forceRefresh || summaryCache.key !== key) {
            const paramsCopy = new URLSearchParams(params);
            const keyCopy = key;
            const fetchPromise = (async () => {
                const data = await fetchSummary(paramsCopy);
                summaryCache = {
                    key: keyCopy,
                    items: data.items,
                    total: data.total,
                    found: data.found,
                    loaded: true
                };
                return summaryCache;
            })();
            summaryPromise = fetchPromise;
            fetchPromise.finally(() => {
                if (summaryPromise === fetchPromise) {
                    summaryPromise = null;
                }
            });
        }
        try {
            return await summaryPromise;
        } catch (error) {
            summaryCache = { key: '', items: [], total: 0, found: 0, loaded: false };
            throw error;
        }
    }

    async function updateCounts(forceRefresh = false) {
        if (!scanEl || !foundEl) return;
        try {
            const summary = await ensureSummary(forceRefresh);
            scanEl.textContent = summary.total ?? 0;
            foundEl.textContent = summary.found ?? 0;
            if (summaryModalEl?.classList.contains('show')) {
                const items = activeSummaryFilter === 'found'
                    ? summary.items.filter(item => item.isFound)
                    : summary.items;
                renderSummaryTable(items);
            }
        } catch {
            scanEl.textContent = 'N/A';
            foundEl.textContent = 'N/A';
            if (summaryModalEl?.classList.contains('show')) {
                renderSummaryTable([]);
            }
        }
    }

    function isFoundValue(value) {
        if (value === undefined || value === null) {
            return false;
        }
        if (typeof value === 'boolean') {
            return value;
        }
        if (typeof value === 'number') {
            return value !== 0;
        }
        if (typeof value === 'string') {
            const normalized = value.trim().toLowerCase();
            if (normalized === '' || normalized === '0' || normalized === 'false' || normalized === 'no' || normalized === 'n') {
                return false;
            }
            return ['1', 'true', 'yes', 'y', 'found'].includes(normalized);
        }
        return Boolean(value);
    }

    function renderSearchLists(data) {
        const lists = Array.isArray(data)
            ? data.slice().sort((a, b) => {
                const aTime = a?.createdAt ? new Date(a.createdAt).getTime() : 0;
                const bTime = b?.createdAt ? new Date(b.createdAt).getTime() : 0;
                return bTime - aTime;
            })
            : [];
        if (!listsTable) {
            listsTable = $('#searchListsTable').DataTable({
                data: lists,
                dom: '<"top d-flex align-items-center"fB>rt<"bottom"ip>',
                buttons: [
                    {
                        extend: 'excelHtml5',
                        text: '<img src="/assets/img/excel.png" style="height:32px; margin:5px;">',
                        title: 'Search List',
                        exportOptions: {
                            columns: ':visible:not(:last-child)'
                        }
                    }
                ],
                columns: [
                    { data: 'listName', title: 'List Name', defaultContent: '' },
                    {
                        data: 'totalItems',
                        title: 'Tổng SN',
                        render: value => (value ?? 0)
                    },
                    {
                        data: 'foundItems',
                        title: 'Đã tìm thấy',
                        render: value => (value ?? 0)
                    },
                    { data: 'createdBy', title: 'Người tạo', defaultContent: '' },
                    {
                        data: 'createdAt',
                        title: 'Ngày tạo',
                        render: (value, type) => {
                            if (!value) {
                                return '';
                            }
                            const date = new Date(value);
                            if (Number.isNaN(date.getTime())) {
                                return '';
                            }
                            if (type === 'sort' || type === 'type') {
                                return date.getTime();
                            }
                            return date.toLocaleString();
                        },
                        defaultContent: ''
                    },
                    {
                        data: null,
                        title: 'Chi tiết',
                        orderable: false,
                        searchable: false,
                        className: 'text-center',
                        defaultContent: '',
                        render: () => '<button type="button" class="btn btn-link detail-link p-0" data-action="detail">Detail</button>'
                    }
                ],
                order: [[4, 'desc']],
                language: {
                    emptyTable: 'Không có dữ liệu'
                }
            });

            $('#searchListsTable tbody').on('click', 'button[data-action="detail"]', function () {
                const rowData = listsTable.row($(this).closest('tr')).data();
                if (rowData) {
                    showListItems(rowData);
                }
            });
        } else {
            listsTable.clear().rows.add(lists);
        }
        listsTable.order([4, 'desc']);
        listsTable.draw();
    }

    function renderListItems(items) {
        currentListItems = Array.isArray(items)
            ? items.map(item => ({ ...item }))
            : [];
        const data = currentListItems;
        if (!itemsTable) {
            itemsTable = $('#listItemsTable').DataTable({
                data,
                dom: '<"top d-flex align-items-center"fB>rt<"bottom"ip>',
                buttons: [
                    {
                        extend: 'excelHtml5',
                        text: '<img src="/assets/img/excel.png" style="height:32px; margin:5px;">',
                        title: () => (listTitle?.textContent?.trim() || 'Danh sách SerialNumber')
                    }
                ],
                columns: [
                    { data: 'serialNumber', title: 'Serial Number', defaultContent: '' },
                    {
                        data: 'isFound',
                        title: 'Đã tìm thấy',
                        render: value => (isFoundValue(value) ? 'Có' : 'Không')
                    },
                    {
                        data: 'scanTime',
                        title: 'Thời gian scan',
                        render: (value, type) => {
                            if (!value) {
                                return '';
                            }
                            const date = new Date(value);
                            if (Number.isNaN(date.getTime())) {
                                return '';
                            }
                            if (type === 'sort' || type === 'type') {
                                return date.getTime();
                            }
                            return date.toLocaleString();
                        }
                    }
                ],
                order: [[0, 'asc']],
                language: {
                    emptyTable: 'Không có dữ liệu'
                },
                rowCallback: (row, rowData) => {
                    if (rowData && isFoundValue(rowData.isFound)) {
                        row.classList.add('table-success');
                    } else {
                        row.classList.remove('table-success');
                    }
                    const serial = normalizeSerial(rowData?.serialNumber ?? rowData?.SerialNumber);
                    if (lastMatchedSerial && serial === lastMatchedSerial) {
                        row.classList.add('sn-match-highlight');
                    } else {
                        row.classList.remove('sn-match-highlight');
                    }
                }
            });
        } else {
            itemsTable.clear().rows.add(data);
            itemsTable.order([0, 'asc']);
            itemsTable.draw();
        }
    }

    function showListItems(listData) {
        if (!listData) return;
        currentListData = listData;
        lastMatchedSerial = null;
        if (listTitle) {
            const name = listData.listName ?? '';
            listTitle.textContent = name ? `Danh sách SerialNumber - ${name}` : 'Danh sách SerialNumber';
        }
        const items = Array.isArray(listData.items)
            ? listData.items
            : Array.isArray(listData.serials)
                ? listData.serials
                : Array.isArray(listData.serialNumbers)
                    ? listData.serialNumbers
                    : [];
        renderListItems(items);
        updateScanHint(listData);
        if (scanInput) {
            scanInput.value = '';
        }
        if (detailModal) {
            detailModal.show();
        }
    }

    function normalizeSummaryRecord(record) {
        if (!record || typeof record !== 'object') {
            return {
                serialNumber: '',
                listName: '',
                isFound: false,
                scanTime: ''
            };
        }
        const serialNumber = record.serialNumber ?? record.serial ?? record.serial_number ?? '';
        const listName = record.listName ?? record.list ?? record.list_name ?? '';
        const scanTime = record.scanTime ?? record.scan_time ?? record.createdAt ?? record.updatedAt ?? '';
        const isFoundRaw = record.isFound ?? record.found ?? record.is_found ?? record.status;
        return {
            serialNumber,
            listName,
            isFound: isFoundValue(isFoundRaw),
            scanTime
        };
    }

    function renderSummaryTable(items) {
        const data = Array.isArray(items) ? items : [];
        if (!summaryTable) {
            summaryTable = $('#scanSummaryTable').DataTable({
                data,
                dom: '<"top d-flex align-items-center"fB>rt<"bottom"ip>',
                buttons: [
                    {
                        extend: 'excelHtml5',
                        text: '<img src="/assets/img/excel.png" style="height:32px; margin:5px;">',
                        title: () => (summaryTitleEl?.textContent?.trim() || 'Dữ liệu Scan')
                    }
                ],
                columns: [
                    { data: 'serialNumber', title: 'SerialNumber', defaultContent: '' },
                    { data: 'listName', title: 'List Name', defaultContent: '' },
                    {
                        data: 'isFound',
                        title: 'Đã tìm thấy',
                        render: value => (isFoundValue(value) ? 'Có' : 'Không')
                    },
                    {
                        data: 'scanTime',
                        title: 'Thời gian scan',
                        render: (value, type) => {
                            if (!value) {
                                return '';
                            }
                            const date = new Date(value);
                            if (Number.isNaN(date.getTime())) {
                                return '';
                            }
                            if (type === 'sort' || type === 'type') {
                                return date.getTime();
                            }
                            return date.toLocaleString();
                        }
                    }
                ],
                order: [[3, 'desc']],
                language: {
                    emptyTable: 'Không có dữ liệu'
                },
                rowCallback: (row, rowData) => {
                    if (rowData && isFoundValue(rowData.isFound)) {
                        row.classList.add('table-success');
                    } else {
                        row.classList.remove('table-success');
                    }
                }
            });
        } else {
            summaryTable.clear().rows.add(data);
            summaryTable.order([3, 'desc']);
            summaryTable.draw();
        }
    }

    async function openSummaryModal(filterType) {
        toggleSpinner(true);
        activeSummaryFilter = filterType;
        if (summaryTitleEl) {
            summaryTitleEl.textContent = filterType === 'found'
                ? 'Danh sách Serial Number đã tìm thấy'
                : 'Danh sách Serial Number đã scan';
        }
        try {
            const summary = await ensureSummary();
            const filtered = filterType === 'found'
                ? summary.items.filter(item => item.isFound)
                : summary.items;
            renderSummaryTable(filtered);
        } catch {
            renderSummaryTable([]);
        }
        if (summaryModal) {
            summaryModal.show();
        }
        toggleSpinner(false);
    }

    async function loadSearchLists() {
        toggleSpinner(true);
        try {
            const resp = await fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/scan/search-lists/details');
            if (!resp.ok) {
                renderSearchLists([]);
                return;
            }
            const payload = await resp.json();
            renderSearchLists(payload?.lists ?? []);
        } catch {
            renderSearchLists([]);
        } finally {
            toggleSpinner(false);
        }
    }


    async function updateScannedStatusOnServer(listId, serialNumber, isFound = true) {
        if (!listId && listId !== 0) {
            throw new Error('Không xác định được danh sách cần cập nhật.');
        }
        if (!serialNumber) {
            throw new Error('Serial Number không hợp lệ.');
        }
        const payload = {
            searchListId: listId,
            serialNumber,
            isFound
        };
        const response = await fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Search/UpdateScannedStatus', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        let data;
        try {
            data = await response.json();
        } catch {
            data = null;
        }
        if (!response.ok || (data && data.success === false)) {
            const message = data?.message || `Không thể cập nhật trạng thái (HTTP ${response.status}).`;
            throw new Error(message);
        }
        return data;
    }

    async function handleSerialLookup(serialValue) {
        const trimmed = serialValue?.trim();
        if (!trimmed) {
            return;
        }
        if (!currentListData) {
            const message = 'Vui lòng chọn danh sách Serial Number trước khi quét.';
            if (window.Swal) {
                Swal.fire({ icon: 'warning', title: 'Chưa chọn danh sách', text: message });
            } else {
                alert(message);
            }
            return;
        }
        if (!itemsTable) {
            const message = 'Danh sách Serial Number chưa được tải.';
            if (window.Swal) {
                Swal.fire({ icon: 'warning', title: 'Không có dữ liệu', text: message });
            } else {
                alert(message);
            }
            return;
        }

        const normalized = normalizeSerial(trimmed);
        let serialInfoPromise = fetchSerialInfo(trimmed);
        let matchedRowApi = null;
        itemsTable.rows().every(function () {
            const data = this.data();
            const serial = normalizeSerial(data?.serialNumber ?? data?.SerialNumber);
            if (serial && serial === normalized) {
                matchedRowApi = this;
                return false;
            }
            return undefined;
        });

        if (!matchedRowApi) {
            clearHighlight();
            const serialInfo = await serialInfoPromise.catch(() => null);
            const wipGroup = extractWipGroup(serialInfo);
            const notFoundWithWip = `WIP GROUP: ${wipGroup}`;
            if (window.Swal) {
                Swal.fire({ icon: 'error', title: 'Không tìm thấy', text: notFoundWithWip });
            } else {
                alert(notFoundWithWip);
            }
            return;
        }

        const rowData = { ...matchedRowApi.data() };
        const serialDisplay = rowData.serialNumber ?? rowData.SerialNumber ?? trimmed;
        if (serialDisplay && normalizeSerial(serialDisplay) !== normalized) {
            serialInfoPromise = fetchSerialInfo(serialDisplay);
        }
        const alreadyFound = isFoundValue(rowData.isFound ?? rowData.IsFound);
        const listId = getListId(currentListData);

        try {
            if (!alreadyFound) {
                await updateScannedStatusOnServer(listId, serialDisplay, true);
                rowData.isFound = true;
                if (!rowData.scanTime && !rowData.ScanTime) {
                    rowData.scanTime = new Date().toISOString();
                }
                matchedRowApi.data(rowData).draw(false);
                syncCurrentListItem(rowData);
                if (Array.isArray(currentListData.items)) {
                    currentListData.items = currentListItems;
                } else if (Array.isArray(currentListData.serials)) {
                    currentListData.serials = currentListItems;
                } else if (Array.isArray(currentListData.serialNumbers)) {
                    currentListData.serialNumbers = currentListItems;
                }
                incrementFoundCount(currentListData);
                updateScanHint(currentListData);
                updateCounts(true).catch(() => { /* ignore errors */ });
            } else {
                matchedRowApi.data(rowData).draw(false);
            }

            lastMatchedSerial = normalized;
            itemsTable.rows().invalidate().draw(false);
            if (highlightTimerId) {
                clearTimeout(highlightTimerId);
            }
            highlightTimerId = setTimeout(() => {
                if (lastMatchedSerial === normalized) {
                    clearHighlight();
                }
            }, 5000);

            const serialInfo = await serialInfoPromise.catch(() => null);
            const wipGroup = extractWipGroup(serialInfo);
            const successMessage = alreadyFound
                ? `Serial Number ${serialDisplay} đã được tìm thấy trước đó.`
                : `Đã tìm thấy Serial Number ${serialDisplay} trong danh sách.`;
            const messageWithWip = wipGroup
                ? `${successMessage}\nWIP Group: ${wipGroup}`
                : successMessage;
            if (window.Swal) {
                Swal.fire({
                    icon: 'success',
                    title: 'Tìm thấy',
                    text: messageWithWip,
                    timer: 2000,
                    showConfirmButton: false
                });
            } else {
                alert(messageWithWip);
            }
        } catch (error) {
            clearHighlight();
            const errorMessage = error?.message || 'Không thể cập nhật trạng thái quét.';
            if (window.Swal) {
                Swal.fire({ icon: 'error', title: 'Lỗi', text: errorMessage });
            } else {
                alert(errorMessage);
            }
        }
    }

    async function processScanField() {
        if (!scanInput || isProcessingScan) {
            return;
        }
        isProcessingScan = true;
        try {
            const value = scanInput.value?.trim();
            if (!value) {
                clearHighlight();
                return;
            }
            await handleSerialLookup(value);
        } finally {
            requestAnimationFrame(() => {
                if (scanInput) {
                    scanInput.focus();
                    scanInput.select();
                }
            });
            isProcessingScan = false;
        }
    }


    async function searchSerials() {
        const lines = textarea.value
            .split(/\r?\n/)
            .map(l => l.trim())
            .filter(Boolean);

        try {
            const resp = await fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/scan/SearchBySN', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ serialNumber: lines })
            });

            if (resp.ok) {
                const data = await resp.json();
                renderTable(Array.isArray(data) ? data : []);
            } else if (resp.status === 404) {
                // API trả về 404
                alert("⚠️ Không tìm thấy dữ liệu Serial Number!");
                renderTable([]);
            } else {
                alert("⚠️ Lỗi server (" + resp.status + ")");
                renderTable([]);
            }
        } catch (err) {
            console.error("Fetch error:", err);
            alert("⚠️ Không thể kết nối tới API!");
            renderTable([]);
        }
    }


    searchBtn?.addEventListener('click', e => {
        e.preventDefault();
        searchSerials();
    });

    scanInput?.addEventListener('keydown', event => {
        if (event.key === 'Enter') {
            event.preventDefault();
            processScanField();
        }
    });

    scanInput?.addEventListener('focus', () => {
        requestAnimationFrame(() => {
            scanInput.select();
        });
    });

    scanInput?.addEventListener('input', () => {
        if (highlightTimerId) {
            clearTimeout(highlightTimerId);
            highlightTimerId = null;
        }
        clearHighlight();
    });

    applyBtn?.addEventListener('click', e => {
        e.preventDefault();
        updateCounts(true);
    });

    totalScanBtn?.addEventListener('click', e => {
        e.preventDefault();
        openSummaryModal('total');
    });

    foundScanBtn?.addEventListener('click', e => {
        e.preventDefault();
        openSummaryModal('found');
    });

    detailModalEl?.addEventListener('shown.bs.modal', () => {
        if (itemsTable) {
            itemsTable.columns.adjust();
        }
        if (scanInput) {
            requestAnimationFrame(() => {
                scanInput.focus();
                scanInput.select();
            });
        }
    });

    detailModalEl?.addEventListener('hidden.bs.modal', () => {
        currentListData = null;
        clearHighlight();
        if (scanInput) {
            scanInput.value = '';
        }
        updateScanHint(null);
    });

    summaryModalEl?.addEventListener('shown.bs.modal', () => {
        if (summaryTable) {
            summaryTable.columns.adjust();
        }
    });

    updateScanHint(null);
    setDefaultRange();
    updateCounts(true);
    loadSearchLists();
});
