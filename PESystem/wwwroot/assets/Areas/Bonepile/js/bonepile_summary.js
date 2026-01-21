// Bonepile Summary dashboard aligned with the Bonepile After layout
// and upgraded to use ECharts for visualizations.

document.addEventListener('DOMContentLoaded', async function () {
    const apiBase = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Bonepile2';
    const beforeDetailUrl = `${apiBase}/adapter-repair-records`;
    const afterDetailUrl = `${apiBase}/bonepile-after-kanban-basic`;
    const locationUrl = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Search/FindLocations';

    const beforeStatuses = [
        'ScrapHasTask',
        'ScrapLackTask',
        'WaitingApprovalScrap',
        'WaitingApprovalBGA',
        'ApprovedBGA',
        'ReworkFG',
        'WaitingCheckOut',
        'RepairInRE',
        'RepairInPD',
        "Can'tRepairProcess",
        'B36V',
        'WaitingLink',
        'Linked'
    ];

    const statusAliases = {
        CantRepairProcess: "Can'tRepairProcess",
        "Can't Repair Process": "Can'tRepairProcess",
        'Scrap Lack Task': 'ScrapLackTask',
        'Scrap Has Task': 'ScrapHasTask'
    };

    const statusColorMap = {
        ScrapHasTask: '#05b529',
        ScrapLackTask: '#ffc107',
        WaitingApprovalScrap: '#dc3545',
        WaitingApprovalBGA: '#17a2b8',
        ApprovedBGA: '#17b86d',
        ReworkFG: '#6c757d',
        WaitingCheckOut: '#fe8307',
        RepairInRE: '#ff8307',
        RepairInPD: '#ffba69',
        WaitingLink: '#17a2b8',
        Linked: '#28a745',
        B36V: '#28a745',
        "Can'tRepairProcess": '#ffc107'
    };

    const statusDisplayMap = {
        ScrapHasTask: 'Scrap Has Task',
        ScrapLackTask: 'Scrap Lacks Task',
        WaitingApprovalScrap: 'Pending Scrap SPE Approval',
        WaitingApprovalBGA: 'Pending BGA SPE Approval',
        ApprovedBGA: 'Approved BGA',
        ReworkFG: 'Rework FG',
        WaitingCheckOut: 'Waiting Check Out',
        RepairInRE: 'Repair In RE',
        RepairInPD: 'Repair In PD',
        WaitingLink: 'Waiting Link',
        Linked: 'Linked',
        B36V: 'B36V',
        "Can'tRepairProcess": "Can't Repair Process"
    };

    const kpiValueMap = {};
    document.querySelectorAll('.donut-box[data-status]').forEach(box => {
        const status = box.dataset.status;
        const valueEl = box.querySelector('.donut-percent');
        if (status && valueEl) {
            kpiValueMap[status] = valueEl;
        }
    });

    const statusChart = initChart('statusBarChart');
    const charts = statusChart ? [statusChart] : [];

    let dataTable = null;

    function normalizeSn(sn) {
        return (sn || '').toString().trim().toUpperCase();
    }

    function buildLocationMap(rawData) {
        if (!Array.isArray(rawData)) return {};
        return rawData.reduce((acc, item) => {
            const sn = normalizeSn(item?.serialNumber || item?.sn || item?.SERIAL_NUMBER);
            if (!sn) return acc;
            const warehouse = item?.warehouse || item?.Warehouse || '';
            const location = item?.location || item?.Location || '';
            const display = [warehouse, location].filter(Boolean).join(' - ');
            acc[sn] = { warehouse, location, display };
            return acc;
        }, {});
    }

    function normalizeStatus(status) {
        if (status === null || status === undefined) return 'Unknown';
        const raw = status.toString().trim();
        if (!raw) return 'Unknown';
        const alias = statusAliases[raw] || raw;
        return alias;
    }

    function uniq(arr) {
        return Array.from(new Set(arr));
    }

    function nowLocalStringTZ7() {
        const now = new Date();
        const offset = 7 * 60;
        const localDate = new Date(now.getTime() + offset * 60 * 1000);
        const dateStr = localDate.toISOString().slice(0, 10).replace(/-/g, '');
        const timeStr = localDate.toTimeString().slice(0, 8).replace(/:/g, '');
        return `${dateStr}_${timeStr}`;
    }

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

    function updateKpis(statusCountsMap, total) {
        const totalEl = document.getElementById('totalCount');
        if (totalEl) {
            totalEl.textContent = total;
        }
        Object.entries(kpiValueMap).forEach(([status, element]) => {
            const value = statusCountsMap[status] || 0;
            element.textContent = value;
        });
    }

    function renderBarChart(statusCounts, total) {
        if (!statusChart) return;

        const sortedStatusCounts = [...statusCounts].sort((a, b) => b.count - a.count);
        const categories = sortedStatusCounts.map(item => statusDisplayMap[item.status] || item.status);
        const seriesData = sortedStatusCounts.map(item => ({
            value: item.count,
            status: item.status,
            percent: total > 0 ? (item.count / total * 100) : 0,
            itemStyle: {
                color: statusColorMap[item.status] || '#5470c6'
            }
        }));

        statusChart.setOption({
            tooltip: {
                trigger: 'axis',
                axisPointer: { type: 'shadow' },
                formatter: params => {
                    if (!Array.isArray(params) || !params.length) return '';
                    const data = params[0].data;
                    const label = params[0].axisValueLabel;
                    const percentText = data.percent ? ` (${data.percent.toFixed(1)}%)` : ' (0%)';
                    return `${label}: ${data.value}${percentText}`;
                }
            },
            grid: { left: '3%', right: '4%', bottom: '8%', containLabel: true },
            xAxis: {
                type: 'category',
                data: categories,
                axisLabel: {
                    interval: 0,
                    rotate: categories.length > 6 ? 25 : 0
                }
            },
            yAxis: {
                type: 'value',
                minInterval: 1
            },
            series: [{
                type: 'bar',
                barMaxWidth: 36,
                label: {
                    show: true,
                    position: 'top',
                    formatter: params => {
                        const { percent } = params.data;
                        return `${percent ? percent.toFixed(1) : 0}%`;
                    },
                    fontWeight: 'bold'
                },
                data: seriesData
            }]
        });
    }

    async function fetchBeforeOverview(statuses = beforeStatuses) {
        const params = {};
        if (Array.isArray(statuses) && statuses.length > 0) {
            params.statuses = statuses;
        }
        const response = await axios.get(beforeDetailUrl, { params });
        return response.data;
    }

    async function loadDashboardData() {
        try {
            showSpinner();
            const [beforeOverview, afterBasicRes] = await Promise.all([
                fetchBeforeOverview(),
                axios.get(afterDetailUrl)
            ]);

            const beforeTotal = Number(beforeOverview?.totalCount || 0);
            const afterBasic = afterBasicRes.data?.data || [];
            const afterTotal = afterBasic.length;

            const statusCountsMap = {};

            (beforeOverview?.statusCounts || []).forEach(s => {
                const key = normalizeStatus(s.status ?? s.Status);
                const count = Number(s.count ?? s.Count ?? 0);
                if (!key) return;
                statusCountsMap[key] = (statusCountsMap[key] || 0) + count;
            });

            afterBasic.forEach(a => {
                const key = normalizeStatus(a.status || a.Status || '');
                if (!key) return;
                statusCountsMap[key] = (statusCountsMap[key] || 0) + 1;
            });

            const statusCounts = Object.keys(statusCountsMap).map(k => ({ status: k, count: statusCountsMap[k] }));
            const total = beforeTotal + afterTotal;

            updateKpis(statusCountsMap, total);
            renderBarChart(statusCounts, total);

            await loadTableData(afterBasic, beforeOverview);
        } catch (error) {
            console.error('Error loading dashboard', error);
            alert('Không thể tải dữ liệu dashboard.');
        } finally {
            hideSpinner();
        }
    }

    async function loadTableData(afterBasicData, beforeOverview) {
        try {
            showSpinner();

            let beforeResponse = beforeOverview;
            if (!beforeResponse) {
                beforeResponse = await fetchBeforeOverview();
            }

            const beforeData = beforeResponse?.data || [];
            let afterBasic = afterBasicData ? [...afterBasicData] : [];

            if (!afterBasicData) {
                const afterRes = await axios.get(afterDetailUrl);
                afterBasic = afterRes.data?.data || [];
            }

            const afterSNs = uniq(afterBasic.map(a => normalizeSn(a.sn || a.SN)).filter(Boolean));
            const beforeSNs = uniq(beforeData.map(b => normalizeSn(b.sn || b.SN)).filter(Boolean));
            const locationRequestSNs = uniq(afterSNs.concat(beforeSNs));

            let locationMap = {};
            try {
                const locRes = await axios.post(locationUrl, locationRequestSNs);
                locationMap = buildLocationMap(locRes.data?.data);
            } catch (locError) {
                console.warn('Location fetch failed:', locError);
            }

            const mappedBefore = beforeData.map(b => ({
                type: 'Before',
                sn: b.sn || b.SN,
                productLine: b.productLine || b.ProductLine,
                modelName: b.modelName || b.ModelName,
                moNumber: b.moNumber || b.MoNumber,
                wipGroup: b.wipGroup || b.WipGroup,
                wipGroupKANBAN: '',
                testGroup: b.testGroup || b.TestGroup || '',
                testCode: b.testCode || b.TestCode || '',
                errorCodeItem: b.errorCodeItem || '',
                testTime: b.testTime || b.TestTime || '',
                errorDesc: b.errorDesc || b.ErrorDesc || '',
                status: normalizeStatus(b.status || b.Status || ''),
                statusV2: '',
                aging: b.agingDay || b.AgingDay || '',
                location: locationMap[normalizeSn(b.sn || b.SN)]?.display || '',
                repair: b.repair || b.Repair || ''
            }));

            const mappedAfter = afterBasic.map(a => {
                const sn = a.sn || a.SN;
                const normalizedSn = normalizeSn(sn);
                return {
                    type: 'After',
                    sn,
                    productLine: a.productLine || a.ProductLine,
                    modelName: a.modelName || a.ModelName,
                    moNumber: a.moNumber || a.MoNumber,
                    wipGroup: a.wipGroupSFC || a.WipGroupSFC || '',
                    wipGroupKANBAN: a.wipGroupKANBAN || a.WipGroupKANBAN || '',
                    testGroup: a.testGroup || a.TestGroup || '',
                    testCode: a.testCode || a.TestCode || '',
                    errorCodeItem: a.errorCodeItem || a.ErrorCodeItem || '',
                    testTime: a.testTime || a.TestTime || '',
                    errorDesc: a.errorDesc || a.ErrorDesc || '',
                    status: normalizeStatus(a.status || a.Status || ''),
                    statusV2: a.statusV2 || a.StatusV2 || '',
                    aging: a.aging ?? a.Aging ?? '',
                    location: locationMap[normalizedSn]?.display || '',
                    repair: ''
                };
            });

            const combined = mappedBefore.concat(mappedAfter);

            if (dataTable) {
                dataTable.clear().rows.add(combined).draw();
            } else {
                dataTable = $('#sumMaterialsTable').DataTable({
                    data: combined,
                    scrollX: true,
                    columns: [
                        { data: 'type' },
                        { data: 'sn' },
                        { data: 'productLine' },
                        { data: 'modelName' },
                        { data: 'moNumber' },
                        { data: 'wipGroup' },
                        { data: 'wipGroupKANBAN' },
                        { data: 'testGroup' },
                        { data: 'testCode' },
                        { data: 'errorCodeItem' },
                        { data: 'testTime' },
                        { data: 'errorDesc' },
                        { data: 'status' },
                        { data: 'statusV2' },
                        { data: 'aging' },
                        { data: 'location' },
                        { data: 'repair' }
                    ],
                    dom: '<"top d-flex align-items-center"flB>rt<"bottom"ip>',
                    buttons: [{
                        extend: 'excelHtml5',
                        text: '<img src="/assets/img/excel.png" class="excel-icon excel-button"/>',
                        title: '',
                        filename: () => `Bonepile_summary_${nowLocalStringTZ7()}`,
                        exportOptions: {
                            columns: ':visible',
                            modifier: { selected: null },
                            format: { header: d => (d || '').trim() }
                        }
                    }],
                    destroy: true,
                    language: {
                        search: '',
                        emptyTable: 'Không có dữ liệu để hiển thị',
                        zeroRecords: 'Không tìm thấy bản ghi phù hợp'
                    },
                    initComplete: function () {
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
            console.error('Error loading table', error);
            alert('Không thể tải dữ liệu bảng.');
        } finally {
            hideSpinner();
        }
    }

    await loadDashboardData();
});
