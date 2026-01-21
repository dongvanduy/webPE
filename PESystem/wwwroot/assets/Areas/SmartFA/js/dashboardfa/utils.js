export const utils = {
    hasDataTable() {
        return Boolean(window.$ && $.fn && $.fn.DataTable);
    },
    destroyDataTable(selector) {
        if (!this.hasDataTable()) return;
        if ($.fn.DataTable.isDataTable(selector)) {
            const instance = $(selector).DataTable();
            instance.clear();
            instance.destroy();
        }
    },
    initializeDataTable(selector, config = {}) {
        if (!this.hasDataTable()) return null;
        const defaultConfig = {
            paging: true,
            searching: true,
            ordering: false,
            scrollX: true,
            autoWidth: false
        };
        return $(selector).DataTable({ ...defaultConfig, ...config });
    },
    truncateText(text, maxLength) {
        return (text && typeof text === 'string' && text.length > maxLength)
            ? `${text.substring(0, maxLength)}...`
            : text || '';
    },
    getValueIgnoreCase(item, key, defaultValue = '') {
        if (!item || !key) return defaultValue;
        if (Object.prototype.hasOwnProperty.call(item, key)) {
            const direct = item[key];
            if (direct !== undefined && direct !== null && direct !== '') {
                return direct;
            }
        }
        const lowerKey = key.toString().toLowerCase();
        for (const prop in item) {
            if (!Object.prototype.hasOwnProperty.call(item, prop)) continue;
            if (prop.toString().toLowerCase() === lowerKey) {
                const value = item[prop];
                if (value !== undefined && value !== null && value !== '') {
                    return value;
                }
            }
        }
        return defaultValue;
    },
    getTextValue(item, keys, fallback = '') {
        if (!item) return fallback;
        const keyList = Array.isArray(keys) ? keys : [keys];
        for (const key of keyList) {
            const value = this.getValueIgnoreCase(item, key);
            if (value !== undefined && value !== null && value !== '') {
                return value.toString();
            }
        }
        return fallback;
    },
    createCell(row, value, maxLength = 30, titleValue) {
        const cell = document.createElement('td');
        const text = (value ?? '').toString();
        cell.textContent = this.truncateText(text, maxLength);
        cell.title = titleValue ?? text;
        row.appendChild(cell);
        return cell;
    },
    safeParseDate(value) {
        if (!value) return null;
        if (value instanceof Date) {
            return isNaN(value) ? null : value;
        }
        const parsed = new Date(value);
        return isNaN(parsed) ? null : parsed;
    },
    calculateStatusAging(value) {
        const date = this.safeParseDate(value);
        if (!date) {
            return { hours: '', display: '' };
        }
        const now = new Date();
        let diffMs = now.getTime() - date.getTime();
        if (diffMs < 0) {
            diffMs = 0;
        }
        const hours = diffMs / 36e5;
        const totalMinutes = Math.floor(diffMs / 60000);
        const days = Math.floor(totalMinutes / (60 * 24));
        const hoursPart = Math.floor((totalMinutes - days * 24 * 60) / 60);
        const minutes = totalMinutes % 60;
        const parts = [];
        if (days) parts.push(`${days}d`);
        if (hoursPart) parts.push(`${hoursPart}h`);
        if (minutes || parts.length === 0) parts.push(`${minutes}m`);
        return {
            hours: hours.toFixed(2),
            display: parts.join(' ')
        };
    },
    formatDateTime(dt) {
        const pad = (n) => n.toString().padStart(2, '0');
        return `${dt.getFullYear()}-${pad(dt.getMonth() + 1)}-${pad(dt.getDate())}T${pad(dt.getHours())}:${pad(dt.getMinutes())}`;
    },
    formatDateTimeDisplay(value) {
        const date = this.safeParseDate(value);
        if (!date) {
            return value || '';
        }
        const pad = (n) => n.toString().padStart(2, '0');
        return `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
    },
    formatDisplayRange(start, end) {
        if (!(start instanceof Date) || !(end instanceof Date) || isNaN(start) || isNaN(end)) {
            return '';
        }
        const pad = (n) => n.toString().padStart(2, '0');
        const format = (date) => `${pad(date.getDate())}/${pad(date.getMonth() + 1)} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
        return `${format(start)} - ${format(end)}`;
    },
    resolveInventoryStatusValue(item) {
        const status = this.getValueIgnoreCase(item, 'STATUS')
            || this.getValueIgnoreCase(item, 'status')
            || this.getValueIgnoreCase(item, 'DATA11')
            || '';
        return status ? status.toString().trim() : '';
    },
    getSerialForLocation(item) {
        if (!item) return '';
        const source = (item.__sourceType || '').toString().toLowerCase();
        if (source === 'after') {
            return (this.getValueIgnoreCase(item, 'SFG') || this.getValueIgnoreCase(item, 'SERIAL_NUMBER') || this.getValueIgnoreCase(item, 'FG')).toString();
        }
        return (this.getValueIgnoreCase(item, 'SFG') || this.getValueIgnoreCase(item, 'FG') || this.getValueIgnoreCase(item, 'SERIAL_NUMBER') || '').toString();
    }
};
