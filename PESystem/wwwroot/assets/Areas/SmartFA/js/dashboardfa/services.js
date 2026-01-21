import { utils } from './utils.js?v=2';

export const services = {
    async fetchLocationMapBySerials(serials = [], options = {}) {
        const uniqueSerials = [...new Set((serials || []).filter(sn => sn))];
        if (!uniqueSerials.length) {
            return {};
        }
        try {
            const response = await fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/Search/FindLocations', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(uniqueSerials)
            });
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }
            const payload = await response.json();
            if (payload?.success && Array.isArray(payload.data)) {
                return payload.data.reduce((acc, entry) => {
                    const key = (entry.serialNumber || '').toString().trim().toUpperCase();
                    if (!key) return acc;
                    acc[key] = {
                        location: entry.location || '',
                        warehouse: entry.warehouse || '',
                        borrowStatus: entry.borrowStatus || ''
                    };
                    return acc;
                }, {});
            }
        } catch (error) {
            if (!options.silent) {
                console.error('FindFG error:', error);
            }
        }
        return {};
    },
    async attachLocationInfo(items = [], options = {}) {
        const normalized = Array.isArray(items) ? items.map(item => ({ ...item })) : [];
        const lookupSerials = [];
        normalized.forEach(item => {
            const key = utils.getSerialForLocation(item).trim().toUpperCase();
            item.__lookupKey = key;
            if (key) {
                lookupSerials.push(key);
            }
        });
        const locationMap = await services.fetchLocationMapBySerials(lookupSerials, options);
        normalized.forEach(item => {
            const existingLocation = (utils.getValueIgnoreCase(item, 'LOCATION') || utils.getValueIgnoreCase(item, 'DATA18') || '').toString().trim();
            if (existingLocation) {
                item.location = existingLocation;
            } else if (item.__lookupKey) {
                const locInfo = locationMap[item.__lookupKey];
                if (locInfo && (locInfo.location || locInfo.warehouse)) {
                    item.location = [locInfo.warehouse, locInfo.location].filter(Boolean).join(' - ');
                    if (!item.borrowStatus) {
                        item.borrowStatus = locInfo.borrowStatus || utils.getValueIgnoreCase(item, 'BORROW_STATUS');
                    }
                    if (!item.datA18 && locInfo.location) {
                        item.datA18 = locInfo.location;
                    }
                } else {
                    item.location = '';
                }
            } else {
                item.location = existingLocation;
            }
        });
        return { items: normalized, locationMap };
    },
    async fetchSapInOutData(startDate, endDate) {
        const result = { records: [], total: 0 };
        try {
            const url = new URL('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/CheckInOut/GetSAPInOut');
            if (startDate) url.searchParams.append('startDate', startDate);
            if (endDate) url.searchParams.append('endDate', endDate);
            const response = await fetch(url);
            if (!response.ok) {
                throw new Error(`GetSAPInOut error: ${response.status}`);
            }
            const data = await response.json();
            const groups = data?.groups || data?.Groups || [];
            const records = [];
            groups.forEach(group => {
                const groupName = group?.GroupName || group?.groupName || '';
                const details = group?.Details || group?.details || [];
                (details || []).forEach(detail => {
                    records.push({
                        ...detail,
                        GROUP_NAME: utils.getValueIgnoreCase(detail, 'GROUP_NAME') || groupName
                    });
                });
            });
            result.records = records;
            result.total = typeof data?.total === 'number' ? data.total : records.length;
        } catch (error) {
            console.error('fetchSapInOutData error:', error);
        }
        return result;
    },
    async fetchRepairTasksBySerials(serials = [], options = {}) {
        const uniqueSerials = [...new Set((serials || []).map(sn => (sn || '').toString().trim()).filter(Boolean))];
        if (!uniqueSerials.length) {
            return [];
        }
        const chunkSize = Number.isInteger(options.chunkSize) && options.chunkSize > 0 ? options.chunkSize : 80;
        const aggregated = [];
        for (let index = 0; index < uniqueSerials.length; index += chunkSize) {
            const chunk = uniqueSerials.slice(index, index + chunkSize);
            try {
                const response = await fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SearchFA/search', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ serialNumbers: chunk })
                });
                if (!response.ok) {
                    throw new Error(`HTTP ${response.status}`);
                }
                const payload = await response.json();
                if (Array.isArray(payload?.data)) {
                    aggregated.push(...payload.data);
                }
            } catch (error) {
                if (!options.silent) {
                    console.error('fetchRepairTasksBySerials error:', error);
                }
                throw error;
            }
        }
        return aggregated;
    },
    async fetchTonKhoNoLocationRange(start, end) {
        const url = new URL('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/CheckInOut/GetTonKhoSummary');
        url.searchParams.append('startDate', utils.formatDateTime(start));
        url.searchParams.append('endDate', utils.formatDateTime(end));
        const response = await fetch(url);
        if (!response.ok) {
            throw new Error(`GetTonKhoSummary error: ${response.status}`);
        }
        const tonKhoJson = await response.json();
        const before = (tonKhoJson?.beforeKanban?.data || []).map(item => ({ ...item, __sourceType: 'before' }));
        const after = (tonKhoJson?.afterKanban?.data || []).map(item => ({ ...item, __sourceType: 'after' }));
        const combined = [...before, ...after];
        const { items: enrichedItems } = await services.attachLocationInfo(combined, { silent: true });
        const noLocationItems = enrichedItems.filter(item => !item.location);
        return { enrichedItems, noLocationItems };
    }
};
