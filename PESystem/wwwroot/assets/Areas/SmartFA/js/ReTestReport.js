document.addEventListener('DOMContentLoaded', function () {
    // Ensure Chart.js and plugins are loaded
    if (typeof Chart === 'undefined' || typeof window.ChartDataLabels === 'undefined') {
        console.error('Chart.js or ChartDataLabels plugin is not loaded.');
        alert('Required charting libraries failed to load. Please check your internet connection or script sources.');
        return;
    }

    // Register the datalabels plugin
    Chart.register(window.ChartDataLabels);

    // Get current time
    const now = new Date();
    const currentHour = now.getUTCHours() + 7; // Adjust for +07 timezone
    const currentDate = now.toISOString().split('T')[0];
    let dayStart, dayEnd, nightStart, nightEnd;

    // Determine date ranges based on current time (08:48 AM +07, within 07:30–19:30)
    if (currentHour >= 7 && currentHour < 19) { // 07:30–19:30
        dayStart = new Date(now.setUTCHours(7, 30, 0, 0)).toISOString(); // Corrected to 07:30
        dayEnd = new Date(now.setUTCHours(19, 30, 0, 0)).toISOString();
        nightStart = new Date(now.setUTCHours(19, 30, 0, 0) - 24 * 60 * 60 * 1000).toISOString();
        nightEnd = new Date(now.setUTCHours(7, 30, 0, 0)).toISOString();
    } else if (currentHour >= 19 || currentHour < 7) { // 19:30–07:30
        if (currentHour >= 19 && currentHour <= 23) { // 19:30–24:00
            dayStart = new Date(now.setUTCHours(7, 30, 0, 0)).toISOString();
            dayEnd = new Date(now.setUTCHours(19, 30, 0, 0)).toISOString();
            nightStart = new Date(now.setUTCHours(19, 30, 0, 0)).toISOString();
            nightEnd = new Date(now.setUTCHours(7, 30, 0, 0) + 24 * 60 * 60 * 1000).toISOString();
        } else { // 00:00–07:30
            dayStart = new Date(now.setUTCHours(7, 30, 0, 0) - 24 * 60 * 60 * 1000).toISOString();
            dayEnd = new Date(now.setUTCHours(19, 30, 0, 0) - 24 * 60 * 60 * 1000).toISOString();
            nightStart = new Date(now.setUTCHours(19, 30, 0, 0) - 24 * 60 * 60 * 1000).toISOString();
            nightEnd = new Date(now.setUTCHours(7, 30, 0, 0)).toISOString();
        }
    }

    // Fetch 7-day data from summary-retest-result
    fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SFC/summary-retest-result')
        .then(response => {
            if (!response.ok) {
                throw new Error('Network response was not ok');
            }
            return response.json();
        })
        .then(sevenDayData => {
            // Sort 7-day data by TEST_DATE
            sevenDayData.sort((a, b) => new Date(a.TEST_DATE) - new Date(b.TEST_DATE));

            // Process 7-day data for day and night shifts
            const dayShiftSevenData = sevenDayData.filter(item => item.TIME_FRAME === '07:30–19:30');
            const nightShiftSevenData = sevenDayData.filter(item => item.TIME_FRAME === '19:30–07:30');

            // Get all unique dates for 7-day data
            const allSevenDates = [...new Set(sevenDayData.map(item => item.TEST_DATE.split('T')[0]))].sort();

            // Format labels for 7-day data
            const formatLabel = (dateStr) => {
                const date = new Date(dateStr + 'T00:00:00');
                return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
            };
            const sevenLabels = allSevenDates.map(formatLabel);

            // Prepare datasets for 7-day day shift
            const dayShiftSevenPassData = allSevenDates.map(date => {
                const item = dayShiftSevenData.find(d => d.TEST_DATE.split('T')[0] === date);
                return item ? item.PASS_COUNT : 0;
            });
            const dayShiftSevenFailData = allSevenDates.map(date => {
                const item = dayShiftSevenData.find(d => d.TEST_DATE.split('T')[0] === date);
                return item ? item.FAIL_COUNT : 0;
            });
            const dayShiftSevenTotalData = allSevenDates.map(date => {
                const item = dayShiftSevenData.find(d => d.TEST_DATE.split('T')[0] === date);
                return item ? item.TOTAL_SN_TESTED : 0;
            });

            // Prepare datasets for 7-day night shift
            const nightShiftSevenPassData = allSevenDates.map(date => {
                const item = nightShiftSevenData.find(d => d.TEST_DATE.split('T')[0] === date);
                return item ? item.PASS_COUNT : 0;
            });
            const nightShiftSevenFailData = allSevenDates.map(date => {
                const item = nightShiftSevenData.find(d => d.TEST_DATE.split('T')[0] === date);
                return item ? item.FAIL_COUNT : 0;
            });
            const nightShiftSevenTotalData = allSevenDates.map(date => {
                const item = nightShiftSevenData.find(d => d.TEST_DATE.split('T')[0] === date);
                return item ? item.TOTAL_SN_TESTED : 0;
            });

            // Calculate global max for 7-day y-axis
            let allSevenTotals = [];
            allSevenDates.forEach(date => {
                const dayItem = dayShiftSevenData.find(d => d.TEST_DATE.split('T')[0] === date);
                if (dayItem) allSevenTotals.push(dayItem.TOTAL_SN_TESTED);
                const nightItem = nightShiftSevenData.find(d => d.TEST_DATE.split('T')[0] === date);
                if (nightItem) allSevenTotals.push(nightItem.TOTAL_SN_TESTED);
            });
            const sevenGlobalMax = Math.max(...allSevenTotals, 0);

            // Common options for 7-day scales
            const sevenCommonScaleOptions = {
                beginAtZero: true,
                max: sevenGlobalMax,
                title: {
                    display: true,
                    text: 'Count'
                }
            };

            // Create 7-day Day Shift Chart
            const dayShiftCtx = document.getElementById('dayShiftChart').getContext('2d');
            new Chart(dayShiftCtx, {
                type: 'bar',
                data: {
                    labels: sevenLabels,
                    datasets: [
                        {
                            type: 'bar',
                            label: 'Pass',
                            data: dayShiftSevenPassData,
                            backgroundColor: 'rgba(75, 192, 192, 0.6)',
                            borderColor: 'rgba(75, 192, 192, 1)',
                            borderWidth: 1
                        },
                        {
                            type: 'bar',
                            label: 'Fail',
                            data: dayShiftSevenFailData,
                            backgroundColor: 'rgba(255, 99, 132, 0.6)',
                            borderColor: 'rgba(255, 99, 132, 1)',
                            borderWidth: 1
                        },
                        {
                            type: 'line',
                            label: 'Total SN Tested',
                            data: dayShiftSevenTotalData,
                            borderColor: 'rgb(54, 162, 235)',
                            backgroundColor: 'rgba(54, 162, 235, 0.1)',
                            tension: 0.1,
                            fill: false,
                            yAxisID: 'y'
                        }
                    ]
                },
                options: {
                    plugins: {
                        title: {
                            display: true,
                            text: 'Day Shift Retest Results (07:30–19:30)'
                        },
                        datalabels: {
                            display: true,
                            color: 'black',
                            font: {
                                weight: 'bold'
                            },
                            formatter: (value, ctx) => {
                                if (ctx.dataset.type === 'line') {
                                    return value > 0 ? value : '';
                                }
                                return value > 0 ? value : '';
                            },
                            anchor: (ctx) => ctx.dataset.type === 'line' ? 'end' : 'center',
                            align: (ctx) => ctx.dataset.type === 'line' ? 'top' : 'center',
                            offset: (ctx) => ctx.dataset.type === 'line' ? 4 : 0
                        }
                    },
                    scales: {
                        x: {
                            title: {
                                display: true,
                                text: 'Test Date'
                            }
                        },
                        y: sevenCommonScaleOptions
                    }
                },
                plugins: [window.ChartDataLabels]
            });

            // Create 7-day Night Shift Chart
            const nightShiftCtx = document.getElementById('nightShiftChart').getContext('2d');
            new Chart(nightShiftCtx, {
                type: 'bar',
                data: {
                    labels: sevenLabels,
                    datasets: [
                        {
                            type: 'bar',
                            label: 'Pass',
                            data: nightShiftSevenPassData,
                            backgroundColor: 'rgba(75, 192, 192, 0.6)',
                            borderColor: 'rgba(75, 192, 192, 1)',
                            borderWidth: 1
                        },
                        {
                            type: 'bar',
                            label: 'Fail',
                            data: nightShiftSevenFailData,
                            backgroundColor: 'rgba(255, 99, 132, 0.6)',
                            borderColor: 'rgba(255, 99, 132, 1)',
                            borderWidth: 1
                        },
                        {
                            type: 'line',
                            label: 'Total SN Tested',
                            data: nightShiftSevenTotalData,
                            borderColor: 'rgb(54, 162, 235)',
                            backgroundColor: 'rgba(54, 162, 235, 0.1)',
                            tension: 0.1,
                            fill: false,
                            yAxisID: 'y'
                        }
                    ]
                },
                options: {
                    plugins: {
                        title: {
                            display: true,
                            text: 'Night Shift Retest Results (19:30–07:30)'
                        },
                        datalabels: {
                            display: true,
                            color: 'black',
                            font: {
                                weight: 'bold'
                            },
                            formatter: (value, ctx) => {
                                if (ctx.dataset.type === 'line') {
                                    return value > 0 ? value : '';
                                }
                                return value > 0 ? value : '';
                            },
                            anchor: (ctx) => ctx.dataset.type === 'line' ? 'end' : 'center',
                            align: (ctx) => ctx.dataset.type === 'line' ? 'top' : 'center',
                            offset: (ctx) => ctx.dataset.type === 'line' ? 4 : 0
                        }
                    },
                    scales: {
                        x: {
                            title: {
                                display: true,
                                text: 'Test Date'
                            }
                        },
                        y: sevenCommonScaleOptions
                    }
                },
                plugins: [window.ChartDataLabels]
            });

            // Fetch today's data for day shift from summary-retest-model
            fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SFC/summary-retest-model', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ startTime: dayStart, endTime: dayEnd })
            })
                .then(response => {
                    if (!response.ok) {
                        throw new Error('Network response was not ok for day shift');
                    }
                    return response.json();
                })
                .then(dayTodayData => {
                    // Sort by TOTAL_TESTS descending
                    dayTodayData.sort((a, b) => b.TOTAL_TESTS - a.TOTAL_TESTS);

                    const dayTodayLabels = dayTodayData.map(item => item.MODEL_NAME);
                    const dayTodayPassData = dayTodayData.map(item => item.PASS_COUNT);
                    const dayTodayFailData = dayTodayData.map(item => item.FAIL_COUNT);
                    const dayTodayTotalData = dayTodayData.map(item => item.TOTAL_TESTS);

                    const dayTodayMax = Math.max(...dayTodayTotalData, 0);

                    const dayTodayScaleOptions = {
                        beginAtZero: true,
                        max: dayTodayMax,
                        title: {
                            display: true,
                            text: 'Count'
                        }
                    };

                    const dayShiftTodayCtx = document.getElementById('dayShiftTodayChart').getContext('2d');
                    new Chart(dayShiftTodayCtx, {
                        type: 'bar',
                        data: {
                            labels: dayTodayLabels,
                            datasets: [
                                {
                                    type: 'bar',
                                    label: 'Pass',
                                    data: dayTodayPassData,
                                    backgroundColor: 'rgba(75, 192, 192, 0.6)',
                                    borderColor: 'rgba(75, 192, 192, 1)',
                                    borderWidth: 1
                                },
                                {
                                    type: 'bar',
                                    label: 'Fail',
                                    data: dayTodayFailData,
                                    backgroundColor: 'rgba(255, 99, 132, 0.6)',
                                    borderColor: 'rgba(255, 99, 132, 1)',
                                    borderWidth: 1
                                },
                                {
                                    type: 'line',
                                    label: 'Total Tests',
                                    data: dayTodayTotalData,
                                    borderColor: 'rgb(54, 162, 235)',
                                    backgroundColor: 'rgba(54, 162, 235, 0.1)',
                                    tension: 0.1,
                                    fill: false,
                                    yAxisID: 'y'
                                }
                            ]
                        },
                        options: {
                            plugins: {
                                title: {
                                    display: true,
                                    text: 'Day Shift Retest Capacity Today (07:30–19:30)'
                                },
                                datalabels: {
                                    display: true,
                                    color: 'black',
                                    font: {
                                        weight: 'bold'
                                    },
                                    formatter: (value, ctx) => {
                                        if (ctx.dataset.type === 'line') {
                                            return value > 0 ? value : '';
                                        }
                                        return value > 0 ? value : '';
                                    },
                                    anchor: (ctx) => ctx.dataset.type === 'line' ? 'end' : 'center',
                                    align: (ctx) => ctx.dataset.type === 'line' ? 'top' : 'center',
                                    offset: (ctx) => ctx.dataset.type === 'line' ? 4 : 0
                                }
                            },
                            scales: {
                                x: {
                                    title: {
                                        display: true,
                                        text: 'Model Name'
                                    }
                                },
                                y: dayTodayScaleOptions
                            }
                        },
                        plugins: [window.ChartDataLabels]
                    });
                })
                .catch(error => console.error('Error fetching day shift today data:', error));

            // Fetch today's data for night shift from summary-retest-model
            fetch('https://pe-vnmbd-nvidia-cns.myfiinet.com/api/SFC/summary-retest-model', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ startTime: nightStart, endTime: nightEnd })
            })
                .then(response => {
                    if (!response.ok) {
                        throw new Error('Network response was not ok for night shift');
                    }
                    return response.json();
                })
                .then(nightTodayData => {
                    // Sort by TOTAL_TESTS descending
                    nightTodayData.sort((a, b) => b.TOTAL_TESTS - a.TOTAL_TESTS);

                    const nightTodayLabels = nightTodayData.map(item => item.MODEL_NAME);
                    const nightTodayPassData = nightTodayData.map(item => item.PASS_COUNT);
                    const nightTodayFailData = nightTodayData.map(item => item.FAIL_COUNT);
                    const nightTodayTotalData = nightTodayData.map(item => item.TOTAL_TESTS);

                    const nightTodayMax = Math.max(...nightTodayTotalData, 0);

                    const nightTodayScaleOptions = {
                        beginAtZero: true,
                        max: nightTodayMax,
                        title: {
                            display: true,
                            text: 'Count'
                        }
                    };

                    const nightShiftTodayCtx = document.getElementById('nightShiftTodayChart').getContext('2d');
                    new Chart(nightShiftTodayCtx, {
                        type: 'bar',
                        data: {
                            labels: nightTodayLabels,
                            datasets: [
                                {
                                    type: 'bar',
                                    label: 'Pass',
                                    data: nightTodayPassData,
                                    backgroundColor: 'rgba(75, 192, 192, 0.6)',
                                    borderColor: 'rgba(75, 192, 192, 1)',
                                    borderWidth: 1
                                },
                                {
                                    type: 'bar',
                                    label: 'Fail',
                                    data: nightTodayFailData,
                                    backgroundColor: 'rgba(255, 99, 132, 0.6)',
                                    borderColor: 'rgba(255, 99, 132, 1)',
                                    borderWidth: 1
                                },
                                {
                                    type: 'line',
                                    label: 'Total Tests',
                                    data: nightTodayTotalData,
                                    borderColor: 'rgb(54, 162, 235)',
                                    backgroundColor: 'rgba(54, 162, 235, 0.1)',
                                    tension: 0.1,
                                    fill: false,
                                    yAxisID: 'y'
                                }
                            ]
                        },
                        options: {
                            plugins: {
                                title: {
                                    display: true,
                                    text: 'Night Shift Retest Capacity Today (19:30–07:30)'
                                },
                                datalabels: {
                                    display: true,
                                    color: 'black',
                                    font: {
                                        weight: 'bold'
                                    },
                                    formatter: (value, ctx) => {
                                        if (ctx.dataset.type === 'line') {
                                            return value > 0 ? value : '';
                                        }
                                        return value > 0 ? value : '';
                                    },
                                    anchor: (ctx) => ctx.dataset.type === 'line' ? 'end' : 'center',
                                    align: (ctx) => ctx.dataset.type === 'line' ? 'top' : 'center',
                                    offset: (ctx) => ctx.dataset.type === 'line' ? 4 : 0
                                }
                            },
                            scales: {
                                x: {
                                    title: {
                                        display: true,
                                        text: 'Model Name'
                                    }
                                },
                                y: nightTodayScaleOptions
                            }
                        },
                        plugins: [window.ChartDataLabels]
                    });
                })
                .catch(error => console.error('Error fetching night shift today data:', error));
        })
        .catch(error => {
            console.error('Error fetching 7-day data:', error);
            alert('Failed to load retest data.');
        });
});