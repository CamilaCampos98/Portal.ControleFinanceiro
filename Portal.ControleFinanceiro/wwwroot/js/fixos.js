function confirmarExcluir(id, mesAno, tipo) {
	Swal.fire({
		title: 'Tem certeza?',
		text: `Deseja realmente excluir o fixo '${tipo}' do mês ${mesAno}?`,
		icon: 'warning',
		showCancelButton: true,
		confirmButtonColor: '#d33',
		cancelButtonColor: '#3085d6',
		confirmButtonText: 'Sim, excluir',
		cancelButtonText: 'Cancelar'
	}).then((result) => {
		if (result.isConfirmed) {
			document.getElementById('formExcluir_Id').value = id;
			document.getElementById('formExcluir_MesAno').value = mesAno;
			document.getElementById('formExcluir').submit();
		}
	});
}

document.addEventListener('DOMContentLoaded', function () {
	const btn = document.getElementById('btnTogglePago');
	const icon = document.getElementById('iconTogglePago');
	let isVisible = false;

	btn.addEventListener('click', function () {
		const ths = document.querySelectorAll('#gridFixos th.pago-col');
		const tds = document.querySelectorAll('#gridFixos td.pago-col');

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

// Inicializa o Choices no seletor
const tiposFixos = new Choices('#TiposSelecionados', {
	removeItemButton: true,
	shouldSort: false,
});

document.addEventListener('DOMContentLoaded', function () {
	const grid = document.getElementById('gridFixos');
	if (!grid) return;

	let selectedRow = null;

	grid.addEventListener('click', function (e) {
		const row = e.target.closest('tr');
		if (!row || !grid.contains(row)) return;

		// Clique no botão dividir
		if (e.target.classList.contains('btnDividir')) {
			const id = e.target.dataset.id;
			document.getElementById('modalIdGasto').value = id;

			const modal = new bootstrap.Modal(document.getElementById('dividirModal'));
			modal.show();
			return;
		}

		// Seleção da linha
		if (selectedRow) {
			selectedRow.classList.remove('table-primary');
			const btnAnterior = selectedRow.querySelector('.btnDividir');
			if (btnAnterior) btnAnterior.classList.add('d-none');
		}

		selectedRow = row;
		row.classList.add('table-primary');

		const btnAtual = row.querySelector('.btnDividir');
		if (btnAtual) btnAtual.classList.remove('d-none');
	});
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