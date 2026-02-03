document.addEventListener("DOMContentLoaded", async function () {
    // ========== GLOBAL VARIABLES ==========
    const apiBase = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Bonepile2";
    const apiDetailUrl = `${apiBase}/adapter-repair-records`;
    const apiAgingUrl = `${apiBase}/adapter-repair-aging-count`;
    const locationUrl = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Search/FindLocations';
    let agingData = [];
    let dataTable;
    let modalTable;
    let statusChart;
    let agingChart;
    const charts = []; // vẫn giữ để đăng ký chart (dùng cho việc destroy nếu cần)

    // ========== UTILITY FUNCTIONS ==========
    function normalizeSn(sn) {
        return (sn || "").toString().trim().toUpperCase();
    }

    function buildLocationMap(rawData) {
        if (!Array.isArray(rawData)) return {};
        return rawData.reduce((acc, item) => {
            const sn = normalizeSn(item?.serialNumber || item?.sn || item?.SERIAL_NUMBER);
            if (!sn) return acc;
            const warehouse = item?.warehouse || item?.Warehouse || "";
            const location = item?.location || item?.Location || "";
            const display = [warehouse, location].filter(Boolean).join(" - ");
            acc[sn] = { warehouse, location, display };
            return acc;
        }, {});
    }

    function registerChart(chart) {
        if (chart && !charts.includes(chart)) {
            charts.push(chart);
        }
        return chart || null;
    }

    function resizeCharts() {
        charts.forEach(chart => {
            if (chart) {
                chart.resize();
            }
        });
    }

    window.addEventListener('resize', resizeCharts);

    // ========== CONSTANTS ==========
    const validStatuses = [
        "ScrapLackTask", "ScrapHasTask", "WaitingApprovalScrap", "ApprovedBGA",
        "WaitingApprovalBGA", "ReworkFG", "RepairInRE", "WaitingCheckOut",
        "RepairInPD", "Can'tRepairProcess", "PendingInstructions"
    ];

    const statusColorMap = {
        "ScrapLackTask": "#ffc107",
        "ScrapHasTask": "#05b529",
        "ApprovedBGA": "#17b86d",
        "WaitingApprovalScrap": "#dc3545",
        "ReworkFG": "#6c757d",
        "RepairInRE": "#ff8307",
        "WaitingCheckOut": "#fe8307",
        "RepairInPD": "#17a2b8"
    };

    const agingColorMap = {
        "<45": "#28a745",
        "45-89": "#ffc107",
        ">=90": "#dc3545"
    };

    // ========== API FUNCTIONS ==========
    async function fetchOverview(statuses) {
        const params = {};
        if (statuses && statuses.length > 0 && statuses !== validStatuses) {
            params.statuses = statuses;
        }
        const response = await axios.get(apiDetailUrl, { params });
        return response.data;
    }

    // ========== MAIN DASHBOARD LOAD ==========
    async function loadDashboardData() {
        try {
            showSpinner();
            const [overview, agingRes] = await Promise.all([
                fetchOverview(),
                axios.get(apiAgingUrl)
            ]);

            const totalCount = Number(overview?.totalCount ?? 0);
            const statusCounts = overview?.statusCounts ?? [];
            agingData = agingRes.data?.agingCounts ?? [];

            // Update KPIs
            updateKPIs(statusCounts, totalCount);

            // Status Bar Chart
            createStatusBarChart(statusCounts);

            // Aging Pie Chart
            createAgingPieChart(agingRes.data?.agingCounts ?? []);

            // Load main table
            await loadTableData(validStatuses, overview);

        } catch (error) {
            console.error("Lỗi khi tải dashboard:", error);
            alert("Không thể tải dữ liệu dashboard. Vui lòng thử lại!");
        } finally {
            hideSpinner();
        }
    }

    function updateKPIs(statusCounts, totalCount) {
        document.getElementById("totalCount").innerText = totalCount || 0;
        document.getElementById("noTaskscrapCount").innerText = statusCounts.find(s => s.status === "ScrapLackTask")?.count || 0;
        document.getElementById("scrapCount").innerText = statusCounts.find(s => s.status === "ScrapHasTask")?.count || 0;
        document.getElementById("waitingScrapCount").innerText = statusCounts.find(s => s.status === "WaitingApprovalScrap")?.count || 0;
        document.getElementById("reworkFGCount").innerText = statusCounts.find(s => s.status === "ReworkFG")?.count || 0;
        document.getElementById("waitingCheckOutCount").innerText = statusCounts.find(s => s.status === "WaitingCheckOut")?.count || 0;
        document.getElementById("underRepairRECount").innerText = statusCounts.find(s => s.status === "RepairInRE")?.count || 0;
        document.getElementById("underRepairPDCount").innerText = statusCounts.find(s => s.status === "RepairInPD")?.count || 0;
        document.getElementById("approvedBGACount").innerText = statusCounts.find(s => s.status === "ApprovedBGA")?.count || 0;
        document.getElementById("notRepairProcessCount").innerText = statusCounts.find(s => s.status === "Can'tRepairProcess")?.count || 0;
        document.getElementById("pedingInstructionCount").innerText = statusCounts.find(s => s.status === "PendingInstructions")?.count || 0;
    }

    function createStatusBarChart(statusCounts) {
        const sortedStatusCounts = [...statusCounts].sort((a, b) => b.count - a.count);
        const labels = sortedStatusCounts.map(s => s.status);
        const values = sortedStatusCounts.map(s => s.count);
        const total = values.reduce((sum, v) => sum + v, 0);

        const colorMap = {
            "ScrapLackTask": "#FACC15",
            "ScrapHasTask": "#22D3EE",
            "ApprovedBGA": "#17B86D",
            "WaitingApprovalScrap": "#F87171",
            "ReworkFG": "#94A3B8",
            "RepairInRE": "#FB923C",
            "WaitingCheckOut": "#FB923C",
            "RepairInPD": "#38BDF8",
            "Can'tRepairProcess": "#9CA3AF",
            "PendingInstructions": "#1CE3A3"
        };
        const colors = labels.map(l => colorMap[l] || "#93C5FD");

        const chartDom = document.getElementById("statusBarChart");
        statusChart = registerChart(echarts.init(chartDom));

        const option = {
            grid: { top: 30, left: 40, right: 20, bottom: 55 },
            tooltip: {
                trigger: 'axis',
                axisPointer: { type: 'shadow' },
                formatter: (params) => {
                    const p = params[0];
                    const pct = total > 0 ? ((p.value / total) * 100).toFixed(1) : 0;
                    return `${p.name}<br/>${p.value} (${pct}%)`;
                }
            },
            xAxis: {
                type: 'category',
                data: labels,
                axisLabel: { color: '#1E2A45', rotate: 30, fontSize: 10, fontWeight: '500' },
                axisTick: { show: false },
                axisLine: { lineStyle: { color: '#CBD5E1' } }
            },
            yAxis: {
                type: 'value',
                axisLabel: { color: '#1E2A45', fontSize: 10 },
                splitLine: { lineStyle: { color: 'rgba(0,0,0,0.1)' } }
            },
            series: [{
                type: 'bar',
                data: values,
                barWidth: '55%',
                itemStyle: {
                    borderRadius: [4, 4, 0, 0],
                    color: (params) => {
                        const c = colors[params.dataIndex];
                        return new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                            { offset: 0, color: c },
                            { offset: 1, color: '#cbd5e1' }
                        ]);
                    },
                    shadowBlur: 8,
                    shadowColor: 'rgba(0,0,0,0.2)'
                },
                label: {
                    show: true,
                    position: 'top',
                    color: '#0046b8',
                    fontSize: 10,
                    formatter: (p) => {
                        const pct = total > 0 ? ((p.value / total) * 100).toFixed(1) : 0;
                        return `${pct}%`;
                    }
                }
            }]
        };
        statusChart.setOption(option);
    }

    function createAgingPieChart(agingDataList) {
        if (!Array.isArray(agingDataList) || agingDataList.length === 0) return;

        const total = agingDataList.reduce((sum, a) => sum + a.count, 0);
        const data = agingDataList.map(a => ({
            name: a.ageRange,
            value: a.count,
            percent: total > 0 ? ((a.count / total) * 100).toFixed(1) : 0
        }));

        const colorMap = {
            "<45": "#22D3EE",
            "45-89": "#FACC15",
            ">=90": "#F87171"
        };
        const colors = data.map(d => colorMap[d.name] || "#93C5FD");

        const chartDom = document.getElementById("agingPieChart");
        agingChart = registerChart(echarts.init(chartDom));

        const option = {
            tooltip: {
                trigger: "item",
                formatter: (p) => `${p.name}: ${p.value} (${p.percent}%)`
            },
            series: [{
                name: "AGING",
                type: "pie",
                radius: ["30%", "80%"],
                center: ["50%", "55%"],
                avoidLabelOverlap: true,
                itemStyle: {
                    borderRadius: 0,
                    borderColor: "#fff",
                    borderWidth: 1
                },
                label: {
                    show: true,
                    position: "outside",
                    formatter: (p) => `${p.name}\n${p.percent}%`,
                    color: "#1e2a45",
                    fontWeight: "600",
                    fontSize: 10,
                    lineHeight: 18
                },
                labelLine: {
                    show: true,
                    length: 20,
                    length2: 20,
                    smooth: true,
                    lineStyle: { color: "#888", width: 1.5 }
                },
                emphasis: {
                    scale: true,
                    scaleSize: 10,
                    itemStyle: { shadowBlur: 15, shadowColor: "rgba(0,0,0,0.25)" }
                },
                color: colors,
                data: data
            }]
        };
        agingChart.setOption(option);

        // Click để mở modal chi tiết aging
        agingChart.on('click', function (params) {
            const label = params.name;
            const records = agingData.find(a => a.ageRange === label)?.records || [];
            loadTableFromRecords(records);
        });
    }

    // ========== TABLE FUNCTIONS ==========
    async function loadTableData(statuses, overviewResponse) {
        try {
            showSpinner();
            let response = overviewResponse || await fetchOverview(statuses);
            const tableData = response?.data || [];

            // Fetch location
            const serials = Array.from(new Set(tableData.map(r => normalizeSn(r.sn)).filter(Boolean)));
            let locationMap = {};
            if (serials.length > 0) {
                try {
                    const locRes = await axios.post(locationUrl, serials);
                    locationMap = buildLocationMap(locRes.data?.data);
                } catch (err) {
                    console.error('Error fetching locations', err);
                }
            }

            tableData.forEach(r => {
                const info = locationMap[normalizeSn(r.sn)];
                r.location = info?.display || '';
            });

            if (dataTable) {
                dataTable.clear().rows.add(tableData).draw();
            } else {
                initMainDataTable(tableData);
            }
        } catch (error) {
            console.error("Lỗi khi tải dữ liệu bảng:", error);
            alert("Không thể tải dữ liệu bảng. Vui lòng thử lại!");
        } finally {
            hideSpinner();
        }
    }

    function initMainDataTable(tableData) {
        dataTable = $('#sumMaterialsTable').DataTable({
            data: tableData,
            scrollX: true,
            fixedColumns: {
                leftColumns: 1   // freeze cột đầu tiên
            },
            columns: [
                { data: "sn" }, { data: "productLine" }, { data: "modelName" },
                { data: "moNumber" }, { data: "wipGroup" }, { data: "testGroup" },
                { data: "testCode" }, { data: "errorCodeItem" }, { data: "testTime" },
                { data: "errorDesc" }, { data: "workFlag" }, { data: "errorFlag" },
                { data: "checkInDate" }, { data: "agingDay" }, { data: "location" },
                { data: "status" }, { data: "repair" }, { data: "groupTestOff" },
                { data: "testResultOff" }, { data: "detailTestOff" }, { data: "timeTestOff" }
            ],
            dom: '<"top d-flex align-items-center"flB>rt<"bottom"ip>',
            info: false,
            buttons: [{
                extend: 'excelHtml5',
                text: '<img src="/assets/img/excel.png" class="excel-icon excel-button"/>',
                title: '',
                filename: getExportFilename('before'),
                exportOptions: {
                    columns: ':visible',
                    modifier: { selected: null },
                    format: { header: (data) => data.trim() }
                }
            }],
            destroy: true,
            language: {
                search: "",
                emptyTable: "Không có dữ liệu để hiển thị",
                zeroRecords: "Không tìm thấy bản ghi phù hợp"
            },
            initComplete: function () {
                addStatusFilter(this.api());
            }
        });
    }
    //JS để click vào row thì giữ highlight
    $('#sumMaterialsTable tbody').on('click', 'tr', function () {
        // Bỏ chọn các row khác
        $('#sumMaterialsTable tbody tr').removeClass('selected-row');

        // Gán class highlight
        $(this).addClass('selected-row');
    });


    function addStatusFilter(api) {
        const selectHtml = `
            <div class="form-group mb-0" style="min-width: 200px;">
                <select id="statusFilterDt" class="form-control">
                    <option value="">Tất cả</option>
                    ${validStatuses.map(status => `<option value="${status}">${getStatusLabel(status)}</option>`).join('')}
                </select>
            </div>
        `;
        $('.dataTables_wrapper .top').prepend(selectHtml);

        $('#statusFilterDt').on('change', async function () {
            const selectedStatus = this.value;
            const statuses = selectedStatus ? [selectedStatus] : validStatuses;
            await loadTableData(statuses);
        });

        $('.dataTables_filter input[type="search"]').attr('placeholder', 'Tìm kiếm');
    }

    function getStatusLabel(status) {
        const labels = {
            "ScrapHasTask": "Scrap Has Task",
            "ScrapLackTask": "Scrap Lack Task",
            "WaitingApprovalScrap": "Pending Scrap SPE Approval",
            "ApprovedBGA": "SPE Approved BGA",
            "WaitingApprovalBGA": "Pending BGA SPE Approval",
            "ReworkFG": "Rework FG",
            "RepairInRE": "Repair In RE",
            "WaitingCheckOut": "Waiting Check Out",
            "RepairInPD": "Repair In PD",
            "Can'tRepairProcess": "Can't Repair Process"
        };
        return labels[status] || status;
    }

    async function loadTableFromRecords(records) {
        try {
            showSpinner();
            const serials = Array.from(new Set(records.map(r => normalizeSn(r.sn)).filter(Boolean)));
            let locationMap = {};
            if (serials.length > 0) {
                try {
                    const locRes = await axios.post(locationUrl, serials);
                    locationMap = buildLocationMap(locRes.data?.data);
                } catch (err) {
                    console.error('Error fetching locations for modal', err);
                }
            }

            records.forEach(r => {
                const info = locationMap[normalizeSn(r.sn)];
                r.location = info?.display || '';
            });

            if (modalTable) {
                modalTable.clear().rows.add(records).draw();
            } else {
                modalTable = $('#recordsTable').DataTable({
                    data: records,
                    scrollX: true,
                    columns: [
                        { data: "sn" }, { data: "productLine" }, { data: "modelName" },
                        { data: "moNumber" }, { data: "wipGroup" }, { data: "testGroup" },
                        { data: "testCode" }, { data: "errorCodeItem" }, { data: "testTime" },
                        { data: "errorDesc" }, { data: "workFlag" }, { data: "errorFlag" },
                        { data: "checkInDate" }, { data: "agingDay" }, { data: "location" },
                        { data: "status" }, { data: "repair" }, { data: "groupTestOff" },
                        { data: "testResultOff" }, { data: "detailTestOff" }, { data: "timeTestOff" }
                    ],
                    buttons: [{
                        extend: 'excelHtml5',
                        text: '<img src="/assets/img/excel.png" class="excel-icon excel-button"/>',
                        title: '',
                        filename: getExportFilename('after_aging'),
                        exportOptions: { columns: ':visible' }
                    }],
                    destroy: true,
                    language: {
                        search: "",
                        emptyTable: "Không có dữ liệu để hiển thị",
                        zeroRecords: "Không tìm thấy bản ghi phù hợp"
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

    function getExportFilename(type) {
        const now = new Date();
        const offset = 7 * 60; // UTC+7
        const localDate = new Date(now.getTime() + offset * 60 * 1000);
        const dateStr = localDate.toISOString().slice(0, 10).replace(/-/g, '');
        const timeStr = localDate.toTimeString().slice(0, 8).replace(/:/g, '');
        return `Bonepile_${type}_${dateStr}_${timeStr}`;
    }

    // ========== INIT ==========
    await loadDashboardData();
});