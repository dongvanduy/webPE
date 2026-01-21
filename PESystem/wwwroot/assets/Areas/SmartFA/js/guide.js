var SearchFaNormalizer = (function () {
    const existing = typeof window !== 'undefined' ? window.SearchFaNormalizer : undefined;
    if (existing) {
        return existing;
    }
    const hasValue = (value) => value !== undefined && value !== null && value !== '';
    const fieldMappings = {
        seriaL_NUMBER: ['SERIAL_NUMBER', 'serialNumber', 'SerialNumber', 'serial_number'],
        productLine: ['PRODUCT_LINE', 'productLine', 'ProductLine'],
        modeL_NAME: ['MODEL_NAME', 'modelName', 'ModelName'],
        wiP_GROUP: ['WIP_GROUP', 'wipGroup', 'WipGroup'],
        tesT_GROUP: ['TEST_GROUP', 'testGroup', 'TestGroup'],
        tesT_CODE: ['TEST_CODE', 'testCode', 'ERROR_CODE', 'errorCode'],
        datA1: ['DATA1', 'data1', 'ERROR_DESC', 'errorDesc'],
        datA11: ['DATA11', 'data11', 'STATUS', 'status'],
        datA12: ['DATA12', 'data12', 'PR_STATUS', 'prStatus'],
        datA13: ['DATA13', 'data13', 'HANDOVER', 'handover'],
        datA14: ['DATA14', 'data14'],
        datA15: ['DATA15', 'data15'],
        datA17: ['DATA17', 'data17', 'ACTION', 'action'],
        datA18: ['DATA18', 'data18', 'POSITION', 'position', 'LOCATION', 'location'],
        datA19: ['DATA19', 'data19', 'KANBAN_WIP', 'kanbanWip'],
        datE3: ['DATE3', 'date3'],
        tester: ['TESTER', 'tester'],
        mO_NUMBER: ['MO_NUMBER', 'moNumber'],
        mo_NUMBER: ['MO_NUMBER', 'moNumber']
    };

    function normalizeItem(item = {}) {
        if (!item || typeof item !== 'object') {
            return {};
        }
        const normalized = { ...item };
        Object.entries(fieldMappings).forEach(([targetKey, sourceKeys]) => {
            if (hasValue(normalized[targetKey])) {
                return;
            }
            for (const sourceKey of sourceKeys) {
                if (hasValue(item[sourceKey])) {
                    normalized[targetKey] = item[sourceKey];
                    break;
                }
            }
        });
        return normalized;
    }

    function normalizeData(data) {
        if (!Array.isArray(data)) {
            return [];
        }
        return data.map(normalizeItem);
    }

    function normalizeResponse(response) {
        if (!response || typeof response !== 'object') {
            return { success: false, data: [] };
        }
        return { ...response, data: normalizeData(response.data) };
    }

    const api = { normalizeItem, normalizeData, normalizeResponse };
    if (typeof window !== 'undefined') {
        window.SearchFaNormalizer = api;
    }
    return api;
})();

$(document).ready(function () {
    const table = $('#sn-table').DataTable({
        dom: 't',
        paging: false,
        info: false,
        ordering: false // Tắt sorting
    });

    let existingSNs = new Set();

    // Gắn sự kiện keypress cho ô nhập SN
    $('#serialNumber').on('keypress', function (e) {
        if (e.key === 'Enter') { // Khi nhấn Enter
            e.preventDefault(); // Ngăn reload trang
            $('#sn-form').trigger('submit'); // Gọi submit
        }
    });

    // Gọi API lấy danh sách ProductLine khi trang được tải
    fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/FixGuide/GetProductLines', {
        method: 'GET',
        headers: {
            'Content-Type': 'application/json'
        }
    })
        .then(response => {
            if (!response.ok) {
                throw new Error('Lỗi khi gọi API lấy danh sách ProductLine');
            }
            return response.json();
        })
        .then(data => {
            const dropdown = $('#productLineDropdown');
            data.productLines.forEach(productLine => {
                dropdown.append(`<option value="${productLine}">${productLine}</option>`);
            });
        })
        .catch(error => {
            console.error('Lỗi khi lấy ProductLine:', error);
        });


    $('#sn-form').on('submit', function (e) {
        e.preventDefault(); // Ngăn chặn reload trang

        const serialNumber = $('#serialNumber').val().trim();

        // Kiểm tra nếu SN không được nhập
        if (!serialNumber) {
            alert('Vui lòng nhập Serial Number!');
            return;
        }

        // Kiểm tra trùng lặp
        if (existingSNs.has(serialNumber)) {
            alert(`Serial Number "${serialNumber}" đã tồn tại trong danh sách!`);
            return;
        }

        // Gọi API để lấy dữ liệu theo SN
        fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/search', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                serialNumbers: [serialNumber], // Sửa lại trường "serialNumbers" để khớp với API
                modelName: "",
                testCode: "",
                status: "",
                data1: "",
                handoverStatus: "",
                location: ""
            })
        })
            .then(response => {
                if (!response.ok) {
                    throw new Error('Lỗi khi gọi API SearchProductsBySN');
                }
                return response.json();
            })
            .then(data => {
                // Kiểm tra nếu không có dữ liệu trả về
                const normalizedItems = SearchFaNormalizer.normalizeData(data.data);
                if (!Array.isArray(normalizedItems) || normalizedItems.length === 0) {
                    alert(`Không tìm thấy dữ liệu cho Serial Number "${serialNumber}"!`);
                    return;
                }

                // Xử lý từng phần tử trong mảng dữ liệu
                normalizedItems.forEach(item => {
                    const sn = item.seriaL_NUMBER?.trim() || ''; // Đọc trường chính xác từ API

                    if (sn === '') return; // Bỏ qua nếu SN rỗng

                    existingSNs.add(sn); // Thêm vào danh sách SN

                    // Gọi API để lấy CheckPoint dựa trên ProductLine và TestCode
                    fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/FixGuide/GetCheckPoints', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify({
                            productLine: item.productLine || '', // Sử dụng productLine trả về từ API
                            testCode: item.tesT_CODE || '' // Sử dụng testCode trả về từ API
                        })
                    })
                        .then(res => res.json())
                        .then(checkPointData => {
                            const checkpointsArray = (checkPointData.success && checkPointData.checkPoints)
                                ? (Array.isArray(checkPointData.checkPoints?.$values)
                                    ? checkPointData.checkPoints.$values
                                    : [checkPointData.checkPoints.toString()])
                                : ["Không tìm thấy CheckPoint"];

                            const encodedCheckpoints = encodeURIComponent(JSON.stringify(checkpointsArray));
                            const encodedDetail = encodeURIComponent(checkPointData.detail || 'Không có thông tin chi tiết');

                            // Thêm hàng vào DataTable
                            table.row.add([
                                sn,
                                item.productLine || '',
                                item.modeL_NAME || '',
                                item.wiP_GROUP || '',
                                item.tesT_CODE || '',
                                item.tesT_GROUP || '',
                                `<span title="${item.datA1 || ''}">${item.datA1 || ''}</span>`,
                                `<span title="${checkpointsArray.join(', ') || ''}">${checkpointsArray.join(', ') || ''}</span>`,
                                `<button class="btn btn-info btn-sm view-detail" 
                                data-sn="${sn}" 
                                data-model="${item.modeL_NAME}" 
                                data-product-line="${item.productLine}" 
                                data-checkpoints="${encodedCheckpoints}" 
                                data-detail="${encodedDetail}">
                                Xem chi tiết
                            </button>`,
                                item.datA13 || '',
                                item.datA14 || '',
                                item.datA15 || ''
                            ]).draw(false);

                            $('#serialNumber').val(''); // Reset input
                        })
                        .catch(err => {
                            console.error('Lỗi lấy CheckPoint:', err);

                            // Thêm giá trị mặc định vào DataTable khi API lỗi
                            const checkpointsArray = ["Không tìm thấy CheckPoint"];

                            table.row.add([
                                sn,
                                item.modeL_NAME || '',
                                item.wiP_GROUP || '',
                                item.productLine || '',
                                item.tesT_CODE || '',
                                `<span title="${item.datA1 || 'Không có dữ liệu'}">${item.datA1 || 'Không có dữ liệu'}</span>`,
                                `<span title="Không tìm thấy CheckPoint">Không tìm thấy CheckPoint</span>`,
                                `<button class="btn btn-info btn-sm view-detail" 
                                data-sn="${sn}" 
                                data-model="${item.modeL_NAME}" 
                                data-product-line="${item.productLine}" 
                                data-checkpoints="${encodeURIComponent('[]')}" 
                                data-detail="${encodeURIComponent('Không có thông tin chi tiết')}">
                                Xem chi tiết
                            </button>`,
                                item.datA13 || '',
                                item.datA14 || '',
                                item.datA15 || ''
                            ]).draw(false);

                            $('#serialNumber').val('');
                        });
                });
            })
            .catch(error => {
                console.error('Lỗi khi gọi API SearchProductsBySN:', error);
            });

        // Load lịch sử sửa chữa
        loadRepairHistory(serialNumber);
    });

    $('#sn-table tbody').on('click', '.view-detail', function () {
        const serialNumber = $(this).data('sn');
        const modelName = $(this).data('model');
        const productLine = $(this).data('product-line');

        // Giải mã dữ liệu trước khi sử dụng
        const checkpoints = JSON.parse(decodeURIComponent($(this).data('checkpoints')));
        const detail = decodeURIComponent($(this).data('detail'));

        // Đổ dữ liệu vào modal
        $('#modal-sn').text(serialNumber);
        $('#modal-model').text(modelName);
        $('#modal-product-line').text(productLine);
        $('#modal-checkpoints').empty();
        checkpoints.forEach(cp => {
            $('#modal-checkpoints').append(`<li>${cp}</li>`);
        });
        $('#modal-repair-detail').text(detail);

        // Hiển thị file PDF
        const pdfUrl = `/pdfs/product-lines/${productLine}.pdf`; // Đường dẫn file PDF
        $('#modal-product-line-pdf').attr('src', pdfUrl).on('error', function () {
            $(this).attr('src', '/pdfs/product-lines/default.pdf'); // File PDF mặc định nếu không tìm thấy
        });

        // Hiển thị modal
        $('#repairDetailModal').modal('show');
    });

    // Xử lý sự kiện khi nhấn nút Add Guide
    $('#addGuideButton').on('click', function () {
        const fileInput = document.getElementById('guideFile');
        const file = fileInput.files[0];
        const productLine = $('#productLineDropdown').val();

        if (!file || !productLine) {
            alert('Vui lòng chọn file và ProductLine!');
            return;
        }

        const formData = new FormData();
        formData.append('file', file);
        formData.append('productLine', productLine);

        fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/FixGuide/Upload', {
            method: 'POST',
            body: formData
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    alert('Hướng dẫn đã được tải lên thành công!');

                    // Reset các trường về mặc định
                    $('#guideFile').val(''); // Xóa file đã chọn
                    $('#productLineDropdown').val(''); // Reset dropdown về giá trị mặc định
                } else {
                    alert(`Lỗi tải hướng dẫn: ${data.message}`);
                }
            })
            .catch(error => {
                console.error('Lỗi tải hướng dẫn:', error);
                alert('Đã xảy ra lỗi khi kết nối đến API!');
            });
    });


    // Hàm hiển thị lịch sử sửa chữa
    function loadRepairHistory(serialNumber) {
        fetch(`https://pe-vnmbd-nvidia-cns.myfiinet.com/api/FixGuide/GetRepairHistoryBySerialNumber/${serialNumber}`, {
            method: 'GET',
            headers: {
                'Content-Type': 'application/json'
            }
        })
            .then(response => {
                if (!response.ok) {
                    throw new Error('Lỗi khi gọi API lấy lịch sử sửa chữa.');
                }
                return response.json();
            })
            .then(data => {
                console.log('Dữ liệu lịch sử:', data); // Kiểm tra dữ liệu trả về

                const historyTable = $('#history-table tbody');
                historyTable.empty(); // Xóa nội dung cũ của bảng

                if (!data || data.length === 0) {
                    historyTable.append('<tr><td colspan="5">Không có dữ liệu lịch sử sửa chữa.</td></tr>');
                    return;
                }

                // Lặp qua tất cả các hành động sửa chữa
                data.forEach(history => {
                    history.repairActions.forEach(action => {
                        console.log("ghi chu nhan duoc tu API la:", action.note);
                        historyTable.append(`
                        <tr>
                            <td>${new Date(action.actionTime).toLocaleString()}</td>
                            <td title = "${action.data1 || ''}">${action.data1}</td>
                            <td>${action.responsiblePerson}</td>
                            <td title = "${action.actionDescription || ''}">${action.actionDescription}</td>
                            <td title = "${action.note || ''}">${action.note}</td>
                        </tr>
                    `);
                    });
                });
            })
            .catch(error => {
                console.error('Lỗi:', error);
                //alert('Không thể tải lịch sử sửa chữa.');
            });
    }


    //Xử lý sự kiện lưu lịch sử sửa chữa ?
    $('#repair-form').on('submit', function (e) {
        e.preventDefault();

        // Lấy serialNumber từ input hoặc bảng
        let serialNumber = $('#serialNumber').val().trim();
        if (!serialNumber) {
            serialNumber = $('#sn-table tbody tr:first-child td:first-child').text().trim();
        }

        const description = $('#repairDescription').val().trim();
        const responsiblePerson = $('#repairPerson').val().trim();
        const note = $('#repairNote').val().trim();

        if (!serialNumber || !description) {
            alert('Vui lòng nhập đầy đủ thông tin sửa chữa!');
            return;
        }

        // Lấy thông tin từ bảng hoặc nguồn khác để xây dựng RepairHistory
        const modelName = $('#sn-table tbody tr:first-child td:nth-child(3)').text().trim(); // ModelName từ cột 2
        const productLine = $('#sn-table tbody tr:first-child td:nth-child(2)').text().trim(); // ProductLine từ cột 3
        const data1 = $('#sn-table tbody tr:first-child td:nth-child(6)').text().trim(); // Data1 từ cột 5

        // Xây dựng payload theo yêu cầu API
        const repairHistory = {
            id: 0, // Để trống hoặc mặc định, API sẽ xử lý
            serialNumber,
            modelName,
            productLine,
            repairTime: new Date().toISOString(), // Lấy thời gian hiện tại
            createdAt: new Date().toISOString(),
            repairActions: [
                {
                    id: 0, // Để trống hoặc mặc định
                    repairHistoryId: 0, // API sẽ tự xử lý
                    actionDescription: description,
                    actionTime: new Date().toISOString(),// Lấy thời gian hiện tại
                    responsiblePerson,
                    note,
                    data1,
                }
            ]
        };
        console.log('Payload:', repairHistory); // Debug dữ liệu gửi

        // Gửi dữ liệu qua API
        fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/FixGuide/SaveRepairHistoryWithActions', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(repairHistory),
        })
            .then((response) => {
                if (!response.ok) {
                    response.text().then((error) => console.error('Error Response:', error));
                    throw new Error('Lỗi khi lưu thông tin sửa chữa.');
                }
                return response.text();
            })
            .then((data) => {
                alert(data);
                // Tải lại lịch sử sửa chữa
                loadRepairHistory(serialNumber);

                //Làm trống 2 ô nhập khi update thành công
                $('#repairDescription').val('');
                $('#repairNote').val('');
            })
            .catch((error) => console.error('Lỗi:', error));
    });

});
