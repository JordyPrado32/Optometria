window.optometriaSession = {
    get: function (key) {
        return window.sessionStorage.getItem(key);
    },
    set: function (key, value) {
        window.sessionStorage.setItem(key, value);
    },
    remove: function (key) {
        window.sessionStorage.removeItem(key);
    },
    openRxFolder: function (folderPath) {
        if (!folderPath) {
            return;
        }

        const normalized = folderPath.startsWith("file:///")
            ? folderPath
            : "file:///" + folderPath.replace(/\\/g, "/");

        window.open(normalized, "_blank", "noopener,noreferrer");
    }
};
