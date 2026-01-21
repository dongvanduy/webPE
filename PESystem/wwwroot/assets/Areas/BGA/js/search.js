const API_BASE_URL = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/bga-replace";
const STATUS_LABELS = {
    3: "Replaced BGA ok",
    4: "Waiting approve replace BGA",
    10: "Check in barking",
    11: "Check out barking",
    12: "VI after barking",
    13: "Remove BGA, clean pad",
    14: "Check VI BGA pad",
    15: "Replace BGA",
    16: "Xray",
    17: "ICT, FT",
    18: "Replaced BGA ok"
};

document.addEventListener("DOMContentLoaded", () => {
    const snInput = document.getElementById("lookup-sn-input");
    const searchCurrentBtn = document.getElementById("search-current-btn");
    const searchHistoryBtn = document.getElementById("search-history-btn");
    const clearBtn = document.getElementById("clear-lookup-btn");
    const feedback = document.getElementById("lookup-feedback");
    const resultContainer = document.getElementById("lookup-result");
    const downloadBtn = document.getElementById("download-result-btn");
    const title = document.getElementById("lookup-title");
    const subtitle = document.getElementById("lookup-subtitle");

    if (!snInput || !searchCurrentBtn || !searchHistoryBtn || !resultContainer) {
        return;
    }

    let lastExportData = [];
    let lastExportSheet = "TraCuu";

    const parseSNs = (raw) => raw
        .split(/[\s,;]+/)
        .map(item => item.trim())
        .filter(item => item.length);

    const setFeedback = (message, type) => {
        if (!feedback) {
            return;
        }
        if (!message) {
            feedback.classList.add("d-none");
            feedback.textContent = "";
            feedback.classList.remove("alert-danger", "alert-success", "alert-warning", "alert-info");
            return;
        }
        feedback.textContent = message;
        feedback.classList.remove("d-none", "alert-danger", "alert-success", "alert-warning", "alert-info");
        feedback.classList.add(`alert-${type}`);
    };

    const setSubtitle = (text) => {
        if (subtitle) {
            subtitle.textContent = text;
        }
    };

    const setTitle = (text) => {
        if (title) {
            title.textContent = text;
        }
    };

    const disableDownload = () => {
        if (downloadBtn) {
            downloadBtn.disabled = true;
        }
        lastExportData = [];
    };

    const renderCurrentTable = (items) => {
        if (!Array.isArray(items) || !items.length) {
            resultContainer.innerHTML = "<p class=\"text-muted\">Không có dữ liệu.</p>";
            setSubtitle("Không có dữ liệu.");
            disableDownload();
            return;
        }

        const rows = items.map((item, index) => {
            if (item.isValid === false) {
                return `
                    <tr class="table-danger">
                        <td>${index + 1}</td>
                        <td>${item.sn ?? ""}</td>
                        <td colspan="10">${item.message || "Không tìm thấy dữ liệu."}</td>
                    </tr>
                `;
            }

            const statusName = item.statusName ?? STATUS_LABELS[item.applyTaskStatus] ?? item.applyTaskStatus ?? "";
            const applyTime = item.applyTime ? new Date(item.applyTime).toLocaleString() : "";
            const sourceLabel = item.source === "History"
                ? "Lịch sử"
                : item.source === "Current"
                    ? "Hiện tại"
                    : "";
            const note = item.findBoardStatus ?? "";
            const remark = item.remark ?? "";
            const category = item.category ?? "";
            return `
                <tr>
                    <td>${index + 1}</td>
                    <td>${item.sn ?? ""}</td>
                    <td>${statusName}</td>
                    <td>${note}</td>
                    <td>${sourceLabel}</td>
                    <td>${category}</td>
                    <td>${remark}</td>
                    <td>${item.approveScrapperson ?? ""}</td>
                    <td>${applyTime}</td>
                    <td>${item.taskNumber ?? ""}</td>
                    <td>${item.internalTask ?? ""}</td>
                    <td>${item.desc ?? ""}</td>
                </tr>
            `;
        }).join("");

        resultContainer.innerHTML = `
            <table class="table table-striped table-hover table-sm align-middle">
                <thead class="table-light">
                    <tr>
                        <th>#</th>
                        <th>SN</th>
                        <th>Trạng thái</th>
                        <th>Note</th>
                        <th>Nguồn</th>
                        <th>Category</th>
                        <th>Remark</th>
                        <th>Approve</th>
                        <th>Apply Time</th>
                        <th>Task Number</th>
                        <th>Internal Task</th>
                        <th>Description</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        `;

        setSubtitle(`${items.length} dòng dữ liệu.`);

        lastExportData = items.map((item, index) => ({
            STT: index + 1,
            SN: item.sn ?? "",
            TrangThai: item.statusName ?? STATUS_LABELS[item.applyTaskStatus] ?? item.applyTaskStatus ?? "",
            Note: item.findBoardStatus ?? "",
            Nguon: item.source === "History" ? "History" : item.source === "Current" ? "Current" : "",
            Category: item.category ?? "",
            Remark: item.remark ?? "",
            Approve: item.approveScrapperson ?? "",
            ApplyTime: item.applyTime ? new Date(item.applyTime).toLocaleString() : "",
            TaskNumber: item.taskNumber ?? "",
            InternalTask: item.internalTask ?? "",
            Description: item.desc ?? "",
            GhiChu: item.message ?? ""
        }));

        lastExportSheet = "TrangThaiHienTai";
        if (downloadBtn) {
            downloadBtn.disabled = false;
        }
    };

    const renderHistoryTable = (items) => {
        if (!Array.isArray(items) || !items.length) {
            resultContainer.innerHTML = "<p class=\"text-muted\">Không có lịch sử cho các SN đã nhập.</p>";
            setSubtitle("Không có dữ liệu lịch sử.");
            disableDownload();
            return;
        }

        const rows = items.map((item, index) => {
            const statusName = item.statusName ?? STATUS_LABELS[item.applyTaskStatus] ?? item.applyTaskStatus ?? "";
            const applyTime = item.applyTime ? new Date(item.applyTime).toLocaleString() : "";
            const createTime = item.createTime ? new Date(item.createTime).toLocaleString() : "";
            const sourceLabel = item.source === "Current" ? "Hiện tại" : "Lịch sử";
            const note = item.findBoardStatus ?? "";
            return `
                <tr>
                    <td>${index + 1}</td>
                    <td>${item.sn ?? ""}</td>
                    <td>${statusName}</td>
                    <td>${note}</td>
                    <td>${sourceLabel}</td>
                    <td>${item.category ?? ""}</td>
                    <td>${item.remark ?? ""}</td>
                    <td>${item.approveScrapperson ?? ""}</td>
                    <td>${applyTime}</td>
                    <td>${createTime}</td>
                    <td>${item.taskNumber ?? ""}</td>
                    <td>${item.internalTask ?? ""}</td>
                    <td>${item.desc ?? ""}</td>
                </tr>
            `;
        }).join("");

        resultContainer.innerHTML = `
            <table class="table table-striped table-hover table-sm align-middle">
                <thead class="table-light">
                    <tr>
                        <th>#</th>
                        <th>SN</th>
                        <th>Trạng thái</th>
                        <th>Note</th>
                        <th>Nguồn</th>
                        <th>Category</th>
                        <th>Remark</th>
                        <th>Approve</th>
                        <th>Apply Time</th>
                        <th>Create Time</th>
                        <th>Task Number</th>
                        <th>Internal Task</th>
                        <th>Description</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        `;

        setSubtitle(`${items.length} dòng dữ liệu lịch sử.`);
        lastExportData = items.map((item, index) => ({
            STT: index + 1,
            SN: item.sn ?? "",
            TrangThai: item.statusName ?? STATUS_LABELS[item.applyTaskStatus] ?? item.applyTaskStatus ?? "",
            Note: item.findBoardStatus ?? "",
            Nguon: item.source === "Current" ? "Current" : "History",
            Category: item.category ?? "",
            Remark: item.remark ?? "",
            Approve: item.approveScrapperson ?? "",
            ApplyTime: item.applyTime ? new Date(item.applyTime).toLocaleString() : "",
            CreateTime: item.createTime ? new Date(item.createTime).toLocaleString() : "",
            TaskNumber: item.taskNumber ?? "",
            InternalTask: item.internalTask ?? "",
            Description: item.desc ?? ""
        }));
        lastExportSheet = "LichSu";
        if (downloadBtn) {
            downloadBtn.disabled = false;
        }
    };

    const fetchCurrent = async (sns) => {
        setTitle("Kết quả tra cứu trạng thái hiện tại");
        resultContainer.innerHTML = "<p class=\"text-muted\">Đang tải dữ liệu...</p>";
        setSubtitle("Đang tải...");
        disableDownload();
        setFeedback("", "info");

        try {
            const response = await fetch(`${API_BASE_URL}/status/bulk`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ sns })
            });
            const data = await response.json();
            if (!response.ok) {
                throw new Error(data.message || "Không thể tra cứu trạng thái.");
            }
            renderCurrentTable(data);
        } catch (error) {
            resultContainer.innerHTML = "";
            setSubtitle("Có lỗi xảy ra.");
            disableDownload();
            setFeedback(error.message, "danger");
        }
    };

    const fetchHistory = async (sns) => {
        setTitle("Kết quả tra cứu lịch sử");
        resultContainer.innerHTML = "<p class=\"text-muted\">Đang tải lịch sử...</p>";
        setSubtitle("Đang tải...");
        disableDownload();
        setFeedback("", "info");

        try {
            const response = await fetch(`${API_BASE_URL}/history`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ sns })
            });
            const data = await response.json();
            if (!response.ok) {
                throw new Error(data.message || "Không thể tra cứu lịch sử.");
            }
            renderHistoryTable(data);
        } catch (error) {
            resultContainer.innerHTML = "";
            setSubtitle("Có lỗi xảy ra.");
            disableDownload();
            setFeedback(error.message, "danger");
        }
    };

    const handleSearch = (mode) => {
        const sns = parseSNs(snInput.value);
        if (!sns.length) {
            setFeedback("Vui lòng nhập ít nhất một SN.", "warning");
            resultContainer.innerHTML = "";
            setSubtitle("Không có dữ liệu.");
            disableDownload();
            return;
        }

        setFeedback("", "info");
        if (mode === "current") {
            fetchCurrent(sns);
        } else {
            fetchHistory(sns);
        }
    };

    searchCurrentBtn.addEventListener("click", () => handleSearch("current"));
    searchHistoryBtn.addEventListener("click", () => handleSearch("history"));
    clearBtn?.addEventListener("click", () => {
        snInput.value = "";
        resultContainer.innerHTML = "";
        setFeedback("", "info");
        setSubtitle("Chưa có dữ liệu.");
        disableDownload();
    });

    downloadBtn?.addEventListener("click", () => {
        if (!window.XLSX) {
            setFeedback("Không thể tải Excel vì thiếu thư viện XLSX.", "warning");
            return;
        }
        if (!lastExportData.length) {
            setFeedback("Không có dữ liệu để tải xuống.", "warning");
            return;
        }

        const worksheet = XLSX.utils.json_to_sheet(lastExportData);
        const workbook = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(workbook, worksheet, lastExportSheet);
        XLSX.writeFile(workbook, `${lastExportSheet}-${new Date().toISOString().replace(/[:T]/g, "-").split(".")[0]}.xlsx`);
    });
});
