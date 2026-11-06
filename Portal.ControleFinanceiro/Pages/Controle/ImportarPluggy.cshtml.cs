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
    private const string CartaoCamila = "Itau Titular";
    private const string CartaoDiego = "Itau Adicional";
    private const string CartaoBradesco = "Bradesco Titular";
    private const string FonteFixa = "Salario";
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
    [BindProperty(SupportsGet = true)] public string Origem { get; set; } = "itau";
    [BindProperty] public List<string> Selecionados { get; set; } = new();
    [TempData] public string? Aviso { get; set; }
    public string? Erro { get; private set; }
    public bool Configurado => TrySource(out var source) && _pluggy.IsConfiguredFor(source);
    public string Titulo => Origem?.ToLowerInvariant() switch
    {
        "bradesco-camila" => "Conferir Bradesco da Camila",
        "bradesco-diego" => "Conferir Bradesco do Diego",
        _ => "Conferir fatura do Itaú"
    };
    public string CartoesInformados => Origem?.ToLowerInvariant() switch
    {
        "bradesco-camila" => "Finais 6311 e 0442: Camila, Bradesco Titular.",
        "bradesco-diego" => "Finais 7937 e 2243: Diego, Bradesco Titular.",
        _ => "Finais 8930 e 6235: Camila, Itaú Titular. Finais 5169 e 3008: Diego, Itaú Adicional."
    };
    public IReadOnlyList<PluggyCardTransaction> Compras { get; private set; } = Array.Empty<PluggyCardTransaction>();
    public IReadOnlyList<PluggyCardTransaction> ComprasVisiveis => Compras
        .Where(compra => !Conferencia.TryGetValue(compra.Id, out var resultado) || resultado.Status != "Existente")
        .ToList();
    public IReadOnlyDictionary<string, ResultadoConferencia> Conferencia { get; private set; } =
        new Dictionary<string, ResultadoConferencia>();
    public bool ConferenciaDisponivel { get; private set; }

    public sealed record LancamentoConferencia(int Linha, DateTime Data, decimal Valor, string Pessoa,
        string Cartao, string Parcela, string Descricao);
    public sealed record ResultadoConferencia(string Id, string Status,
        LancamentoConferencia? Correspondente, int QuantidadeCorrespondencias);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await CarregarAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!TrySource(out var source))
        {
            Erro = "Escolha uma conexão válida.";
            return Page();
        }
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
            var all = await _pluggy.ReadTransactionsAsync(source, cancellationToken);
            var available = ForMonth(all, month).ToDictionary(item => item.Id);
            using var api = _httpClientFactory.CreateClient();
            var apiUrl = _configuration["UrlApi"] ?? throw new InvalidOperationException("API financeira não configurada.");
            using var compatibility = await api.GetAsync($"{apiUrl}Compra/ImportacaoPluggyDisponivel", cancellationToken);
            if (!compatibility.IsSuccessStatusCode)
            {
                await CarregarAsync(cancellationToken);
                Erro = "A API financeira ainda não recebeu a atualização das datas. Nenhuma compra foi importada; tente novamente após a atualização do servidor.";
                return Page();
            }
            var conferencia = await ConferirAsync(api, apiUrl, available.Values.ToList(), source, cancellationToken);
            if (conferencia is null)
            {
                await CarregarAsync(cancellationToken);
                Erro = "Não foi possível conferir as compras com a planilha. Nenhuma compra foi importada.";
                return Page();
            }
            var selecao = Selecionados.Distinct().Select(id =>
                conferencia.TryGetValue(id, out var resultado) ? resultado.Status : "Indisponivel").ToList();
            var jaLancadas = selecao.Count(status => status == "Existente");
            if (jaLancadas == selecao.Count)
            {
                Aviso = $"Compras já lançadas na planilha: {jaLancadas}. Nenhuma duplicata foi criada.";
                return RedirectToPage(new { mes = Mes, origem = Origem });
            }
            if (selecao.Any(status => status != "NaoEncontrada"))
            {
                await CarregarAsync(cancellationToken);
                var revisar = selecao.Count(status => status is "Revisar" or "CartaoDivergente");
                var indisponiveis = selecao.Count - jaLancadas - revisar -
                    selecao.Count(status => status == "NaoEncontrada");
                Erro = $"A seleção mudou desde que a página foi aberta. Já lançadas: {jaLancadas}; " +
                    $"precisam de revisão: {revisar}; indisponíveis: {indisponiveis}. " +
                    "Nenhuma nova compra foi importada nesta tentativa. Atualize a página e selecione apenas as pendentes.";
                return Page();
            }
            var cardResponse = await api.GetAsync($"{apiUrl}Compra/GetCartoes", cancellationToken);
            cardResponse.EnsureSuccessStatusCode();
            var allowedCards = await cardResponse.Content.ReadFromJsonAsync<List<string>>(JsonOptions, cancellationToken) ?? new();
            var registeredCards = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var person in Selecionados.Where(available.ContainsKey)
                         .Select(id => available[id].Person).Where(person => person is "Camila" or "Diego").Distinct())
            {
                var expectedCard = CardFor(source, person);
                var registeredCard = allowedCards.FirstOrDefault(card =>
                    string.Equals(card?.Trim(), expectedCard, StringComparison.OrdinalIgnoreCase));
                if (registeredCard is null)
                {
                    await CarregarAsync(cancellationToken);
                    Erro = $"O cartão {expectedCard} não está cadastrado no financeiro. Nenhuma compra foi importada.";
                    return Page();
                }
                registeredCards[person] = registeredCard;
            }
            var imported = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var id in Selecionados.Distinct())
            {
                if (!available.TryGetValue(id, out var transaction)) { skipped++; continue; }
                var person = transaction.Person;
                if (person is not ("Camila" or "Diego")) { skipped++; continue; }
                var card = registeredCards[person];
                var firstDate = transaction.InstallmentDate;
                var remaining = transaction.TotalInstallments - transaction.InstallmentNumber + 1;
                if (remaining < 1) { skipped++; continue; }
                var purchase = new CompraModel
                {
                    Pessoa = person,
                    Descricao = transaction.Description,
                    ValorTotal = transaction.Amount * remaining,
                    Data = firstDate,
                    FormaPgto = "C",
                    TotalParcelas = transaction.TotalInstallments,
                    ParcelaInicial = transaction.InstallmentNumber,
                    Fonte = FonteFixa,
                    Cartao = card,
                    ImportacaoPluggy = true,
                    DataCompraOriginal = transaction.PurchaseDate
                };
                using var response = await api.PostAsJsonAsync($"{apiUrl}Compra/RegistrarCompra", purchase, cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict) { skipped++; continue; }
                if (!response.IsSuccessStatusCode) { failed++; continue; }
                imported++;
            }

            Aviso = $"Importadas: {imported}. Ignoradas (já existentes ou sem dados completos): {skipped}. Falhas: {failed}.";
            return RedirectToPage(new { mes = Mes, origem = Origem });
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
        if (!TrySource(out var source))
        {
            Erro = "Escolha uma conexão válida.";
            return;
        }
        if (!TryMonth(out var month))
        {
            Erro = "Escolha um mês válido da fatura.";
            return;
        }
        if (!Configurado) return;
        try
        {
            var all = await _pluggy.ReadTransactionsAsync(source, cancellationToken);
            Compras = ForMonth(all, month).OrderByDescending(item => item.InstallmentDate).ToList();
            using var api = _httpClientFactory.CreateClient();
            var apiUrl = _configuration["UrlApi"] ?? throw new InvalidOperationException("API financeira não configurada.");
            Dictionary<string, ResultadoConferencia>? conferencia;
            try { conferencia = await ConferirAsync(api, apiUrl, Compras, source, cancellationToken); }
            catch (HttpRequestException) { conferencia = null; }
            catch (TaskCanceledException) { conferencia = null; }
            catch (JsonException) { conferencia = null; }
            if (conferencia is null)
            {
                Erro = "Não foi possível conferir com a planilha. A importação está desabilitada até a API financeira ser atualizada ou a conexão voltar.";
                return;
            }
            Conferencia = conferencia;
            ConferenciaDisponivel = true;
        }
        catch (Exception)
        {
            Erro = $"Não foi possível consultar {Titulo} na Pluggy. Confira a conexão e tente novamente.";
        }
    }

    private bool TryMonth(out DateTime month) => DateTime.TryParseExact(Mes, "yyyy-MM", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out month);

    private bool TrySource(out PluggyCardSource source)
    {
        source = Origem?.ToLowerInvariant() switch
        {
            "itau" => PluggyCardSource.Itau,
            "bradesco-camila" => PluggyCardSource.BradescoCamila,
            "bradesco-diego" => PluggyCardSource.BradescoDiego,
            _ => (PluggyCardSource)(-1)
        };
        return Enum.IsDefined(source);
    }

    private static string CardFor(PluggyCardSource source, string person) => (source, person) switch
    {
        (PluggyCardSource.Itau, "Camila") => CartaoCamila,
        (PluggyCardSource.Itau, "Diego") => CartaoDiego,
        (PluggyCardSource.BradescoCamila, "Camila") => CartaoBradesco,
        (PluggyCardSource.BradescoDiego, "Diego") => CartaoBradesco,
        _ => throw new InvalidOperationException("A pessoa não corresponde à conexão do cartão.")
    };

    private static IEnumerable<PluggyCardTransaction> ForMonth(IEnumerable<PluggyCardTransaction> transactions, DateTime month) =>
        transactions.Where(item => item.BillMonth == month.ToString("yyyy-MM", CultureInfo.InvariantCulture) ||
            string.IsNullOrEmpty(item.BillMonth) && (item.ReportedDate ?? item.Date).Year == month.Year &&
            (item.ReportedDate ?? item.Date).Month == month.Month);

    private static async Task<Dictionary<string, ResultadoConferencia>?> ConferirAsync(HttpClient api, string apiUrl,
        IEnumerable<PluggyCardTransaction> compras, PluggyCardSource source, CancellationToken cancellationToken)
    {
        var parcelas = compras.Where(compra => compra.Person.Length > 0).Select(compra => new
        {
            compra.Id,
            Data = compra.InstallmentDate,
            Valor = compra.Amount,
            Pessoa = compra.Person,
            Cartao = CardFor(source, compra.Person),
            Parcela = $"{compra.InstallmentNumber}/{compra.TotalInstallments}",
            Descricao = compra.Description,
            DataInformadaPluggy = compra.ReportedDate
        }).ToList();
        using var response = await api.PostAsJsonAsync($"{apiUrl}Compra/ConferirImportacaoPluggy", parcelas,
            cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var resultados = await response.Content.ReadFromJsonAsync<List<ResultadoConferencia>>(JsonOptions, cancellationToken);
        if (resultados is null || resultados.Count != parcelas.Count ||
            resultados.Select(resultado => resultado.Id).Distinct().Count() != parcelas.Count)
            return null;
        return resultados.ToDictionary(resultado => resultado.Id);
    }

}
