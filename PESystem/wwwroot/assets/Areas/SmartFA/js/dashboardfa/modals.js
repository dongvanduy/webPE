import { state } from './state.js';
import { utils } from './utils.js?v=2';
import { columns } from './columns.js';
import { feedback } from './feedback.js';

const bootstrapModal = () => (typeof bootstrap !== 'undefined' ? bootstrap : null);

export const modals = {
    ensureInstances() {
        const bootstrapLib = bootstrapModal();
        if (!bootstrapLib) {
            console.warn('Bootstrap modal library is not available.');
            return;
        }
        if (!state.statusModalInstance) {
            state.statusModalElement = document.getElementById('statusModal');
            if (state.statusModalElement) {
                state.statusModalInstance = new bootstrapLib.Modal(state.statusModalElement);
            }
        }
        if (!state.cioModalInstance) {
            state.cioModalElement = document.getElementById('cioModal');
            if (state.cioModalElement) {
                state.cioModalInstance = new bootstrapLib.Modal(state.cioModalElement);
            }
        }
    },
    async showCioModal(data, title, options = {}) {
        try {
            const tableElement = document.getElementById('cio-modal-table');
            if (!tableElement) return console.error('Không tìm thấy bảng CheckInOut!');
            const tableHead = tableElement.querySelector('thead');
            const tableBody = tableElement.querySelector('tbody');
            if (!tableHead || !tableBody) return console.error('Không tìm thấy cấu trúc bảng CheckInOut!');
            utils.destroyDataTable('#cio-modal-table');
            tableHead.innerHTML = '';
            tableBody.innerHTML = '';
            const type = options.type === 'sap' ? 'sap' : 'default';
            const columnConfig = columns.cioModal[type];
            const headerRow = document.createElement('tr');
            columnConfig.forEach(column => {
                const th = document.createElement('th');
                th.textContent = column.header;
                headerRow.appendChild(th);
            });
            tableHead.appendChild(headerRow);
            const dataSource = Array.isArray(data) ? data : [];
            if (!dataSource.length) {
                const emptyRow = document.createElement('tr');
                const emptyCell = document.createElement('td');
                emptyCell.colSpan = columnConfig.length;
                emptyCell.className = 'text-center';
                emptyCell.textContent = 'Không có dữ liệu';
                emptyRow.appendChild(emptyCell);
                tableBody.appendChild(emptyRow);
            } else {
                const fragment = document.createDocumentFragment();
                dataSource.forEach(item => {
                    const row = document.createElement('tr');
                    columnConfig.forEach(column => {
                        utils.createCell(row, column.getter(item) || '', 30);
                    });
                    fragment.appendChild(row);
                });
                tableBody.appendChild(fragment);
            }
            const headerTitle = document.getElementById('cioModalLabel');
            if (headerTitle) {
                headerTitle.textContent = title || 'Chi tiết Check In/Out';
            }
            state.cioModalData = [...dataSource];
            state.cioModalExportType = type;
            state.cioModalInstance?.show();
            utils.initializeDataTable('#cio-modal-table', { ordering: true, pageLength: 10 });
            // === CLICK ROW TO HIGHLIGHT ===
            $('#cio-modal-table tbody').on('click', 'tr', function () {
                // Bỏ highlight cũ
                $('#cio-modal-table tbody tr').removeClass('selected-row');

                // Nếu click chính row đó → toggle chọn
                $(this).toggleClass('selected-row');
            });

        } catch (error) {
            console.error('showCioModal error:', error);
            feedback.showError('Không thể hiển thị dữ liệu chi tiết!');
        }
    }
};
