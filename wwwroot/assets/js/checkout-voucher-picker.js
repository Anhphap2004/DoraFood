(() => {
    const form = document.getElementById("checkout-review-form");

    if (!form) {
        return;
    }

    const field = form.elements.namedItem("Input.VoucherCode");

    const status = document.getElementById(
        "checkout-voucher-selection-status"
    );

    const modal = document.getElementById(
        "checkout-voucher-modal"
    );

    if (!field || !status || !modal) {
        return;
    }

    field.addEventListener("input", () => {
        status.textContent = "";
    });

    modal.querySelectorAll(
        "[data-checkout-select-voucher]"
    ).forEach(button => {
        button.addEventListener("click", () => {
            const code = button.dataset.code;

            if (!code) {
                return;
            }

            field.value = code;

            // Dùng lại cơ chế vô hiệu hóa xác nhận cũ trong checkout.
            field.dispatchEvent(
                new Event("input", { bubbles: true })
            );

            field.dispatchEvent(
                new Event("change", { bubbles: true })
            );

            status.textContent =
                `Đã chọn mã ${code}. `
                + "Anh bấm Kiểm tra thông tin để cập nhật tiền giảm.";
        });
    });
})();