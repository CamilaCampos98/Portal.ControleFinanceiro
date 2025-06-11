const formaPgto = document.querySelector('[name="Input.FormaPgto"]');
const cartaoDiv = document.querySelector('#cartao-div');
const cartaoSelect = document.querySelector('[name="Input.Cartao"]');

function toggleCartao() {
    if (formaPgto.value === 'C') {
        cartaoDiv.style.display = 'block';
        cartaoSelect.setAttribute('required', 'required');
    } else {
        cartaoDiv.style.display = 'none';
        cartaoSelect.removeAttribute('required');
        cartaoSelect.value = '';
    }
}

formaPgto.addEventListener('change', toggleCartao);

// Executa no carregamento inicial
toggleCartao();

document.addEventListener('DOMContentLoaded', function () {
    const overlay = document.getElementById('loadingOverlay');

    const forms = document.querySelectorAll('form');
    const buttons = document.querySelectorAll('form button[type="submit"]');

    forms.forEach(form => {
        form.addEventListener('submit', function () {
            // Ativa overlay
            overlay.style.display = 'flex';

            // Ativa loading nos botões do formulário
            const btns = form.querySelectorAll('button[type="submit"]');
            btns.forEach(btn => {
                btn.classList.add('btn-loading');
            });
        });
    });
});