document.addEventListener("DOMContentLoaded", () => {
    const apiContainer = document.querySelector("[data-api-base]");
    const apiBaseUrl = apiContainer?.dataset.apiBase?.trim() || "";
    const apiConfigBase = apiBaseUrl ? `${apiBaseUrl}/api/Config` : "";

    const chartElement = document.getElementById("current-status-chart");
    const summaryElement = document.getElementById("current-status-summary");
    const modalElement = document.getElementById("currentStatusModal");
    const modalTitle = document.getElementById("currentStatusModalLabel");

    if (!apiConfigBase) {
        alert("Không tìm thấy API base URL.");
        return;
    }

    if (!chartElement) {
        console.error("Không tìm thấy vùng hiển thị biểu đồ.");
        return;
    }

    const chart = echarts.init(chartElement);

    const buildChart = (labels, counts) => {
        const option = {
            tooltip: {
                trigger: "axis",
                axisPointer: {
                    type: "shadow"
                }
            },
            grid: {
                left: "3%",
                right: "4%",
                bottom: "15%",
                containLabel: true
            },
            xAxis: {
                type: "category",
                data: labels,
                axisLabel: {
                    interval: 0,
                    rotate: 30
                }
            },
            yAxis: {
                type: "value"
            },
            series: [
                {
                    name: "Số lượng",
                    type: "bar",
                    data: counts,
                    itemStyle: {
                        color: "#4e79a7"
                    },
                    label: {
                        show: true,
                        position: "top"
                    }
                }
            ]
        };

        chart.setOption(option);
    };

    const fetchSummary = async () => {
        try {
            const response = await fetch(`${apiConfigBase}/dpu-status-summary`);
            if (!response.ok) {
                throw new Error(await response.text());
            }

            const result = await response.json();
            if (!result.success) {
                throw new Error(result.message || "Không thể lấy dữ liệu thống kê.");
            }

            const data = Array.isArray(result.data) ? result.data : [];
            const labels = data.map(item => item.currentStatus);
            const counts = data.map(item => item.count);

            if (summaryElement) {
                const total = counts.reduce((acc, val) => acc + val, 0);
                summaryElement.textContent = `Tổng số SN: ${total}`;
            }

            buildChart(labels, counts);
            chart.on("click", params => {
                if (params?.name) {
                    loadStatusDetail(params.name);
                }
            });
        } catch (error) {
            console.error("Lỗi khi lấy thống kê CurrentStatus:", error);
        }
    };

    const formatDateTime = value => {
        if (!value) {
            return "";
        }
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return value;
        }
        return date.toLocaleString("vi-VN");
    };

    const updateDetailTable = data => {
        if ($.fn.DataTable.isDataTable("#status-detail-table")) {
            $("#status-detail-table").DataTable().destroy();
        }

        const tableBody = document.querySelector("#status-detail-table tbody");
        if (!tableBody) {
            return;
        }
        tableBody.innerHTML = "";

        data.forEach(item => {
            const row = document.createElement("tr");
            row.innerHTML = `
                <td>${item.serialNumber ?? ""}</td>
                <td>${item.currentStatus ?? ""}</td>
                <td>${item.modelName ?? ""}</td>
                <td>${item.typeBonpile ?? ""}</td>
                <td>${item.hB_MB ?? ""}</td>
                <td>${item.type ?? ""}</td>
                <td>${formatDateTime(item.first_Fail_Time)}</td>
                <td>${item.descFirstFail ?? ""}</td>
                <td>${item.ddrToolResult ?? ""}</td>
                <td>${item.qTY_RAM_FAIL ?? 0}</td>
                <td>${item.nV_Instruction ?? ""}</td>
                <td>${item.reworkFXV ?? ""}</td>
                <td>${item.cutInBP2 ?? ""}</td>
                <td>${item.remark ?? ""}</td>
                <td>${item.remark2 ?? ""}</td>
                <td>${formatDateTime(item.created_At)}</td>
                <td>${formatDateTime(item.updated_At)}</td>
            `;
            tableBody.appendChild(row);
        });

        $("#status-detail-table").DataTable({
            destroy: true,
            paging: true,
            searching: true,
            ordering: true,
            language: {
                search: "Tìm kiếm:",
                lengthMenu: "Hiển thị _MENU_ dòng",
                info: "Hiển thị _START_ đến _END_ của _TOTAL_ dòng",
                paginate: {
                    first: "Đầu",
                    last: "Cuối",
                    next: "Tiếp",
                    previous: "Trước"
                }
            }
        });
    };

    const loadStatusDetail = async status => {
        try {
            const response = await fetch(`${apiConfigBase}/dpu-status-detail`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({ currentStatus: status })
            });

            if (!response.ok) {
                throw new Error(await response.text());
            }

            const result = await response.json();
            if (!result.success) {
                throw new Error(result.message || "Không thể lấy chi tiết CurrentStatus.");
            }

            const data = Array.isArray(result.data) ? result.data : [];
            updateDetailTable(data);

            if (modalTitle) {
                modalTitle.textContent = `Chi tiết CurrentStatus: ${status}`;
            }

            if (modalElement) {
                const modal = new bootstrap.Modal(modalElement);
                modal.show();
            }
        } catch (error) {
            console.error("Lỗi khi lấy chi tiết CurrentStatus:", error);
        }
    };

    fetchSummary();
});
