document.addEventListener("DOMContentLoaded", () => {
    const form = document.querySelector("#vacationForm");
    if (!form) return;
    const rows = [...form.querySelectorAll(".vacation-person")];
    const split = form.querySelector("#vacationSplit");
    const splitRange = form.querySelector("#vacationSplitRange");
    const multi = form.querySelector("#vacationMultiSplit");
    const confirm = form.querySelector("#vacationConfirmButton");
    const preview = form.querySelector("#vacationPreviewButton");
    const previewText = preview.querySelector(".vacation-preview-text");
    const money = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
    let comparing = false;

    form.addEventListener("submit", event => {
        if (event.submitter !== preview) return;
        if (comparing) {
            event.preventDefault();
            return;
        }
        comparing = true;
        preview.classList.add("is-loading");
        preview.setAttribute("aria-busy", "true");
        previewText.textContent = "Comparando datas...";
    });
    window.addEventListener("pageshow", () => {
        comparing = false;
        preview.classList.remove("is-loading");
        preview.removeAttribute("aria-busy");
        previewText.textContent = "Comparar datas";
    });

    const selected = () => rows.filter(row => row.querySelector(".vacation-person-check").checked);
    const percent = row => Number(row.querySelector(".vacation-person-percent").value) || 0;
    const setPercent = (row, value) => { row.querySelector(".vacation-person-percent").value = value; };
    const budget = () => {
        const raw = form.querySelector("#Input_BudgetText").value.replace(/R\$|\s/g, "");
        const normalized = raw.includes(",") ? raw.replace(/\./g, "").replace(",", ".")
            : /\.\d{3}$/.test(raw) ? raw.replace(/\./g, "") : raw;
        return Number(normalized) || 0;
    };
    const markDirty = () => { if (confirm) confirm.disabled = true; };

    function render() {
        const active = selected();
        rows.forEach(row => {
            const share = row.querySelector(".vacation-person-share");
            share.textContent = row.querySelector(".vacation-person-check").checked
                ? `${percent(row)}% · ${money.format(budget() * percent(row) / 100)}` : "";
        });
        split.hidden = active.length !== 2;
        multi.hidden = active.length <= 2;
        if (active.length === 2) {
            form.querySelector("#vacationLeftPerson").textContent = `${active[0].querySelector(".vacation-person-name").textContent} ${percent(active[0])}%`;
            form.querySelector("#vacationRightPerson").textContent = `${active[1].querySelector(".vacation-person-name").textContent} ${percent(active[1])}%`;
            splitRange.value = percent(active[0]);
            splitRange.style.setProperty("--split", `${percent(active[0])}%`);
        }
        if (active.length > 2) {
            multi.replaceChildren();
            active.forEach((row, index) => {
                const label = document.createElement("label");
                label.textContent = `${row.querySelector(".vacation-person-name").textContent}: ${percent(row)}%`;
                const range = document.createElement("input");
                range.type = "range";
                range.min = "0";
                range.max = "100";
                range.step = "1";
                range.value = percent(row);
                range.setAttribute("aria-label", `Parte de ${row.querySelector(".vacation-person-name").textContent}`);
                range.addEventListener("input", () => {
                    const target = Number(range.value);
                    const others = active.filter((_, current) => current !== index);
                    const totalOthers = others.reduce((sum, other) => sum + percent(other), 0);
                    let remainder = 100 - target;
                    others.forEach((other, otherIndex) => {
                        const next = otherIndex === others.length - 1 ? remainder :
                            Math.round((100 - target) * (totalOthers ? percent(other) / totalOthers : 1 / others.length));
                        setPercent(other, next);
                        remainder -= next;
                    });
                    setPercent(row, target);
                    markDirty();
                    active.forEach((person, personIndex) => {
                        const label = multi.children[personIndex];
                        label.firstChild.textContent = `${person.querySelector(".vacation-person-name").textContent}: ${percent(person)}%`;
                        label.querySelector("input").value = percent(person);
                        person.querySelector(".vacation-person-share").textContent =
                            `${percent(person)}% · ${money.format(budget() * percent(person) / 100)}`;
                    });
                });
                label.append(range);
                multi.append(label);
            });
        }
    }

    function equalize() {
        const active = selected();
        let remaining = 100;
        active.forEach((row, index) => {
            const value = index === active.length - 1 ? remaining : Math.round(100 / active.length);
            setPercent(row, value);
            remaining -= value;
        });
        rows.filter(row => !active.includes(row)).forEach(row => setPercent(row, 0));
        render();
        markDirty();
    }

    function watchPerson(row) {
        row.querySelector(".vacation-person-check").addEventListener("change", equalize);
    }
    rows.forEach(watchPerson);
    splitRange.addEventListener("input", () => {
        const active = selected();
        if (active.length !== 2) return;
        setPercent(active[0], Number(splitRange.value));
        setPercent(active[1], 100 - Number(splitRange.value));
        render();
        markDirty();
    });
    ["Input_StartDate", "Input_EndDate", "Input_BudgetText"].forEach(id => {
        form.querySelector(`#${id}`).addEventListener("input", () => {
            render(); markDirty();
        });
    });
    form.querySelector("#Input_UseGuardado")?.addEventListener("change", markDirty);
    form.querySelectorAll(".vacation-alternative").forEach(button => button.addEventListener("click", () => {
        form.querySelector("#Input_StartDate").value = button.dataset.start;
        form.querySelector("#Input_EndDate").value = button.dataset.end;
        preview.click();
    }));
    const active = selected();
    if (active.length && active.reduce((sum, row) => sum + percent(row), 0) !== 100) equalize();
    else render();
});
