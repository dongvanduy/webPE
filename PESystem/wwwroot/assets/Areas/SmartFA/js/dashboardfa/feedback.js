const getGlobalErrorHandler = () => {
    if (typeof window !== 'undefined' && typeof window.showError === 'function') {
        return window.showError.bind(window);
    }
    return (message) => console.error(message);
};

export const feedback = {
    showError(message) {
        getGlobalErrorHandler()(message);
    }
};
