using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Portal.ControleFinanceiro.Pages.Controle;
using Portal.ControleFinanceiro.Services;

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{description}: esperado {expected}; obtido {actual}.");
}

var reversed = new PluggyCardTransaction("original", new DateTime(2026, 11, 2),
    "SAM S TAMBORE LJ 4932BARUERIBR", 169.58m, "8930", 1, 1, "2026-11", null);
Equal<PluggyCardTransaction?>(null, PluggyTransactionCorrection.Apply(reversed),
    "compra original estornada não deve aparecer");

var installment = new PluggyCardTransaction("parcela", new DateTime(2026, 11, 4),
    "parc SAM S TAMBOR01/04", 47.62m, "5169", 1, 4, "2026-11", new DateTime(2026, 11, 2));
var corrected = PluggyTransactionCorrection.Apply(installment)!;
Equal("8930", corrected.CardNumber, "parcela do Sam's deve usar o final da Camila");
Equal("5169", corrected.ReportedCardNumber, "final informado pela Pluggy permanece auditável");
Equal("Camila", corrected.Person, "parcela deve ser atribuída à Camila");
Equal(47.62m, corrected.Amount, "valor da parcela não pode mudar");

var future = installment with { Id = "segunda", Date = new DateTime(2026, 12, 4),
    Description = "parc SAM S TAMBOR02/04", InstallmentNumber = 2 };
Equal("Camila", PluggyTransactionCorrection.Apply(future)!.Person,
    "parcela seguinte do mesmo parcelamento permanece com Camila");
Equal("Diego", PluggyTransactionCorrection.Apply(installment with { Amount = 48m })!.Person,
    "outra compra no cartão 5169 não deve ser alterada");

var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Pluggy:ClientId"] = "test-client",
    ["Pluggy:ClientSecret"] = "test-secret",
    ["Pluggy:ItemId"] = "test-item",
    ["UrlApi"] = "https://api.test/"
}).Build();
using var http = new HttpClient(new PluggyHandler());
using var cache = new MemoryCache(new MemoryCacheOptions());
var service = new PluggyCardService(http, configuration, cache);
var transactions = await service.ReadInfiniteTransactionsAsync(CancellationToken.None);
Equal(3, transactions.Count, "estorno e compra original não entram na conferência");
Equal("Camila", transactions.Single(item => item.Id == "parcela").Person,
    "leitura da Pluggy usa o cartão corrigido");
Equal("Diego", transactions.Single(item => item.Id == "outra").Person,
    "demais compras do 5169 continuam com Diego");
Equal(new DateTime(2026, 10, 31), transactions.Single(item => item.Id == "food").Date,
    "data UTC à meia-noite deve ser interpretada no horário de Brasília");
Equal(new DateTime(2026, 11, 1), transactions.Single(item => item.Id == "food").ReportedDate,
    "data original da Pluggy permanece auditável");

var alreadyImported = new ImportarPluggyModel(service, configuration,
    new ApiClientFactory(new ConferenciaHandler(allExisting: true)))
{ Mes = "2026-11", Selecionados = ["parcela", "outra"] };
await alreadyImported.OnPostAsync(CancellationToken.None);
Equal(true, alreadyImported.Aviso?.Contains("Já lançadas na planilha: 2", StringComparison.OrdinalIgnoreCase) == true,
    "reenvio de compras já lançadas deve informar o resultado sem tratar como erro");

var mixedSelection = new ImportarPluggyModel(service, configuration,
    new ApiClientFactory(new ConferenciaHandler(allExisting: false)))
{ Mes = "2026-11", Selecionados = ["parcela", "outra"] };
await mixedSelection.OnPostAsync(CancellationToken.None);
Equal(true, mixedSelection.Erro?.Contains("Já lançadas: 1", StringComparison.OrdinalIgnoreCase) == true,
    "seleção mista deve indicar quantas compras já constam na planilha");
Equal(true, mixedSelection.Erro?.Contains("Nenhuma nova compra", StringComparison.Ordinal) == true,
    "seleção mista deve deixar claro que o bloqueio vale apenas para esta tentativa");
Console.WriteLine("Conferência da Pluggy passou.");

sealed class ApiClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public ApiClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

sealed class ConferenciaHandler : HttpMessageHandler
{
    private readonly bool _allExisting;
    public ConferenciaHandler(bool allExisting) => _allExisting = allExisting;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        object body = request.RequestUri!.AbsolutePath switch
        {
            "/Compra/ImportacaoPluggyDisponivel" => new { },
            "/Compra/ConferirImportacaoPluggy" => (await request.Content!
                .ReadFromJsonAsync<List<ParcelaId>>(cancellationToken: cancellationToken))!
                .Select(item => new { item.Id, Status = _allExisting || item.Id == "parcela"
                    ? "Existente" : "NaoEncontrada", Correspondente = (object?)null,
                    QuantidadeCorrespondencias = 0 }).ToList(),
            _ => throw new Exception($"Rota inesperada: {request.RequestUri.AbsolutePath}")
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
    }

    private sealed record ParcelaId(string Id);
}

sealed class PluggyHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        object body = path switch
        {
            "/auth" => new { apiKey = "test-key" },
            "/accounts" => new { results = new[] { new { id = "account", number = "5808",
                name = "LATAM PASS INFINITE", type = "CREDIT", subtype = "CREDIT_CARD" } } },
            "/bills" => new { results = Array.Empty<object>(), totalPages = 0 },
            "/v2/transactions" => new { results = new object[]
            {
                Transaction("original", "DEBIT", "2026-11-02", "SAM S TAMBORE LJ 4932BARUERIBR",
                    169.58m, "8930", 1, 1),
                Transaction("estorno", "CREDIT", "2026-11-02", "Estorno SAM S TAMBORE",
                    169.58m, "8930", 1, 1),
                Transaction("parcela", "DEBIT", "2026-11-04", "parc SAM S TAMBOR01/04",
                    47.62m, "5169", 1, 4),
                Transaction("outra", "DEBIT", "2026-11-04", "Outra compra",
                    30m, "5169", 1, 1),
                Transaction("food", "DEBIT", "2026-11-01T00:00:00.000Z", "99Food The Burger",
                    96.39m, "6235", 1, 1)
            }, next = "" },
            _ => throw new Exception($"Rota inesperada: {path}")
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        });
    }

    private static object Transaction(string id, string type, string date, string description,
        decimal amount, string cardNumber, int installment, int total) =>
        new { id, type, date, description, amount, currencyCode = "BRL",
            creditCardMetadata = new { cardNumber, installmentNumber = installment,
                totalInstallments = total, billForecastDate = "2026-11",
                purchaseDate = total > 1 ? "2026-11-02" : null } };
}
