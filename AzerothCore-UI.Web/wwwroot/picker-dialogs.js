window.azerothCoreUi = window.azerothCoreUi || {};

window.azerothCoreUi.focusAndSelect = element => {
    if (!element) {
        return;
    }

    window.requestAnimationFrame(() => {
        element.focus();
        element.select();
    });
};
