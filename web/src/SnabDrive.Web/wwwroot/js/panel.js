// Прижимает всплывающую панель (история этапов) к кнопке, из которой её открыли.
// Панель живёт внутри прокручиваемой таблицы, поэтому позиционируется fixed — иначе
// контейнер с overflow: auto обрезал бы её. Если скрипт не загрузился, панель
// остаётся по центру экрана: см. .stage-history__panel в css/app.css.
(function () {
    'use strict';

    window.snabPanel = {
        place: function (anchor, panel) {
            if (!anchor || !panel || typeof anchor.getBoundingClientRect !== 'function') {
                return;
            }

            var rect = anchor.getBoundingClientRect();
            var width = panel.offsetWidth;
            var height = panel.offsetHeight;
            var margin = 8;

            var left = rect.left;
            if (left + width > window.innerWidth - margin) {
                left = Math.max(margin, window.innerWidth - width - margin);
            }

            var top = rect.bottom + 4;
            if (top + height > window.innerHeight - margin) {
                // снизу не помещается — раскрываем вверх от кнопки
                top = Math.max(margin, rect.top - height - 4);
            }

            panel.style.left = Math.round(left) + 'px';
            panel.style.top = Math.round(top) + 'px';
            panel.style.transform = 'none';
        }
    };
})();
