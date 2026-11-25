"use strict";

document.querySelectorAll("[data-dish-workspace]").forEach((workspace) => {
    const form = workspace.querySelector("[data-dish-cart]");
    const lines = form.querySelector("[data-cart-lines]");
    const fields = form.querySelector("[data-cart-fields]");
    const submit = form.querySelector("[data-cart-submit]");
    const status = form.querySelector("[data-cart-status]");
    const selected = new Map();
    const currency = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 2 });
    const cards = [...workspace.querySelectorAll("[data-dish-name]")];
    const search = workspace.querySelector("[data-dish-search]");

    function normalize(text) {
        return text.toLocaleLowerCase("vi-VN").normalize("NFD")
            .replace(/\p{Diacritic}/gu, "").replace(/đ/g, "d");
    }

    search.addEventListener("input", () => {
        const query = normalize(search.value.trim());
        cards.forEach((card) => {
            card.hidden = !normalize(card.dataset.dishName).includes(query);
        });
        workspace.querySelector("[data-no-results]").hidden = cards.some((card) => !card.hidden);
    });

    function syncSummary() {
        const total = [...selected.values()].reduce((sum, dish) => sum + dish.quantity * dish.price, 0);
        form.querySelector("[data-cart-total]").textContent = currency.format(total);
        form.querySelector("[data-cart-empty]").hidden = selected.size > 0;
        submit.disabled = selected.size === 0;
    }

    function appendLine(dish, addButton) {
        const line = document.createElement("div");
        line.className = "df-cart-line";

        const title = document.createElement("strong");
        title.textContent = dish.name;

        const remove = document.createElement("button");
        remove.type = "button";
        remove.textContent = "Bỏ";
        remove.className = "btn btn-sm df-table-dishes__add";
        remove.setAttribute("aria-label", "Bỏ " + dish.name);
        remove.addEventListener("click", () => {
            selected.delete(dish.id);
            line.remove();
            syncSummary();
            addButton.focus();
            status.textContent = "Đã bỏ " + dish.name;
        });

        const quantity = document.createElement("input");
        quantity.type = "number";
        quantity.min = "1";
        quantity.max = "999";
        quantity.step = "1";
        quantity.required = true;
        quantity.value = "1";
        quantity.className = "form-control";
        quantity.setAttribute("aria-label", "Số lượng " + dish.name);
        quantity.addEventListener("input", () => {
            dish.quantity = Number(quantity.value) || 0;
            syncSummary();
        });
        dish.quantityInput = quantity;

        const note = document.createElement("input");
        note.type = "text";
        note.maxLength = 500;
        note.placeholder = "Ghi chú";
        note.className = "form-control";
        note.setAttribute("aria-label", "Ghi chú " + dish.name);
        note.addEventListener("input", () => { dish.note = note.value; });

        line.append(title, remove, quantity, note);
        lines.append(line);
    }

    workspace.querySelectorAll("[data-add-dish]").forEach((button) => {
        button.addEventListener("click", () => {
            const id = button.dataset.addDish;
            const existing = selected.get(id);
            if (existing) {
                existing.quantity = Math.min(999, existing.quantity + 1);
                existing.quantityInput.value = String(existing.quantity);
            } else {
                if (selected.size >= 100) {
                    status.textContent = "Đã chọn tối đa 100 món.";
                    return;
                }
                const dish = {
                    id, name: button.closest("[data-dish-name]").dataset.dishName,
                    price: Number(button.dataset.price), quantity: 1, note: ""
                };
                selected.set(id, dish);
                appendLine(dish, button);
            }

            syncSummary();
            const dish = selected.get(id);
            status.textContent = dish.name + ", đã chọn " + dish.quantity + " phần.";
        });
    });

    form.addEventListener("submit", (event) => {
        if (selected.size === 0 || submit.disabled || !form.checkValidity()) {
            event.preventDefault();
            return;
        }

        fields.replaceChildren();
        [...selected.values()].forEach((dish, index) => {
            Object.entries({ VariantId: dish.id, Quantity: dish.quantity, Note: dish.note }).forEach(([key, value]) => {
                const field = document.createElement("input");
                field.type = "hidden";
                field.name = `items[${index}].${key}`;
                field.value = String(value);
                fields.append(field);
            });
        });
        submit.disabled = true;
        submit.textContent = "Đang lưu…";
    });

    window.addEventListener("pageshow", () => {
        submit.textContent = "Xác nhận thêm món";
        syncSummary();
    });
});
