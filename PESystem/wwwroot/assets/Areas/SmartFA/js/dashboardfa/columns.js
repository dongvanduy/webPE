import { utils } from './utils.js?v=2';

export const columns = {
    modalSN: [
        { getter: item => utils.getTextValue(item, ['SERIAL_NUMBER', 'FG']), maxLength: 20 },
        { getter: item => utils.getTextValue(item, 'PRODUCT_LINE'), maxLength: 20 },
        { getter: item => utils.getTextValue(item, 'MODEL_NAME'), maxLength: 20 },
        { getter: item => utils.getTextValue(item, 'WIP_GROUP'), maxLength: 20 },
        { getter: item => utils.getTextValue(item, 'TEST_GROUP'), maxLength: 20 },
        { getter: item => utils.getTextValue(item, ['TEST_CODE', 'ERROR_CODE']), maxLength: 20 },
        { getter: item => utils.getTextValue(item, ['DATA1', 'ERROR_DESC']), maxLength: 30 },
        { getter: item => utils.getTextValue(item, ['STATUS_FA', 'INVENTORY_STATUS']), maxLength: 20 },
        {
            getter: item => utils.getTextValue(item, 'STATUS_AGING_DISPLAY'),
            maxLength: 20,
            getTitle: item => {
                const display = utils.getTextValue(item, 'STATUS_AGING_DISPLAY');
                const hours = utils.getTextValue(item, 'STATUS_AGING_HOURS');
                return display && hours ? `${display} (${hours}h)` : display;
            }
        },
        { getter: item => utils.getTextValue(item, 'TESTER'), maxLength: 20 },
        { getter: item => utils.formatDateTimeDisplay(utils.getValueIgnoreCase(item, 'DATE3')), maxLength: 20 },
        { getter: item => utils.getTextValue(item, 'DATA13'), maxLength: 20 },
        { getter: item => utils.getTextValue(item, ['DATA18', 'LOCATION']), maxLength: 20 },
        { getter: item => utils.getTextValue(item, 'BORROW_STATUS'), maxLength: 20 }
    ],
    cioModal: {
        default: [
            { header: 'SFG', getter: item => utils.getTextValue(item, ['SFG', 'SERIAL_NUMBER']) },
            { header: 'FG', getter: item => utils.getTextValue(item, 'FG') },
            { header: 'MO_NUMBER', getter: item => utils.getTextValue(item, 'MO_NUMBER') },
            { header: 'MODEL_NAME', getter: item => utils.getTextValue(item, 'MODEL_NAME') },
            { header: 'PRODUCT_LINE', getter: item => utils.getTextValue(item, 'PRODUCT_LINE') },
            { header: 'WORK_FLAG', getter: item => utils.getTextValue(item, 'WORK_FLAG') },
            { header: 'ERROR_FLAG', getter: item => utils.getTextValue(item, 'ERROR_FLAG') },
            { header: 'LOCATION', getter: item => item.location || utils.getTextValue(item, ['DATA18', 'LOCATION']) },
            { header: 'AGING_HOURS', getter: item => utils.getTextValue(item, 'AGING_HOURS') },
            { header: 'P_SENDER', getter: item => utils.getTextValue(item, 'P_SENDER') },
            { header: 'IN_DATETIME', getter: item => utils.formatDateTimeDisplay(utils.getValueIgnoreCase(item, 'IN_DATETIME')) },
            { header: 'OUT_DATETIME', getter: item => utils.formatDateTimeDisplay(utils.getValueIgnoreCase(item, 'OUT_DATETIME')) },
            { header: 'REPAIRER', getter: item => utils.getTextValue(item, 'REPAIRER') },
            { header: 'STATION_NAME', getter: item => utils.getTextValue(item, 'STATION_NAME') },
            { header: 'ERROR_CODE', getter: item => utils.getTextValue(item, 'ERROR_CODE') },
            { header: 'ERROR_DESC', getter: item => utils.getTextValue(item, 'ERROR_DESC') },
            { header: 'TYPE', getter: item => utils.getTextValue(item, ['CHECKIN_STATUS', 'TYPE']) }
        ],
        sap: [
            { header: 'SERIAL_NUMBER', getter: item => utils.getTextValue(item, 'SERIAL_NUMBER') },
            { header: 'GROUP_NAME', getter: item => utils.getTextValue(item, 'GROUP_NAME') },
            { header: 'MODEL_NAME', getter: item => utils.getTextValue(item, 'MODEL_NAME') },
            { header: 'PRODUCT_LINE', getter: item => utils.getTextValue(item, 'PRODUCT_LINE') },
            { header: 'MO_NUMBER', getter: item => utils.getTextValue(item, 'MO_NUMBER') },
            { header: 'IN_STATION_TIME', getter: item => utils.formatDateTimeDisplay(utils.getValueIgnoreCase(item, 'IN_STATION_TIME')) },
            { header: 'KEY_PART_NO', getter: item => utils.getTextValue(item, 'KEY_PART_NO') },
            { header: 'SHIPPING_SN2', getter: item => utils.getTextValue(item, 'SHIPPING_SN2') },
            { header: 'MSN', getter: item => utils.getTextValue(item, 'MSN') },
            { header: 'ATE_STATION_NO', getter: item => utils.getTextValue(item, 'ATE_STATION_NO') },
            { header: 'EMP_NO', getter: item => utils.getTextValue(item, 'EMP_NO') },
            { header: 'WIP_GROUP', getter: item => utils.getTextValue(item, 'WIP_GROUP') },
            { header: 'LOCATION', getter: item => item.location || utils.getTextValue(item, 'LOCATION') }
        ]
    }
};
