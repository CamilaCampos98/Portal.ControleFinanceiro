document.addEventListener("DOMContentLoaded", () => {
    const overlay = document.getElementById("loadingOverlay");
    if (!overlay) return;

    document.querySelectorAll("form").forEach((form) => {
        form.addEventListener("submit", (event) => {
            if (form.dataset.loadingMessage && form.dataset.submitting === "true") {
                event.preventDefault();
                return;
            }
            if (form.dataset.loadingMessage) {
                form.dataset.submitting = "true";
                const message = overlay.querySelector("[data-loading-text]");
                if (message) message.textContent = form.dataset.loadingMessage;
            }
            overlay.style.display = "flex";

            form.querySelectorAll('button[type="submit"]').forEach((button) => {
                button.classList.add("btn-loading");
                button.setAttribute("aria-busy", "true");
            });
        });
    });

    document.querySelectorAll("[data-loading-link]").forEach((link) => {
        link.addEventListener("click", (event) => {
            if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            overlay.style.display = "flex";
        });
    });

    window.addEventListener("pageshow", () => {
        overlay.style.display = "none";
        document.querySelectorAll("form[data-submitting]").forEach((form) => delete form.dataset.submitting);
        document.querySelectorAll("button.btn-loading").forEach((button) => {
            button.classList.remove("btn-loading");
            button.removeAttribute("aria-busy");
        });
    });
});
