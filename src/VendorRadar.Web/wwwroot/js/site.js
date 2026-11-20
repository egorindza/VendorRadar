document.querySelectorAll("[data-theme-toggle]").forEach(button => {
    const reflect = () => button.setAttribute("aria-pressed", String(document.documentElement.dataset.theme === "dark"));
    reflect();
    button.addEventListener("click", () => {
        const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
        document.documentElement.dataset.theme = next;
        try { localStorage.setItem("vendorradar:theme", next); } catch { /* Browser storage is optional. */ }
        reflect();
    });
});

const menu = document.querySelector(".menu-toggle");
const sidebar = document.querySelector("#sidebar");
function showMenu(open) {
    if (!menu || !sidebar) return;
    sidebar.classList.toggle("shown", open);
    menu.setAttribute("aria-expanded", String(open));
}
menu?.addEventListener("click", () => showMenu(menu.getAttribute("aria-expanded") === "false"));
document.addEventListener("keydown", event => { if (event.key === "Escape") showMenu(false); });
document.addEventListener("click", event => {
    if (sidebar && menu && !sidebar.contains(event.target) && !menu.contains(event.target)) showMenu(false);
});
document.body.addEventListener("htmx:responseError", event => {
    const xhr = event.detail.xhr;
    const redirect = xhr.getResponseHeader("HX-Redirect");
    if (redirect) window.location.assign(redirect);
});

if (document.body.dataset.authenticated === "yes" && window.signalR) {
    const connection = new signalR.HubConnectionBuilder().withUrl("/realtime").withAutomaticReconnect().build();
    const text = document.querySelector("[data-live-text]");
    const dot = document.querySelector("[data-live-dot]");
    let refreshPending = false;

    const state = (name, connected) => {
        text.textContent = name;
        dot.classList.toggle("connected", connected);
    };
    const refresh = () => {
        const region = document.querySelector("[data-live-refresh]");
        if (!region || !window.htmx) return;
        if (region.querySelector("form.hx-request") ||
            Array.from(region.querySelectorAll(".task-form input[name=evidence]")).some(input => input.value.trim())) {
            refreshPending = true;
            return;
        }
        refreshPending = false;
        window.htmx.trigger(region, "workspace-refresh");
    };
    document.body.addEventListener("htmx:afterSettle", () => { if (refreshPending) refresh(); });
    document.body.addEventListener("input", () => { if (refreshPending) refresh(); });
    connection.on("WorkspaceChanged", refresh);
    connection.onreconnecting(() => state("Переподключение", false));
    connection.onreconnected(() => { state("В реальном времени", true); refresh(); });
    connection.onclose(() => state("Связь прервана", false));
    connection.start().then(() => state("В реальном времени", true)).catch(() => state("Связь недоступна", false));
}
