(() => {
    const storagePrefix = "azerothcore.nav.";

    function initialiseNavigation() {
        const menu = document.querySelector(".nav-scrollable");
        if (!menu || menu.dataset.initialised === "true") {
            return;
        }

        menu.dataset.initialised = "true";

        menu.querySelectorAll("details[data-nav-group]").forEach(group => {
            const activeGroup = group.querySelector("a.active") !== null;
            const storedState = localStorage.getItem(storagePrefix + group.dataset.navGroup);

            if (activeGroup) {
                group.open = true;
            } else if (storedState !== null) {
                group.open = storedState === "open";
            }

            group.addEventListener("toggle", () => {
                localStorage.setItem(
                    storagePrefix + group.dataset.navGroup,
                    group.open ? "open" : "closed");
            });
        });

        menu.addEventListener("click", event => {
            if (!event.target.closest("a, form button")) {
                return;
            }

            const toggle = document.querySelector(".navbar-toggler");
            if (toggle?.checked) {
                toggle.checked = false;
            }
        });
    }

    function initialiseLayout() {
        initialiseNavigation();
    }

    document.addEventListener("DOMContentLoaded", initialiseLayout);
    document.addEventListener("enhancedload", initialiseLayout);
    initialiseLayout();
})();
