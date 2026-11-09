(() => {
    const field = document.getElementById("ContentHtml");
    const $ = window.jQuery;

    if (!field?.form || !$?.fn?.summernote) {
        return;
    }

    const form = field.form;
    const importButton = document.getElementById("post-import-html");
    const sourceField = document.getElementById("post-html-source");
    const importStatus = document.getElementById("post-import-status");

    field.value = field.dataset.editorHtml;

    const $editor = $(field);

    $editor.summernote({
        height: 360,
        placeholder: "Viết nội dung bài viết...",
        disableDragAndDrop: true,
        styleWithCSS: true,
        styleTags: ["p", "h2", "h3", "h4", "blockquote"],
        toolbar: [
            ["history", ["undo", "redo"]],
            ["style", ["style"]],
            ["fontname", ["fontname"]],
            ["fontsize", ["fontsize"]],
            ["font", ["bold", "italic", "underline", "strikethrough"]],
            ["script", ["superscript", "subscript", "clear"]],
            ["color", ["color"]],
            ["paragraph", ["ul", "ol", "paragraph", "height"]],
            ["insert", ["link", "table", "hr"]],
            ["view", ["fullscreen", "help"]]
        ]
    });

    const editable = document.querySelector("#post-content-editor .note-editable");

    editable.setAttribute("aria-labelledby", "post-content-label");

    editable.addEventListener("paste", event => {
        event.preventDefault();
        event.stopImmediatePropagation();

        const text = event.clipboardData?.getData("text/plain");

        if (text) {
            $editor.summernote("insertText", text);
        }
    }, true);

    importButton.addEventListener("click", async () => {
        const html = sourceField.value.trim();

        if (!html) {
            importStatus.textContent = "Anh hãy dán mã HTML trước.";
            sourceField.focus();
            return;
        }

        const token = form.querySelector('input[name="__RequestVerificationToken"]');
        const url = importButton.dataset.url;

        if (!token || !url) {
            importStatus.textContent = "Thiếu cấu hình nhập HTML. Anh hãy kiểm tra action ImportHtml.";
            return;
        }

        const data = new FormData();
        data.append("html", html);
        data.append(token.name, token.value);

        importButton.disabled = true;
        importStatus.textContent = "Đang xử lý HTML...";

        try {
            const response = await fetch(url, {
                method: "POST",
                body: data
            });

            const contentType = response.headers.get("content-type") ?? "";

            if (!contentType.includes("application/json")) {
                importStatus.textContent = "Máy chủ không trả về kết quả nhập HTML.";
                return;
            }

            const result = await response.json();

            if (!response.ok || typeof result.html !== "string") {
                importStatus.textContent = result.message || "Không nhập được HTML.";
                return;
            }

            if (!$editor.summernote("isEmpty")
                && !window.confirm("Thay nội dung hiện tại bằng HTML vừa nhập?")) {
                importStatus.textContent = "Đã hủy nhập HTML.";
                return;
            }

            $editor.summernote("code", result.html);
            importStatus.textContent = "Đã đưa HTML vào bài viết.";
        } catch {
            importStatus.textContent = "Không xử lý được HTML. Anh hãy thử lại.";
        } finally {
            importButton.disabled = false;
        }
    });

    form.addEventListener("submit", event => {
        if (importButton.disabled) {
            event.preventDefault();
            importStatus.textContent = "Anh đợi HTML được xử lý xong rồi bấm lưu.";
            return;
        }

        field.value = $editor.summernote("isEmpty")
            ? ""
            : $editor.summernote("code");
    }, true);
})();