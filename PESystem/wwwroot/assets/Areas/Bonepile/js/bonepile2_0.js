document.addEventListener('DOMContentLoaded', () => {
    // Set endDate thành thời gian hiện tại (múi giờ +07:00)
    const endDateInput = document.getElementById('endDate');
    const now = new Date();
    const offset = 7 * 60; // +07:00
    const localDate = new Date(now.getTime() + offset * 60 * 1000);
    const formattedDate = localDate.toISOString().slice(0, 16);
    endDateInput.value = formattedDate;

    // Danh sách trạng thái cố định
    const statuses = ["Scrap", "WaitingLink", "CheckIn", "Repair", "Online", "WaitingKanBanIn", "WaitingApproveScrap", "CheckOut"];
    const statusDisplayMap = {
        Scrap: "Scrap",
        WaitingLink: "Waiting Link",
        CheckIn: "Waiting Check In",
        Repair: "Under Repair",
        Online: "Online",
        WaitingKanBanIn: "Waiting Kanban In",
        WaitingApproveScrap: "Waiting Approve Scrap",
        CheckOut: "Waiting Check Out"
    };
    const statusColorMap = {
        Scrap: "#dc3545",
        WaitingLink: "#ff8307",
        CheckIn: "#ffc107",
        Repair: "#0d6efd",
        Online: "#198754",
        WaitingKanBanIn: "#20c997",
        WaitingApproveScrap: "#6f42c1",
        CheckOut: "#17a2b8"
    };
    const fallbackColors = ["#ff8307", "#05b529", "#ffc107", "#75b507", "#17a2b8", "#17b86d", "#dc3545", "#6c757d"];

    let sumMaterialsTable;
    const charts = [];
    let statusDonutChart = registerChart(initChart('statusDonutChart'));

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


    // Hàm gọi API cho số lượng trạng thái
    async function fetchStatusCounts() {

        showSpinner();
        const startDate = document.getElementById('startDate').value.replace('T', ' ').replace(/-/g, '/').slice(0, 16);
        const endDate = document.getElementById('endDate').value.replace('T', ' ').replace(/-/g, '/').slice(0, 16);

        const payload = {
            statuses: statuses,
            startDate: startDate,
            endDate: endDate
        };
        console.log('fetchStatusCounts payload:', payload); // Log payload

        try {
            const response = await axios.post('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Bonepile2/status-count', payload);
            const { count, statusCounts } = response.data;
            console.log('fetchStatusCounts response:', { count, statusCounts });
            updateStatusCounts(count, statusCounts);
        } catch (error) {
            console.error('Error fetching status counts:', error);
            alert('Lỗi khi lấy số lượng trạng thái: ' + error.message);
        } finally {
            hideSpinner();
        }
    }

    // Hàm gọi API cho dữ liệu chi tiết
    async function fetchData(selectedStatus = null) {
        if (typeof axios === 'undefined') {
            console.error('Axios không được tải.');
            alert('Không thể tải Axios.');
            return;
        }
        if (typeof $ === 'undefined') {
            console.error('jQuery không được tải.');
            alert('Không thể tải jQuery.');
            return;
        }
        if (typeof $.fn.DataTable === 'undefined') {
            console.error('DataTables không được tải.');
            alert('Không thể tải DataTables.');
            return;
        }

        showSpinner();

        // Đảm bảo Statuses không bao giờ rỗng
        let statusesToFetch;
        if (selectedStatus && typeof selectedStatus === 'string' && selectedStatus.length > 0) {
            statusesToFetch = [selectedStatus];
        } else {
            statusesToFetch = statuses;
        }
        const startDate = document.getElementById('startDate').value.replace('T', ' ').replace(/-/g, '/').slice(0, 16);
        const endDate = document.getElementById('endDate').value.replace('T', ' ').replace(/-/g, '/').slice(0, 16);

        const payload = {
            statuses: statusesToFetch,
            startDate: startDate,
            endDate: endDate
        };
        console.log('fetchData payload:', payload); // Log payload
        console.log('selectedStatus:', selectedStatus); // Log giá trị selectedStatus

        try {
            const response = await axios.post('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Bonepile2/data', payload);
            const { count, data } = response.data;
            console.log('fetchData response:', { count, data });
            updateDashboard(count, data);
        } catch (error) {
            console.error('Error fetching data:', error);
            alert('Lỗi khi lấy dữ liệu: ' + error.message);
        } finally {
            //document.getElementById('loading').style.display = 'none';
            hideSpinner();
        }
    }

    // Hàm cập nhật số lượng trạng thái
    function updateStatusCounts(count, statusCounts = {}) {
        const counts = statusCounts || {};
        const waitingScrap = counts.WaitingApproveScrap ?? counts.WaitingApprovalScrap ?? 0;
        const waitingOut = counts.CheckOut ?? counts.WaitingCheckOut ?? 0;

        const totalCountEl = document.getElementById('totalCount');
        if (totalCountEl) totalCountEl.textContent = count ?? 'N/A';
        const waitingScrapEl = document.getElementById('waitingScrapCount');
        if (waitingScrapEl) waitingScrapEl.textContent = waitingScrap;
        const repairEl = document.getElementById('repairCount');
        if (repairEl) repairEl.textContent = counts.Repair || 0;
        const onlineEl = document.getElementById('onlinePd');
        if (onlineEl) onlineEl.textContent = counts.Online || 0;
        const kanbanInEl = document.getElementById('kanbanIn');
        if (kanbanInEl) kanbanInEl.textContent = counts.WaitingKanBanIn || 0;
        const waitingLinkEl = document.getElementById('waitingLink');
        if (waitingLinkEl) waitingLinkEl.textContent = counts.WaitingLink || 0;
        const waitingOutEl = document.getElementById('waitingOut');
        if (waitingOutEl) waitingOutEl.textContent = waitingOut;
        const waitingInEl = document.getElementById('waitingIn');
        if (waitingInEl) waitingInEl.textContent = counts.CheckIn || 0;
        const kanbanDoneEl = document.getElementById('kanbanDone');
        if (kanbanDoneEl && counts.KanbanDone !== undefined) {
            kanbanDoneEl.textContent = counts.KanbanDone;
        }

        const orderedStatuses = [...statuses];
        Object.keys(counts).forEach(status => {
            if (!orderedStatuses.includes(status)) {
                orderedStatuses.push(status);
            }
        });

        const chartData = orderedStatuses.map((status, idx) => {
            const value = Number(counts[status]) || 0;
            const color = statusColorMap[status] || fallbackColors[idx % fallbackColors.length];
            return {
                value,
                name: statusDisplayMap[status] || status,
                rawStatus: status,
                itemStyle: { color }
            };
        });

        const total = chartData.reduce((sum, item) => sum + item.value, 0);

        if (!statusDonutChart) {
            statusDonutChart = registerChart(initChart('statusDonutChart'));
        }

        statusDonutChart.setOption({
            tooltip: {
                trigger: 'item',
                formatter: params => {
                    const label = statusDisplayMap[params.data?.rawStatus] || params.name;
                    return `${label}: ${params.value} (${params.percent}%)`;
                }
            },
            legend: {
                type: 'scroll',
                bottom: 0,
                icon: 'circle',
                data: chartData.map(item => item.name),
                textStyle: {
                    color: '#1e2a45',
                    fontSize: 12
                }
            },
            series: [
                {
                    name: 'Trạng thái',
                    type: 'pie',
                    radius: ['42%', '72%'],
                    center: ['50%', '45%'],
                    avoidLabelOverlap: true,
                    itemStyle: {
                        borderColor: '#fff'
                    },
                    label: {
                        show: total > 0,
                        formatter: '{b}\n{c} ({d}%)',
                        color: '#1e2a45',
                        fontSize: 11,
                        lineHeight: 16
                    },
                    labelLine: {
                        length: 18,
                        length2: 12,
                        smooth: true
                    },
                    data: chartData
                }
            ]
        }, true);
        requestAnimationFrame(resizeCharts);
    }

    // Hàm cập nhật dashboard
    function updateDashboard(count, data) {
        // Cập nhật sumMaterialsTable
        if (!sumMaterialsTable) {
            sumMaterialsTable = $('#sumMaterialsTable').DataTable({
                data: data,
                columns: [
                    { data: 'sn' },
                    { data: 'productLine' },
                    { data: 'modelName' },
                    { data: 'moNumber' },
                    { data: 'wipGroup' },
                    { data: 'failStation' },
                    { data: 'symptom' },
                    { data: 'errorDescription' },
                    { data: 'time' },
                    { data: 'flag' },
                    { data: 'poNo' },
                    { data: 'poItem' },
                    { data: 'failureAging', render: data => data ? data.toFixed(1) + ' ngày' : 'N/A' },
                    { data: 'versionCode' },
                    { data: 'workFlag' },
                    { data: 'errorFlag' },
                    { data: 'moNew' },
                    { data: 'status' },
                    { data: 'checkinRepairTime' },
                    { data: 'checkoutRepairTime' },
                    { data: 'category' }
                ],
                pageLength: 10,
                responsive: true,
                scrollCollapse: true,
                scrollX: true,
                order: [[12, 'desc']], // Sắp xếp theo FAIL_AGING
                dom: '<"top d-flex align-items-center"<"status-filter">Blf>rt<"bottom"ip><"clear">',
                buttons: [
                    {
                        extend: 'excelHtml5',
                        text: '<img src="/assets/img/excel.png"  class="excel-icon excel-button"/>',
                        filename: function () {
                            const now = new Date();
                            const offset = 7 * 60; // +07:00
                            const localDate = new Date(now.getTime() + offset * 60 * 1000);
                            const dateStr = localDate.toISOString().slice(0, 10).replace(/-/g, '');
                            const timeStr = localDate.toTimeString().slice(0, 8).replace(/:/g, '');
                            return `Bonepile2_${dateStr}_${timeStr}`;
                        },
                        exportOptions: {
                            columns: ':visible'
                        }
                    }
                ],
                language: {
                    search: "",
                    lengthMenu: "_MENU_",
                    info: "Hiển thị _START_ đến _END_ của _TOTAL_ bản ghi",
                    paginate: {
                        first: "Đầu",
                        last: "Cuối",
                        next: "Tiếp",
                        previous: "Trước"
                    }
                },
                // Thêm statusFilter vào vị trí "status-filter"
                initComplete: function () {
                    const statusFilter = document.getElementById('statusFilter');
                    if (statusFilter) {
                        const statusFilterWrapper = this.api().table().container().querySelector('.status-filter');
                        if (statusFilterWrapper) {
                            statusFilterWrapper.appendChild(statusFilter);
                        }
                    }
                    // Set placeholder cho ô search
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
        } else {
            // Cập nhật dữ liệu mà không phá hủy DataTable
            sumMaterialsTable.clear(); // Xóa dữ liệu cũ
            sumMaterialsTable.rows.add(data); // Thêm dữ liệu mới
            sumMaterialsTable.draw(); // Vẽ lại bảng
        }

    }
    // Sự kiện áp dụng bộ lọc
    document.getElementById('applyFilters').addEventListener('click', () => {
        console.log('applyFilters clicked');
        fetchStatusCounts();
        fetchData();
    });

    // Sự kiện thay đổi select
    document.getElementById('statusFilter').addEventListener('change', () => {
        const statusFilter = document.getElementById('statusFilter').value;
        console.log('statusFilter changed:', statusFilter);
        fetchData(statusFilter || null);
    });

    // Tải dữ liệu lần đầu
    console.log('Initial page load');
    fetchStatusCounts();
    fetchData();
});