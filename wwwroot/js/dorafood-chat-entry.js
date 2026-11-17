(() => {
    document.addEventListener("submit", async event => {
        const form = event.target;

        if (!(form instanceof HTMLFormElement)
            || !form.matches("[data-chat-open]")) {
            return;
        }

        event.preventDefault();

        if (form.dataset.busy === "true") return;

        const button = form.querySelector('button[type="submit"]');
        const originalHtml = button.innerHTML;

        form.dataset.busy = "true";
        button.disabled = true;
        button.innerHTML =
            '<i class="fas fa-spinner fa-spin" aria-hidden="true"></i>';
        button.setAttribute("aria-busy", "true");

        try {
            const response = await fetch(form.action, {
                method: "POST",
                body: new FormData(form),
                credentials: "same-origin",
                headers: {
                    Accept: "application/json"
                }
            });

            if (response.status === 401 || response.redirected) {
                throw new Error(
                    "Bạn hãy đăng nhập lại để mở trò chuyện."
                );
            }

            const contentType =
                response.headers.get("content-type") || "";

            if (!contentType.includes("application/json")) {
                throw new Error("Chưa mở được hội thoại.");
            }

            const result = await response.json();

            if (!response.ok) {
                throw new Error(
                    result.message || "Chưa mở được hội thoại."
                );
            }

            if (!result.url) {
                throw new Error("Chưa có đường dẫn hội thoại.");
            }

            const destination = new URL(
                result.url,
                window.location.origin
            );

            if (destination.origin !== window.location.origin) {
                throw new Error("Đường dẫn hội thoại không hợp lệ.");
            }

            window.location.assign(destination.href);
        } catch (error) {
            window.alert(
                error.message || "Không kết nối được. Bạn hãy thử lại."
            );
        } finally {
            form.dataset.busy = "false";
            button.disabled = false;
            button.innerHTML = originalHtml;
            button.removeAttribute("aria-busy");
        }
    });
})();