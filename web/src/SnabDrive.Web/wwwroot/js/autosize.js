// Автоматическая высота многострочных полей: ячейки реестра и поля в карточках стадий.
//
// CSS-свойство field-sizing: content, которое уже используется в app.css, поддержано
// только в Chromium 123+. Скрипт дублирует то же поведение для всех браузеров: поле
// растёт по содержимому и не показывает полос прокрутки — как поля в стадиях.
(function () {
    'use strict';

    var SELECTOR = 'textarea.cell-input--area, textarea.wf-area';

    function resize(el) {
        if (!el || el.dataset.autosizing === '1') {
            return;
        }

        el.dataset.autosizing = '1';
        el.style.height = 'auto';
        el.style.height = (el.scrollHeight + 2) + 'px';
        delete el.dataset.autosizing;
    }

    function resizeAll(root) {
        var scope = root && root.querySelectorAll ? root : document;
        var list = scope.querySelectorAll(SELECTOR);
        for (var i = 0; i < list.length; i++) {
            resize(list[i]);
        }
    }

    function onEvent(e) {
        var el = e.target;
        if (el && el.matches && el.matches(SELECTOR)) {
            resize(el);
        }
    }

    // Рост во время ввода.
    document.addEventListener('input', onEvent, true);
    // Подбор высоты при входе в уже заполненное поле.
    document.addEventListener('focusin', onEvent, true);

    // Blazor отрисовал редактор ячейки или карточку стадии — сразу подбираем высоту.
    if (typeof MutationObserver !== 'undefined') {
        var observer = new MutationObserver(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                var nodes = mutations[i].addedNodes;
                for (var j = 0; j < nodes.length; j++) {
                    var node = nodes[j];
                    if (!node || node.nodeType !== 1) {
                        continue;
                    }
                    if (node.matches && node.matches(SELECTOR)) {
                        resize(node);
                    }
                    if (node.querySelectorAll) {
                        resizeAll(node);
                    }
                }
            }
        });
        observer.observe(document.documentElement, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { resizeAll(document); });
    } else {
        resizeAll(document);
    }
})();
