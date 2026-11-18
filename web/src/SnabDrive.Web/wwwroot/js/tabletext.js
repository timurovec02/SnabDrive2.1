// Размер текста в таблицах (.registry-grid, .data-table) отдельно от интерфейса.
// Значение хранится в localStorage и применяется к CSS-переменной --table-text-scale.
window.SnabDriveTableText = (function () {
    var KEY = "snabdrive.table-text-scale";
    var MIN = 0.8, MAX = 1.8;

    function clamp(v) {
        v = parseFloat(v);
        if (isNaN(v)) { return 1; }
        return Math.min(MAX, Math.max(MIN, v));
    }

    function apply(v) {
        document.documentElement.style.setProperty("--table-text-scale", String(clamp(v)));
    }

    function get() {
        try { return clamp(localStorage.getItem(KEY)); } catch (e) { return 1; }
    }

    function set(v) {
        v = clamp(v);
        apply(v);
        try { localStorage.setItem(KEY, String(v)); } catch (e) { }
        return v;
    }

    apply(get());

    return { get: get, set: set };
})();
