(function () {
    const root = document.getElementById('repair-root');
    if (!root) {
        return;
    }

    const serialNumber = root.dataset.serial;
    const API_BASE = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api';

    const state = {
        faultId: null,
        cookbooks: {},
        testResult: null
    };

    const setText = (id, value) => {
        const el = document.getElementById(id);
        if (el) {
            el.textContent = value || '--';
        }
    };

    const postJson = async (url, body) => {
        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!response.ok) {
            throw new Error(`Request failed: ${response.status}`);
        }
        return response.json();
    };

    const getJson = async (url) => {
        const response = await fetch(url);
        if (!response.ok) {
            throw new Error(`Request failed: ${response.status}`);
        }
        return response.json();
    };

    const renderSuggestions = (suggestions) => {
        const tbody = document.getElementById('ai-suggestions-body');
        if (!tbody) return;
        tbody.innerHTML = '';

        if (!suggestions || suggestions.length === 0) {
            tbody.innerHTML = '<tr><td colspan="5" class="text-center text-muted">Chưa có dữ liệu</td></tr>';
            return;
        }

        suggestions.forEach((item) => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td>${item.reasonCode || ''}</td>
                <td>${item.errorItems || ''}</td>
                <td>${item.ratePercent || ''}</td>
            `;
            tbody.appendChild(tr);
        });
    };

    const buildStepRow = (step) => {
        const order = step.order || step.Order;
        const text = step.text || step.Text || '';

        const row = document.createElement('div');
        row.className = 'step-row';
        row.innerHTML = `
            <div class="step-text">
                <span class="step-index">${order}.</span>${text}
            </div>
        `;
        return row;
    };

    const renderCookbook = (cookbook, containerId, noteId) => {
        const container = document.getElementById(containerId);
        const noteEl = document.getElementById(noteId);
        const testNote = state.testResult?.displayValue || state.testResult?.DisplayValue || 'Pending Test';
        if (noteEl) {
            noteEl.textContent = testNote;
        }
        if (!container) return;
        container.innerHTML = '';

        if (!cookbook || !cookbook.steps || cookbook.steps.length === 0) {
            container.innerHTML = '<div class="text-muted">Chưa có bước hướng dẫn.</div>';
            return;
        }

        const steps = cookbook.steps || cookbook.Steps || [];
        steps.forEach(step => {
            container.appendChild(buildStepRow(step));
        });
    };

    const loadSuggestions = async () => {
        if (!serialNumber) return;
        try {
            const resp = await postJson(`https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/get-repair-suggestions`, { serialNumber });
            renderSuggestions(resp?.data || []);
        } catch (err) {
            console.warn('Không thể tải AI suggestions', err);
        }
    };

    const loadSearchInfo = async () => {
        if (!serialNumber) return;
        try {
            const resp = await postJson(`https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/search`, { SerialNumbers: [serialNumber] });
            const first = resp?.data && resp.data[0];
            if (first) {
                setText('fault-sn', first.seriaL_NUMBER || '--');
                setText('fault-model', first.modeL_NAME || first.MODEL_NAME || '--');
                setText('fault-error', first.tesT_CODE || first.ERROR_CODE || '--');
                setText('fault-desc', first.datA1 || first.ERROR_DESC || '--');
                setText('fault-group', first.tesT_GROUP || first.TEST_GROUP || '--');
            }
        } catch (err) {
            console.warn('Không thể tải thông tin lỗi qua SearchFA', err);
        }
    };

    const loadRepairData = async () => {
        if (!serialNumber) return;
        try {
            const resp = await getJson(`${API_BASE}/repair/${serialNumber}`);
            if (!resp?.success || !resp.data) return;
            const data = resp.data;
            const fault = data.fault || data.Fault;
            const nvCookbook = data.nvCookbook || data.NvCookbook;
            const fxnCookbook = data.fxnCookbook || data.FxnCookbook;
            const testResult = data.testResult || data.TestResult;

            state.faultId = fault?.faultId || fault?.FaultId;
            state.cookbooks = {
                nv: nvCookbook?.cookbookId || nvCookbook?.CookbookId,
                fxn: fxnCookbook?.cookbookId || fxnCookbook?.CookbookId
            };
            const nvSteps = nvCookbook?.steps || nvCookbook?.Steps || [];
            const fxnSteps = fxnCookbook?.steps || fxnCookbook?.Steps || [];
            state.cookbooks.nvStep = nvSteps[0]?.stepId || nvSteps[0]?.StepId;
            state.cookbooks.fxnStep = fxnSteps[0]?.stepId || fxnSteps[0]?.StepId;
            state.testResult = testResult;

            setText('fault-model', fault?.modelName || fault?.ModelName);
            setText('fault-error', fault?.errorCode || fault?.ErrorCode);
            setText('fault-desc', fault?.errorDesc || fault?.ErrorDesc);
            setText('fault-group', fault?.testGroup || fault?.TestGroup);
            setText('fault-line', fault?.productLine || fault?.ProductLine);

            renderCookbook(nvCookbook, 'nv-steps', 'nv-test-note');
            renderCookbook(fxnCookbook, 'fxn-steps', 'fxn-test-note');
        } catch (err) {
            console.error(err);
        }
    };

    const createFxnCookbook = async () => {
        if (!state.faultId) {
            alert('Chưa có FaultId. Vui lòng tải lại trang.');
            return;
        }

        const { value: stepText, isConfirmed } = await Swal.fire({
            title: 'Thêm Cookbook FXN',
            input: 'text',
            inputLabel: 'Nội dung',
            inputPlaceholder: 'Nhập nội dung',
            showCancelButton: true,
            confirmButtonText: 'Lưu',
            cancelButtonText: 'Hủy',
            inputValidator: (value) => {
                if (!value || !value.trim()) {
                    return 'Vui lòng nhập nội dung bước';
                }
                return undefined;
            }
        });

        if (!isConfirmed) return;

        try {
            await postJson(`${API_BASE}/repair/fxn-cookbook`, { faultId: state.faultId, stepText: stepText?.trim() });
            await Swal.fire('Thành công', 'Đã thêm bước vào cookbook FXN.', 'success');
            await loadRepairData();
        } catch (err) {
            console.error(err);
            await Swal.fire('Lỗi', 'Không thể tạo cookbook FXN.', 'error');
        }
    };

    const init = async () => {
        if (!serialNumber) return;
        const addFxnBtn = document.getElementById('add-fxn-btn');
        if (addFxnBtn) {
            addFxnBtn.addEventListener('click', createFxnCookbook);
        }
        await Promise.all([loadRepairData(), loadSuggestions(), loadSearchInfo()]);
    };

    init();
})();
