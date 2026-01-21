const apiBase = `https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Bonepile2`;
let chartInstance = null;
let cachedData = [];
let modalTable = null;
let allTable = null;
const sidebar = document.getElementById('sidebar');
const mainContent = document.getElementById('mainContent');
const header = document.querySelector('.header');
const sidebarFixedToggle = document.getElementById('toggleSidebarFixed');
const dashboardWrapper = document.querySelector('.dashboard-wrapper');
let layoutRefreshTimeout = null;

/* ---------------------- UTILITIES ---------------------- */

const formatDateTime = (value) => {
    if (!value) return '';
    const date = new Date(value);
    return isNaN(date.getTime()) ? '' : date.toLocaleString();
};

const scheduleLayoutRefresh = (delay = 0) => {
    if (layoutRefreshTimeout) {
        clearTimeout(layoutRefreshTimeout);
    }
    layoutRefreshTimeout = setTimeout(() => {
        layoutRefreshTimeout = null;
        requestAnimationFrame(applyLayoutRefresh);
    }, delay);
};

const applyLayoutRefresh = () => {
    if (chartInstance) {
        chartInstance.resize();
    }
    if (allTable) {
        allTable.columns.adjust();
    }
    if (modalTable) {
        modalTable.columns.adjust();
    }
};

window.addEventListener('resize', () => scheduleLayoutRefresh());
if (window.visualViewport) {
    window.visualViewport.addEventListener('resize', () => scheduleLayoutRefresh());
}
if (sidebar) {
    sidebar.addEventListener('transitionend', () => scheduleLayoutRefresh());
}
if (mainContent) {
    mainContent.addEventListener('transitionend', () => scheduleLayoutRefresh());
}
if (header) {
    header.addEventListener('transitionend', () => scheduleLayoutRefresh());
}
if (sidebarFixedToggle) {
    sidebarFixedToggle.addEventListener('change', () => scheduleLayoutRefresh(200));
}
if (window.ResizeObserver && dashboardWrapper) {
    const resizeObserver = new ResizeObserver(() => scheduleLayoutRefresh());
    resizeObserver.observe(dashboardWrapper);
}

/* ---------------------- MAPPING ---------------------- */

const mapBefore = (detail, productLine) => ({
    type: 'Before',
    productLine: productLine || '',
    serialNumber: detail.serialNumber || '',
    fg: detail.fg || '',
    modelName: detail.modelName || '',
    moNumber: detail.moNumber || '',
    wipGroup: detail.wipGroup || '',
    wipGroupKanban: detail.wipGroupKanban || '',
    testGroup: detail.testGroup || '',
    testTime: detail.testTime,
    testCode: detail.testCode || '',
    errorItem: detail.errorItemCode || '',
    errorDesc: detail.errorDesc || '',
    repair: detail.repair || '',
    aging: detail.aging || detail.agingOldest || ''
});

const mapAfter = (detail, productLine) => ({
    type: 'After',
    productLine: productLine || '',
    serialNumber: detail.serialNumber || '',
    fg: detail.fg || '',
    modelName: detail.modelName || '',
    moNumber: detail.moNumber || '',
    wipGroup: detail.wipGroup || '',
    wipGroupKanban: detail.wipGroupKanban || '',
    testGroup: detail.testGroup || '',
    testTime: detail.testTime,
    testCode: detail.testCode || '',
    errorItem: detail.errorItemCode || '',
    errorDesc: detail.errorDesc || '',
    repair: detail.repair || '',
    aging: detail.aging || detail.agingOldest || ''
});

/* ---------------------- FLATTEN ---------------------- */

const flattenAllRows = () => {
    if (!cachedData.length) return [];

    return cachedData.flatMap((pl) => {
        const beforeRows = (pl.beforeDetails || []).map((d) =>
            mapBefore(d, pl.productLine)
        );

        const afterRows = (pl.afterDetails || []).map((d) =>
            mapAfter(d, pl.productLine)
        );

        return [...beforeRows, ...afterRows];
    });
};

/* ---------------------- RENDER ALL TABLE ---------------------- */

const renderAllTable = () => {
    const rows = flattenAllRows();

    if (allTable) {
        allTable.clear();
        allTable.rows.add(rows);
        allTable.draw();
    } else {
        allTable = $('#sumMaterialsTable').DataTable({
            data: rows,
            columns: [
                { data: 'type' },
                { data: 'productLine' },
                { data: 'serialNumber' },
                { data: 'fg' },
                { data: 'modelName' },
                { data: 'moNumber' },
                { data: 'wipGroup' },
                { data: 'wipGroupKanban' },
                { data: 'testGroup' },
                {
                    data: 'testTime',
                    render: (data) => formatDateTime(data)
                },
                { data: 'testCode' },
                { data: 'errorItem' },
                { data: 'errorDesc' },
                { data: 'repair' },
                { data: 'aging' }
            ],
            pageLength: 25,
            dom: 'B<"top d-flex align-items-center gap-2"f>rt<"bottom d-flex justify-content-between"ip>',
            buttons: [
                {
                    extend: 'excelHtml5',
                    title: 'Bonepile_NPI_All',
                    className: 'd-none',
                    exportOptions: {
                        columns: ':visible'
                    }
                }
            ]
        });
        //JS để click vào row thì giữ highlight
        $('#sumMaterialsTable tbody').on('click', 'tr', function () {
            // Bỏ chọn các row khác
            $('#sumMaterialsTable tbody tr').removeClass('selected-row');

            // Gán class highlight
            $(this).addClass('selected-row');
        });
    }

    scheduleLayoutRefresh();
};

/* ---------------------- RENDER CHART ---------------------- */

/* ---------------------- RENDER CHART (ECHARTS) ---------------------- */

const renderChart = () => {
    if (!cachedData.length) {
        if (chartInstance) chartInstance.dispose();
        return;
    }

    const labels = cachedData.map((d) => d.productLine || '');
    const beforeCounts = cachedData.map((d) => d.beforeCount || 0);
    const afterCounts = cachedData.map((d) => d.afterCount || 0);

    const chartDom = document.getElementById('bonpileChart');

    if (chartInstance) {
        chartInstance.dispose();
    }

    chartInstance = echarts.init(chartDom);

    const option = {
        tooltip: {
            trigger: 'axis',
            axisPointer: { type: 'shadow' },
            formatter: (params) => {
                let html = `${params[0].name}<br>`;
                params.forEach(p => {
                    html += `${p.marker} ${p.seriesName}: ${p.value}<br>`;
                });
                return html;
            }
        },
        legend: {
            data: ['Before', 'After'],
            top: 0
        },
        grid: {
            left: 40,
            right: 20,
            top: 40,
            bottom: 60
        },
        xAxis: {
            type: 'category',
            data: labels,
            axisLabel: {
                rotate: 30,
                fontSize: 11
            }
        },
        yAxis: {
            type: 'value',
            minInterval: 1
        },
        series: [
            {
                name: 'Before',
                type: 'bar',
                data: beforeCounts,
                barWidth: '35%',
                label: {
                    show: true,
                    position: 'top',
                    color: '#0046b8',
                    fontWeight: '600',
                    fontSize: 11,
                    formatter: '{c}'
                },
                itemStyle: {
                    color: '#4C9FFF'
                }
            },
            {
                name: 'After',
                type: 'bar',
                data: afterCounts,
                barWidth: '35%',
                label: {
                    show: true,
                    position: 'top',
                    color: '#b80032',
                    fontWeight: '600',
                    fontSize: 11,
                    formatter: '{c}'
                },
                itemStyle: {
                    color: '#FF6B81'
                }
            }
        ]
    };

    chartInstance.setOption(option);

    // CLICK → mở modal Product Line
    chartInstance.on('click', function (event) {
        const index = event.dataIndex;
        const productLine = labels[index];
        if (productLine) openProductLineModal(productLine);
    });
};


/* ---------------------- MODAL PRODUCT LINE ---------------------- */

const openProductLineModal = (productLine) => {
    const plData = cachedData.find(
        (x) => (x.productLine || '').toLowerCase() === productLine.toLowerCase()
    );

    if (!plData) return;

    $('#modalProductLine').text(`Product Line: ${productLine}`);

    const rows = [
        ...(plData.beforeDetails || []).map((d) => mapBefore(d, productLine)),
        ...(plData.afterDetails || []).map((d) => mapAfter(d, productLine))
    ];

    if (modalTable) {
        modalTable.clear();
        modalTable.rows.add(rows);
        modalTable.draw();
    } else {
        modalTable = $('#productLineTable').DataTable({
            data: rows,
            columns: [
                { data: 'type' },
                { data: 'productLine' },
                { data: 'serialNumber' },
                { data: 'fg' },
                { data: 'modelName' },
                { data: 'moNumber' },
                { data: 'wipGroup' },
                { data: 'wipGroupKanban' },
                { data: 'testGroup' },
                { data: 'testTime', render: (data) => formatDateTime(data) },
                { data: 'testCode' },
                { data: 'errorItem' },
                { data: 'errorDesc' },
                { data: 'repair' },
                { data: 'aging' }
            ],
            pageLength: 15,
            dom: 'B<"top d-flex align-items-center gap-2"f>rt<"bottom d-flex justify-content-between"ip>',
            info: false,
            buttons: [
                {
                    extend: 'excelHtml5',
                    title: `Bonepile_NPI_${productLine}`,
                    className: 'd-none',
                    exportOptions: {
                        columns: ':visible'
                    }
                }
            ]
        });
    }

    scheduleLayoutRefresh();

    $('#productLineModal').modal('show');
};

/* ---------------------- EXPORT ---------------------- */

const exportAllTable = () => {
    if (allTable) allTable.button('.buttons-excel').trigger();
};

const exportModalTable = () => {
    if (modalTable) modalTable.button('.buttons-excel').trigger();
};

/* ---------------------- TOTALS ---------------------- */

const renderTotals = (before, after) => {
    $('#totalBefore').text(before);
    $('#totalAfter').text(after);
};

/* ---------------------- FETCH API ---------------------- */

const fetchBonepileNpi = async () => {
    try {
        showSpinner();
        const response = await axios.get(`${apiBase}/bonepile-npi`);
        const payload = response.data || {};

        cachedData = payload.data || [];

        renderTotals(payload.totalBefore || 0, payload.totalAfter || 0);
        renderChart();
        renderAllTable();

        scheduleLayoutRefresh();

    } catch (err) {
        console.error('Không thể tải Bonepile NPI', err);
        alert('Không thể tải dữ liệu Bonepile NPI');
    } finally {
        hideSpinner();
    }
};

/* ---------------------- ON PAGE LOAD ---------------------- */

$(document).ready(() => {
    fetchBonepileNpi();
    $('#exportAll').on('click', exportAllTable);
    $('#exportModal').on('click', exportModalTable);
});
