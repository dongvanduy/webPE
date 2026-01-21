document.addEventListener('DOMContentLoaded', function () {
    // --- 1. KHAI BÁO BIẾN & CẤU HÌNH ---
    const pdStockApiBase = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/DdRepositorys';
    const productApiBase = 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Product';

    const searchOption = document.getElementById('search-options');
    const locationField = document.querySelector('.location-field');
    const entryBtn = document.getElementById('entry-btn');
    const entryPerson = document.getElementById('entryPerson')?.value || 'CurrentUser';

    // Khu vực nhập SN
    const snSection = document.getElementById('input-sn-option');
    const snInput = document.getElementById('sn-input');
    const snModelOutput = snSection.querySelector('textarea[name="modelName"]');
    const snLineOutput = snSection.querySelector('textarea[name="productLine"]');

    // Khu vực nhập Carton
    const cartonSection = document.getElementById('input-carton-option');
    const cartonInput = document.getElementById('carton-input');
    const cartonSNOutput = cartonSection.querySelector('textarea[name="serialNumber"]');
    const cartonModelOutput = cartonSection.querySelector('textarea[name="modelName"]');

    // Các ô hiển thị vị trí (Readonly)
    const shelfField = document.getElementById('shelf');
    const columnField = document.getElementById('column');
    const levelField = document.getElementById('level');
    const trayField = document.getElementById('tray');

    // Bảng hiển thị lịch sử trong vị trí
    const serialNumberInTrayBody = document.getElementById('serial-number-in-tray');
    const cartonInLocationBody = document.getElementById('carton-in-location');

    // --- HÀM LÀM MỚI FORM (GIỮ NGUYÊN SEARCH OPTION) ---
    const resetInputs = () => {
        // 1. Xóa dữ liệu các ô nhập liệu chính
        snInput.value = '';
        cartonInput.value = '';

        // 2. Xóa các ô hiển thị kết quả (Readonly)
        snModelOutput.value = '';
        snLineOutput.value = '';
        cartonSNOutput.value = '';
        cartonModelOutput.value = '';

        // 3. Xóa thông tin vị trí (để người dùng nhập vị trí mới)
        // Nếu bạn muốn giữ lại vị trí để bắn tiếp thì comment dòng dưới lại
        locationField.value = '';
        [shelfField, columnField, levelField, trayField].forEach(el => el.value = "");

        // 4. Reset bộ đếm
        document.getElementById('serial-count').textContent = 'Tổng số SN đã nhập: 0';

        // 5. Focus lại vào ô Vị trí để bắt đầu quy trình mới
        locationField.focus();
    };

    // --- 2. LOGIC ĐIỀU HƯỚNG & TÁCH VỊ TRÍ ---

    // Focus vào ô vị trí khi chọn chức năng
    searchOption.addEventListener('change', function () {
        toggleSections();
        if (this.value !== "Chọn chức năng") {
            locationField.focus();
        }
    });

    // Tách vị trí A23-K1 OR A23 và nhảy xuống ô nhập liệu
    locationField.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            const val = this.value.trim().toUpperCase();

            // Định dạng 1: A12-K1 (Kệ - Cột - Tầng - Khay)
            const regexFull = /^([A-Z])(\d)(\d)-K(\d+)$/;
            // Định dạng 2: A12 (Kệ - Cột)
            const regexShort = /^([A-Z])(\d+)$/;

            const matchFull = val.match(regexFull);
            const matchShort = val.match(regexShort);

            // Reset các ô hiển thị phụ trước khi điền mới
            [shelfField, columnField, levelField, trayField].forEach(el => el.value = "");

            if (matchFull) {
                // Xử lý A12-K1 -> Kệ A, Cột 1, Tầng 2, Khay 1
                shelfField.value = matchFull[1];
                columnField.value = matchFull[2];
                levelField.value = matchFull[3];
                trayField.value = matchFull[4];
                proceedToInput();
            }
            else if (matchShort) {
                // Xử lý A12 -> Kệ A, Cột 12
                shelfField.value = matchShort[1];
                columnField.value = matchShort[2];
                proceedToInput();
            }
            else {
                alert("Định dạng vị trí không hợp lệ! (VD: A12-K1 hoặc A12)");
                this.select();
            }

            // Hàm hỗ trợ nhảy con trỏ và load lịch sử
            function proceedToInput() {
                if (searchOption.value === 'sn-option') snInput.focus();
                else if (searchOption.value === 'carton-option') cartonInput.focus();
                //loadLocationStockList(val); // Load lịch sử tồn kho tại vị trí này
            }
        }
    });
    // --- 3. XỬ LÝ NHẬP LIỆU REAL-TIME (CHO MÁY QUÉT) ---

    // --- Hàm kiểm tra trùng mã ---
    const isDuplicate = (newValue, existingTextarea) => {
        if (!existingTextarea.value.trim()) return false;
        const existingLines = existingTextarea.value.trim().split('\n').map(line => {
            // Nếu là ô Carton, loại bỏ phần (số lượng) để so sánh mã thuần
            return line.split(' ')[0].trim().toUpperCase();
        });
        return existingLines.includes(newValue.toUpperCase());
    };

    // Gọi API lấy thông tin 1 SN
    const getSingleSNInfo = async (serial) => {
        try {
            const response = await fetch(`${productApiBase}/GetSNInfo?serialNumber=${serial}`);
            const result = await response.json();
            return {
                modelName: result.data?.modelName || "N/A",
                productLine: result.data?.productLine || "N/A"
            };
        } catch (error) {
            return { modelName: "Error", productLine: "Error" };
        }
    };

    // Sự kiện Enter cho ô SN
    // --- Cập nhật sự kiện Enter cho ô SN ---
    snInput.addEventListener('keydown', async (e) => {
        if (e.key === 'Enter') {
            setTimeout(async () => {
                const lines = snInput.value.trim().split('\n');
                const lastSN = lines[lines.length - 1].trim().toUpperCase();

                if (!lastSN) return;

                // Kiểm tra trùng SN trong danh sách hiện tại
                const occurences = lines.filter(l => l.trim().toUpperCase() === lastSN).length;
                if (occurences > 1) {
                    alert(`CẢNH BÁO: Serial Number [${lastSN}] đã bị trùng trong danh sách!`);
                    lines.pop();
                    snInput.value = lines.join('\n') + (lines.length > 0 ? '\n' : '');
                    return;
                }

                showSpinner();
                try {
                    const info = await getSingleSNInfo(lastSN);
                    snModelOutput.value += (snModelOutput.value ? '\n' : '') + info.modelName;
                    snLineOutput.value += (snLineOutput.value ? '\n' : '') + info.productLine;
                    updateCount(lines.length);
                } catch (err) {
                    alert("Lỗi kết nối server!");
                } finally {
                    hideSpinner();
                }
            }, 50);
        }
    });


    // --- Hàm gọi API cho 1 Carton (Nhận về 1 list SN bên trong) ---
    const getCartonDetails = async (cartonNo) => {
        try {
            const response = await fetch(`${pdStockApiBase}/GetR107ByInputList`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify([cartonNo]) // Gửi mã carton vừa quét
            });

            const result = await response.json();
            const dataList = result.data || []; // Đây là danh sách các SN trong thùng

            if (dataList.length > 0) {
                // Lấy toàn bộ Serial Numbers và nối lại bằng dấu xuống dòng
                const allSNs = dataList.map(item => item.seriaL_NUMBER || "").join('\n');
                // Lấy Model Name (thường trong 1 thùng sẽ cùng 1 Model)
                const modelName = dataList[0].modeL_NAME || "N/A";

                return {
                    serialNumbers: allSNs,
                    modelName: modelName,
                    count: dataList.length
                };
            }
            return { serialNumbers: "Not Found", modelName: "N/A", count: 0 };
        } catch (error) {
            console.error("Lỗi API Carton:", error);
            return { serialNumbers: "Error", modelName: "Error", count: 0 };
        }
    };

    // --- Cập nhật sự kiện Keydown cho ô Carton ---

    // Sự kiện Enter cho ô Carton
    cartonInput.addEventListener('keydown', async (e) => {
        if (e.key === 'Enter') {
            // Delay 50ms để textarea nhận đủ ký tự từ máy quét
            setTimeout(async () => {
                const lines = cartonInput.value.trim().split('\n');
                const lastCarton = lines[lines.length - 1].trim();

                if (!lastCarton || lastCarton === "") return;
                //Kiem tra dup
                const occurences = lines.filter(l => l.split(' ')[0].trim().toUpperCase() === lastCarton).length;

                if (occurences > 1) {
                    alert(`CẢNH BÁO: Mã Carton [${lastCarton}] đã có trong danh sách vừa quét!`);
                    lines.pop(); // Xóa dòng vừa nhập trùng
                    cartonInput.value = lines.join('\n') + (lines.length > 0 ? '\n' : '');
                    return;
                }
                showSpinner();
                // Gọi hàm lấy chi tiết (đã viết ở bước trước)
                const info = await getCartonDetails(lastCarton);
                try {
                    if (info.count > 0) {
                        // 1. Cập nhật lại dòng mã Carton cuối cùng kèm số lượng
                        // Xóa dòng cuối hiện tại và thay bằng định dạng: VC... (15)
                        const currentCartonLines = cartonInput.value.trim().split('\n');
                        currentCartonLines[currentCartonLines.length - 1] = `${lastCarton} (${info.count})`;
                        cartonInput.value = currentCartonLines.join('\n') + '\n';

                        // 2. Hiển thị toàn bộ SN tìm được vào ô kết quả (cộng dồn)
                        cartonSNOutput.value += (cartonSNOutput.value ? '\n' : '') + info.serialNumbers;

                        // 3. Hiển thị Model Name (lặp lại đúng số lượng dòng của SN)
                        const modelLines = Array(info.count).fill(info.modelName).join('\n');
                        cartonModelOutput.value += (cartonModelOutput.value ? '\n' : '') + modelLines;

                        // 4. Cập nhật tổng số lượng hiển thị trên nhãn
                        const totalSNs = cartonSNOutput.value.trim().split('\n').length;
                        document.getElementById('serial-count').textContent = `Tổng số SN đã nhập: ${totalSNs}`;
                    } else {
                        alert(`Không tìm thấy dữ liệu cho thùng: ${lastCarton}`);
                    }
                } catch (err) {
                    alert("Lỗi kết nối server!");
                } finally {
                    hideSpinner(); // Ẩn spinner và mở khóa
                }
            }, 50);
        }
    });
    // --- 4. CÁC HÀM HỖ TRỢ UI ---

    function toggleSections() {
        const val = searchOption.value;
        cartonSection.style.display = (val === 'carton-option') ? 'flex' : 'none';
        snSection.style.display = (val === 'sn-option') ? 'flex' : 'none';
    }

    function updateCount(count) {
        document.getElementById('serial-count').textContent = `Tổng số mã đã quét: ${count}`;
    }

    //const loadLocationStockList = async (locationValue) => {
    //    try {
    //        const response = await fetch(`${pdStockApiBase}/GetAll`);
    //        const result = await response.json();
    //        const data = Array.isArray(result.data) ? result.data : [];
    //        const filtered = data.filter(item => (item.locationStock || '').trim() === locationValue);

    //        renderLocationRows(filtered, serialNumberInTrayBody);
    //        renderLocationRows(filtered, cartonInLocationBody);
    //    } catch (error) { console.error('Lỗi load lịch sử vị trí:', error); }
    //};

    const renderLocationRows = (rows, tableBody) => {
        if (!tableBody) return;
        tableBody.innerHTML = rows.map(row => `
            <tr>
                <td>${row.locationStock || '-'}</td>
                <td>${row.serialNumber || row.cartonNo || '-'}</td>
            </tr>
        `).join('');
    };

    // --- 5. LOGIC LƯU DỮ LIỆU (NHẬP KHO) ---

    entryBtn.addEventListener('click', async function () {
        const locationStock = locationField.value.trim();
        if (!locationStock) return alert("Vui lòng nhập vị trí!");

        let dataToPost = [];
        const isSNMode = searchOption.value === 'sn-option';

        if (isSNMode) {
            const sns = snInput.value.split('\n').map(v => v.trim()).filter(Boolean);
            const models = snModelOutput.value.split('\n').map(v => v.trim());
            dataToPost = sns.map((sn, i) => ({
                serialNumber: sn,
                modelName: models[i] || "",
                cartonNo: "N/A",
                locationStock: locationStock,
                entryOp: entryPerson
            }));
        } else {
            // 1. Lấy danh sách các dòng từ ô nhập Carton
            const rawCartons = cartonInput.value.split('\n').map(v => v.trim()).filter(Boolean);
            const sns = cartonSNOutput.value.split('\n').map(v => v.trim());
            const models = cartonModelOutput.value.split('\n').map(v => v.trim());

            let snIndex = 0;
            rawCartons.forEach((cartonLine) => {
                // GIẢI PHÁP: Sử dụng Regex để chỉ lấy phần mã trước khoảng trắng và dấu ngoặc
                // Ví dụ: "VC5176800516000058 (15)" -> sẽ chỉ lấy "VC5176800516000058"
                const cleanCarton = cartonLine.split(' ')[0].trim();

                // Lấy số lượng trong ngoặc để biết cần lặp bao nhiêu SN cho thùng này
                const countMatch = cartonLine.match(/\((\d+)\)/);
                const count = countMatch ? parseInt(countMatch[1]) : 0;

                for (let j = 0; j < count; j++) {
                    if (sns[snIndex]) { // Đảm bảo SN tồn tại
                        dataToPost.push({
                            serialNumber: sns[snIndex],
                            modelName: models[snIndex] || "",
                            cartonNo: cleanCarton, // Gửi mã đã làm sạch về DB
                            locationStock: locationStock,
                            entryOp: entryPerson
                        });
                    }
                    snIndex++;
                }
            });
        }

        if (dataToPost.length === 0) return alert("Không có dữ liệu!");

        try {
            const response = await fetch(`${pdStockApiBase}/PostToTable`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(dataToPost)
            });

            if (response.ok) {
                alert("Nhập kho thành công!");
                //location.reload(); // Reload để reset sạch sẽ dữ liệu
                resetInputs();
            } else {
                const err = await response.text();
                alert("Lỗi: " + err);
            }
        } catch (error) { alert("Lỗi kết nối Server!"); }
    });
});