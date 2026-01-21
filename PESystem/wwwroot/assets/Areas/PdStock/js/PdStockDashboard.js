const pdStockApiBase = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/DdRepositorys";
const pdStockModelChartElement = document.getElementById('pdstock-model-chart');
const btnExportAll = document.getElementById('pdstock-export-sn');

let pdStockModelChartInstance = null;
let pdStockSearchResultsCache = [];
let pdStockSearchParamsCache = null;

document.addEventListener('DOMContentLoaded', () => {
    initializePdStockModelChart();
    loadPdStockDashboard(); // Hàm mới: Load toàn bộ Card và Chart

    if (btnExportAll) {
        btnExportAll.addEventListener('click', exportFullInventory);
    }
});

/**
 * 1. LOAD DASHBOARD (CARDS & CHART)
 * Gọi các API thống kê nhanh thay vì tải data nặng
 */
function loadPdStockDashboard() {
    const totalSnElement = document.getElementById('pdstock-total-sn');
    const totalCartonElement = document.getElementById('pdstock-total-carton');
    const b31mElement = document.getElementById('total-b31m'); // ID từ HTML của bạn
    const b23fElement = document.getElementById('total-b23f'); // ID từ HTML của bạn

    // A. Lấy tổng số lượng SN và Carton (API đếm nhanh)
    fetch(`${pdStockApiBase}/GetStockSummaryCount`)
        .then(response => response.json())
        .then(data => {
            if (totalSnElement) totalSnElement.textContent = data.totalSn.toLocaleString();
            if (totalCartonElement) totalCartonElement.textContent = data.totalCartons.toLocaleString();
        })
        .catch(err => {
            console.error('Lỗi lấy số lượng tổng:', err);
            if (totalSnElement) totalSnElement.textContent = "0";
        });

    // B. Lấy thống kê phân loại B31M và B23F (API mới viết)
    fetch(`${pdStockApiBase}/GetStockClassification`)
        .then(response => response.json())
        .then(data => {
            if (b31mElement) b31mElement.textContent = data.b31m.toLocaleString();
            if (b23fElement) b23fElement.textContent = data.b23f.toLocaleString();
        })
        .catch(err => {
            console.error('Lỗi lấy dữ liệu phân loại:', err);
            if (b31mElement) b31mElement.textContent = "0";
            if (b23fElement) b23fElement.textContent = "0";
        });

    // C. Lấy thống kê theo từng Model để vẽ biểu đồ
    fetch(`${pdStockApiBase}/GetStockSummaryByModel`)
        .then(response => response.json())
        .then(result => {
            updatePdStockModelChartFromSummary(result.data);
        })
        .catch(err => {
            console.error('Lỗi lấy thống kê model:', err);
            updatePdStockModelChartFromSummary([]);
        });
}
/**
 * Vẽ biểu đồ từ dữ liệu thống kê gọn nhẹ
 */
function updatePdStockModelChartFromSummary(summaryData) {
    if (!pdStockModelChartInstance) return;

    const modelLabels = summaryData.map(item => item.modelName || 'N/A');
    const modelValues = summaryData.map(item => item.count);

    pdStockModelChartInstance.setOption({
        xAxis: { data: modelLabels },
        series: [{
            data: modelValues,
            label: { show: true, position: 'top' }
        }]
    });
}

/**
 * 2. TÌM KIẾM CHI TIẾT THEO NGÀY
 */
document.getElementById('searchBtn').addEventListener('click', function () {
    const startDate = document.getElementById('startDate').value;
    const endDate = document.getElementById('endDate').value;
    const searchOption = document.getElementById('search-options').value;
    const resultsSection = document.getElementById('PDStock-search-results-section');

    if (!startDate || !endDate || searchOption === "TYPE") {
        showPdStockAlert('warning', 'Thiếu thông tin', 'Vui lòng chọn khoảng thời gian và loại tìm kiếm.');
        return;
    }

    let url = (searchOption === "add-stock")
        ? `${pdStockApiBase}/GetProductsByDateRange`
        : `${pdStockApiBase}/GetExportedProductsByDateRange`;

    const requestData = { startDate, endDate };

    resultsSection.innerHTML = '<div class="text-center py-4"><div class="spinner-border text-primary"></div><p>Đang truy vấn dữ liệu...</p></div>';

    fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requestData)
    })
        .then(response => response.json())
        .then(data => {
            pdStockSearchParamsCache = { url, requestData };
            pdStockSearchResultsCache = Array.isArray(data.data) ? data.data : [];
            displayResultsAsTable(pdStockSearchResultsCache, resultsSection);
        })
        .catch(error => {
            console.error('Search error:', error);
            resultsSection.innerHTML = `<div class="alert alert-danger">Lỗi: ${error.message}</div>`;
        });
});

function displayResultsAsTable(data, resultsSection) {
    resultsSection.innerHTML = '';
    if (!data || data.length === 0) {
        resultsSection.innerHTML = '<div class="alert alert-warning">Không tìm thấy dữ liệu phù hợp.</div>';
        return;
    }

    const table = document.createElement('table');
    table.className = 'table table-hover PdStock-results-table';
    table.innerHTML = `
        <thead>
            <tr>
                <th>SERIAL_NUMBER</th>
                <th>PRODUCT_LINE</th>
                <th>MODEL_NAME</th>
                <th>CARTON_NO</th>
                <th>LOCATION</th>
                <th>ENTRY_DATE</th>
                <th>ENTRY_OP</th>
            </tr>
        </thead>
        <tbody>
            ${data.map(item => `
                <tr>
                    <td>${item.serialNumber || '-'}</td>
                    <td>${item.productLine || '-'}</td>
                    <td>${item.modelName || '-'}</td>
                    <td>${item.cartonNo || '-'}</td>
                    <td>${item.locationStock || '-'}</td>
                    <td>${item.entryDate || '-'}</td>
                    <td>${item.entryOp || '-'}</td>
                </tr>
            `).join('')}
        </tbody>
    `;
    resultsSection.appendChild(table);
}

/**
 * 3. XUẤT EXCEL TOÀN BỘ KHO (KÈM ORACLE R107)
 */
function exportFullInventory() {
    if (window.Swal) {
        Swal.fire({
            title: 'Đang trích xuất dữ liệu...',
            text: 'Hệ thống đang truy vấn dữ liệu. Vui lòng đợi.',
            allowOutsideClick: false,
            didOpen: () => { Swal.showLoading(); }
        });
    }

    fetch(`${pdStockApiBase}/ExportInventoryExcel`)
        .then(response => {
            if (!response.ok) throw new Error("Lỗi kết nối.");
            return response.json();
        })
        .then(result => {
            const data = result.data;
            if (!data || data.length === 0) {
                showPdStockAlert('warning', 'Thông báo', 'Kho trống, không có dữ liệu để xuất.');
                return;
            }

            const header = ["SERIAL_NUMBER", "PRODUCT_LINE", "MODEL_NAME", "CARTON_NO", "LOCATION", "MO_NUMBER", "WIP_GROUP", "ERROR_FLAG", "ENTRY_DATE", "ENTRY_OP"];
            let csvContent = "\ufeff" + header.join(",") + "\n";

            data.forEach(item => {
                const row = [
                    item.serialNumber,
                    item.productLine,
                    item.modelName,
                    item.cartonNo,
                    item.location,
                    item.moNumber,
                    item.wipGroup,
                    item.errorFlag,
                    item.entryDate,
                    item.entryOp
                ].map(val => `"${(val || '').toString().replace(/"/g, '""')}"`).join(",");
                csvContent += row + "\n";
            });

            const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
            const link = document.createElement("a");
            link.href = URL.createObjectURL(blob);
            link.download = `Total_Inventory_Report_${new Date().getTime()}.csv`;
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);

            if (window.Swal) Swal.close();
        })
        .catch(error => {
            console.error(error);
            showPdStockAlert('error', 'Lỗi xuất file', error.message);
        });
}

/**
 * KHỞI TẠO BIỂU ĐỒ
 */
function initializePdStockModelChart() {
    if (!pdStockModelChartElement || typeof echarts === 'undefined') return;
    pdStockModelChartInstance = echarts.init(pdStockModelChartElement);
    pdStockModelChartInstance.setOption({
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
        grid: { left: '3%', right: '4%', bottom: '15%', containLabel: true },
        xAxis: { type: 'category', data: [], axisLabel: { rotate: 35, fontSize: 11 } },
        yAxis: { type: 'value' },
        series: [{
            name: 'Số lượng',
            type: 'bar',
            data: [],
            itemStyle: { color: '#28a745' },
            emphasis: { itemStyle: { color: '#218838' } }
        }]
    });

    window.addEventListener('resize', () => {
        if (pdStockModelChartInstance) pdStockModelChartInstance.resize();
    });
}

function showPdStockAlert(icon, title, text) {
    if (window.Swal) {
        Swal.fire({ icon, title, text, timer: 2500, showConfirmButton: false });
    } else {
        alert(`${title}: ${text}`);
    }
}

// Giữ lại hàm xuất SN đơn giản nếu cần
function exportPdStockSerialNumbers() {
    fetch(`${pdStockApiBase}/GetAllWithR107`) // Lưu ý: API này nên dùng bản Take(3000) đã tối ưu
        .then(response => response.json())
        .then(data => {
            const stockData = Array.isArray(data.data) ? data.data : [];
            if (!stockData.length) return showPdStockAlert('warning', 'Trống', 'Không có SN.');

            const csv = "\ufeffSerialNumber\n" + stockData.map(i => i.serialNumber).join("\n");
            const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
            const link = document.createElement('a');
            link.href = URL.createObjectURL(blob);
            link.download = 'SN_List.csv';
            link.click();
        });
}