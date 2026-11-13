(() => {
    document.querySelectorAll(".df-voucher").forEach(card => {
        const input = card.querySelector("[data-voucher-code]");
        const button = card.querySelector("[data-voucher-copy]");
        const status = card.querySelector("[data-voucher-status]");

        if (!input || !button || !status) {
            return;
        }

        let resetTimer;

        button.addEventListener("click", async () => {
            window.clearTimeout(resetTimer);

            button.disabled = true;
            status.textContent = "";

            try {
                if (!navigator.clipboard?.writeText) {
                    throw new Error("Clipboard unavailable");
                }

                await navigator.clipboard.writeText(input.value);

                button.textContent = "Đã sao chép";
                status.textContent =
                    `Đã sao chép mã ${input.value}. `
                    + "Anh nhập mã tại trang thanh toán.";
            } catch {
                input.focus();
                input.select();
                input.setSelectionRange(0, input.value.length);

                button.textContent = "Sao chép mã";
                status.textContent =
                    "Anh sao chép phần mã đã được chọn "
                    + "bằng bàn phím hoặc menu của thiết bị.";
            } finally {
                button.disabled = false;

                resetTimer = window.setTimeout(() => {
                    button.textContent = "Sao chép mã";
                }, 2000);
            }
        });
    });
})();