(() => {
    const storageKey = "grana-theme";
    const root = document.documentElement;

    try {
        if (localStorage.getItem(storageKey) === "dark") root.dataset.theme = "dark";
    } catch { /* O modo claro continua disponível se o armazenamento estiver bloqueado. */ }

    document.addEventListener("DOMContentLoaded", () => {
        const buttons = document.querySelectorAll("[data-theme-toggle]");
        const updateButtons = () => {
            const dark = root.dataset.theme === "dark";
            buttons.forEach((button) => {
                const label = dark ? "Ativar modo claro" : "Ativar modo escuro";
                button.setAttribute("aria-label", label);
                button.setAttribute("title", label);
                button.setAttribute("aria-pressed", String(dark));
                const icon = button.querySelector("i");
                if (icon) icon.className = dark ? "bi bi-sun" : "bi bi-moon-stars";
                const text = button.querySelector("[data-theme-label]");
                if (text) text.textContent = dark ? "Modo claro" : "Modo escuro";
            });
        };

        buttons.forEach((button) => button.addEventListener("click", () => {
            const dark = root.dataset.theme !== "dark";
            if (dark) root.dataset.theme = "dark";
            else delete root.dataset.theme;
            try { localStorage.setItem(storageKey, dark ? "dark" : "light"); } catch { /* Preferência só nesta página. */ }
            updateButtons();
        }));
        updateButtons();
    });
})();
