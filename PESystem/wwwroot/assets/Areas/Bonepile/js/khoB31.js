document.addEventListener("DOMContentLoaded", async function () {
    const apiBase = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Bonepile2";
    const apiCountUrl = `${apiBase}/adapter-mo-status-count`;
    const apiDetailUrl = `${apiBase}/adapter-mo-records`;
    const locationUrl = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Search/FindLocations';

    // Định nghĩa tất cả trạng thái hợp lệ
    const validStatuses = [
        "ScrapLacksTask",
        "ScrapHasScrap",
        "ApprovedBGA",
        "WaitingApproveScrap",
        "WAITING_LINK",
        "LINKED",
        "WaitingApprovalBGA"
    ];

    const statusColorMap = {
        "ScrapLacksTask": "#ffc107",
        "ScrapHasScrap": "#05b529",
        "ApprovedBGA": "#17b86d",
        "WaitingApproveScrap": "#dc3545",
        "WAITING_LINK": "#6c757d",
        "LINKED": "#ff8307",
        "WaitingApprovalBGA": "#1f8ef1"
    };

    const statusDisplayMap = {
        "ScrapLacksTask": "Scrap Lacks Task",
        "ScrapHasScrap": "Scrap Has Task",
        "ApprovedBGA": "Approved BGA",
        "WaitingApproveScrap": "Waiting Approve Scrap",
        "WAITING_LINK": "Waiting Link",
        "LINKED": "Linked",
        "WaitingApprovalBGA": "Waiting Approval BGA"
    };

    const fallbackColors = ["#ff8307", "#05b529", "#ffc107", "#75b507", "#17a2b8", "#17b86d", "#dc3545", "#6c757d"];

    let dataTable;
    let modalTable;
    const charts = [];
    let statusChart = registerChart(initChart('statusDonutChart'));

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

    // Load KPI + Donut chart
    async function loadDashboardData() {
        try {
            showSpinner();
            const res = await axios.get(apiCountUrl);
            const { totalCount, statusCounts } = res.data;

            // Gán KPI
            document.getElementById("totalCount").innerText = totalCount || 0;
            document.getElementById("noTaskscrapCount").innerText = statusCounts.find(s => s.status === "ScrapLacksTask")?.count || 0;
            document.getElementById("scrapCount").innerText = statusCounts.find(s => s.status === "ScrapHasScrap")?.count || 0;
            document.getElementById("waitingScrapCount").innerText = statusCounts.find(s => s.status === "WaitingApproveScrap")?.count || 0;
            document.getElementById("waitingLinkCount").innerText = statusCounts.find(s => s.status === "WAITING_LINK")?.count || 0;
            document.getElementById("linkedCount").innerText = statusCounts.find(s => s.status === "LINKED")?.count || 0;
            document.getElementById("approvedBGACount").innerText = statusCounts.find(s => s.status === "ApprovedBGA")?.count || 0;
            document.getElementById("waitingBGACount").innerText = statusCounts.find(s => s.status === "WaitingApprovalBGA")?.count || 0;

            // Tính phần trăm cho biểu đồ
            const total = statusCounts.reduce((sum, s) => sum + s.count, 0);
            const percentages = statusCounts.map(s => total > 0 ? ((s.count / total) * 100).toFixed(1) : 0);

            const summaryBody = document.getElementById("statusSummaryBody");
            if (summaryBody) {
                summaryBody.innerHTML = statusCounts.map((status, index) => {
                    const displayName = statusDisplayMap[status.status] || status.status || '';
                    const percentLabel = total > 0 ? `${percentages[index]}%` : '0%';
                    return `<tr><td>${displayName}</td><td>${status.count || 0}</td><td>${percentLabel}</td></tr>`;
                }).join("");
            }

            const chartData = statusCounts.map((status, index) => {
                const value = status.count || 0;
                const rawStatus = status.status || '';
                const displayName = statusDisplayMap[rawStatus] || rawStatus;
                const color = statusColorMap[rawStatus] || fallbackColors[index % fallbackColors.length];
                return {
                    value,
                    name: displayName,
                    rawStatus,
                    itemStyle: { color }
                };
            });

            if (!statusChart) {
                statusChart = registerChart(initChart('statusDonutChart'));
            }

            if (statusChart) {
                const hasData = chartData.some(item => item.value > 0);
                statusChart.setOption({
                    tooltip: {
                        trigger: 'axis',
                        axisPointer: { type: 'shadow' },
                        formatter: params => {
                            const p = params[0];
                            return `${p.name}: ${p.value} (${p.percent || 0}%)`;
                        }
                    },
                    grid: {
                        left: '4%',
                        right: '4%',
                        bottom: '10%',
                        top: '10%',
                        containLabel: true
                    },
                    xAxis: {
                        type: 'category',
                        data: chartData.map(item => item.name),
                        axisLabel: {
                            rotate: 20,
                            color: '#003d99',
                            fontWeight: 500,
                            fontSize: 11
                        },
                        axisLine: {
                            lineStyle: { color: '#c3d9ff' }
                        }
                    },
                    yAxis: {
                        type: 'value',
                        axisLine: { show: false },
                        splitLine: {
                            lineStyle: { color: 'rgba(0,0,0,0.1)' }
                        }
                    },
                    series: [
                        {
                            name: 'Trạng thái',
                            type: 'bar',
                            data: chartData.map(item => ({
                                value: item.value,
                                itemStyle: {
                                    color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                                        { offset: 0, color: item.itemStyle.color },
                                        { offset: 1, color: '#a1c4fd' }
                                    ]),
                                    borderRadius: [6, 6, 0, 0]
                                }
                            })),
                            barWidth: '50%',
                            label: {
                                show: true,
                                position: 'top',
                                color: '#1e2a45',
                                fontSize: 11,
                                fontWeight: 600
                            }
                        }
                    ]
                }, true);

                requestAnimationFrame(resizeCharts);
            }
            // Load dữ liệu bảng ban đầu (Tất cả trạng thái)
            await loadTableData(validStatuses);
        } catch (error) {
            console.error("Lỗi khi tải dashboard:", error);
            alert("Không thể tải dữ liệu dashboard. Vui lòng thử lại!");
        } finally {
            hideSpinner();
        }
    }

    // Load dữ liệu bảng dựa trên trạng thái
    async function loadTableData(statuses) {
        try {
            // Hiển thị spinner
            showSpinner();
            // Log payload để debug
            console.log("Sending payload:", { statuses });

            const params = {};
            if (Array.isArray(statuses) && statuses.length > 0) {
                params.statuses = statuses;
            }

            const response = await axios.get(apiDetailUrl, { params });
            const tableData = response.data?.data || [];

            if (dataTable) {
                dataTable.clear().rows.add(tableData).draw();
            } else {
                dataTable = $('#sumMaterialsTable').DataTable({
                    data: tableData,
                    scrollX: true,
                    columns: [
                        { data: "sn" },
                        { data: "productLine" },
                        { data: "modelName" },
                        { data: "moNumber" },
                        { data: "wipGroup" },
                        { data: "errorFlag" },
                        { data: "groupName" },
                        { data: "stationTime" },
                        { data: "status" }
                    ],
                    dom: '<"top d-flex align-items-center"flB>rt<"bottom"ip>',

                    buttons: [
                        {
                            extend: 'excelHtml5',
                            text: '<img src="/assets/img/excel.png" class="excel-icon excel-button"/>',
                            title: '', // Loại bỏ tiêu đề mặc định
                            filename: function () {
                                const now = new Date();
                                const offset = 7 * 60; // +07:00
                                const localDate = new Date(now.getTime() + offset * 60 * 1000);
                                const dateStr = localDate.toISOString().slice(0, 10).replace(/-/g, '');
                                const timeStr = localDate.toTimeString().slice(0, 8).replace(/:/g, '');
                                return `Bonepile_before_${dateStr}_${timeStr}`;
                            },
                            exportOptions: {
                                columns: ':visible',
                                modifier: {
                                    selected: null
                                },
                                format: {
                                    header: function (data, columnIdx) {
                                        // Loại bỏ khoảng trắng hoặc ký tự không mong muốn
                                        return data.trim();
                                    }
                                }
                            }
                        }
                    ],
                    destroy: true,
                    language: {
                        search: "",
                        emptyTable: "Không có dữ liệu để hiển thị",
                        zeroRecords: "Không tìm thấy bản ghi phù hợp"
                    },
                    initComplete: function () {
                        // Tạo select filter
                        var selectHtml = `
                                                                    <div class="form-group mb-0" style="min-width: 200px;">
                                                                        <select id="statusFilterDt" class="form-control">
                                                                            <option value="">Tất cả trạng thái</option>
                                                                            <option value="ScrapHasTask">Scrap Has Task</option>
                                                                            <option value="ScrapLacksTask">Scrap Lacks Task</option>
                                                                            <option value="LINKED">Linked</option>
                                                                            <option value="WAITING_LINK">WaitingLink</option>
                                                                            <option value="ApprovedBGA">SPE Approve to BGA</option>
                                                                            <option value="WaitingApprovalBGA">Waiting approve BGA</option>
                                                                            <option value="WaitingApproveScrap">Waiting SPE Approve Scrap</option>
                                                                        </select>
                                                                    </div>
                                                                `;

                        // Chèn vào phần `top` bên trái (trước nút excel)
                        $('.dataTables_wrapper .top').prepend(selectHtml);

                        // Gắn sự kiện
                        $('#statusFilterDt').on('change', async function () {
                            const selectedStatus = this.value;
                            const statuses = selectedStatus ? [selectedStatus] : validStatuses;
                            await loadTableData(statuses);
                        });

                        //JS để click vào row thì giữ highlight
                        $('#sumMaterialsTable tbody').on('click', 'tr', function () {
                            // Bỏ chọn các row khác
                            $('#sumMaterialsTable tbody tr').removeClass('selected-row');

                            // Gán class highlight
                            $(this).addClass('selected-row');
                        });

                        // Set placeholder cho ô search
                        $('.dataTables_filter input[type="search"]').attr('placeholder', 'Tìm kiếm');
                    }
                });
            }
        } catch (error) {
            console.error("Lỗi khi tải dữ liệu bảng:", error);
            alert("Không thể tải dữ liệu bảng. Vui lòng thử lại!");
        } finally {
            //document.getElementById("spinner-overlay").style.display = "none";
            hideSpinner();
        }
    }

    async function loadTableFromRecords(records) {
        try {
            showSpinner();
            const serials = Array.from(new Set(records.map(r => r.sn).filter(Boolean)));
            let locationMap = {};
            try {
                const locRes = await axios.post(locationUrl, serials);
                locationMap = locRes.data?.data || {};
            } catch (err) {
                console.error('Error fetching locations for modal', err);
            }
            records.forEach(r => { r.location = locationMap[r.sn] || ''; });
            if (modalTable) {
                modalTable.clear().rows.add(records).draw();
            } else {
                modalTable = $('#recordsTable').DataTable({
                    data: records,
                    scrollX: true,
                    columns: [
                        { data: "sn" },
                        { data: "productLine" },
                        { data: "modelName" },
                        { data: "moNumber" },
                        { data: "wipGroup" },
                        { data: "errorFlag" },
                        { data: "groupName" },
                        { data: "stationTime" },
                        { data: "status" },
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
                                return `B31M_${dateStr}_${timeStr}`;
                            },
                            exportOptions: { columns: ':visible' }
                        }
                    ],
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

    // Khởi tạo dashboard
    await loadDashboardData();
});