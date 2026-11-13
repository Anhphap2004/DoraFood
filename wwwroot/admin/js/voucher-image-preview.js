(() => {
    const maxSize = 5 * 1024 * 1024;

    const allowedTypes = new Set([
        "image/jpeg",
        "image/png",
        "image/webp"
    ]);

    document.querySelectorAll(
        "[data-voucher-image-editor]"
    ).forEach(editor => {
        const input = editor.querySelector("[data-cover-input]");
        const preview = editor.querySelector("[data-cover-preview]");
        const placeholder = editor.querySelector(
            "[data-cover-placeholder]"
        );
        const resetButton = editor.querySelector("[data-cover-reset]");
        const error = editor.querySelector("[data-cover-error]");
        const status = editor.querySelector("[data-cover-status]");

        if (!input || !preview || !placeholder
            || !resetButton || !error || !status) {
            return;
        }

        const originalUrl = editor.dataset.currentUrl || "";
        let selectedUrl = null;

        const releaseSelectedUrl = () => {
            if (selectedUrl) {
                URL.revokeObjectURL(selectedUrl);
                selectedUrl = null;
            }
        };

        const restoreOriginal = () => {
            releaseSelectedUrl();

            input.value = "";
            error.textContent = "";
            status.textContent = "";
            resetButton.disabled = true;

            if (originalUrl) {
                preview.src = originalUrl;
                preview.hidden = false;
                placeholder.hidden = true;
            } else {
                preview.removeAttribute("src");
                preview.hidden = true;
                placeholder.hidden = false;
            }
        };

        const rejectFile = message => {
            restoreOriginal();
            error.textContent = message;
        };

        input.addEventListener("change", () => {
            const file = input.files?.[0];

            if (!file) {
                restoreOriginal();
                return;
            }

            if (file.size <= 0 || file.size > maxSize) {
                rejectFile("Ảnh phải có dữ liệu và tối đa 5 MB.");
                return;
            }

            // Một số trình duyệt có thể không cung cấp MIME type.
            // Server vẫn kiểm tra chữ ký file khi lưu.
            if (file.type && !allowedTypes.has(file.type)) {
                rejectFile("Vui lòng chọn ảnh JPG, PNG hoặc WebP.");
                return;
            }

            releaseSelectedUrl();

            error.textContent = "";
            status.textContent = "Đang đọc ảnh...";

            const url = URL.createObjectURL(file);
            selectedUrl = url;

            const image = new Image();

            image.onload = () => {
                if (selectedUrl !== url) {
                    return;
                }

                preview.src = url;
                preview.hidden = false;
                placeholder.hidden = true;
                resetButton.disabled = false;

                status.textContent =
                    `Đã chọn ${file.name}. Bấm lưu để cập nhật ảnh.`;
            };

            image.onerror = () => {
                if (selectedUrl !== url) {
                    return;
                }

                rejectFile(
                    "Không đọc được ảnh. Anh hãy chọn một file ảnh khác."
                );
            };

            image.src = url;
        });

        resetButton.addEventListener("click", restoreOriginal);
    });
})();