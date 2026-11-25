document.querySelectorAll("[data-quantity]").forEach(stepper => {
    const input = stepper.querySelector("input");
    stepper.querySelectorAll("[data-step]").forEach(button => {
        button.hidden = false;
        button.addEventListener("click", () => {
            const value = input.valueAsNumber;
            const next = Number.isFinite(value) ? value + Number(button.dataset.step) : 1;
            input.value = String(Math.min(Number(input.max), Math.max(Number(input.min), next)));
        });
    });
});

const searchBox = document.querySelector("[data-dish-search-box]");
if (searchBox) {
    const search = searchBox.querySelector("[data-dish-search]");
    const empty = document.querySelector("[data-dish-empty]");
    const dishes = Array.from(document.querySelectorAll("[data-dish-name]"));
    const normalize = value => value.toLocaleLowerCase("vi-VN").normalize("NFD")
        .replace(/\p{M}/gu, "").replace(/đ/g, "d").trim();

    searchBox.hidden = false;
    search.addEventListener("input", () => {
        const term = normalize(search.value);
        dishes.forEach(card => { card.hidden = !normalize(card.dataset.dishName).includes(term); });
        empty.hidden = dishes.some(card => !card.hidden);
    });
}

document.querySelectorAll("[data-staff-print]").forEach(button => {
    button.addEventListener("click", () => window.print());
});
