/* Giữ tính năng template; chỉ khởi tạo khi thư viện tương ứng đang có. */
(function ($) {
    'use strict';
    if (!$) return;
    $(function () {
        $('#spinner').removeClass('show');
        const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        if (!reduceMotion && typeof window.WOW === 'function') new window.WOW().init();
        else $('.wow').css('visibility', 'visible');

        function backToTopVisibility() {
            $('.back-to-top').toggle($(window).scrollTop() > 300);
        }
        backToTopVisibility();
        $(window).on('scroll', backToTopVisibility);
        $('.back-to-top').on('click', function (event) {
            event.preventDefault();
            const easing = $.easing && $.easing.easeInOutExpo ? 'easeInOutExpo' : 'swing';
            $('html, body').animate({ scrollTop: 0 }, reduceMotion ? 0 : 500, easing);
        });

        let videoSource = '';
        $('.btn-play').on('click', function () { videoSource = $(this).data('src') || ''; });
        $('#videoModal').on('shown.bs.modal', function () {
            if (!videoSource) return;
            $('#video').attr('src', videoSource + (videoSource.includes('?') ? '&' : '?') + 'autoplay=1&modestbranding=1&showinfo=0');
        });
        $('#videoModal').on('hide.bs.modal', function () { $('#video').attr('src', ''); });

        if ($.fn.counterUp && !reduceMotion) $('[data-toggle="counter-up"]').counterUp({ delay: 10, time: 1500 });
        if ($.fn.owlCarousel) {
            const options = {
                loop: true, dots: false, margin: 25, autoplay: !reduceMotion,
                slideTransition: 'linear', autoplayTimeout: 5000, autoplaySpeed: 1000,
                autoplayHoverPause: true,
                responsive: { 0: { items: 1 }, 575: { items: 1 }, 767: { items: 2 }, 991: { items: 3 } }
            };
            $('.testimonial-carousel-1').owlCarousel(options);
            $('.testimonial-carousel-2').owlCarousel(Object.assign({}, options, { rtl: true }));
        }
    });
})(window.jQuery);
