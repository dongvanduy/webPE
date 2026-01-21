import { state } from './state.js';
import { utils } from './utils.js?v=2';
import { feedback } from './feedback.js';

const workbookFactory = () => (typeof XLSX !== 'undefined' ? XLSX : null);

const downloadWorkbook = (worksheetData, sheetName, fileName) => {
    const xlsx = workbookFactory();
    if (!xlsx) {
        console.error('XLSX library is not available');
        return;
    }
    const workbook = xlsx.utils.book_new();
    const worksheet = xlsx.utils.json_to_sheet(worksheetData);
    xlsx.utils.book_append_sheet(workbook, worksheet, sheetName);
    xlsx.writeFile(workbook, fileName);
};

export const exporter = {
    exportModalData() {
        if (state.allModalData.length === 0) {
            console.error('Không có dữ liệu để xuất Excel!');
            feedback.showError('Không có dữ liệu để xuất!');
            return;
        }
        const worksheetData = state.allModalData.map(item => {
            const resolvedStatus = utils.resolveInventoryStatusValue(item);
            return {
                'SERIAL_NUMBER': utils.getValueIgnoreCase(item, 'SERIAL_NUMBER') || utils.getValueIgnoreCase(item, 'FG'),
                'PRODUCT_LINE': utils.getValueIgnoreCase(item, 'PRODUCT_LINE'),
                'MODEL_NAME': utils.getValueIgnoreCase(item, 'MODEL_NAME'),
                'WIP_GROUP': utils.getValueIgnoreCase(item, 'WIP_GROUP'),
                'TEST_GROUP': utils.getValueIgnoreCase(item, 'TEST_GROUP'),
                'ERROR_CODE': utils.getValueIgnoreCase(item, 'TEST_CODE'),
                'ERROR_DESC': utils.getValueIgnoreCase(item, 'DATA1'),
                'STATUS_FA': utils.getValueIgnoreCase(item, 'STATUS_FA') || resolvedStatus,
                'STATUS_AGING_HOURS': utils.getValueIgnoreCase(item, 'STATUS_AGING_HOURS'),
                'STATUS_AGING_DISPLAY': utils.getValueIgnoreCase(item, 'STATUS_AGING_DISPLAY'),
                'PERSON_CONFIRM': utils.getValueIgnoreCase(item, 'TESTER'),
                'TIME_CONFIRM': utils.formatDateTimeDisplay(utils.getValueIgnoreCase(item, 'DATE3')),
                'HANDLER': utils.getValueIgnoreCase(item, 'DATA13'),
                'LOCATION': utils.getValueIgnoreCase(item, 'DATA18') || utils.getValueIgnoreCase(item, 'LOCATION'),
                'BORROW_STATUS': utils.getValueIgnoreCase(item, 'BORROW_STATUS')
            };
        });
        downloadWorkbook(worksheetData, 'SerialNumbers', `SerialNumbers_${new Date().toISOString().slice(0, 10)}.xlsx`);
    },
    exportNoLocationData() {
        if (state.cioNoLocationData.length === 0) {
            feedback.showError('Không có dữ liệu để xuất!');
            return;
        }
        const worksheetData = state.cioNoLocationData.map(item => ({
            'TYPE': utils.getValueIgnoreCase(item, 'TYPE'),
            'SERIAL_NUMBER': utils.getValueIgnoreCase(item, 'SFG') || utils.getValueIgnoreCase(item, 'SERIAL_NUMBER'),
            'MODEL_NAME': utils.getValueIgnoreCase(item, 'MODEL_NAME'),
            'PRODUCT_LINE': utils.getValueIgnoreCase(item, 'PRODUCT_LINE'),
            'LOCATION': utils.getValueIgnoreCase(item, 'DATA18') || utils.getValueIgnoreCase(item, 'LOCATION'),
            'WIP_GROUP': utils.getValueIgnoreCase(item, 'WIP_GROUP'),
            'ERROR_FLAG': utils.getValueIgnoreCase(item, 'ERROR_FLAG'),
            'WORK_FLAG': utils.getValueIgnoreCase(item, 'WORK_FLAG')
        }));
        downloadWorkbook(worksheetData, 'TonKhoNoLocation', `TonKhoNoLocation_${new Date().toISOString().slice(0, 10)}.xlsx`);
    },
    exportCioData() {
        if (state.cioModalData.length === 0) {
            console.error('Không có dữ liệu để xuất Excel!');
            feedback.showError('Không có dữ liệu để xuất!');
            return;
        }
        let worksheetData = [];
        let sheetName = 'CheckInOut';
        let filePrefix = 'CheckInOut';
        if (state.cioModalExportType === 'sap') {
            worksheetData = state.cioModalData.map(item => ({
                'SERIAL_NUMBER': utils.getValueIgnoreCase(item, 'SERIAL_NUMBER') || '',
                'GROUP_NAME': utils.getValueIgnoreCase(item, 'GROUP_NAME') || '',
                'MODEL_NAME': utils.getValueIgnoreCase(item, 'MODEL_NAME') || '',
                'PRODUCT_LINE': utils.getValueIgnoreCase(item, 'PRODUCT_LINE') || '',
                'MO_NUMBER': utils.getValueIgnoreCase(item, 'MO_NUMBER') || '',
                'IN_STATION_TIME': utils.formatDateTimeDisplay(utils.getValueIgnoreCase(item, 'IN_STATION_TIME')),
                'KEY_PART_NO': utils.getValueIgnoreCase(item, 'KEY_PART_NO') || '',
                'SHIPPING_SN2': utils.getValueIgnoreCase(item, 'SHIPPING_SN2') || '',
                'MSN': utils.getValueIgnoreCase(item, 'MSN') || '',
                'ATE_STATION_NO': utils.getValueIgnoreCase(item, 'ATE_STATION_NO') || '',
                'EMP_NO': utils.getValueIgnoreCase(item, 'EMP_NO') || '',
                'WIP_GROUP': utils.getValueIgnoreCase(item, 'WIP_GROUP') || '',
                'LOCATION': utils.getValueIgnoreCase(item, 'LOCATION') || ''
            }));
            sheetName = 'SAPInOut';
            filePrefix = 'SAPInOut';
        } else {
            worksheetData = state.cioModalData.map(item => ({
                'SERIAL_NUMBER': utils.getValueIgnoreCase(item, 'SERIAL_NUMBER') || utils.getValueIgnoreCase(item, 'SFG'),
                'FG': utils.getValueIgnoreCase(item, 'FG'),
                'MODEL_NAME': utils.getValueIgnoreCase(item, 'MODEL_NAME'),
                'PRODUCT_LINE': utils.getValueIgnoreCase(item, 'PRODUCT_LINE'),
                'LOCATION': utils.getValueIgnoreCase(item, 'LOCATION'),
                'PERSON_IN': utils.getValueIgnoreCase(item, 'P_SENDER'),
                'IN_DATE': utils.formatDateTimeDisplay(utils.getValueIgnoreCase(item, 'IN_DATETIME')),
                'WIP_GROUP': utils.getValueIgnoreCase(item, 'WIP_GROUP'),
                'ERROR_FLAG': utils.getValueIgnoreCase(item, 'ERROR_FLAG'),
                'WORK_FLAG': utils.getValueIgnoreCase(item, 'WORK_FLAG'),
                'OUT_DATE': utils.formatDateTimeDisplay(utils.getValueIgnoreCase(item, 'OUT_DATETIME')),
                'REPAIRER': utils.getValueIgnoreCase(item, 'REPAIRER'),
                'STATION': utils.getValueIgnoreCase(item, 'STATION_NAME'),
                'ERROR_CODE': utils.getValueIgnoreCase(item, 'ERROR_CODE'),
                'ERROR_DESC': utils.getValueIgnoreCase(item, 'ERROR_DESC'),
                'TYPE': utils.getValueIgnoreCase(item, 'CHECKIN_STATUS') || item.__sourceType || ''
            }));
        }
        downloadWorkbook(worksheetData, sheetName, `${filePrefix}_${new Date().toISOString().slice(0, 10)}.xlsx`);
    }
};
