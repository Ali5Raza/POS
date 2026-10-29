// Global UI helpers for Apex Retail POS & Inventory ERP
document.addEventListener('DOMContentLoaded', function () {
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
