
async function confirmarExcluir(id, mesAno) {
    const resultado = await Swal.fire({
        title: 'Tem certeza?',
        html: `Deseja realmente excluir o ID <strong>${id}</strong> do mês <strong>${mesAno}</strong>?`,
        icon: 'warning',
        showCancelButton: true,
        confirmButtonText: 'Sim, excluir',
        cancelButtonText: 'Cancelar',
        confirmButtonColor: '#d33',
        cancelButtonColor: '#3085d6'
    });

    if (resultado.isConfirmed) {
        document.getElementById('formExcluir_Id').value = id;
        document.getElementById('formExcluir_MesAno').value = mesAno;
        document.getElementById('formExcluir').submit(); // envia o form
    }
}

window.addEventListener("DOMContentLoaded", function () {
    const alvo = document.querySelector(".linha-alvo");
    if (alvo) {
        alvo.scrollIntoView({ behavior: "smooth", block: "center" });
    }
});

window.onload = function () {
    var elemento = document.getElementById('filtroPeriodo');
    if (elemento) {
        IMask(elemento, {
            mask: '00/0000',
            placeholderChar: '_',
        });
    }
}

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

document.addEventListener('DOMContentLoaded', function () {
    const btn = document.getElementById('btnTogglePago');
    const icon = document.getElementById('iconTogglePago');
    let isVisible = false;

    btn.addEventListener('click', function () {
        const ths = document.querySelectorAll('#gridResumo th.pago-col');
        const tds = document.querySelectorAll('#gridResumo td.pago-col');

        isVisible = !isVisible;

        ths.forEach(th => {
            th.style.display = isVisible ? 'table-cell' : 'none';
        });

        tds.forEach(td => {
            td.style.display = isVisible ? 'table-cell' : 'none';
        });

        if (isVisible) {
            icon.classList.remove('bi-eye-slash');
            icon.classList.add('bi-eye');
            btn.innerHTML = '<i class="bi bi-eye" id="iconTogglePago"></i> Ocultar Valor';
        } else {
            icon.classList.remove('bi-eye');
            icon.classList.add('bi-eye-slash');
            btn.innerHTML = '<i class="bi bi-eye-slash" id="iconTogglePago"></i> Mostrar Valor';
        }
    });
});

function toggleTodos() {
    const valores = document.querySelectorAll('.valor-sigiloso');
    const algumRevelado = Array.from(valores).some(v => v.classList.contains('revelado'));

    valores.forEach(v => {
        if (algumRevelado) {
            v.classList.remove('revelado');
        } else {
            v.classList.add('revelado');
        }
    });
}

function editarCompra(idLan) {
    const row = Array.from(document.querySelectorAll('#gridResumo tbody tr'))
        .find(tr => tr.children[0].innerText == idLan);

    const pessoa = document.getElementById('filtroPessoa').value;

    if (row) {
        const dadosCompra = {
            idLan: idLan,
            pessoa: pessoa,
            compra: row.children[2].innerText,
            valor: parseFloat(row.children[3].innerText.replace('R$', '').trim().replace('.', '').replace(',', '.')),
            formaPgto: row.children[4].innerText,
            cartao: row.children[5].innerText,
            parcela: row.children[6].innerText,
            data: row.children[1].innerText // data está na coluna 1, talvez precise converter
        };

        abrirModalEditarCompra(dadosCompra);
    }
}


function abrirModalEditarCompra(dadosCompra) {
    document.getElementById('editIdLan').value = dadosCompra.idLan || '';
    document.getElementById('editCompra').value = dadosCompra.compra || '';
    document.getElementById('editPessoa').value = dadosCompra.pessoa || '';
    document.getElementById('editValor').value = dadosCompra.valor ?? '';
    document.getElementById('editFormaPgto').value = dadosCompra.formaPgto || '';
    document.getElementById('editCartao').value = dadosCompra.cartao || '';
    document.getElementById('editParcela').value = dadosCompra.parcela || '';

    // Tratamento da data
    let dataFormatada = '';
    if (dadosCompra.data) {
        if (typeof dadosCompra.data === 'string' && dadosCompra.data.includes('/')) {
            // Caso a data esteja no formato "dd/MM/yyyy"
            const partes = dadosCompra.data.split('/');
            if (partes.length === 3) {
                const dia = partes[0].padStart(2, '0');
                const mes = partes[1].padStart(2, '0');
                const ano = partes[2];
                dataFormatada = `${ano}-${mes}-${dia}`;
            }
        } else {
            // Caso a data esteja no formato Date ou ISO (ex.: "2025-06-03")
            const dateObj = new Date(dadosCompra.data);
            if (!isNaN(dateObj)) {
                dataFormatada = dateObj.toISOString().split('T')[0];
            }
        }
    }

    document.getElementById('editData').value = dataFormatada;

    var modal = new bootstrap.Modal(document.getElementById('modalEditarCompra'));
    modal.show();
}