/**
 * Quản lý kho B28
 * Chức năng: Tìm kiếm, Nhập kho, Xuất kho, Xuất Excel, Biểu đồ ModelName
 */
const KhoB28Manager = (function () {
    const API_BASE_URL = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/KhoB28";
    const PRODUCT_INFO_URL = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Product/GetSNInfo";

    let searchResults = [];
    let modelChartInstance = null;

    const Utils = {
        getSerialNumbersFromInput: () => {
            const snInput = document.getElementById("sn-input")?.value || "";
            const snImport = document.getElementById("sn-import")?.value || "";
            const source = snInput.trim() ? snInput : snImport;
            return source
                .split("\n")
                .map(sn => sn.trim().toUpperCase())
                .filter(sn => sn && /^[A-Za-z0-9-]+$/.test(sn));
        },
        hasDuplicateSerials: (serialNumbers) => {
            const uniqueSerials = new Set(serialNumbers);
            return uniqueSerials.size !== serialNumbers.length;
        },
        formatDate: (dateValue) => {
            if (!dateValue) return "";
            const date = new Date(dateValue);
            return Number.isNaN(date.getTime()) ? dateValue : date.toLocaleString("vi-VN");
        },
        showError: (message) => Swal.fire({ icon: "error", title: "Lỗi", text: message }),
        showSuccess: (message) => Swal.fire({ icon: "success", title: "Thành công", text: message }),
        showWarning: (message) => Swal.fire({ icon: "warning", title: "Cảnh báo", text: message }),
        displayTotalResults: (count) => {
            const totalResults = document.getElementById("total-results");
            if (totalResults) {
                totalResults.textContent = `Kết quả: ${count}`;
            }
        },
        updateTotalStock: (total) => {
            const totalStockEl = document.getElementById("total-stock");
            if (totalStockEl) {
                totalStockEl.textContent = total ?? "0";
            }
        },
        resetResults: () => {
            searchResults = [];
            Render.renderTable([], "results-body");
            Utils.displayTotalResults(0);
            document.getElementById("search-results").style.display = "none";
            const exportExcelBtn = document.getElementById("export-sn-excel-btn");
            if (exportExcelBtn) exportExcelBtn.style.display = "none";
            const buttonAction = document.getElementById("button-action");
            if (buttonAction) buttonAction.classList.add("hidden");
        }
    };

    const Api = {
        getAll: async () => {
            const response = await fetch(`${API_BASE_URL}/get-all`);
            if (!response.ok) throw new Error("Không thể tải dữ liệu.");
            return response.json();
        },
        addSerials: async (payload) => {
            const response = await fetch(`${API_BASE_URL}/add-serial`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            if (!response.ok) throw new Error("Không thể nhập kho.");
            return response.json();
        },
        exportSerials: async (payload) => {
            const response = await fetch(`${API_BASE_URL}/export`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            if (!response.ok) throw new Error("Không thể xuất kho.");
            return response.json();
        },
        borrowSerials: async (payload) => {
            const response = await fetch(`${API_BASE_URL}/borrow`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            if (!response.ok) throw new Error("Không thể mượn kho.");
            return response.json();
        },
        returnSerials: async (payload) => {
            const response = await fetch(`${API_BASE_URL}/return`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            if (!response.ok) throw new Error("Không thể trả kho.");
            return response.json();
        },
        getCount: async () => {
            const response = await fetch(`${API_BASE_URL}/get-count`);
            if (!response.ok) throw new Error("Không thể tải số lượng kho.");
            return response.json();
        },
        getModelSummary: async () => {
            const response = await fetch(`${API_BASE_URL}/get-model`);
            if (!response.ok) throw new Error("Không thể tải thống kê model.");
            return response.json();
        },
        exportAllExcel: async () => {
            const response = await fetch(`${API_BASE_URL}/export-excel`);
            if (!response.ok) throw new Error("Không thể xuất Excel.");
            return response.json();
        }
    };

    const Render = {
        renderTable: (results, targetElementId) => {
            const resultsBody = document.getElementById(targetElementId);
            if (!resultsBody) return;

            resultsBody.innerHTML = "";
            if (!results || results.length === 0) {
                resultsBody.innerHTML = "<tr><td colspan='12'>Không tìm thấy kết quả!</td></tr>";
                return;
            }

            results.forEach(result => {
                const row = `
                    <tr>
                        <td><input type="checkbox" class="sn-checkbox" data-serial-number="${result.serialNumber || ""}" /></td>
                        <td>${result.serialNumber || ""}</td>
                        <td>${result.modelName || ""}</td>
                        <td>${result.moNumber || ""}</td>
                        <td>${result.wipGroup || ""}</td>
                        <td>${result.location || ""}</td>
                        <td>${result.inBy || ""}</td>
                        <td>${Utils.formatDate(result.inDate)}</td>
                        <td>${result.status || ""}</td>
                        <td>${result.borrower || ""}</td>
                        <td>${Utils.formatDate(result.borrowTime)}</td>
                    </tr>
                `;
                resultsBody.insertAdjacentHTML("beforeend", row);
            });
        },
        renderModelChart: async () => {
            const chartEl = document.getElementById("model-name-chart");
            if (!chartEl || typeof echarts === "undefined") return;

            try {
                const response = await Api.getModelSummary();
                const models = response.data || [];

                if (!models.length) {
                    chartEl.innerHTML = "<div class='text-muted text-center py-4'>Chưa có dữ liệu ModelName.</div>";
                    return;
                }

                const labels = models.map(item => item.modelName || item.ModelName || "");
                const counts = models.map(item => item.count ?? item.Count ?? 0);

                if (!modelChartInstance) {
                    modelChartInstance = echarts.init(chartEl);
                }

                modelChartInstance.setOption({
                    tooltip: { trigger: "axis", axisPointer: { type: "shadow" } },
                    grid: { left: "3%", right: "4%", bottom: "3%", containLabel: true },
                    xAxis: {
                        type: "category",
                        data: labels,
                        axisLabel: { rotate: 30, interval: 0 }
                    },
                    yAxis: { type: "value" },
                    series: [
                        {
                            type: "bar",
                            data: counts,
                            itemStyle: { color: "#2eca6a" }
                        }
                    ]
                });

                window.addEventListener("resize", () => modelChartInstance.resize());
            } catch (error) {
                console.error("Model chart error:", error);
            }
        }
    };

    const Search = {
        searchSerialNumbers: async () => {
            const serialNumbers = Utils.getSerialNumbersFromInput();

            if (serialNumbers.length === 0) {
                Utils.showError("Vui lòng nhập ít nhất một Serial Number hợp lệ!");
                return;
            }

            try {
                const response = await Api.getAll();
                const allData = response.data || [];
                const serialLookup = new Set(serialNumbers);
                searchResults = allData.filter(item => serialLookup.has((item.serialNumber || "").toUpperCase()));

                Render.renderTable(searchResults, "results-body");
                Utils.displayTotalResults(searchResults.length);
                document.getElementById("search-results").style.display = "block";

                const exportExcelBtn = document.getElementById("export-sn-excel-btn");
                if (exportExcelBtn) {
                    exportExcelBtn.style.display = "block";
                    exportExcelBtn.classList.remove("hidden");
                }
                const buttonAction = document.getElementById("button-action");
                if (buttonAction) buttonAction.classList.remove("hidden");

                Utils.showSuccess(`Tìm thấy ${searchResults.length}/${serialNumbers.length} SN`);
            } catch (error) {
                Utils.showError(`Lỗi khi tìm kiếm: ${error.message}`);
            }
        }
    };

    const Import = {
        updateSerialDetails: async (serialNumbers) => {
            const modelNames = [];
            const productLines = [];

            for (const serial of serialNumbers) {
                try {
                    const response = await fetch(`${PRODUCT_INFO_URL}?serialNumber=${encodeURIComponent(serial)}`);
                    const data = await response.json();
                    if (data.success && data.data) {
                        modelNames.push(data.data.modelName || "");
                        productLines.push(data.data.productLine || "");
                    } else {
                        modelNames.push("");
                        productLines.push("");
                    }
                } catch (error) {
                    console.error(`Lỗi khi lấy thông tin SN ${serial}:`, error);
                    modelNames.push("");
                    productLines.push("");
                }
            }

            const modelField = document.querySelector(".model-name-field");
            const productField = document.querySelector(".product-line-field");
            if (modelField) modelField.value = modelNames.join("\n");
            if (productField) productField.value = productLines.join("\n");
        },
        handleEntry: async () => {
            const location = document.querySelector(".location-field")?.value?.trim() || "";
            const currentUser = document.getElementById("entryPerson")?.value || "Unknown";
            const serialNumbers = Utils.getSerialNumbersFromInput();

            if (!location || serialNumbers.length === 0) {
                Utils.showWarning("Vui lòng điền vị trí và danh sách serial!");
                return;
            }

            if (Utils.hasDuplicateSerials(serialNumbers)) {
                Utils.showWarning("Danh sách serial có trùng lặp!");
                return;
            }

            const modelValues = document.querySelector(".model-name-field")?.value || "";
            const modelLines = modelValues.split("\n");

            const payload = serialNumbers.map((serial, index) => ({
                serialNumber: serial,
                modelName: (modelLines[index] || "").trim(),
                location: location,
                inBy: currentUser
            }));

            try {
                const response = await Api.addSerials(payload);
                if (response.errors?.length) {
                    Utils.showWarning(response.message || "Có serial không hợp lệ.");
                }
                Utils.showSuccess("Nhập kho thành công!");

                document.getElementById("sn-import").value = "";
                const modelField = document.querySelector(".model-name-field");
                const productField = document.querySelector(".product-line-field");
                if (modelField) modelField.value = "";
                if (productField) productField.value = "";

                Utils.resetResults();
                await Total.updateTotalAndChart();
            } catch (error) {
                Utils.showError(`Lỗi khi nhập kho: ${error.message}`);
            }
        },
        handleSerialInput: () => {
            const serialInput = document.getElementById("sn-import");
            if (!serialInput) return;

            serialInput.addEventListener("input", async function () {
                const serialNumbers = Utils.getSerialNumbersFromInput();
                const hasDuplicates = Utils.hasDuplicateSerials(serialNumbers);
                const duplicateWarning = document.getElementById("duplicate-warning");
                if (duplicateWarning) {
                    duplicateWarning.style.display = hasDuplicates ? "block" : "none";
                }
                await Import.updateSerialDetails(serialNumbers);
            });
        }
    };

    const Export = {
        exportSerialNumbers: async () => {
            const serialNumbers = Utils.getSerialNumbersFromInput();

            if (serialNumbers.length === 0) {
                Utils.showError("Vui lòng nhập ít nhất một Serial Number để xuất kho!");
                return;
            }

            const result = await Swal.fire({
                title: "Xác nhận xuất kho",
                text: `Bạn có chắc muốn xuất kho ${serialNumbers.length} Serial Number?`,
                icon: "warning",
                showCancelButton: true,
                confirmButtonText: "Xác nhận",
                cancelButtonText: "Hủy"
            });

            if (!result.isConfirmed) return;

            try {
                const response = await Api.exportSerials({ serialNumbers });
                if (response.success) {
                    Utils.showSuccess("Xuất kho thành công!");
                    Utils.resetResults();
                    await Total.updateTotalAndChart();
                } else {
                    Utils.showError(response.message || "Lỗi khi xuất kho Serial Number!");
                }
            } catch (error) {
                Utils.showError(`Lỗi khi xuất kho: ${error.message}`);
            }
        },
        exportToExcel: (data, fileNamePrefix) => {
            if (!data || data.length === 0) {
                Utils.showError("Không có dữ liệu để xuất!");
                return;
            }

            const worksheetData = data.map(result => ({
                "SERIAL_NUMBER": result.serialNumber || "",
                "MODEL_NAME": result.modelName || "",
                "MO_NUMBER": result.moNumber || "",
                "WIP_GROUP": result.wipGroup || "",
                "LOCATION": result.location || "",
                "IN_BY": result.inBy || "",
                "IN_DATE": Utils.formatDate(result.inDate),
                "STATUS": result.status || "",
                "BORROWER": result.borrower || "",
                "BORROW_DATE": Utils.formatDate(result.borrowTime)
            }));

            const workbook = XLSX.utils.book_new();
            const worksheet = XLSX.utils.json_to_sheet(worksheetData);
            XLSX.utils.book_append_sheet(workbook, worksheet, "SearchResults");

            const formattedDate = new Date().toLocaleString("vi-VN", {
                year: "numeric",
                month: "2-digit",
                day: "2-digit",
                hour: "2-digit",
                minute: "2-digit"
            }).replace(/[,:/]/g, "-");

            XLSX.writeFile(workbook, `${fileNamePrefix}-${formattedDate}.xlsx`);
            Utils.showSuccess("Xuất Excel thành công!");
        },
        exportAllToExcel: async () => {
            try {
                const response = await Api.exportAllExcel();
                const data = response.data || [];

                if (!data.length) {
                    Utils.showWarning("Không có dữ liệu để xuất!");
                    return;
                }

                const worksheetData = data.map(result => ({
                    "SERIAL_NUMBER": result.serialNumber || "",
                    "MODEL_NAME": result.modelName || "",
                    "MO_NUMBER": result.moNumber || "",
                    "WIP_GROUP": result.wipGroup || "",
                    "PRODUCT_LINE": result.productLine || "",
                    "LOCATION": result.location || "",
                    "IN_BY": result.inBy || "",
                    "IN_DATE": Utils.formatDate(result.inDate),
                    "STATUS": result.status || "",
                    "BORROWER": result.borrower || "",
                    "BORROW_DATE": Utils.formatDate(result.borrowTime)
                }));

                const workbook = XLSX.utils.book_new();
                const worksheet = XLSX.utils.json_to_sheet(worksheetData);
                XLSX.utils.book_append_sheet(workbook, worksheet, "KhoB28");

                const formattedDate = new Date().toLocaleString("vi-VN", {
                    year: "numeric",
                    month: "2-digit",
                    day: "2-digit",
                    hour: "2-digit",
                    minute: "2-digit"
                }).replace(/[,:/]/g, "-");

                XLSX.writeFile(workbook, `KhoB28-${formattedDate}.xlsx`);
                Utils.showSuccess("Xuất Excel thành công!");
            } catch (error) {
                Utils.showError(`Lỗi khi xuất Excel: ${error.message}`);
            }
        }
    };

    const Total = {
        updateTotalAndChart: async () => {
            try {
                const response = await Api.getCount();
                Utils.updateTotalStock(response.totalSn ?? response.totalSN ?? 0);
            } catch (error) {
                console.error("Total count error:", error);
            }

            await Render.renderModelChart();
        }
    };

    const init = () => {
        document.addEventListener("DOMContentLoaded", async () => {
            await Total.updateTotalAndChart();
            Import.handleSerialInput();

            document.getElementById("submit-sn-btn")?.addEventListener("click", Search.searchSerialNumbers);
            document.getElementById("entry-btn")?.addEventListener("click", Import.handleEntry);
            document.getElementById("export-sn-btn")?.addEventListener("click", Export.exportSerialNumbers);

            document.getElementById("export-scrap-btn")?.addEventListener("click", Export.exportAllToExcel);
            document.getElementById("export-sn-excel-btn")?.addEventListener("click", () => {
                Export.exportToExcel(searchResults, "KhoB28-Search");
            });
        });
    };

    return { init };
})();

KhoB28Manager.init();
