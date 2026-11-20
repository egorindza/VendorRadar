(() => {
    try {
        const saved = localStorage.getItem("vendorradar:theme");
        if (saved === "dark" || saved === "light") document.documentElement.dataset.theme = saved;
        else if (matchMedia("(prefers-color-scheme: dark)").matches) document.documentElement.dataset.theme = "dark";
    } catch {
        // The default theme works when browser storage is disabled.
    }
})();
