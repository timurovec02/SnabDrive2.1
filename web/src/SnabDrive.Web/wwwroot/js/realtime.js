// Необязательный SignalR-клиент для браузера.
//
// Основной канал realtime-обновлений в Blazor Server — сам циркул (серверная шина событий),
// поэтому таблица обновляется и без этого файла. Скрипт добавляет браузерные уведомления
// о чужих изменениях для тех, кто хочет получать их даже на неактивной вкладке.
//
// Чтобы включить: установите JS-клиент SignalR рядом с приложением
//   npm i @microsoft/signalr
//   copy node_modules\@microsoft\signalr\dist\browser\signalr.min.js wwwroot\lib\signalr\signalr.min.js
// и раскомментируйте подключение в Components/App.razor.
(function () {
    'use strict';

    var HUB_URL = '/hubs/registry';
    var SCRIPT_SRC = 'lib/signalr/signalr.min.js';

    function loadScript(src) {
        return new Promise(function (resolve, reject) {
            var script = document.createElement('script');
            script.src = src;
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }

    function notify(text) {
        if (!('Notification' in window) || Notification.permission !== 'granted') {
            return;
        }

        try {
            new Notification('SnabDrive', { body: text });
        } catch (e) {
            // Уведомления недоступны — не критично.
        }
    }

    function start() {
        if (typeof window.signalR === 'undefined') {
            console.info('[SnabDrive] JS-клиент SignalR не найден, работаем только по каналу Blazor.');
            return;
        }

        var connection = new window.signalR.HubConnectionBuilder()
            .withUrl(HUB_URL)
            .withAutomaticReconnect()
            .build();

        connection.on('registryChanged', function (event) {
            if (!event || !event.humanText) {
                return;
            }

            notify(event.humanText);
        });

        connection.on('presenceChanged', function () {
            // Онлайн-список в шапке обновляется сервером, здесь ничего делать не нужно.
        });

        connection.start().catch(function (error) {
            console.warn('[SnabDrive] Не удалось подключиться к хабу:', error);
        });
    }

    if ('Notification' in window && Notification.permission === 'default') {
        Notification.requestPermission();
    }

    loadScript(SCRIPT_SRC).then(start).catch(function () {
        console.info('[SnabDrive] Файл ' + SCRIPT_SRC + ' отсутствует — SignalR-виджет отключён.');
    });
})();
