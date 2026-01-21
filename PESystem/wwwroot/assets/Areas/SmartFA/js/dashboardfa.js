import { state } from './dashboardfa/state.js';
import { utils } from './dashboardfa/utils.js?v=2';
import { tables } from './dashboardfa/tables.js';
import { charts } from './dashboardfa/charts.js';
import { handlers } from './dashboardfa/handlers.js';
import { exporter } from './dashboardfa/exporter.js';
import { modals } from './dashboardfa/modals.js';

function registerEventHandlers() {
    const exportExcelBtn = document.getElementById('exportExcelBtn');
    if (exportExcelBtn) {
        exportExcelBtn.addEventListener('click', () => exporter.exportModalData());
    }
    const exportNoLocationExcelBtn = document.getElementById('exportNoLocationExcelBtn');
    if (exportNoLocationExcelBtn) {
        exportNoLocationExcelBtn.addEventListener('click', () => exporter.exportNoLocationData());
    }
    const exportCioExcelBtn = document.getElementById('exportCioExcelBtn');
    if (exportCioExcelBtn) {
        exportCioExcelBtn.addEventListener('click', () => exporter.exportCioData());
    }
    const previousDayNoLocationBtn = document.getElementById('previousDayNoLocationBtn');
    if (previousDayNoLocationBtn) {
        previousDayNoLocationBtn.addEventListener('click', () => {
            if (!state.previousDayNoLocationRecords.length) {
                return;
            }
            modals.showCioModal(
                state.previousDayNoLocationRecords,
                'Danh sách SN chưa có vị trí - Báo cáo ngày hôm trước',
                { type: 'reportNoLocation' }
            );
        });
    }
    const startInput = document.getElementById('cioStartDate');
    const endInput = document.getElementById('cioEndDate');
    const pickBtn = document.getElementById('pickDateBtn');
    if (pickBtn && startInput && endInput) {
        pickBtn.addEventListener('click', async () => {
            const { value: formValues } = await Swal.fire({
                title: 'Chọn khoảng thời gian',
                html: `
                  <label>Từ:</label>
                  <input id="swalStart" type="datetime-local" class="swal2-input" value="${startInput.value}"></br>
                  <label>Đến:</label>
                  <input id="swalEnd" type="datetime-local" class="swal2-input" value="${endInput.value}">
                `,
                focusConfirm: false,
                showCancelButton: true,
                confirmButtonText: 'OK',
                preConfirm: () => [
                    document.getElementById('swalStart').value,
                    document.getElementById('swalEnd').value
                ]
            });
            if (formValues) {
                startInput.value = formValues[0];
                endInput.value = formValues[1];
                handlers.loadCheckInOutChart();
            }
        });
    }
}

function initializeDateRange() {
    const startInput = document.getElementById('cioStartDate');
    const endInput = document.getElementById('cioEndDate');
    if (!startInput || !endInput) {
        return;
    }
    const now = new Date();
    const yesterday730 = new Date(now);
    yesterday730.setHours(7, 30, 0, 0);
    startInput.value = utils.formatDateTime(yesterday730);
    endInput.value = utils.formatDateTime(now);
}

function initializeDashboard() {
    modals.ensureInstances();
    registerEventHandlers();
    handlers.loadStatusChart().catch(error => console.error('Error loading status chart:', error));
    handlers.loadNoLocationTrendChart().catch(error => console.error('Error loading no-location trend chart:', error));
    handlers.loadPreviousDayReport().catch(error => console.error('Error loading previous day report:', error));
    initializeDateRange();
    handlers.loadCheckInOutChart().catch(error => console.error('Error loading CheckIn/Out chart:', error));
}

function resizeAllCharts() {
    const chartKeys = [
        'sapChartInstance',
        'inventoryStatusChartInstance',
        'statusChartInstance',
        'checkInOutChartInstance',
        'checkInBeforeAfterChartInstance',
        'noLocationTrendChartInstance'
    ];
    chartKeys.forEach(key => {
        const instance = state[key];
        if (instance && typeof instance.resize === 'function') {
            instance.resize();
        }
    });
}

document.addEventListener('DOMContentLoaded', initializeDashboard);
window.addEventListener('resize', resizeAllCharts);

window.SmartFADashboard = {
    state,
    utils,
    tables,
    charts,
    handlers,
    exporter,
    showCioModal: modals.showCioModal
};
