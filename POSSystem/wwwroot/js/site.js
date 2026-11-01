// Global UI helpers for Apex Retail POS & Inventory ERP
(function () {
    const STORAGE_KEY = 'apex_sidebar_collapsed';

    function syncSidebarIcon(isCollapsed) {
        const icon = document.getElementById('sidebarCollapseIcon');
        if (icon) {
            icon.className = isCollapsed ? 'bi bi-chevron-double-right' : 'bi bi-chevron-double-left';
        }
    }

    window.toggleAppSidebar = function () {
        const wrapper = document.getElementById('appWrapper');
        const sidebar = document.getElementById('appSidebar');
        const backdrop = document.getElementById('sidebarBackdrop');

        if (!wrapper || !sidebar) return;

        if (window.innerWidth < 992) {
            // Mobile / tablet drawer toggle
            const isOpen = sidebar.classList.toggle('show');
            if (backdrop) {
                backdrop.classList.toggle('show', isOpen);
            }
        } else {
            // Desktop minimize / expand toggle
            const isCollapsed = wrapper.classList.toggle('sidebar-collapsed');
            syncSidebarIcon(isCollapsed);
            try {
                localStorage.setItem(STORAGE_KEY, isCollapsed ? '1' : '0');
            } catch { }
        }
    };

    document.addEventListener('DOMContentLoaded', function () {
        const wrapper = document.getElementById('appWrapper');
        if (wrapper && window.innerWidth >= 992) {
            try {
                if (localStorage.getItem(STORAGE_KEY) === '1') {
                    wrapper.classList.add('sidebar-collapsed');
                    syncSidebarIcon(true);
                }
            } catch { }
        }

        // Auto-dismiss success alerts after 6 seconds
        setTimeout(function () {
            document.querySelectorAll('.alert-success.alert-dismissible').forEach(function (el) {
                if (window.bootstrap) {
                    const inst = bootstrap.Alert.getOrCreateInstance(el);
                    inst.close();
                }
            });
        }, 6000);
    });
})();
