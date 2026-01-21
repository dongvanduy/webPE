const API_BASE_URL = "https://pe-vnmbd-nvidia-cns.myfiinet.com/api/bga-replace";
const STATUS_MAP = {
    3: "Return PD line ok",
    4: "Waiting approve replace BGA",
    10: "Check in barking",
    11: "Check out barking",
    12: "VI after barking",
    13: "Remove BGA, clean pad",
    14: "Check VI BGA pad",
    15: "Replace BGA",
    16: "Xray",
    17: "ICT, FT",
    18: "Waiting return PD line ok",
    19: "Return PD line ok"
};

document.addEventListener("DOMContentLoaded", () => {
    const currentStatusSelect = document.getElementById("current-status");
    const snInput = document.getElementById("sn-input");
    const remarkInput = document.getElementById("remark-input");
    const updateButton = document.getElementById("update-status-btn");
    const updateFeedback = document.getElementById("update-status-feedback");
    const decisionGroup = document.getElementById("status-decision-group");
    const decisionLabel = document.getElementById("status-decision-label");
    const previewBody = document.getElementById("status-preview-body");
    const previewFeedback = document.getElementById("status-preview-feedback");
    const previewTotal = document.getElementById("preview-total");

    if (!currentStatusSelect || !snInput || !updateButton) {
        return;
    }

    let debounceTimer = null;

    const setButtonLoading = (button, isLoading, loadingText) => {
        if (!button) {
            return;
        }
        if (isLoading) {
            button.disabled = true;
            button.dataset.originalText = button.dataset.originalText || button.innerHTML;
            button.innerHTML = loadingText || '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Đang xử lý...';
        } else {
            button.disabled = false;
            const original = button.dataset.originalText;
            if (original) {
                button.innerHTML = original;
            }
        }
    };

    const clearDecisionSelection = () => {
        document.querySelectorAll('input[name="status-decision"]').forEach(radio => {
            radio.checked = false;
        });
    };

    const toggleDecisionGroup = () => {
        const selectedStatus = parseInt(currentStatusSelect.value, 10);
        if (selectedStatus === 16 || selectedStatus === 17) {
            const labelText = selectedStatus === 16 ? "Kết quả Xray" : "Kết quả ICT/FT";
            if (decisionLabel) {
                decisionLabel.textContent = labelText;
            }
            decisionGroup?.classList.remove("d-none");
            clearDecisionSelection();
        } else {
            decisionGroup?.classList.add("d-none");
            clearDecisionSelection();
        }
    };

    const parseSNs = (raw) => {
        return raw
            .split(/[\s,;]+/)
            .map(item => item.trim())
            .filter(item => item.length);
    };

    const showUpdateFeedback = (message, isSuccess) => {
        updateFeedback.textContent = message;
        updateFeedback.classList.remove("d-none");
        updateFeedback.classList.toggle("alert-success", isSuccess);
        updateFeedback.classList.toggle("alert-danger", !isSuccess);
    };

    const resetPreview = (message) => {
        previewFeedback?.classList.add("d-none");
        previewFeedback?.classList.remove("alert-danger", "alert-warning", "alert-info");
        previewBody.innerHTML = `<tr class="text-muted"><td colspan="4" class="text-center">${message}</td></tr>`;
        if (previewTotal) {
            previewTotal.textContent = "0 SN";
        }
    };

    const renderPreview = (items) => {
        if (!Array.isArray(items) || !items.length) {
            resetPreview("Không có SN hợp lệ.");
            return;
        }

        const rows = items.map((item, index) => {
            const statusName = item.statusName ?? STATUS_MAP[item.applyTaskStatus] ?? item.applyTaskStatus ?? "N/A";
            const category = item.isValid === false && item.message
                ? item.message
                : item.category ?? "";
            const statusClass = item.isValid === false ? "text-danger" : "";
            return `
                <tr class="${statusClass}">
                    <td>${index + 1}</td>
                    <td>${item.sn ?? ""}</td>
                    <td>${statusName}</td>
                    <td>${category}</td>
                </tr>
            `;
        }).join("");

        previewBody.innerHTML = rows;
        if (previewTotal) {
            previewTotal.textContent = `${items.length} SN`;
        }
    };

    const fetchPreview = async () => {
        const sns = parseSNs(snInput.value);
        if (!sns.length) {
            resetPreview("Nhập SN để xem trạng thái hiện tại.");
            return;
        }

        previewBody.innerHTML = `<tr><td colspan="4" class="text-center text-muted">Đang tải...</td></tr>`;
        previewFeedback?.classList.add("d-none");

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
            renderPreview(data);
        } catch (error) {
            previewBody.innerHTML = `<tr><td colspan="4" class="text-center text-danger">${error.message}</td></tr>`;
            if (previewFeedback) {
                previewFeedback.textContent = error.message;
                previewFeedback.classList.remove("d-none");
                previewFeedback.classList.add("alert-danger");
            }
        }
    };

    const debouncePreview = () => {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(fetchPreview, 500);
    };

    snInput.addEventListener("input", debouncePreview);
    toggleDecisionGroup();
    currentStatusSelect.addEventListener("change", toggleDecisionGroup);

    updateButton.addEventListener("click", async () => {
        const snRaw = snInput.value;
        const remark = (remarkInput?.value || "").trim();
        const currentStatus = parseInt(currentStatusSelect.value, 10);

        updateFeedback.classList.add("d-none");
        updateFeedback.textContent = "";
        updateFeedback.classList.remove("alert-success", "alert-danger");

        const sns = parseSNs(snRaw);
        if (!sns.length) {
            showUpdateFeedback("Vui lòng nhập ít nhất một SN.", false);
            return;
        }

        if (!remark) {
            showUpdateFeedback("Remark không được để trống.", false);
            return;
        }

        if (remark.length > 50) {
            showUpdateFeedback("Remark tối đa 50 ký tự.", false);
            return;
        }

        const payload = {
            sns,
            currentStatus,
            remark
        };

        if (currentStatus === 16 || currentStatus === 17) {
            const decisionSelection = document.querySelector('input[name="status-decision"]:checked');
            if (!decisionSelection) {
                const message = currentStatus === 16
                    ? "Vui lòng chọn kết quả Xray."
                    : "Vui lòng chọn kết quả ICT/FT.";
                showUpdateFeedback(message, false);
                return;
            }
            payload.decision = decisionSelection.value;
            if (currentStatus === 16) {
                payload.xrayResult = decisionSelection.value;
            }
        }

        try {
            setButtonLoading(updateButton, true);
            const response = await fetch(`${API_BASE_URL}/update-status`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            const data = await response.json();
            if (!response.ok) {
                const message = data.message || "Cập nhật trạng thái thất bại.";
                if (Array.isArray(data.details) && data.details.length) {
                    showUpdateFeedback(`${message} ${data.details.join("; ")}`, false);
                } else {
                    showUpdateFeedback(message, false);
                }
                return;
            }

            showUpdateFeedback(data.message || "Cập nhật thành công.", true);
            remarkInput.value = "";
            snInput.value = "";
            clearDecisionSelection();
            resetPreview("Nhập SN để xem trạng thái hiện tại.");
        } catch (error) {
            showUpdateFeedback(error.message || "Đã xảy ra lỗi khi cập nhật.", false);
        } finally {
            setButtonLoading(updateButton, false);
        }
    });
});
