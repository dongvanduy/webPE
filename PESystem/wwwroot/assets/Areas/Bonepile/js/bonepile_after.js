document.addEventListener('DOMContentLoaded', async function () {
    const apiBase = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Bonepile2';
    const apiAgingCountUrl = `${apiBase}/bonepile-after-kanban-aging-count`;
    const apiBasicUrl = `${apiBase}/bonepile-after-kanban-basic`;
    const locationUrl = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Search/FindLocations';
    const apiCheckInOutBase = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/CheckInOut';
    const apiTestResultUrl = `${apiCheckInOutBase}/get-test-result-list`;

    const validStatuses = [
        'ScrapHasTask',
        'ScrapLackTask',
        'WaitingApprovalScrap',
        'ApprovedBGA',
        'WaitingApprovalBGA',
        'RepairInRE',
        'WaitingCheckOut',
        'WaitingLink',
        'Linked',
        "Can'tRepairProcess",
        'B36V'
    ];

    const statusColorMap = {
        ScrapHasTask: '#05b529',
        ScrapLackTask: '#ffc107',
        WaitingApprovalScrap: '#dc3545',
        WaitingApprovalBGA: '#17a2b8',
        ApprovedBGA: '#17b86d',
        RepairInRE: '#ff8307',
        WaitingCheckOut: '#fe8307',
        WaitingLink: '#17a2b8',
        Linked: '#6c757d',
        "Can'tRepairProcess": '#ffc107',
        B36V: '#28a745'
    };

    const statusDisplayMap = {
        ScrapHasTask: 'Scrap Has Task',
        ScrapLackTask: 'Scrap Lacks Task',
        WaitingApprovalScrap: 'Pending Scrap SPE Approval',
        ApprovedBGA: 'Approved BGA',
        WaitingApprovalBGA: 'Pending BGA SPE Approval',
        RepairInRE: 'Repair In RE',
        WaitingCheckOut: 'Waiting Check Out',
        WaitingLink: 'Waiting Link',
        Linked: 'Linked',
        "Can'tRepairProcess": "Can't Repair Process",
        B36V: 'B36V'
    };

    let dataTable = null;
    let modalTable = null;
    let basicData = {};
    let agingData = [];
    let productLineAgingData = [];

    const charts = [];
    const statusChart = initChart('statusBarChart');
    const agingChart = initChart('agingPieChart');
    const productLineChart = initChart('productLineAgingChart');

    if (statusChart) charts.push(statusChart);
    if (agingChart) charts.push(agingChart);
    if (productLineChart) charts.push(productLineChart);

    await loadDashboardData();

    function initChart(elementId) {
        if (typeof echarts === 'undefined') {
            console.warn('ECharts library is not loaded.');
            return null;
        }
        const element = document.getElementById(elementId);
        if (!element) {
            console.warn(`Element with id "${elementId}" not found.`);
            return null;
        }
        return echarts.init(element);
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function createTooltipCell(data) {
        const safeText = escapeHtml(data);
        return `<span class="tooltip-trigger" data-tooltip="${safeText}">${safeText}</span>`;
    }

    function normalizeSn(sn) {
        return (sn || '').toString().trim().toUpperCase();
    }

    function formatDateTime(value) {
        if (!value) {
            return '';
        }
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return value;
        }
        const pad = (num) => num.toString().padStart(2, '0');
        const year = date.getFullYear();
        const month = pad(date.getMonth() + 1);
        const day = pad(date.getDate());
        const hours = pad(date.getHours());
        const minutes = pad(date.getMinutes());
        const seconds = pad(date.getSeconds());
        return `${year}-${month}-${day} ${hours}:${minutes}:${seconds}`;
    }

    function buildLocationMap(rawData) {
        if (!Array.isArray(rawData)) {
            return {};
        }
        return rawData.reduce((acc, item) => {
            const sn = normalizeSn(item?.serialNumber || item?.sn || item?.SERIAL_NUMBER);
            if (!sn) {
                return acc;
            }
            const warehouse = item?.warehouse || item?.Warehouse || '';
            const location = item?.location || item?.Location || '';
            const display = [warehouse, location].filter(Boolean).join(' - ');
            acc[sn] = { warehouse, location, display };
            return acc;
        }, {});
    }

    async function buildTestResultMap(serialNumbers) {
        if (!Array.isArray(serialNumbers) || serialNumbers.length === 0) {
            return {};
        }

        const normalizedSerials = Array.from(new Set(serialNumbers.map(normalizeSn).filter(Boolean)));
        if (!normalizedSerials.length) {
            return {};
        }

        const chunkSize = 900;
        const resultMap = {};

        for (let i = 0; i < normalizedSerials.length; i += chunkSize) {
            const chunk = normalizedSerials.slice(i, i + chunkSize);
            try {
                const response = await axios.post(apiTestResultUrl, chunk);
                const list = response?.data?.data || [];
                list.forEach(item => {
                    const sn = normalizeSn(item?.serialNumber || item?.KEY_PART_SN);
                    if (!sn) {
                        return;
                    }
                    resultMap[sn] = {
                        testResultSn: item?.testSn || item?.TEST_SN || '',
                        testResultGroup: item?.groupName || item?.GROUP_NAME || '',
                        testResultData1: item?.data1 || item?.DATA1 || '',
                        testResultData2: item?.data2 || item?.DATA2 || '',
                        testResultPassDate: item?.passDate ? formatDateTime(item.passDate) : (item?.PASS_DATE || '')
                    };
                });
            } catch (error) {
                console.error('Error fetching test results chunk', error);
            }
        }

        return resultMap;
    }

    function normalizeRecord(record = {}) {
        const normalized = {
            sn: record.sn ?? record.SN ?? '',
            productLine: record.productLine ?? record.ProductLine ?? '',
            modelName: record.modelName ?? record.ModelName ?? '',
            moNumber: record.moNumber ?? record.MoNumber ?? '',
            wipGroupSFC: record.wipGroupSFC ?? record.WipGroupSFC ?? '',
            wipGroupKANBAN: record.wipGroupKANBAN ?? record.wipGroupKanban ?? record.WipGroupKANBAN ?? record.WipGroupKanban ?? '',
            errorFlag: record.errorFlag ?? record.ErrorFlag ?? '',
            workFlag: record.workFlag ?? record.WorkFlag ?? '',
            testGroup: record.testGroup ?? record.TestGroup ?? '',
            testTime: formatDateTime(record.testTime ?? record.TestTime ?? record.test_time ?? ''),
            testCode: record.testCode ?? record.TestCode ?? '',
            errorCodeItem: record.errorCodeItem ?? record.ErrorCodeItem ?? '',
            errorDesc: record.errorDesc ?? record.ErrorDesc ?? '',
            status: record.status ?? record.Status ?? '',
            statusV2: record.statusV2 ?? record.StatusV2 ?? '',
            aging: record.aging ?? record.Aging ?? record.agingDay ?? record.AgingDay ?? '',
            agingOld: record.agingOld ?? record.AgingOld ?? record.agingOldest ?? record.AgingOldest ?? '',
            location: record.location ?? record.Location ?? '',
            testResultSn: record.testResultSn ?? record.TestResultSn ?? '',
            testResultGroup: record.testResultGroup ?? record.TestResultGroup ?? '',
            testResultData1: record.testResultData1 ?? record.TestResultData1 ?? '',
            testResultData2: record.testResultData2 ?? record.TestResultData2 ?? '',
            testResultPassDate: formatDateTime(record.testResultPassDate ?? record.TestResultPassDate ?? '')
        };
        return normalized;
    }

    function normalizeStatusCounts(statusCounts = []) {
        return statusCounts
            .map(item => ({
                status: item?.status ?? item?.Status ?? '',
                count: Number(item?.count ?? item?.Count ?? 0)
            }))
            .filter(item => item.status);
    }

    function normalizeAgingCounts(rawCounts = []) {
        const map = new Map();
        rawCounts.forEach(item => {
            const ageRange = (item?.ageRange ?? item?.AgeRange ?? '').trim() || '>=90';
            const count = Number(item?.count ?? item?.Count ?? 0);
            const records = (item?.records ?? item?.Records ?? []).map(normalizeRecord);
            map.set(ageRange, { ageRange, count, records });
        });

        const orderedRanges = ['<45', '45-89', '>=90'];
        const normalized = orderedRanges.map(range => {
            return map.get(range) || { ageRange: range, count: 0, records: [] };
        });

        map.forEach((value, key) => {
            if (!orderedRanges.includes(key)) {
                normalized.push(value);
            }
        });

        return normalized;
    }

    function normalizeProductLineAging(rawData = []) {
        return rawData
            .map(item => {
                const productLine = item?.productLine ?? item?.ProductLine ?? 'Unknown';
                const counts = { '<45': 0, '45-89': 0, '>=90': 0 };
                const agingCounts = item?.agingCounts ?? item?.AgingCounts ?? [];
                agingCounts.forEach(ac => {
                    const range = (ac?.ageRange ?? ac?.AgeRange ?? '>=90').trim() || '>=90';
                    const value = Number(ac?.count ?? ac?.Count ?? 0);
                    counts[range] = value;
                });
                const total = counts['<45'] + counts['45-89'] + counts['>=90'];
                return { productLine, counts, total };
            })
            .filter(item => item.total > 0);
    }

    function filterRecordsByStatuses(map, statuses = []) {
        if (!map || !Array.isArray(statuses) || statuses.length === 0) {
            return [];
        }
        const statusSet = new Set(statuses.map(status => (status || '').toLowerCase()));
        return Object.entries(map)
            .filter(([key]) => statusSet.has((key || '').toLowerCase()))
            .flatMap(([, list]) => Array.isArray(list) ? list : []);
    }

    function getStatusCount(statusCounts, statusName) {
        const match = statusCounts.find(item => item.status.toLowerCase() === statusName.toLowerCase());
        return match ? Number(match.count || 0) : 0;
    }

    function updateKpi(statusCounts, totalCount) {
        const setText = (id, value) => {
            const el = document.getElementById(id);
            if (el) {
                el.innerText = value;
            }
        };
        const formatNumber = value => Number(value || 0).toLocaleString();

        setText('totalCount', formatNumber(totalCount));
        setText('scrapCount', formatNumber(getStatusCount(statusCounts, 'ScrapHasTask')));
        setText('noTaskscrapCount', formatNumber(getStatusCount(statusCounts, 'ScrapLackTask')));
        setText('waitingScrapCount', formatNumber(getStatusCount(statusCounts, 'WaitingApprovalScrap')));
        setText('waitingBGACount', formatNumber(getStatusCount(statusCounts, 'WaitingApprovalBGA')));
        setText('approvedBGACount', formatNumber(getStatusCount(statusCounts, 'ApprovedBGA')));
        setText('repairInRECount', formatNumber(getStatusCount(statusCounts, 'RepairInRE')));
        setText('waitingCheckOutCount', formatNumber(getStatusCount(statusCounts, 'WaitingCheckOut')));
        setText('notRepairProcessCount', formatNumber(getStatusCount(statusCounts, "Can'tRepairProcess")));
        setText('waitingLinkCount', formatNumber(getStatusCount(statusCounts, 'WaitingLink')));
        setText('linkedCount', formatNumber(getStatusCount(statusCounts, 'Linked')));
    }

    function renderStatusChart(statusCounts) {
        if (!statusChart) {
            return;
        }
        const sorted = [...statusCounts].sort((a, b) => b.count - a.count);
        const labels = sorted.map(item => item.status);
        const data = sorted.map(item => item.count);
        const total = data.reduce((sum, value) => sum + value, 0);

        const option = {
            tooltip: {
                trigger: 'axis',
                axisPointer: { type: 'shadow' },
                formatter: params => {
                    if (!Array.isArray(params) || !params.length) {
                        return '';
                    }
                    const item = params[0];
                    const percent = total ? ((item.value / total) * 100).toFixed(1) : '0.0';
                    return `${item.name}<br/>Count: ${item.value}<br/>Percent: ${percent}%`;
                }
            },
            grid: { left: '3%', right: '3%',top: '4%', bottom: '5%', containLabel: true },
            xAxis: {
                type: 'category',
                data: labels,
                axisLabel: { interval: 0, rotate: 30 }
            },
            yAxis: {
                type: 'value',
                minInterval: 1
            },
            series: [{
                type: 'bar',
                data,
                itemStyle: {
                    color: params => statusColorMap[params.name] || '#5470C6'
                },
                label: {
                    show: true,
                    position: 'top',
                    formatter: params => {
                        if (!total) {
                            return '';
                        }
                        const percentage = (params.value / total) * 100;
                        return `${percentage.toFixed(1)}%`;
                    }
                }
            }]
        };

        statusChart.setOption(option, true);
    }

    function renderAgingPieChart(agingCounts) {
        if (!agingChart) {
            return;
        }
        const chartData = agingCounts.map(item => ({
            name: item.ageRange,
            value: item.count
        }));

        const option = {
            tooltip: {
                trigger: 'item',
                formatter: params => `${params.name}: ${params.value} (${params.percent}%)`
            },
            legend: {
                orient: 'horizontal',
                bottom: 0
            },
            series: [{
                name: 'Aging',
                type: 'pie',
                radius: ['40%', '70%'],
                center: ['50%', '45%'],
                avoidLabelOverlap: false,
                label: {
                    show: true,
                    formatter: params => `${params.percent.toFixed(1)}%`
                },
                emphasis: {
                    label: {
                        show: true,
                        fontWeight: 'bold'
                    }
                },
                data: chartData
            }]
        };

        agingChart.setOption(option, true);
        agingChart.off('click');
        agingChart.on('click', params => {
            const entry = agingData.find(item => item.ageRange === params.name);
            if (entry && Array.isArray(entry.records)) {
                loadTableFromRecords(entry.records);
            }
        });
    }

    function renderProductLineAging(data) {
        const tableBody = document.getElementById('productLineAgingTableBody');
        if (!tableBody) {
            return;
        }

        const sorted = [...data].sort((a, b) => b.total - a.total);
        const topProductLines = sorted.slice(0, 10);
        const remaining = sorted.slice(10);

        if (remaining.length > 0) {
            const aggregatedCounts = remaining.reduce((acc, item) => {
                acc['<45'] += item.counts['<45'];
                acc['45-89'] += item.counts['45-89'];
                acc['>=90'] += item.counts['>=90'];
                return acc;
            }, { '<45': 0, '45-89': 0, '>=90': 0 });

            const aggregatedTotal = aggregatedCounts['<45'] + aggregatedCounts['45-89'] + aggregatedCounts['>=90'];
            if (aggregatedTotal > 0) {
                topProductLines.push({
                    productLine: 'Other',
                    counts: aggregatedCounts,
                    total: aggregatedTotal
                });
            }
        }

        const rowsHtml = topProductLines.map(item => {
            return `<tr>
                <td>${escapeHtml(item.productLine)}</td>
                <td>${item.counts['<45']}</td>
                <td>${item.counts['45-89']}</td>
                <td>${item.counts['>=90']}</td>
                <td>${item.total}</td>
            </tr>`;
        }).join('');
        tableBody.innerHTML = rowsHtml;

        if (!productLineChart) {
            return;
        }

        const labels = topProductLines.map(item => item.productLine);
        const under45 = topProductLines.map(item => item.counts['<45']);
        const mid = topProductLines.map(item => item.counts['45-89']);
        const over90 = topProductLines.map(item => item.counts['>=90']);

        const option = {
            tooltip: {
                trigger: 'axis',
                axisPointer: { type: 'shadow' }
            },
            legend: {
                top: 0,
                data: ['<45', '45-89', '>=90']
            },
            grid: { left: '3%', right: '3%', top: '4%', bottom: '3%', containLabel: true },
            xAxis: {
                type: 'category',
                data: labels,
                axisLabel: { interval: 0, rotate: 30 }
            },
            yAxis: {
                type: 'value',
                minInterval: 1
            },
            series: [
                {
                    name: '<45',
                    type: 'bar',
                    stack: 'total',
                    emphasis: { focus: 'series' },
                    data: under45,
                    label: {
                        show: true,
                        position: 'inside',
                        formatter: params => params.value > 0 ? params.value : ''
                    }
                },
                {
                    name: '45-89',
                    type: 'bar',
                    stack: 'total',
                    emphasis: { focus: 'series' },
                    data: mid,
                    label: {
                        show: true,
                        position: 'inside',
                        formatter: params => params.value > 0 ? params.value : ''
                    }
                },
                {
                    name: '>=90',
                    type: 'bar',
                    stack: 'total',
                    emphasis: { focus: 'series' },
                    data: over90,
                    label: {
                        show: true,
                        position: 'inside',
                        formatter: params => params.value > 0 ? params.value : ''
                    }
                }
            ]
        };

        productLineChart.setOption(option, true);
    }

    async function loadDashboardData() {
        try {
            showSpinner();
            const [basicRes, agingRes] = await Promise.all([
                axios.get(apiBasicUrl),
                axios.get(apiAgingCountUrl)
            ]);

            basicData = basicRes?.data || {};
            const statusCounts = normalizeStatusCounts(basicData.statusCounts || []);
            agingData = normalizeAgingCounts(agingRes?.data?.agingCounts || agingRes?.agingCounts || []);
            productLineAgingData = normalizeProductLineAging(agingRes?.data?.agingCountsByProductLine || agingRes?.agingCountsByProductLine || []);

            updateKpi(statusCounts, basicData.totalCount || 0);
            renderStatusChart(statusCounts);
            renderAgingPieChart(agingData);
            renderProductLineAging(productLineAgingData);

            await loadTableData(validStatuses, null, true);
        } catch (error) {
            console.error('Lỗi khi tải dashboard:', error);
            alert('Không thể tải dữ liệu dashboard. Vui lòng thử lại!');
        } finally {
            hideSpinner();
        }
    }

    async function loadTableData(statuses = null, statusesV2 = null, skipSpinner = false) {
        try {
            if (!skipSpinner) {
                showSpinner();
            }

            const { data: allData = [], statusDetails = {}, statusDetails_v2 = {} } = basicData || {};

            let rawRows = [];
            if (Array.isArray(statuses) && statuses.length > 0) {
                rawRows = filterRecordsByStatuses(statusDetails, statuses);
            } else if (Array.isArray(statusesV2) && statusesV2.length > 0) {
                rawRows = filterRecordsByStatuses(statusDetails_v2, statusesV2);
            } else {
                rawRows = Array.isArray(allData) ? allData : [];
            }

            const normalizedRows = rawRows.map(normalizeRecord);
            const serials = Array.from(new Set(normalizedRows.map(r => normalizeSn(r.sn)).filter(Boolean)));

            let locationMap = {};
            let testResultMap = {};
            if (serials.length) {
                try {
                    const [locRes, testResultRes] = await Promise.all([
                        axios.post(locationUrl, serials),
                        buildTestResultMap(serials)
                    ]);
                    locationMap = buildLocationMap(locRes?.data?.data);
                    testResultMap = testResultRes;
                } catch (err) {
                    console.error('Error fetching locations or test results', err);
                }
            }

            normalizedRows.forEach(row => {
                const info = locationMap[normalizeSn(row.sn)];
                row.location = info?.display || row.location || '';
                const testInfo = testResultMap[normalizeSn(row.sn)];
                if (testInfo) {
                    row.testResultSn = testInfo.testResultSn || '';
                    row.testResultGroup = testInfo.testResultGroup || '';
                    row.testResultData1 = testInfo.testResultData1 || '';
                    row.testResultData2 = testInfo.testResultData2 || '';
                    row.testResultPassDate = testInfo.testResultPassDate || '';
                }
            });

            if (dataTable) {
                dataTable.clear().rows.add(normalizedRows).draw();
            } else {
                dataTable = $('#sumMaterialsTable').DataTable({
                    data: normalizedRows,
                    scrollX: true,
                    ordering: true,
                    info: true,
                    autoWidth: false,
                    order: [[3, 'desc']],
                    columns: [
                        { data: 'sn' },
                        { data: 'productLine' },
                        { data: 'modelName' },
                        { data: 'moNumber' },
                        { data: 'wipGroupSFC' },
                        { data: 'wipGroupKANBAN' },
                        { data: 'errorFlag' },
                        { data: 'workFlag' },
                        { data: 'testGroup' },
                        { data: 'testTime' },
                        { data: 'testCode' },
                        { data: 'errorCodeItem' },
                        { data: 'errorDesc' },
                        { data: 'status' },
                        { data: 'statusV2' },
                        { data: 'aging' },
                        { data: 'agingOld' },
                        { data: 'location' },
                        { data: 'testResultSn' },
                        { data: 'testResultGroup' },
                        { data: 'testResultData1' },
                        { data: 'testResultData2' },
                        { data: 'testResultPassDate' }
                    ],
                    columnDefs: [
                        {
                            targets: '_all',
                            render: function (data, type) {
                                if (type === 'display') {
                                    return createTooltipCell(data ?? '');
                                }
                                if (type === 'filter') {
                                    return data ?? '';
                                }
                                return data;
                            }
                        }
                    ],
                    dom: '<"top d-flex align-items-center"flB>rt<"bottom"ip>',
                    buttons: [
                        {
                            extend: 'excelHtml5',
                            text: '<img src="/assets/img/excel.png" class="excel-icon excel-button"/>',
                            title: '',
                            filename: function () {
                                const now = new Date();
                                const offset = 7 * 60;
                                const localDate = new Date(now.getTime() + offset * 60 * 1000);
                                const dateStr = localDate.toISOString().slice(0, 10).replace(/-/g, '');
                                const timeStr = localDate.toTimeString().slice(0, 8).replace(/:/g, '');
                                return `Bonepile_after_${dateStr}_${timeStr}`;
                            },
                            exportOptions: {
                                columns: ':visible',
                                modifier: {
                                    selected: null
                                },
                                format: {
                                    header: function (data) {
                                        return (data || '').trim();
                                    }
                                }
                            }
                        }
                    ],
                    destroy: true,
                    language: {
                        search: '',
                        emptyTable: 'Không có dữ liệu để hiển thị',
                        zeroRecords: 'Không tìm thấy bản ghi phù hợp'
                    },
                    initComplete: function () {
                        const optionsHtml = ['<option value="">Tất cả</option>']
                            .concat(validStatuses.map(status => {
                                const label = statusDisplayMap[status] || status;
                                return `<option value="${status}">${escapeHtml(label)}</option>`;
                            }))
                            .join('');

                        const selectHtml = `
                            <div class="form-group mb-0" style="min-width: 200px;">
                                <select id="statusFilterDt" class="form-control">
                                    ${optionsHtml}
                                </select>
                            </div>`;

                        $('.dataTables_wrapper .top').prepend(selectHtml);

                        $('#statusFilterDt').on('change', async function () {
                            const selectedStatus = this.value;
                            const statusesToLoad = selectedStatus ? [selectedStatus] : validStatuses;
                            await loadTableData(statusesToLoad);
                        });

                        $('.dataTables_filter input[type="search"]').attr('placeholder', 'Tìm kiếm');
                    }
                });
                //JS để click vào row thì giữ highlight
                $('#sumMaterialsTable tbody').on('click', 'tr', function () {
                    // Bỏ chọn các row khác
                    $('#sumMaterialsTable tbody tr').removeClass('selected-row');

                    // Gán class highlight
                    $(this).addClass('selected-row');
                });
            }
        } catch (error) {
            console.error('Lỗi khi tải dữ liệu bảng:', error);
            alert('Không thể tải dữ liệu bảng. Vui lòng thử lại!');
        } finally {
            if (!skipSpinner) {
                hideSpinner();
            }
        }
    }



    async function loadTableFromRecords(records) {
        try {
            showSpinner();
            const normalizedRecords = (records || []).map(normalizeRecord);
            const serials = Array.from(new Set(normalizedRecords.map(r => normalizeSn(r.sn)).filter(Boolean)));

            let locationMap = {};
            let testResultMap = {};
            if (serials.length) {
                try {
                    const [locRes, testResultRes] = await Promise.all([
                        axios.post(locationUrl, serials),
                        buildTestResultMap(serials)
                    ]);
                    locationMap = buildLocationMap(locRes?.data?.data);
                    testResultMap = testResultRes;
                } catch (err) {
                    console.error('Error fetching locations or test results for modal', err);
                }
            }

            normalizedRecords.forEach(row => {
                const info = locationMap[normalizeSn(row.sn)];
                row.location = info?.display || row.location || '';
                const testInfo = testResultMap[normalizeSn(row.sn)];
                if (testInfo) {
                    row.testResultSn = testInfo.testResultSn || '';
                    row.testResultGroup = testInfo.testResultGroup || '';
                    row.testResultData1 = testInfo.testResultData1 || '';
                    row.testResultData2 = testInfo.testResultData2 || '';
                    row.testResultPassDate = testInfo.testResultPassDate || '';
                }
            });

            if (modalTable) {
                modalTable.clear().rows.add(normalizedRecords).draw();
            } else {
                modalTable = $('#recordsTable').DataTable({
                    data: normalizedRecords,
                    scrollX: true,
                    searching: true,
                    ordering: false,
                    info: true,
                    columns: [
                        { data: 'sn' },
                        { data: 'productLine' },
                        { data: 'modelName' },
                        { data: 'moNumber' },
                        { data: 'wipGroupSFC' },
                        { data: 'wipGroupKANBAN' },
                        { data: 'errorFlag' },
                        { data: 'workFlag' },
                        { data: 'testGroup' },
                        { data: 'testTime' },
                        { data: 'testCode' },
                        { data: 'errorCodeItem' },
                        { data: 'errorDesc' },
                        { data: 'aging' },
                        { data: 'agingOld' },
                        { data: 'location' },
                        { data: 'testResultSn' },
                        { data: 'testResultGroup' },
                        { data: 'testResultData1' },
                        { data: 'testResultData2' },
                        { data: 'testResultPassDate' }
                    ],
                    columnDefs: [
                        {
                            targets: '_all',
                            width: '140px',
                            render: function (data, type) {
                                if (type === 'display') {
                                    return createTooltipCell(data ?? '');
                                }
                                if (type === 'filter') {
                                    return data ?? '';
                                }
                                return data;
                            }
                        }
                    ],
                    buttons: [
                        {
                            extend: 'excelHtml5',
                            text: '<img src="/assets/img/excel.png" class="excel-icon excel-button"/>',
                            title: '',
                            filename: function () {
                                const now = new Date();
                                const offset = 7 * 60;
                                const localDate = new Date(now.getTime() + offset * 60 * 1000);
                                const dateStr = localDate.toISOString().slice(0, 10).replace(/-/g, '');
                                const timeStr = localDate.toTimeString().slice(0, 8).replace(/:/g, '');
                                return `Bonepile_after_aging_${dateStr}_${timeStr}`;
                            },
                            exportOptions: { columns: ':visible' }
                        }
                    ],
                    destroy: true,
                    language: {
                        search: '',
                        emptyTable: 'Không có dữ liệu để hiển thị',
                        zeroRecords: 'Không tìm thấy bản ghi phù hợp'
                    }
                });
            }

            const modalEl = document.getElementById('recordsModal');
            const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
            modal.show();
        } finally {
            hideSpinner();
        }
    }
});
