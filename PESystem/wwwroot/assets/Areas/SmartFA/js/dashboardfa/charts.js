import { config, state } from './state.js';
import { utils } from './utils.js?v=2';
import { services } from './services.js';
import { chartHelpers } from './chart-helpers.js';

export const charts = {
    aggregateSapRecordsByDate(records = []) {
        const pad = (n) => n.toString().padStart(2, '0');
        const map = new Map();
        (records || []).forEach(item => {
            const groupName = (utils.getValueIgnoreCase(item, 'GROUP_NAME') || '').toString().toUpperCase();
            if (!['B28M', 'B30M', 'B36R'].includes(groupName)) {
                return;
            }
            const date = utils.safeParseDate(utils.getValueIgnoreCase(item, 'IN_STATION_TIME'));
            if (!date) {
                return;
            }
            const key = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
            if (!map.has(key)) {
                map.set(key, { date, B28M: 0, B30M: 0, B36R: 0 });
            }
            const entry = map.get(key);
            entry[groupName] = (entry[groupName] || 0) + 1;
        });
        return Array.from(map.values()).sort((a, b) => a.date - b.date);
    },
    renderSapInOutChart(records = []) {
        const chartElement = document.getElementById('sapInOutChart');
        if (!chartElement) {
            return;
        }
        const aggregated = charts.aggregateSapRecordsByDate(records);
        if (!aggregated.length) {
            chartHelpers.dispose('sapChartInstance');
            chartHelpers.showEmptyState('sapInOutChart', 'Không có dữ liệu SAP In/Out');
            return;
        }
        const categories = aggregated.map(entry => utils.formatDateTimeDisplay(entry.date));
        const series = [
            { name: 'B28M', data: aggregated.map(entry => entry.B28M || 0) },
            { name: 'B30M', data: aggregated.map(entry => entry.B30M || 0) },
            { name: 'B36R', data: aggregated.map(entry => entry.B36R || 0) }
        ];
        const option = {
            color: ['#26C6DA', '#66BB6A', '#9575CD'],
            tooltip: {
                trigger: 'axis',
                axisPointer: { type: 'shadow' }
            },
            legend: {
                textStyle: { color: '#e0f7fa' }
            },
            grid: { left: '3%', right: '4%', bottom: '3%', containLabel: true },
            xAxis: {
                type: 'category',
                data: categories,
                axisLabel: { color: '#b2ebf2', interval: 0, rotate: 15 },
                axisLine: { lineStyle: { color: '#00d9ff' } }
            },
            yAxis: {
                type: 'value',
                name: 'Số lượng',
                axisLabel: { color: '#b2ebf2' },
                splitLine: { lineStyle: { color: 'rgba(255,255,255,0.1)' } },
                axisLine: { lineStyle: { color: '#00d9ff' } }
            },
            series: series.map(item => ({
                name: item.name,
                type: 'bar',
                barMaxWidth: 32,
                data: item.data
            }))
        };
        chartHelpers.render('sapChartInstance', 'sapInOutChart', option);
    },
    resetInventoryStatusSummary(total = 0, message = '') {
        const summaryElement = document.getElementById('inventoryStatusSummary');
        if (summaryElement) {
            summaryElement.textContent = total ? `Tổng: ${total}` : message || 'Tổng: 0';
        }
    },
    async renderInventoryStatusChart(records = [], options = {}) {
        const { onPointClick } = options;
        const chartElement = document.getElementById('inventoryStatusChart');
        if (!chartElement) {
            return;
        }
        chartHelpers.dispose('inventoryStatusChartInstance');
        state.inventoryStatusDetailsMap = {};
        if (!Array.isArray(records) || records.length === 0) {
            chartHelpers.showEmptyState('inventoryStatusChart', 'Không có dữ liệu tồn kho');
            charts.resetInventoryStatusSummary(0);
            return;
        }
        const tonKhoSerialMap = new Map();
        (records || []).forEach(item => {
            const rawSerial = (utils.getValueIgnoreCase(item, 'FG') 
                || utils.getValueIgnoreCase(item, 'SFG')
                || utils.getValueIgnoreCase(item, 'SERIAL_NUMBER')
                || '')
                .toString()
                .trim();
            if (!rawSerial) {
                return;
            }
            const key = rawSerial.toUpperCase();
            if (!tonKhoSerialMap.has(key)) {
                tonKhoSerialMap.set(key, {
                    serial: rawSerial,
                    record: { ...item }
                });
            }
        });
        const fgSerials = [...tonKhoSerialMap.values()].map(entry => entry.serial).filter(Boolean);
        if (!fgSerials.length) {
            chartHelpers.showEmptyState('inventoryStatusChart', 'Không tìm thấy Serial Number hợp lệ');
            charts.resetInventoryStatusSummary(0);
            return;
        }
        let repairTasks = [];
        try {
            repairTasks = await services.fetchRepairTasksBySerials(fgSerials);
        } catch (error) {
            chartElement.innerHTML = "<div class='text-center text-danger py-5'>Không thể tải dữ liệu trạng thái tồn kho</div>";
            charts.resetInventoryStatusSummary(0, '');
            return;
        }
        const statusMap = {};
        config.INVENTORY_STATUS_KEYS.forEach(status => { statusMap[status] = []; });
        const inventorySerialKeys = new Set(tonKhoSerialMap.keys());
        const latestRepairTaskBySerial = new Map();
        (repairTasks || []).forEach(item => {
            const rawSerial = (utils.getValueIgnoreCase(item, 'FG')
                || utils.getValueIgnoreCase(item, 'SFG')
                || utils.getValueIgnoreCase(item, 'SERIAL_NUMBER')
                || '')
                .toString()
                .trim();
            if (!rawSerial) {
                return;
            }
            const key = rawSerial.toUpperCase();
            if (!inventorySerialKeys.has(key)) {
                return;
            }
            const candidateDate = utils.safeParseDate(utils.getValueIgnoreCase(item, 'DATE3'));
            if (!latestRepairTaskBySerial.has(key)) {
                latestRepairTaskBySerial.set(key, { item, candidateDate });
                return;
            }
            const current = latestRepairTaskBySerial.get(key);
            if (!current.candidateDate && candidateDate) {
                latestRepairTaskBySerial.set(key, { item, candidateDate });
                return;
            }
            if (candidateDate && current.candidateDate && candidateDate >= current.candidateDate) {
                latestRepairTaskBySerial.set(key, { item, candidateDate });
            }
        });
        tonKhoSerialMap.forEach((entry, key) => {
            const baseRecord = entry?.record || {};
            const repairEntry = latestRepairTaskBySerial.get(key);
            const repairItem = repairEntry?.item;
            const combined = { ...baseRecord, ...(repairItem || {}) };
            if (combined.sfg) {
                combined.sfg = entry.serial;
            }
            else if (combined.fg) {
                combined.fg = entry.serial;
            }
            else if (combined.seriaL_NUMBER) {
                combined.seriaL_NUMBER = entry.serial;
            }
            const resolvedStatus = utils.resolveInventoryStatusValue(repairItem || baseRecord);
            const normalized = resolvedStatus.toUpperCase();
            if (!normalized || !config.INVENTORY_STATUS_KEYS.includes(normalized)) {
                return;
            }
            const displayStatus = resolvedStatus || normalized;
            const agingInfo = utils.calculateStatusAging(utils.getValueIgnoreCase(repairItem || baseRecord, 'DATE3'));
            statusMap[normalized].push({
                ...combined,
                STATUS_FA: utils.getValueIgnoreCase(repairItem, 'STATUS_FA')
                    || utils.getValueIgnoreCase(baseRecord, 'STATUS_FA')
                    || displayStatus,
                STATUS_DATA11: resolvedStatus || normalized,
                INVENTORY_STATUS: displayStatus,
                STATUS_AGING_HOURS: agingInfo.hours,
                STATUS_AGING_DISPLAY: agingInfo.display
            });
        });
        state.inventoryStatusDetailsMap = statusMap;
        const categories = [...config.INVENTORY_STATUS_KEYS];
        const totalCount = categories.reduce((sum, status) => sum + (statusMap[status]?.length || 0), 0);
        if (!totalCount) {
            chartHelpers.showEmptyState('inventoryStatusChart', 'Không tìm thấy dữ liệu trạng thái');
            charts.resetInventoryStatusSummary(0);
            return;
        }
        const seriesData = categories.map(status => ({
            value: statusMap[status]?.length || 0,
            itemStyle: {
                color: config.INVENTORY_STATUS_COLORS[status] || config.INVENTORY_STATUS_COLORS['KHÁC']
            },
            customData: {
                status,
                statusLabel: status,
                records: statusMap[status] || []
            }
        }));
        const option = {
            tooltip: {
                trigger: 'axis',
                axisPointer: { type: 'shadow' },
                formatter: params => {
                    if (!Array.isArray(params) || !params.length) return '';
                    const point = params[0];
                    const percent = totalCount ? ((point.value / totalCount) * 100).toFixed(2) : 0;
                    return `<div><strong>${point.name}</strong><br/>Số lượng: ${point.value}<br/>Tỷ lệ: ${percent}%</div>`;
                }
            },
            grid: { left: '3%', right: '4%', bottom: '8%', containLabel: true },
            xAxis: {
                type: 'category',
                data: categories,
                axisLabel: { color: '#b2ebf2', interval: 0, rotate: 20 },
                axisLine: { lineStyle: { color: '#00d9ff' } }
            },
            yAxis: {
                type: 'value',
                name: 'Số lượng',
                axisLabel: { color: '#b2ebf2' },
                splitLine: { lineStyle: { color: 'rgba(255,255,255,0.1)' } },
                axisLine: { lineStyle: { color: '#00d9ff' } }
            },
            series: [{
                type: 'bar',
                barMaxWidth: 36,
                label: {
                    show: true,
                    position: 'top',
                    color: '#ffffff'
                },
                data: seriesData
            }]
        };
        chartHelpers.render('inventoryStatusChartInstance', 'inventoryStatusChart', option, {
            click: params => {
                const custom = params?.data?.customData;
                if (params?.seriesType === 'bar' && typeof onPointClick === 'function' && custom?.records) {
                    onPointClick({ custom, name: params.name });
                }
            }
        });
        charts.resetInventoryStatusSummary(totalCount);
    }
};
