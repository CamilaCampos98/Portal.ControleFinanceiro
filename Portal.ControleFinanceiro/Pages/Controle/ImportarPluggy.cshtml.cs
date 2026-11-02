using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portal.ControleFinanceiro.Models;
using Portal.ControleFinanceiro.Services;

namespace Portal.ControleFinanceiro.Pages.Controle;

[Authorize]
public sealed class ImportarPluggyModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly PluggyCardService _pluggy;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public ImportarPluggyModel(PluggyCardService pluggy, IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _pluggy = pluggy;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    [BindProperty(SupportsGet = true)] public string Mes { get; set; } = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    [BindProperty] public List<string> Selecionados { get; set; } = new();
    [BindProperty] public string CartaoCamila { get; set; } = "";
    [BindProperty] public string CartaoDiego { get; set; } = "";
    [BindProperty] public string Fonte { get; set; } = "Salario";
    [TempData] public string? Aviso { get; set; }
    public string? Erro { get; private set; }
    public bool Configurado => _pluggy.IsConfigured;
    public IReadOnlyList<PluggyCardTransaction> Compras { get; private set; } = Array.Empty<PluggyCardTransaction>();
    public IReadOnlyList<string> Cartoes { get; private set; } = Array.Empty<string>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await CarregarAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!TryMonth(out var month))
        {
            Erro = "Escolha um mês válido da fatura.";
            return Page();
        }
        if (Selecionados.Count == 0)
        {
            Erro = "Selecione pelo menos uma compra para confirmar.";
            await CarregarAsync(cancellationToken);
            return Page();
        }

        try
        {
            var all = await _pluggy.ReadInfiniteTransactionsAsync(cancellationToken);
            var available = ForMonth(all, month).ToDictionary(item => item.Id);
            using var api = _httpClientFactory.CreateClient();
            var apiUrl = _configuration["UrlApi"] ?? throw new InvalidOperationException("API financeira não configurada.");
            var cardResponse = await api.GetAsync($"{apiUrl}Compra/GetCartoes", cancellationToken);
            cardResponse.EnsureSuccessStatusCode();
            var allowedCards = await cardResponse.Content.ReadFromJsonAsync<List<string>>(JsonOptions, cancellationToken) ?? new();
            var known = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var imported = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var id in Selecionados.Distinct())
            {
                if (!available.TryGetValue(id, out var transaction)) { skipped++; continue; }
                var person = Request.Form[$"Pessoa_{id}"].ToString();
                if (person is not ("Camila" or "Diego")) { skipped++; continue; }
                var card = person == "Camila" ? CartaoCamila : CartaoDiego;
                if (!allowedCards.Contains(card, StringComparer.OrdinalIgnoreCase)) { skipped++; continue; }
                if (!known.TryGetValue(person, out var existing))
                {
                    using var existingResponse = await api.GetAsync(
                        $"{apiUrl}Compra/GetCompras?pessoa={Uri.EscapeDataString(person)}", cancellationToken);
                    existingResponse.EnsureSuccessStatusCode();
                    existing = await existingResponse.Content.ReadFromJsonAsync<HashSet<string>>(JsonOptions, cancellationToken) ?? new();
                    known[person] = existing;
                }

                var periodUrl = $"{apiUrl}Compra/PeriodoFatura?pessoa={Uri.EscapeDataString(person)}" +
                    $"&mesAno={Uri.EscapeDataString(month.ToString("MM/yyyy", CultureInfo.InvariantCulture))}" +
                    $"&cartao={Uri.EscapeDataString(card)}";
                using var periodResponse = await api.GetAsync(periodUrl, cancellationToken);
                if (!periodResponse.IsSuccessStatusCode) { failed++; continue; }
                using var period = await JsonDocument.ParseAsync(await periodResponse.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);
                var start = period.RootElement.GetProperty("inicio").GetDateTime().Date;
                var end = period.RootElement.GetProperty("fim").GetDateTime().Date;
                var firstDate = transaction.Date;
                while (firstDate < start) firstDate = firstDate.AddMonths(1);
                if (firstDate > end) { skipped++; continue; }

                var remaining = transaction.TotalInstallments - transaction.InstallmentNumber + 1;
                if (remaining < 1) { skipped++; continue; }
                var projectedKeys = Enumerable.Range(0, remaining).Select(index =>
                    PurchaseKey(person, firstDate.AddMonths(index), transaction.Amount)).ToList();
                if (projectedKeys.Any(existing.Contains)) { skipped++; continue; }

                var purchase = new CompraModel
                {
                    Pessoa = person,
                    Descricao = transaction.Description,
                    ValorTotal = transaction.Amount * remaining,
                    Data = firstDate,
                    FormaPgto = "C",
                    TotalParcelas = transaction.TotalInstallments,
                    ParcelaInicial = transaction.InstallmentNumber,
                    Fonte = string.IsNullOrWhiteSpace(Fonte) ? "Salario" : Fonte.Trim(),
                    Cartao = card
                };
                using var response = await api.PostAsJsonAsync($"{apiUrl}Compra/RegistrarCompra", purchase, cancellationToken);
                if (!response.IsSuccessStatusCode) { failed++; continue; }
                imported++;
                foreach (var key in projectedKeys) existing.Add(key);
            }

            Aviso = $"Importadas: {imported}. Ignoradas (já existentes ou sem dados completos): {skipped}. Falhas: {failed}.";
            return RedirectToPage(new { mes = Mes });
        }
        catch (Exception)
        {
            Erro = "Não foi possível concluir a importação. Confira a conexão com a Pluggy e tente novamente.";
            await CarregarAsync(cancellationToken);
            return Page();
        }
    }

    private async Task CarregarAsync(CancellationToken cancellationToken)
    {
        if (!TryMonth(out var month))
        {
            Erro = "Escolha um mês válido da fatura.";
            return;
        }
        if (!Configurado) return;
        try
        {
            var all = await _pluggy.ReadInfiniteTransactionsAsync(cancellationToken);
            Compras = ForMonth(all, month).OrderByDescending(item => item.Date).ToList();
            using var api = _httpClientFactory.CreateClient();
            using var response = await api.GetAsync($"{_configuration["UrlApi"]}Compra/GetCartoes", cancellationToken);
            if (response.IsSuccessStatusCode)
                Cartoes = await response.Content.ReadFromJsonAsync<List<string>>(JsonOptions, cancellationToken) ?? new();
        }
        catch (Exception)
        {
            Erro = "Não foi possível consultar o LATAM Pass Infinite na Pluggy. Confira a conexão e tente novamente.";
        }
    }

    private bool TryMonth(out DateTime month) => DateTime.TryParseExact(Mes, "yyyy-MM", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out month);

    private static IEnumerable<PluggyCardTransaction> ForMonth(IEnumerable<PluggyCardTransaction> transactions, DateTime month) =>
        transactions.Where(item => item.BillMonth == month.ToString("yyyy-MM", CultureInfo.InvariantCulture) ||
            string.IsNullOrEmpty(item.BillMonth) && item.Date >= month.AddMonths(-1) &&
            item.Date < month.AddMonths(1));

    private static string PurchaseKey(string person, DateTime date, decimal amount) =>
        $"{person.Trim().ToUpperInvariant()}|{date:yyyy-MM-dd}|{amount.ToString(CultureInfo.InvariantCulture)}";
}
