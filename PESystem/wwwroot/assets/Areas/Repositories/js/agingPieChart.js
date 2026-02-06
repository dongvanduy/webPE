document.addEventListener("DOMContentLoaded", function () {
    const warehouseEl = document.getElementById("warehouse-aging-chart");
    const waitingEl = document.getElementById("b36r-waiting-aging-chart");
    const waitingMO = document.getElementById("b36r-waiting-open-chart");
    const openedMO = document.getElementById("b36r-opened-aging-chart");
    const linkedEl = document.getElementById("b36r-linked-aging-chart");
    const baseUrl = typeof API_BASE_URL !== "undefined" ? API_BASE_URL : "";

    function renderBarChart(element, title, items) {
        if (!element) {
            return null;
        }

        const safeItems = items || [];
        const labels = safeItems.map(item => item.label || "");
        const values = safeItems.map(item => item.count || 0);

        const chart = echarts.init(element);
        chart.setOption({
            title: {
                text: title,
                left: "center",
                top: 0,
                textStyle: {
                    fontSize: 12,
                    fontWeight: 600
                }
            },
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" }
            },
            grid: {
                left: "3%",
                right: "4%",
                bottom: "10%",
                containLabel: true
            },
            xAxis: {
                type: "category",
                data: labels,
                axisTick: { alignWithLabel: true },
                axisLabel: {
                    interval: 0,
                    fontSize: 11
                }
            },
            yAxis: {
                type: "value"
            },
            series: [
                {
                    type: "bar",
                    data: values,
                    barMaxWidth: 40,
                    itemStyle: {
                        color: "#5470c6"
                    },
                    label: {
                        show: true,
                        position: "top"
                    }
                }
            ]
        });

        window.addEventListener("resize", () => chart.resize());
        return chart;
    }

    function getBucketKey(label) {
        if (label === "<1 ngày") return "lessThanOneDay";
        if (label === "1-3 ngày") return "oneToThreeDays";
        if (label === ">3 ngày") return "moreThanThreeDays";
        return null;
    }

    function buildLocation(item) {
        if (!item) return "";
        const shelf = item.shelfCode || "";
        const column = item.columnNumber ?? "";
        const level = item.levelNumber ?? "";
        if (!shelf && column === "" && level === "") {
            return "";
        }
        return `${shelf}${column}-${level}`;
    }

    function openAgingModal(title, rows, statusLabel) {
        const modalEl = document.getElementById("agingDetailModal");
        if (!modalEl) {
            return;
        }

        const modalTitle = document.getElementById("agingDetailModalLabel");
        if (modalTitle) {
            modalTitle.textContent = title;
        }

        const tbody = document.querySelector("#aging-detail-table tbody");
        if (tbody) {
            tbody.innerHTML = "";
        }

        const tableData = (rows || []).map(item => ({
            serialNumber: item.serialNumber || item.SerialNumber || "",
            productLine: item.productLine || item.ProductLine || "",
            modelName: item.modelName || item.ModelName || "",
            location: item.location || buildLocation(item),
            date: item.exportDate || item.ExportDate || item.entryDate || item.EntryDate || "",
            agingDays: item.agingDays ?? item.AgingDays ?? "",
            status: statusLabel || ""
        }));

        if ($.fn.DataTable.isDataTable("#aging-detail-table")) {
            $("#aging-detail-table").DataTable().clear().destroy();
        }

        $("#aging-detail-table").DataTable({
            data: tableData,
            columns: [
                { data: "serialNumber" },
                { data: "productLine" },
                { data: "modelName" },
                { data: "location" },
                { data: "date" },
                { data: "agingDays" },
                { data: "status" }
            ],
            scrollX: true,
            ordering: false,
            info: true,
            autoWidth: false,
            dom: '<"top d-flex align-items-center"flB>rt<"bottom"ip>',
            buttons: [
                {
                    extend: "excelHtml5",
                    text: '<img src="/assets/img/excel.png" class="excel-icon excel-button"/>',
                    title: "",
                    className: "excel-button"
                }
            ],
            destroy: true
        });

        const modal = new bootstrap.Modal(modalEl);
        modal.show();
    }

    async function loadWarehouseAging() {
        if (!warehouseEl) {
            return;
        }

        try {
            const response = await fetch(`${baseUrl}/api/product/warehouse-aging`);
            const payload = await response.json();
            if (!payload.success) {
                throw new Error(payload.message || "Warehouse aging failed");
            }
            const chart = renderBarChart(warehouseEl, "Aging kho", payload.data);
            if (chart) {
                chart.on("click", function (params) {
                    const bucketKey = getBucketKey(params.name);
                    if (!bucketKey || !payload.details) {
                        return;
                    }
                    openAgingModal(`Aging kho: ${params.name}`, payload.details[bucketKey] || [], "Kho OK");
                });
            }
        } catch (error) {
            console.error("Warehouse aging error", error);
            warehouseEl.innerHTML = `<div class="text-danger">Không tải được dữ liệu.</div>`;
        }
    }

    async function loadB36RAging() {
        if (!openedMO && !linkedEl && !waitingMO) {
            return;
        }

        try {
            const response = await fetch(`${baseUrl}/api/checking-b36r/aging`);
            const payload = await response.json();
            if (!payload.success) {
                throw new Error(payload.message || "B36R aging failed");
            }

            const openedChart = renderBarChart(openedMO, "ĐÃ MỞ MO", payload.openedMo);
            if (openedChart) {
                openedChart.on("click", function (params) {
                    const bucketKey = getBucketKey(params.name);
                    if (!bucketKey || !payload.openedMoDetails) {
                        return;
                    }
                    openAgingModal(`B36R Đã mở MO: ${params.name}`, payload.openedMoDetails[bucketKey] || [], "ĐÃ MỞ MO");
                });
            }

            const openingChart = renderBarChart(waitingMO, "CHỜ MỞ MO", payload.waitingOpenMo);
            if (openingChart) {
                openingChart.on("click", function (params) {
                    const bucketKey = getBucketKey(params.name);
                    if (!bucketKey || !payload.waitingOpenMoDetails) {
                        return;
                    }
                    openAgingModal(`B36R Chờ mở MO: ${params.name}`, payload.waitingOpenMoDetails[bucketKey] || [], "CHỜ MỞ MO");
                });
            }

            const linkedChart = renderBarChart(linkedEl, "Linked", payload.linked);
            if (linkedChart) {
                linkedChart.on("click", function (params) {
                    const bucketKey = getBucketKey(params.name);
                    if (!bucketKey || !payload.linkedDetails) {
                        return;
                    }
                    openAgingModal(`B36R Linked: ${params.name}`, payload.linkedDetails[bucketKey] || [], "Linked");
                });
            }
        } catch (error) {
            console.error("B36R aging error", error);
            if (waitingMO) {
                waitingMO.innerHTML = `<div class="text-danger">Không tải được dữ liệu.</div>`;
            }
            if (openedMO) {
                openedMO.innerHTML = `<div class="text-danger">Không tải được dữ liệu.</div>`;
            }
            if (linkedEl) {
                linkedEl.innerHTML = `<div class="text-danger">Không tải được dữ liệu.</div>`;
            }
        }
    }

    loadWarehouseAging();
    loadB36RAging();
});
