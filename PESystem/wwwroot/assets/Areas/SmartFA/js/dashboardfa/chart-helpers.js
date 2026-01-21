import { state } from './state.js';

export const chartHelpers = {
    getEcharts() {
        return (typeof window !== 'undefined' && window.echarts) ? window.echarts : null;
    },
    dispose(instanceName) {
        const chartInstance = state[instanceName];
        if (chartInstance && typeof chartInstance.dispose === 'function') {
            chartInstance.dispose();
        }
        state[instanceName] = null;
    },
    render(instanceName, elementId, option, events = {}) {
        const echartsLib = chartHelpers.getEcharts();
        const element = document.getElementById(elementId);
        if (!echartsLib || !element) {
            return null;
        }
        chartHelpers.dispose(instanceName);
        const chart = echartsLib.init(element);
        chart.setOption(option);
        Object.entries(events).forEach(([eventName, handler]) => {
            if (typeof handler === 'function') {
                chart.on(eventName, handler);
            }
        });
        state[instanceName] = chart;
        return chart;
    },
    showEmptyState(elementId, message) {
        const element = document.getElementById(elementId);
        if (element) {
            element.innerHTML = `<div class='text-center text-muted py-5'>${message}</div>`;
        }
    }
};
