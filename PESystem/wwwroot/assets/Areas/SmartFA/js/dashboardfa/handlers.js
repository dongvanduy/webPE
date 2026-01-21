import { tables } from './tables.js';
import { charts } from './charts.js';
import { state } from './state.js';
import { services } from './services.js';
import { modals } from './modals.js';
import { utils } from './utils.js?v=2';
import { chartHelpers } from './chart-helpers.js';
import { feedback } from './feedback.js';

const extractCustomPayload = (params) => {
    if (!params) return null;
    const payload = params.data?.customData || params.data?.custom || params.customData || params.custom;
    if (!payload) return null;
    return {
        ...payload,
        title: payload.title || params.name || payload.statusLabel || ''
    };
};

const handleChartBarClick = (params) => {
    if (params?.seriesType !== 'bar') {
        return;
    }
    const custom = extractCustomPayload(params);
    if (!custom || !Array.isArray(custom.records) || !custom.records.length) {
        feedback.showError('Không có dữ liệu chi tiết cho cột này!');
        return;
    }
    modals.showCioModal(custom.records, custom.title || '', custom);
};

async function handleInventoryStatusPointClick(point) {
    try {
        const custom = point?.custom || {};
        const statusLabel = custom.statusLabel || custom.status || point?.category || point?.name || '';
        const records = Array.isArray(custom.records) ? custom.records : [];
        if (!records.length) {
            feedback.showError('Không có dữ liệu chi tiết cho trạng thái này!');
            return;
        }
        const success = await tables.updateModalSNTable(records);
        if (success && state.statusModalInstance) {
            const titleEl = document.getElementById('statusModalLabel');
            if (titleEl) {
                titleEl.textContent = `Danh sách tồn kho - ${statusLabel}`;
            }
            state.statusModalInstance.show();
        } else if (!success) {
            feedback.showError('Không thể cập nhật DataTable!');
        }
    } catch (error) {
        console.error('handleInventoryStatusPointClick error:', error);
        feedback.showError('Không thể hiển thị dữ liệu chi tiết!');
    }
}

async function loadStatusChart() {
    try {
        const response = await fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/get-status-counts', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify('ALL')
        });
        const result = await response.json();
        const chartElement = document.getElementById('statusChart');
        if (!chartElement) {
            return;
        }
        if (result.success && Array.isArray(result.data) && result.data.length > 0) {
            const categories = result.data.map(item => item.status);
            const seriesData = result.data.map(item => item.count);
            const lineData = seriesData.map(value => {
                const percent = result.totalCount ? (value / result.totalCount) * 100 : 0;
                return Number(percent.toFixed(2));
            });
            const openStatusDetail = async (selectedStatus) => {
                try {
                    const payload = { serialNumbers: [], modelName: '', testCode: '', status: selectedStatus, data1: '', handoverStatus: '', location: '' };
                    const searchResponse = await fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/search', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(payload)
                    });
                    if (!searchResponse.ok) {
                        throw new Error(`API Error: ${searchResponse.status} ${searchResponse.statusText}`);
                    }
                    const searchResult = await searchResponse.json();
                    if (searchResult.success && Array.isArray(searchResult.data) && searchResult.data.length > 0) {
                        const success = await tables.updateModalSNTable(searchResult.data);
                        if (success && state.statusModalInstance) {
                            state.statusModalInstance.show();
                        } else {
                            feedback.showError('Không thể cập nhật DataTable!');
                        }
                    } else {
                        feedback.showError(`No data for status: ${selectedStatus}`);
                    }
                } catch (error) {
                    console.error('Lỗi khi gọi API search:', error);
                    feedback.showError('Lỗi khi gọi API search: ' + (error.message || 'Không xác định'));
                }
            };
            const option = {
                color: ['#FF9800', '#00E5FF'],
                tooltip: {
                    trigger: 'axis',
                    axisPointer: { type: 'cross' },
                    formatter: params => {
                        if (!Array.isArray(params) || !params.length) return '';
                        const title = params[0]?.axisValueLabel || '';
                        const rows = params.map(point => {
                            const suffix = point.seriesName === 'Tỷ lệ' ? '%' : '';
                            return `<div><span style="display:inline-block;margin-right:8px;width:10px;height:10px;background:${point.color};border-radius:50%"></span>${point.seriesName}: <b>${point.value}${suffix}</b></div>`;
                        }).join('');
                        return `<div><strong>${title}</strong>${rows}</div>`;
                    }
                },
                legend: {
                    data: ['Số lượng', 'Tỷ lệ'],
                    textStyle: { color: '#e0f7fa' }
                },
                grid: { left: '3%', right: '4%', bottom: '8%', containLabel: true },
                xAxis: {
                    type: 'category',
                    data: categories,
                    axisLabel: {
                        color: '#b2ebf2',
                        formatter: value => value && value.length > 6 ? `${value.substring(0, 6)}...` : value
                    },
                    axisLine: { lineStyle: { color: '#00d9ff' } }
                },
                yAxis: [{
                    type: 'value',
                    name: 'Số lượng',
                    axisLabel: { color: '#b2ebf2' },
                    splitLine: { lineStyle: { color: 'rgba(255,255,255,0.1)' } },
                    axisLine: { lineStyle: { color: '#00d9ff' } }
                }, {
                    type: 'value',
                    name: 'Tỷ lệ (%)',
                    axisLabel: {
                        color: '#b2ebf2',
                        formatter: value => `${value}%`
                    },
                    min: 0,
                    max: 100,
                    splitLine: { show: false },
                    axisLine: { lineStyle: { color: '#00d9ff' } }
                }],
                series: [{
                    name: 'Số lượng',
                    type: 'bar',
                    barMaxWidth: 28,
                    label: {
                        show: true,
                        position: 'top',
                        color: '#ffffff',
                        formatter: ({ value }) => (value ?? 0).toLocaleString('en-US')
                    },
                    itemStyle: { borderRadius: [6, 6, 0, 0] },
                    data: seriesData
                }, {
                    name: 'Tỷ lệ',
                    type: 'line',
                    smooth: true,
                    yAxisIndex: 1,
                    symbol: 'circle',
                    symbolSize: 8,
                    lineStyle: { width: 3 },
                    label: {
                        show: true,
                        formatter: ({ value }) => `${value}%`,
                        color: '#00E5FF'
                    },
                    data: lineData
                }]
            };
            chartHelpers.render('statusChartInstance', 'statusChart', option, {
                click: params => {
                    if (params?.name && (params.seriesName === 'Số lượng' || params.seriesName === 'Tỷ lệ')) {
                        openStatusDetail(params.name);
                    }
                }
            });
        } else {
            chartHelpers.showEmptyState('statusChart', 'Không có dữ liệu trạng thái');
        }
    } catch (error) {
        console.error('Lỗi khi gọi API hoặc vẽ biểu đồ:', error);
    }
}

async function loadCheckInOutChart() {
    const startInput = document.getElementById('cioStartDate');
    const endInput = document.getElementById('cioEndDate');
    if (!startInput || !endInput) {
        console.error('Không tìm thấy input ngày');
        return;
    }
    try {
        const startDate = startInput.value;
        const endDate = endInput.value;
        const urlBefore = new URL('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/CheckInOut/GetCheckInOutBeforeKanban');
        urlBefore.searchParams.append('startDate', startDate);
        urlBefore.searchParams.append('endDate', endDate);
        const resBefore = await fetch(urlBefore);
        if (!resBefore.ok) throw new Error('API Before error');
        const dataBefore = await resBefore.json();
        const checkInBefore = (dataBefore?.checkIn?.data || []).map(item => ({ ...item, __sourceType: 'before' }));
        const checkOutTon = (dataBefore?.checkOut?.tonKhoCu?.data || []).map(item => ({ ...item, __sourceType: 'before' }));
        const checkOutTrongNgay = (dataBefore?.checkOut?.trongNgay?.data || []).map(item => ({ ...item, __sourceType: 'before' }));
        const tonKhoTrongNgay = (dataBefore?.tonKhoTrongNgay?.data || []).map(item => ({ ...item, __sourceType: 'before' }));
        const urlAfter = new URL('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/CheckInOut/GetCheckInAfterKanBan');
        urlAfter.searchParams.append('startDate', startDate);
        urlAfter.searchParams.append('endDate', endDate);
        const resAfter = await fetch(urlAfter);
        if (!resAfter.ok) throw new Error('API After error');
        const dataAfter = await resAfter.json();
        const checkInAfter = (dataAfter?.data || []).map(item => ({ ...item, __sourceType: 'after' }));
        const urlTonKho = new URL('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/CheckInOut/GetTonKhoSummary');
        urlTonKho.searchParams.append('startDate', startDate);
        urlTonKho.searchParams.append('endDate', endDate);
        let tonKhoSummaryBefore = [];
        let tonKhoSummaryAfter = [];
        try {
            const tonKhoRes = await fetch(urlTonKho);
            if (tonKhoRes.ok) {
                const tonKhoJson = await tonKhoRes.json();
                tonKhoSummaryBefore = (tonKhoJson?.beforeKanban?.data || []).map(item => ({ ...item, __sourceType: 'before' }));
                tonKhoSummaryAfter = (tonKhoJson?.afterKanban?.data || []).map(item => ({ ...item, __sourceType: 'after' }));
            }
        } catch (err) {
            console.error('Lỗi khi gọi API tồn kho:', err);
        }
        const tonKhoCombined = [...tonKhoSummaryBefore, ...tonKhoSummaryAfter];
        await tables.updateNoLocationInventory(tonKhoCombined);
        await charts.renderInventoryStatusChart(tonKhoCombined, { onPointClick: handleInventoryStatusPointClick });
        const sapResult = await services.fetchSapInOutData(startDate, endDate);
        state.sapInOutRecords = Array.isArray(sapResult.records) ? [...sapResult.records] : [];
        charts.renderSapInOutChart(state.sapInOutRecords);
        const sapTotal = typeof sapResult.total === 'number' ? sapResult.total : state.sapInOutRecords.length;
        const beforeAfterCategories = ['CheckIn Before', 'CheckIn After', 'Tồn kho', 'SAP In'];
        const beforeAfterData = [
            { value: checkInBefore.length, itemStyle: { color: '#00E5FF' }, customData: { records: checkInBefore, title: 'Danh sách Check In Before', type: 'default' } },
            { value: checkInAfter.length, itemStyle: { color: '#FF5722' }, customData: { records: checkInAfter, title: 'Danh sách Check In After', type: 'default' } },
            { value: tonKhoCombined.length, itemStyle: { color: '#4CAF50' }, customData: { records: tonKhoCombined, title: 'Danh sách tồn kho', type: 'default' } },
            { value: sapTotal, itemStyle: { color: '#673AB7' }, customData: { records: state.sapInOutRecords, title: 'Danh sách SN vào SAP', type: 'sap' } }
        ];
        const beforeAfterOption = {
            tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
            grid: { left: '3%', right: '4%', bottom: '6%', containLabel: true },
            xAxis: {
                type: 'category',
                data: beforeAfterCategories,
                axisLabel: { color: '#b2ebf2', interval: 0 },
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
                barMaxWidth: 40,
                label: { show: true, position: 'top', color: '#ffffff' },
                data: beforeAfterData
            }]
        };
        chartHelpers.render('checkInBeforeAfterChartInstance', 'checkInBeforeAfer', beforeAfterOption, {
            click: handleChartBarClick
        });
        const cioCategories = ['Check In', 'Check Out tồn', 'Check Out trong ngày', 'Tồn kho trong ngày'];
        const cioData = [
            { value: checkInBefore.length, itemStyle: { color: '#00E5FF' }, customData: { records: checkInBefore, title: 'Danh sách Check In', type: 'before' } },
            { value: checkOutTon.length, itemStyle: { color: '#FF5722' }, customData: { records: checkOutTon, title: 'Check Out hàng tồn', type: 'before' } },
            { value: checkOutTrongNgay.length, itemStyle: { color: '#FF9800' }, customData: { records: checkOutTrongNgay, title: 'Check Out trong ngày', type: 'before' } },
            { value: tonKhoTrongNgay.length, itemStyle: { color: '#4CAF50' }, customData: { records: tonKhoTrongNgay, title: 'Tồn kho trong ngày', type: 'before' } }
        ];
        const cioOption = {
            tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
            grid: { left: '3%', right: '4%', bottom: '6%', containLabel: true },
            xAxis: {
                type: 'category',
                data: cioCategories,
                axisLabel: { color: '#b2ebf2', interval: 0 },
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
                barMaxWidth: 40,
                label: { show: true, position: 'top', color: '#ffffff' },
                data: cioData
            }]
        };
        chartHelpers.render('checkInOutChartInstance', 'checkInOutChart', cioOption, {
            click: handleChartBarClick
        });
    } catch (error) {
        console.error('Lỗi khi lấy dữ liệu Check In/Out:', error);
        tables.renderNoLocationTable([]);
    }
}

async function loadNoLocationTrendChart() {
    const chartElement = document.getElementById('noLocationTrendChart');
    if (!chartElement) {
        return;
    }
    try {
        const now = new Date();
        const anchorToday = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 7, 30, 0, 0);
        const dayMs = 24 * 60 * 60 * 1000;
        const startDay2 = new Date(anchorToday.getTime() - dayMs * 2);
        const startDay3 = new Date(anchorToday.getTime() - dayMs * 1);
        const windows = [
            { start: startDay2, end: new Date(startDay3) },
            { start: startDay3, end: new Date(anchorToday) },
            { start: anchorToday, end: now }
        ];
        state.noLocationTrendDetails = [];
        const categories = [];
        const seriesData = [];
        for (const window of windows) {
            const { noLocationItems } = await services.fetchTonKhoNoLocationRange(window.start, window.end);
            const label = utils.formatDisplayRange(window.start, window.end);
            categories.push(label);
            state.noLocationTrendDetails.push({
                label,
                start: new Date(window.start),
                end: new Date(window.end),
                items: noLocationItems
            });
            seriesData.push({
                value: noLocationItems.length,
                itemStyle: { color: '#F44336' },
                customData: {
                    records: noLocationItems,
                    title: `Tồn kho chưa có vị trí (${label})`,
                    type: 'tonKho'
                }
            });
        }
        const option = {
            tooltip: {
                trigger: 'axis',
                axisPointer: { type: 'shadow' },
                formatter: params => {
                    const item = params?.[0];
                    if (!item) return '';
                    const label = categories[item.dataIndex] || '';
                    return `<div><strong>${label}</strong><br/>Số lượng: <b>${item.value}</b></div>`;
                }
            },
            grid: { left: '3%', right: '4%', bottom: '8%', containLabel: true },
            xAxis: {
                type: 'category',
                data: categories,
                axisLabel: { color: '#b2ebf2', interval: 0 },
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
                name: 'Chưa có vị trí',
                barMaxWidth: 36,
                label: { show: true, position: 'top', color: '#ffffff' },
                data: seriesData
            }]
        };
        chartHelpers.render('noLocationTrendChartInstance', 'noLocationTrendChart', option, {
            click: handleChartBarClick
        });
    } catch (error) {
        console.error('loadNoLocationTrendChart error:', error);
    }
}

async function loadPreviousDayReport() {
    const rangeElement = document.getElementById('previousDayReportRange');
    const checkInElement = document.getElementById('previousDayCheckInCount');
    const hasLocationElement = document.getElementById('previousDayHasLocationCount');
    const noLocationElement = document.getElementById('previousDayNoLocationCount');
    const viewButton = document.getElementById('previousDayNoLocationBtn');
    if (!rangeElement || !checkInElement || !hasLocationElement || !noLocationElement || !viewButton) {
        return;
    }
    try {
        const now = new Date();
        const anchorToday = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 7, 30, 0, 0);
        let reportEnd = new Date(anchorToday);
        if (now < anchorToday) {
            reportEnd = new Date(anchorToday);
        }
        const reportStart = new Date(reportEnd.getTime() - 24 * 60 * 60 * 1000);
        rangeElement.textContent = utils.formatDisplayRange(reportStart, reportEnd);
        const beforeUrl = new URL('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/CheckInOut/GetCheckInOutBeforeKanban');
        beforeUrl.searchParams.append('startDate', utils.formatDateTime(reportStart));
        beforeUrl.searchParams.append('endDate', utils.formatDateTime(reportEnd));
        const afterUrl = new URL('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/CheckInOut/GetCheckInAfterKanBan');
        afterUrl.searchParams.append('startDate', utils.formatDateTime(reportStart));
        afterUrl.searchParams.append('endDate', utils.formatDateTime(reportEnd));
        const [beforeResponse, afterResponse] = await Promise.all([fetch(beforeUrl), fetch(afterUrl)]);
        if (!beforeResponse.ok || !afterResponse.ok) {
            throw new Error('Report API error');
        }
        const beforeData = await beforeResponse.json();
        const afterData = await afterResponse.json();
        const checkInRecords = (beforeData?.checkIn?.data || []).map(item => ({ ...item, __sourceType: 'before' }));
        const tonKhoRecords = (beforeData?.tonKhoTrongNgay?.data || []).map(item => ({ ...item, __sourceType: 'before' }));
        const afterRecords = (afterData?.data || []).map(item => ({ ...item, __sourceType: 'after' }));
        const tonKhoCombined = [...tonKhoRecords, ...afterRecords];
        const { items: enrichedItems } = await services.attachLocationInfo(tonKhoCombined);
        const uniqueRecords = new Map();
        enrichedItems.forEach(item => {
            const key = utils.getSerialForLocation(item).trim().toUpperCase();
            if (key && !uniqueRecords.has(key)) {
                uniqueRecords.set(key, item);
            }
        });
        const hasLocationCount = [...uniqueRecords.values()].filter(item => (item.location || '').trim()).length;
        const noLocationRecords = [...uniqueRecords.values()].filter(item => !(item.location || '').trim());
        checkInElement.textContent = uniqueRecords.size.toString();
        hasLocationElement.textContent = hasLocationCount.toString();
        noLocationElement.textContent = noLocationRecords.length.toString();
        state.previousDayNoLocationRecords = [...noLocationRecords];
        viewButton.disabled = noLocationRecords.length === 0;
    } catch (error) {
        console.error('loadPreviousDayReport error:', error);
        feedback.showError('Không thể tải báo cáo ngày hôm trước!');
    }
}

export const handlers = {
    handleInventoryStatusPointClick,
    loadStatusChart,
    loadCheckInOutChart,
    loadNoLocationTrendChart,
    loadPreviousDayReport
};
