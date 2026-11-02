using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Portal.ControleFinanceiro.Services;

public sealed class PluggyCardService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    public PluggyCardService(HttpClient http, IConfiguration configuration, IMemoryCache cache)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://api.pluggy.ai/");
        _configuration = configuration;
        _cache = cache;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Pluggy:ClientId"]) &&
        !string.IsNullOrWhiteSpace(_configuration["Pluggy:ClientSecret"]) &&
        !string.IsNullOrWhiteSpace(_configuration["Pluggy:ItemId"]);

    public async Task<IReadOnlyList<PluggyCardTransaction>> ReadInfiniteTransactionsAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("A conexão Pluggy ainda não foi configurada no servidor.");

        var key = await GetApiKeyAsync(cancellationToken);
        using var accounts = await GetAsync($"accounts?itemId={Uri.EscapeDataString(_configuration["Pluggy:ItemId"]!)}", key, cancellationToken);
        var creditAccounts = accounts.RootElement.GetProperty("results").EnumerateArray()
            .Where(account => ReadString(account, "type") == "CREDIT" && ReadString(account, "subtype") == "CREDIT_CARD")
            .ToList();
        var infinite = creditAccounts.Where(account =>
                ReadString(account, "number") == "5808" &&
                ReadString(account, "name").Contains("INFINITE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (infinite.Count != 1)
            throw new InvalidOperationException("Não foi possível identificar com segurança o LATAM Pass Infinite final 5808 na Pluggy.");

        var accountId = ReadString(infinite[0], "id");
        var billMonths = await ReadBillMonthsAsync(accountId, key, cancellationToken);
        var result = new List<PluggyCardTransaction>();
        var next = $"?accountId={Uri.EscapeDataString(accountId)}";
        for (var page = 0; !string.IsNullOrEmpty(next) && page < 30; page++)
        {
            using var document = await GetAsync("v2/transactions" + next, key, cancellationToken);
            foreach (var transaction in document.RootElement.GetProperty("results").EnumerateArray())
            {
                var amount = ReadDecimal(transaction, "amount");
                if (ReadString(transaction, "currencyCode") != "BRL")
                    amount = ReadDecimal(transaction, "amountInAccountCurrency");
                if (amount <= 0) continue;
                var metadata = transaction.TryGetProperty("creditCardMetadata", out var card) && card.ValueKind == JsonValueKind.Object
                    ? card : default;
                var dateText = ReadString(transaction, "date");
                if (dateText.Length < 10 || !DateTime.TryParseExact(dateText[..10], "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                var billMonth = ReadString(metadata, "billForecastDate");
                if (billMonth.Length != 7)
                    billMonth = billMonths.GetValueOrDefault(ReadString(metadata, "billId")) ?? "";
                var id = ReadString(transaction, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                result.Add(new PluggyCardTransaction(
                    id, date, ReadString(transaction, "description"), amount,
                    ReadString(metadata, "cardNumber"), Math.Max(1, ReadInt(metadata, "installmentNumber")),
                    Math.Max(1, ReadInt(metadata, "totalInstallments")), billMonth));
            }
            next = ReadString(document.RootElement, "next");
            if (next.Length > 0 && (!next.StartsWith('?') || next.Contains("//", StringComparison.Ordinal)))
                throw new InvalidOperationException("Paginação inválida na resposta da Pluggy.");
        }
        if (!string.IsNullOrEmpty(next))
            throw new InvalidOperationException("A fatura retornou mais páginas do que o limite de segurança. Tente novamente mais tarde.");
        return result;
    }

    private async Task<Dictionary<string, string>> ReadBillMonthsAsync(string accountId, string key,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>();
        try
        {
            for (var page = 1; page <= 10; page++)
            {
                using var bills = await GetAsync(
                    $"bills?accountId={Uri.EscapeDataString(accountId)}&pageSize=100&page={page}", key, cancellationToken);
                foreach (var bill in bills.RootElement.GetProperty("results").EnumerateArray())
                {
                    var id = ReadString(bill, "id");
                    var dueDate = ReadString(bill, "dueDate");
                    if (id.Length > 0 && dueDate.Length >= 7) result[id] = dueDate[..7];
                }
                var totalPages = ReadInt(bills.RootElement, "totalPages");
                if (totalPages == 0 || page >= totalPages) break;
            }
        }
        catch (InvalidOperationException) { /* Algumas conexões não disponibilizam a lista de faturas. */ }
        return result;
    }

    private async Task<string> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        var clientId = _configuration["Pluggy:ClientId"]!;
        var cacheKey = $"pluggy-api-key:{clientId}";
        if (_cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrEmpty(cached)) return cached;
        using var response = await _http.PostAsJsonAsync("auth", new
        {
            clientId, clientSecret = _configuration["Pluggy:ClientSecret"]
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("A Pluggy recusou a autenticação. Confira as credenciais no servidor.");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var key = ReadString(document.RootElement, "apiKey");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("A Pluggy não retornou uma chave de acesso.");
        _cache.Set(cacheKey, key, TimeSpan.FromMinutes(110));
        return key;
    }

    private async Task<JsonDocument> GetAsync(string path, string key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-API-KEY", key);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Não foi possível consultar a fatura na Pluggy. Verifique a conexão e tente novamente.");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    private static string ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static int ReadInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : 0;

    private static decimal ReadDecimal(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result) ? result : 0;
}

public sealed record PluggyCardTransaction(string Id, DateTime Date, string Description, decimal Amount,
    string CardNumber, int InstallmentNumber, int TotalInstallments, string BillMonth)
{
    public string Person => CardNumber.EndsWith("8930", StringComparison.Ordinal) ||
        CardNumber.EndsWith("6235", StringComparison.Ordinal) ? "Camila" :
        CardNumber.EndsWith("5169", StringComparison.Ordinal) ||
        CardNumber.EndsWith("3008", StringComparison.Ordinal) ? "Diego" : "";
};
