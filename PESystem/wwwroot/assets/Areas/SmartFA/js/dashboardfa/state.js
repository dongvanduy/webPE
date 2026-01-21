export const state = {
    statusModalInstance: null,
    statusModalElement: null,
    cioModalInstance: null,
    cioModalElement: null,
    allModalData: [],
    cioModalData: [],
    cioNoLocationData: [],
    cioNoLocationTableInstance: null,
    noLocationTrendDetails: [],
    previousDayNoLocationRecords: [],
    sapInOutRecords: [],
    sapChartInstance: null,
    cioModalExportType: 'default',
    inventoryStatusChartInstance: null,
    statusChartInstance: null,
    checkInOutChartInstance: null,
    checkInBeforeAfterChartInstance: null,
    noLocationTrendChartInstance: null,
    inventoryStatusDetailsMap: {}
};

export const config = {
    RECEIVING_STATUS_URL: 'https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/get-unique-status',
    INVENTORY_STATUS_KEYS: ['FA', 'CHECK_LIST', 'VI-RE', 'THAY LIỆU', 'RETEST', 'PROCESS', 'KHÓ SỬA', 'CHỜ TRẢ', 'KHÁC'],
    INVENTORY_STATUS_COLORS: {
        'FA': '#1976D2',
        'CHECK_LIST': '#7B1FA2',
        'VI-RE': '#F57C00',
        'THAY LIỆU': '#388E3C',
        'RETEST': '#E88E3C',
        'PROCESS': '#3F8E3C',
        'KHÓ SỬA': '#5F8E3C',
        'CHỜ TRẢ': '#2F8E3C',
        'KHÁC': '#546E7A'
    }
};
