document.addEventListener("DOMContentLoaded", () => {
    const overlay = document.getElementById("loadingOverlay");
    if (!overlay) return;

    document.querySelectorAll("form").forEach((form) => {
        form.addEventListener("submit", () => {
            overlay.style.display = "flex";

            form.querySelectorAll('button[type="submit"]').forEach((button) => {
                button.classList.add("btn-loading");
                button.setAttribute("aria-busy", "true");
            });
        });
    });
});
