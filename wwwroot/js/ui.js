// Transición de entrada del contenido en cada navegación.
(function () {
    function animar() {
        var c = document.querySelector('.content');
        if (!c) return;
        c.classList.remove('page-enter');
        void c.offsetWidth;          // fuerza un reflow para reiniciar la animación
        c.classList.add('page-enter');
    }

    function init() {
        // Blazor dispara 'enhancedload' al terminar una navegación mejorada.
        if (window.Blazor && Blazor.addEventListener) {
            Blazor.addEventListener('enhancedload', animar);
            animar(); // primera carga
        } else {
            setTimeout(init, 50);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();

// Abre el cliente de correo con el ticket pre-armado (widget de soporte).
window.abrirCorreo = function (url) {
    window.location.href = url;
};
