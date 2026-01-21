(function () {
    'use strict';

    //const API_BASE_URL = window.SmartFAApiBaseUrl || window.location.origin;
    const API_BASE_URL = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/'
    const ENDPOINTS = {
        locations: 'api/DataChart/getCountLocation',
        receivingStatus: 'api/RepairStatus/receiving-status'
    };
    const REFRESH_INTERVAL = 5 * 60 * 1000; // 5 phút
    const SHIFT_ENDS = [
        { label: 'ca 1', hour: 18, minute: 30 },
        { label: 'ca 2', hour: 6, minute: 30 }
    ];

    const cellDataMap = new Map();

    let cellModalInstance = null;
    let reminderModalInstance = null;
    let reminderTimeoutId = null;
    let lastReminderKey = '';

    document.addEventListener('DOMContentLoaded', () => {
        initialiseModals();
        attachCellEvents();
        attachFormHandler();
        fetchAndRenderCells();
        scheduleReminder();
        setInterval(fetchAndRenderCells, REFRESH_INTERVAL);
    });

    function initialiseModals() {
        const cellModalElement = document.getElementById('cellDetailModal');
        if (cellModalElement) {
            cellModalInstance = new bootstrap.Modal(cellModalElement);
        }
        const reminderElement = document.getElementById('reminderModal');
        if (reminderElement) {
            reminderModalInstance = new bootstrap.Modal(reminderElement);
        }
    }

    function attachCellEvents() {
        const cellElements = document.querySelectorAll('.cell[data-cell]');
        cellElements.forEach(cell => {
            cell.addEventListener('click', () => openCellModal(cell));
            cell.addEventListener('keypress', event => {
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    openCellModal(cell);
                }
            });
            cell.setAttribute('tabindex', '0');
            cell.setAttribute('aria-label', `Cell ${cell.dataset.cell}`);
        });
    }

    function attachFormHandler() {
        const form = document.getElementById('updateLocationForm');
        if (!form) {
            return;
        }
        form.addEventListener('submit', async event => {
            event.preventDefault();
            await handleUpdateSubmit();
        });
    }

    async function fetchAndRenderCells() {
        try {
            hideDiagramAlert();
            const response = await fetch(buildUrl(ENDPOINTS.locations));
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }
            const payload = await response.json();
            const locations = Array.isArray(payload)
                ? payload
                : payload?.locations || payload?.locations || [];

            cellDataMap.clear();
            locations.forEach(item => {
                if (!item || !item.location) {
                    return;
                }
                const key = (item.location || '').trim().toUpperCase();
                const details = item.Details || item.details || [];
                const total = Number(item.TotalCount ?? item.totalCount ?? details.length ?? 0);
                const lastUpdatedAtRaw = item.LastUpdatedAt ?? item.lastUpdatedAt ?? null;
                const parsedLastUpdated = parseDateValue(lastUpdatedAtRaw);
                const isUpdated = Boolean(item.IsUpdated ?? item.isUpdated ?? parsedLastUpdated);

                cellDataMap.set(key, {
                    total,
                    details: Array.isArray(details) ? details : [],
                    isUpdated,
                    lastUpdatedAt: parsedLastUpdated
                });
            });

            renderCellCounts();
        } catch (error) {
            console.error('Không thể tải dữ liệu vị trí:', error);
            showDiagramAlert('Không thể tải dữ liệu vị trí. Vui lòng thử lại sau.');
        }
    }

    function renderCellCounts() {
        const cellElements = document.querySelectorAll('.cell[data-cell]');
        cellElements.forEach(cell => {
            const cellName = (cell.dataset.cell || '').toUpperCase();
            const info = cellDataMap.get(cellName);
            const countElement = cell.querySelector('.cell-count');
            if (countElement) {
                countElement.textContent = info ? info.total : 0;
            }
            if (info?.isUpdated) {
                cell.classList.add('cell-updated');
            } else {
                cell.classList.remove('cell-updated');
            }
        });
    }

    function upsertCellMetadata(cellName, overrides) {
        if (!cellName) {
            return;
        }

        const normalized = cellName.toUpperCase();
        const current = cellDataMap.get(normalized) || {
            total: 0,
            details: [],
            isUpdated: false,
            lastUpdatedAt: null
        };

        cellDataMap.set(normalized, {
            ...current,
            ...overrides
        });
    }

    function openCellModal(cell) {
        const cellName = (cell.dataset.cell || '').toUpperCase();
        const info = cellDataMap.get(cellName) || { total: 0, details: [], isUpdated: false, lastUpdatedAt: null };
        const modalCellName = document.getElementById('modalCellName');
        const cellNameInput = document.getElementById('cellNameInput');
        const serialListInput = document.getElementById('serialListInput');
        const lastUpdatedInfo = document.getElementById('lastUpdatedInfo');

        if (modalCellName) {
            modalCellName.textContent = cellName;
        }
        if (cellNameInput) {
            cellNameInput.value = cellName;
        }
        if (serialListInput) {
            serialListInput.value = ''; // Luôn để trống để user nhập
        }
        if (lastUpdatedInfo) {
            lastUpdatedInfo.textContent = info?.lastUpdatedAt
                ? `Đã cập nhật lúc ${formatDateTime(info.lastUpdatedAt)}`
                : '';
        }

        populateDetailTable(info.details);
        hideModalAlert();
        if (cellModalInstance) {
            cellModalInstance.show();
        }
    }

    function populateDetailTable(details) {
        const tbody = document.getElementById('cellDetailTableBody');
        if (!tbody) {
            return;
        }

        tbody.innerHTML = '';
        if (!Array.isArray(details) || details.length === 0) {
            tbody.innerHTML = `<tr><td colspan="6" class="text-center text-muted">Không có SerialNumber trong cell này.</td></tr>`;
            return;
        }

        details.forEach(detail => {
            const row = document.createElement('tr');
            const agingValue = getValue(detail, ['Aging', 'aging', 'AgingDays']);
            const agingText = typeof agingValue === 'number'
                ? agingValue.toFixed(2)
                : (agingValue || '');

            row.innerHTML = `
                <td>${escapeHtml(getValue(detail, ['SerialNumber', 'serialNumber', 'SERIAL_NUMBER']))}</td>
                <td>${escapeHtml(getValue(detail, ['ModelName', 'modelName', 'MODEL_NAME']))}</td>
                <td>${escapeHtml(getValue(detail, ['MONumber', 'moNumber', 'MO_NUMBER']))}</td>
                <td>${escapeHtml(getValue(detail, ['TestCode', 'testCode', 'TEST_CODE']))}</td>
                <td>${escapeHtml(getValue(detail, ['ErrorDesc', 'errorDesc', 'ERROR_DESC']))}</td>
                <td>${escapeHtml(agingText)}</td>`;
            tbody.appendChild(row);
        });
    }

    async function handleUpdateSubmit() {
        const cellName = (document.getElementById('cellNameInput')?.value || '').trim().toUpperCase();
        const serialTextarea = document.getElementById('serialListInput');
        const submitButton = document.getElementById('submitUpdateButton');
        const lastUpdatedInfo = document.getElementById('lastUpdatedInfo');

        if (!cellName) {
            showModalAlert('Không xác định được cell cần cập nhật.');
            return;
        }

        const newSerials = parseSerialList(serialTextarea?.value || '');
        if (newSerials.length === 0) {
            showModalAlert('Vui lòng nhập ít nhất một SerialNumber.');
            return;
        }

        // ✅ Danh sách SN hiện có trong cell (từ API load trước đó)
        const currentInfo = cellDataMap.get(cellName) || { details: [] };
        const currentSerials = currentInfo.details
            .map(detail => getValue(detail, ['SerialNumber', 'serialNumber', 'SERIAL_NUMBER']))
            .filter(Boolean)
            .map(sn => sn.toUpperCase());

        // ✅ Những SN bị bỏ ra (có trong cellDataMap nhưng không còn trong textarea)
        const removedSerials = currentSerials.filter(sn => !newSerials.includes(sn));

        try {
            if (submitButton) {
                submitButton.disabled = true;
                submitButton.dataset.originalText = submitButton.textContent;
                submitButton.textContent = 'Đang cập nhật...';
            }
            hideModalAlert();

            // --- 1. Update các SN nhập mới ---
            if (newSerials.length > 0) {
                const payloadAdd = {
                    serialnumbers: newSerials.join(','),
                    location: cellName,
                    tag: `Update location ${cellName}`
                };
                await callUpdateApi(payloadAdd);
            }

            // --- 2. Clear location cho các SN bị bỏ ra ---
            if (removedSerials.length > 0) {
                const payloadRemove = {
                    serialnumbers: removedSerials.join(','),
                    location: '', // ✅ clear vị trí
                    tag: `Clear location from ${cellName}`
                };
                await callUpdateApi(payloadRemove);
            }

            // ✅ Sau khi update thì refresh lại
            const now = new Date();
            upsertCellMetadata(cellName, { isUpdated: true, lastUpdatedAt: now });
            if (lastUpdatedInfo) {
                lastUpdatedInfo.textContent = `Đã cập nhật lúc ${formatDateTime(now)}`;
            }
            showModalAlert('Cập nhật vị trí thành công.', 'success');
            renderCellCounts();
            await fetchAndRenderCells();

        } catch (error) {
            console.error('Cập nhật vị trí thất bại:', error);
            showModalAlert(error.message || 'Không thể cập nhật vị trí.');
        } finally {
            if (submitButton) {
                submitButton.disabled = false;
                submitButton.textContent = submitButton.dataset.originalText || 'Cập nhật vị trí';
            }
        }
    }

    async function callUpdateApi(payload) {
        const response = await fetch(buildUrl(ENDPOINTS.receivingStatus), {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        const text = await response.text();
        let jsonResponse = null;
        try {
            jsonResponse = text ? JSON.parse(text) : null;
        } catch { }

        if (!response.ok || (jsonResponse && jsonResponse.success === false)) {
            const message = jsonResponse?.message || text || 'Không thể cập nhật vị trí.';
            throw new Error(message);
        }
    }



    function scheduleReminder() {
        if (reminderTimeoutId) {
            clearTimeout(reminderTimeoutId);
        }
        const now = new Date();
        const nextReminder = getNextReminder(now);
        if (!nextReminder) {
            return;
        }

        const { reminderTime, shiftLabel, reminderKey, showImmediately } = nextReminder;
        if (showImmediately) {
            showReminder(shiftLabel, reminderKey);
            reminderTimeoutId = setTimeout(scheduleReminder, 60 * 1000);
            return;
        }

        const delay = reminderTime.getTime() - now.getTime();
        reminderTimeoutId = setTimeout(() => {
            showReminder(shiftLabel, reminderKey);
            scheduleReminder();
        }, Math.max(delay, 0));
    }

    function getNextReminder(now) {
        let selected = null;
        SHIFT_ENDS.forEach(shift => {
            const end = new Date(now);
            end.setHours(shift.hour, shift.minute, 0, 0);
            if (end <= now) {
                end.setDate(end.getDate() + 1);
            }
            const reminderTime = new Date(end.getTime() - 30 * 60 * 1000);
            const showImmediately = now >= reminderTime && now < end;
            const reminderKey = `${shift.label}-${reminderTime.getFullYear()}-${reminderTime.getMonth()}-${reminderTime.getDate()}`;
            const candidate = {
                shiftLabel: shift.label,
                reminderTime,
                reminderKey,
                showImmediately
            };
            if (!selected || reminderTime < selected.reminderTime) {
                selected = candidate;
            }
        });
        return selected;
    }

    function showReminder(shiftLabel, reminderKey) {
        if (!reminderModalInstance || lastReminderKey === reminderKey) {
            return;
        }
        lastReminderKey = reminderKey;
        const reminderMessage = document.getElementById('reminderMessage');
        if (reminderMessage) {
            reminderMessage.textContent = `Sắp hết ${shiftLabel}. Vui lòng nhập lại danh sách SerialNumber để cập nhật vị trí trong từng cell.`;
        }
        reminderModalInstance.show();
    }

    function showDiagramAlert(message) {
        const alert = document.getElementById('diagramAlert');
        if (!alert) {
            return;
        }
        alert.textContent = message;
        alert.classList.remove('d-none');
    }

    function hideDiagramAlert() {
        const alert = document.getElementById('diagramAlert');
        if (!alert) {
            return;
        }
        alert.textContent = '';
        alert.classList.add('d-none');
    }

    function showModalAlert(message, type = 'danger') {
        const alert = document.getElementById('cellModalAlert');
        if (!alert) {
            return;
        }
        alert.textContent = message;
        alert.classList.remove('d-none', 'alert-danger', 'alert-success');
        alert.classList.add(`alert-${type}`);
    }

    function hideModalAlert() {
        const alert = document.getElementById('cellModalAlert');
        if (!alert) {
            return;
        }
        alert.textContent = '';
        alert.classList.add('d-none');
        alert.classList.remove('alert-success', 'alert-danger');
    }

    function parseSerialList(value) {
        return value
            .split(/\r?\n|,/)
            .map(serial => serial.trim().toUpperCase())
            .filter(serial => serial.length > 0);
    }

    function parseDateValue(value) {
        if (!value) {
            return null;
        }

        const parsed = value instanceof Date ? value : new Date(value);
        return Number.isNaN(parsed.getTime()) ? null : parsed;
    }

    function getValue(obj, keys) {
        if (!obj) {
            return '';
        }
        for (const key of keys) {
            if (Object.prototype.hasOwnProperty.call(obj, key) && obj[key] !== null && obj[key] !== undefined) {
                return obj[key];
            }
        }
        return '';
    }

    function escapeHtml(value) {
        if (value === null || value === undefined) {
            return '';
        }
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function buildUrl(path) {
        if (!path) {
            return '';
        }
        if (/^https?:/i.test(path)) {
            return path;
        }
        return `${API_BASE_URL.replace(/\/$/, '')}${path.startsWith('/') ? path : `/${path}`}`;
    }

    function formatDateTime(date) {
        if (!(date instanceof Date) || Number.isNaN(date.getTime())) {
            return '';
        }

        const pad = (num) => num.toString().padStart(2, '0');
        return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
    }
})();
