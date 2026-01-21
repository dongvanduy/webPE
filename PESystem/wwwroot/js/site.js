document.addEventListener("DOMContentLoaded", () => {
    applyPerformanceMode();
    setupSidebar();
    setupUserInfo();
    setupModalGuards();
});


function applyPerformanceMode() {
    const body = document.body;
    if (!body) return;

    const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const lowCpu = navigator.hardwareConcurrency && navigator.hardwareConcurrency <= 4;
    const lowMemory = navigator.deviceMemory && navigator.deviceMemory <= 4;

    if (prefersReducedMotion || lowCpu || lowMemory) {
        body.classList.add("reduced-effects");
    }
}


/* ======================== SIDEBAR ======================== */
/* ======================== SIDEBAR (ĐÃ SỬA) ======================== */
function setupSidebar() {
    const sidebar = document.getElementById('sidebar');
    const dashboardStage = document.querySelector('.dashboard-stage');
    const toggleCheckbox = document.getElementById('toggleSidebarFixed');
    const toggleIcon = document.querySelector('.fixed-checkbox i');
    const sidebarToggleBtn = document.getElementById('sidebarToggle');
    const rootElement = document.documentElement;

    if (!sidebar || !dashboardStage || !toggleCheckbox) {
        console.error('Missing sidebar/dom elements');
        return;
    }

    if (setupSidebar.initialized) return;
    setupSidebar.initialized = true;

    const isMobile = () => window.innerWidth < 768;

    /* --- ĐƯA HÀM NÀY LÊN TRÊN ĐẦU ĐỂ TRÁNH LỖI HOISTING/MINIFY --- */
    /* Core logic - setSidebarState */
    const setSidebarState = (isFixed) => {
        const mobile = isMobile();
        const shouldExpand = isFixed && !mobile;

        rootElement.classList.toggle('sidebar-expanded', shouldExpand);

        if (mobile) {
            sidebar.classList.remove('open', 'sidebar-hover');
            return;
        }

        sidebar.classList.remove('mobile-show');
        sidebar.classList.toggle('open', isFixed);
    };

    /* Mobile toggle */
    sidebarToggleBtn?.addEventListener('click', () => {
        sidebar.classList.toggle('mobile-show');
    });

    /* Update icon */
    const updateToggleIcon = (isFixed) => {
        // Thêm optional chaining (?) để tránh lỗi nếu không tìm thấy icon
        toggleIcon?.classList.toggle('ri-record-circle-line', isFixed);
        toggleIcon?.classList.toggle('ri-checkbox-blank-circle-line', !isFixed);
    };

    /* Load state */
    const loadStoredState = () => {
        try {
            return localStorage.getItem('isSidebarFixed') === 'true';
        } catch (err) {
            console.warn('Unable to read sidebar fixed state', err);
            return false;
        }
    };

    const storeState = (value) => {
        try {
            localStorage.setItem('isSidebarFixed', value);
        } catch (err) {
            console.warn('Unable to persist sidebar fixed state', err);
        }
    };

    /* --- LOGIC CHÍNH CHẠY SAU KHI ĐÃ KHAI BÁO CÁC HÀM --- */
    const isFixed = loadStoredState();
    toggleCheckbox.checked = isFixed;

    // Bây giờ gọi hàm này sẽ an toàn tuyệt đối
    setSidebarState(isFixed);
    updateToggleIcon(isFixed);

    /* Checkbox change */
    toggleCheckbox.addEventListener('change', (e) => {
        const val = e.target.checked;
        storeState(val);
        setSidebarState(val);
        updateToggleIcon(val);
    });

    /* Hover expand */
    sidebar.addEventListener('mouseenter', () => {
        if (!toggleCheckbox.checked && !isMobile()) {
            sidebar.classList.add('sidebar-hover');
        }
    });

    sidebar.addEventListener('mouseleave', () => {
        if (!toggleCheckbox.checked && !isMobile()) {
            sidebar.classList.remove('sidebar-hover');
        }
    });

    /* Resize */
    window.addEventListener('resize', () => {
        setSidebarState(toggleCheckbox.checked);
    });
}
/* ======================== USER INFO DROPDOWN ======================== */
function setupUserInfo() {
    const avatarBtn = document.getElementById("avatarBtn");
    const userInfoCard = document.getElementById("userInfoCard");

    if (!avatarBtn || !userInfoCard) return;

    avatarBtn.addEventListener("click", (e) => {
        e.preventDefault();
        e.stopPropagation();
        userInfoCard.classList.toggle("show");
    });

    document.addEventListener("click", (e) => {
        if (!userInfoCard.contains(e.target) && !avatarBtn.contains(e.target)) {
            userInfoCard.classList.remove("show");
        }
    });

    window.addEventListener("resize", () => {
        userInfoCard.classList.remove("show");
    });
}


/* ======================== SWEET ALERT HELPERS ======================== */
function showError(message) {
    Swal.fire({
        icon: 'error',
        title: 'Lỗi',
        text: message
    });
}

function showSuccess(message) {
    Swal.fire({ icon: "success", title: message });
}

function showWarning(message) {
    Swal.fire({ icon: "warning", title: message });
}

function showInfo(message) {
    Swal.fire({ icon: "info", title: message });
}


/* ======================== SPINNER ======================== */
function showSpinner() {
    document.getElementById("spinner-overlay").style.display = "flex";
}

function hideSpinner() {
    const spinner = document.getElementById("spinner-overlay");
    if (spinner) spinner.style.display = "none";

    clearOrphanModalArtifacts();
}


/* ======================== MODAL GUARDRAILS ======================== */
function setupModalGuards() {
    // Clean up any stale backdrops that may have persisted from previous navigations
    clearOrphanModalArtifacts();
    syncModalStacking();

    // When any modal begins to show, make sure old artifacts are cleared first
    document.addEventListener("show.bs.modal", () => {
        clearOrphanModalArtifacts();
        // Stack adjustment after Bootstrap injects the backdrop
        setTimeout(syncModalStacking, 0);
    });

    document.addEventListener("shown.bs.modal", syncModalStacking);

    // When a modal hides, clear unused backdrops and body classes
    document.addEventListener("hidden.bs.modal", () => {
        clearOrphanModalArtifacts();
        syncModalStacking();
    });
}

function clearOrphanModalArtifacts() {
    const visibleModals = document.querySelectorAll(".modal.show");

    if (visibleModals.length === 0) {
        document.querySelectorAll(".modal-backdrop").forEach(backdrop => backdrop.remove());
        document.body.classList.remove("modal-open");
        document.body.style.removeProperty("padding-right");
        document.body.style.removeProperty("overflow");
    }
}

function syncModalStacking() {
    const backdrops = Array.from(document.querySelectorAll(".modal-backdrop"));
    const modals = Array.from(document.querySelectorAll(".modal.show"));
    const BASE_BACKDROP_Z = 1045;
    const BASE_MODAL_Z = 1055;
    const STACK_STEP = 20;

    backdrops.forEach((backdrop, index) => {
        backdrop.style.zIndex = BASE_BACKDROP_Z + (index * STACK_STEP);
    });

    modals.forEach((modal, index) => {
        modal.style.zIndex = BASE_MODAL_Z + (index * STACK_STEP);
    });

    if (modals.length > 0) {
        document.body.classList.add("modal-open");
    }
}