const apiConfigBase = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/config';

const serialInput = document.getElementById("sn-input");
const hbMbSelect = document.getElementById("hb-mb");
const typeBonepileSelect = document.getElementById("typeBonepile");
const inputButton = document.getElementById("input-btn");

function normalizeSerialNumbers(input) {
    return input
        .split(/[\n,;]+/)
        .map(sn => sn.trim())
        .filter(sn => sn.length > 0);
}

async function submitInput() {
    if (!apiConfigBase) {
        alert("Không tìm thấy API base URL.");
        return;
    }

    const inputValue = serialInput?.value.trim() ?? "";
    if (!inputValue) {
        alert("Vui lòng nhập Serial Number.");
        return;
    }

    const serialNumbers = normalizeSerialNumbers(inputValue);
    if (!serialNumbers.length) {
        alert("Danh sách Serial Number không hợp lệ.");
        return;
    }

    const hbMb = hbMbSelect?.value.trim() ?? "";
    const typeBonepile = typeBonepileSelect?.value.trim() ?? "";

    if (!hbMb || !typeBonepile) {
        alert("Vui lòng chọn HB/MB và Type Bonepile.");
        return;
    }

    const payload = {
        serialNumbers,
        hbMb,
        typeBonepile
    };

    try {
        const response = await fetch(`${apiConfigBase}/add-dpu-manager`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            const errorMessage = await response.text();
            throw new Error(errorMessage || "Không thể input dữ liệu.");
        }

        const result = await response.json();
        alert(result.Message || "Input thành công.");
    } catch (error) {
        console.error("Lỗi khi input DPU_Manager:", error);
        alert(error.message || "Không thể input dữ liệu.");
    }
}

inputButton?.addEventListener("click", submitInput);
