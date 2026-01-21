import { state } from './state.js';
import { utils } from './utils.js?v=2';
import { columns } from './columns.js';
import { services } from './services.js';

export const tables = {
    async updateModalSNTable(data) {
        try {
            const tableElement = document.getElementById('modal-sn-table');
            const tableBody = tableElement?.querySelector('tbody');
            const tableHead = tableElement?.querySelector('thead');
            if (!tableBody) {
                console.error('Không tìm thấy tbody của bảng modal!');
                return false;
            }
            utils.destroyDataTable('#modal-sn-table');
            tableBody.innerHTML = '';
            const columnCount = tableHead?.querySelectorAll('th').length || columns.modalSN.length || 1;
            if (!Array.isArray(data) || data.length === 0) {
                tableBody.innerHTML = `<tr><td colspan='${columnCount}'>Không có dữ liệu</td></tr>`;
                state.allModalData = [];
                console.warn('Dữ liệu rỗng hoặc không phải mảng:', data);
                return false;
            }
            const normalizedData = data.map(item => {
                const resolvedStatus = utils.resolveInventoryStatusValue(item);
                const agingInfo = utils.calculateStatusAging(utils.getValueIgnoreCase(item, 'DATE3'));
                const statusFA = utils.getValueIgnoreCase(item, 'STATUS_FA');
                return {
                    ...item,
                    STATUS_FA: statusFA || resolvedStatus,
                    STATUS_DATA11: resolvedStatus,
                    INVENTORY_STATUS: resolvedStatus,
                    STATUS_AGING_HOURS: agingInfo.hours,
                    STATUS_AGING_DISPLAY: agingInfo.display
                };
            });
            state.allModalData = [...normalizedData];
            const fragment = document.createDocumentFragment();
            normalizedData.forEach(item => {
                const row = document.createElement('tr');
                columns.modalSN.forEach(column => {
                    const value = column.getter(item) || '';
                    const title = column.getTitle ? column.getTitle(item) : undefined;
                    utils.createCell(row, value, column.maxLength, title);
                });
                fragment.appendChild(row);
            });
            tableBody.appendChild(fragment);
            const dataTable = utils.initializeDataTable('#modal-sn-table', {
                dom: 'Bfrtip',
                pageLength: 10,
                buttons: [
                    {
                        extend: 'excelHtml5',
                        text: '📥 Xuất Excel',
                        title: 'SerialNumbers',
                        exportOptions: { columns: ':visible' }
                    }
                ]
            });

            // === CLICK ROW TO HIGHLIGHT ===
            $('#modal-sn-table tbody').on('click', 'tr', function () {
                // Bỏ highlight cũ
                $('#modal-sn-table tbody tr').removeClass('selected-row');

                // Nếu click chính row đó → toggle chọn
                $(this).toggleClass('selected-row');
            });



            if (dataTable) {
                dataTable
                    .on('draw.dt', () => console.log('DataTable drawn successfully'))
                    .on('error.dt', (e, settings, techNote, message) => console.error('DataTable error:', message));
            }
            return true;
        } catch (error) {
            console.error('Lỗi khi cập nhật DataTable:', error);
            return false;
        }
    },
    renderNoLocationTable(items = []) {
        try {
            const section = document.getElementById('cioNoLocationSection');
            const table = document.getElementById('cio-no-location-table');
            const exportBtn = document.getElementById('exportNoLocationExcelBtn');
            if (!section || !table) {
                return;
            }
            const tbody = table.querySelector('tbody');
            if (!tbody) {
                return;
            }
            utils.destroyDataTable('#cio-no-location-table');
            state.cioNoLocationTableInstance = null;
            if (!Array.isArray(items) || items.length === 0) {
                section.style.display = 'none';
                tbody.innerHTML = '';
                state.cioNoLocationData = [];
                if (exportBtn) {
                    exportBtn.disabled = true;
                }
                return;
            }
            section.style.display = '';
            tbody.innerHTML = '';
            state.cioNoLocationData = items;
            if (exportBtn) {
                exportBtn.disabled = false;
            }
            const columnConfig = [
                { getter: item => utils.getTextValue(item, 'TYPE') },
                { getter: item => utils.getTextValue(item, ['SFG', 'SERIAL_NUMBER']) },
                { getter: item => utils.getTextValue(item, 'MODEL_NAME') },
                { getter: item => utils.getTextValue(item, 'PRODUCT_LINE') },
                {
                    getter: item => item.location || utils.getTextValue(item, ['DATA18', 'LOCATION']),
                    onCreate: (cell) => { cell.dataset.role = 'location'; }
                },
                { getter: item => utils.getTextValue(item, 'AGING_HOURS') },
                { getter: item => utils.getTextValue(item, 'WIP_GROUP') },
                { getter: item => utils.getTextValue(item, 'ERROR_FLAG') },
                { getter: item => utils.getTextValue(item, 'WORK_FLAG') }
            ];
            const fragment = document.createDocumentFragment();
            items.forEach((item) => {
                const row = document.createElement('tr');
                columnConfig.forEach(column => {
                    const cell = utils.createCell(row, column.getter(item), 30);
                    if (typeof column.onCreate === 'function') {
                        column.onCreate(cell, item);
                    }
                });
                fragment.appendChild(row);
            });
            tbody.appendChild(fragment);
            state.cioNoLocationTableInstance = utils.initializeDataTable('#cio-no-location-table', { ordering: true });
            // === CLICK ROW TO HIGHLIGHT ===
            $('#cio-no-location-table tbody').on('click', 'tr', function () {
                // Bỏ highlight cũ
                $('#cio-no-location-table tbody tr').removeClass('selected-row');

                // Nếu click chính row đó → toggle chọn
                $(this).toggleClass('selected-row');
            });



        } catch (error) {
            console.error('renderNoLocationTable error:', error);
        }
    },
    async updateNoLocationInventory(items = []) {
        try {
            if (!Array.isArray(items) || items.length === 0) {
                state.cioNoLocationData = [];
                tables.renderNoLocationTable([]);
                return;
            }
            const { items: enrichedItems } = await services.attachLocationInfo(items);
            const noLocationItems = enrichedItems.filter(item => !item.location);
            state.cioNoLocationData = noLocationItems;
            tables.renderNoLocationTable(noLocationItems);
        } catch (error) {
            console.error('updateNoLocationInventory error:', error);
            state.cioNoLocationData = [];
            tables.renderNoLocationTable([]);
        }
    }
};
