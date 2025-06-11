function atualizarCamposEntrada() {
    const tipo = document.getElementById('Input_TipoEntrada').value;
    const horasExtras = document.getElementById('Input_HorasExtras');
    const horasUteis = document.getElementById('Input_HorasUteisMes');
    const valorHora = document.getElementById('Input_ValorHora');

    if (tipo === 'Extra') {
        horasExtras.disabled = false;
        horasUteis.disabled = true;
        valorHora.disabled = true;
        horasUteis.value = '';
        valorHora.value = '';
    } else {
        horasExtras.disabled = true;
        horasExtras.value = '';
        horasUteis.disabled = false;
        valorHora.disabled = false;
    }
}

// Executa na primeira carga para ajustar os campos
document.addEventListener("DOMContentLoaded", function () {
    atualizarCamposEntrada();
});

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