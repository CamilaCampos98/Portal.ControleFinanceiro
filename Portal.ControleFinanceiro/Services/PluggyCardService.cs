using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Portal.ControleFinanceiro.Services;

public enum PluggyCardSource { Itau, BradescoCamila, BradescoDiego }

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

    public bool IsConfigured => IsConfiguredFor(PluggyCardSource.Itau);

    public bool IsConfiguredFor(PluggyCardSource source)
    {
        var connection = GetConnection(source);
        return !string.IsNullOrWhiteSpace(connection.ClientId) &&
            !string.IsNullOrWhiteSpace(connection.ClientSecret) &&
            !string.IsNullOrWhiteSpace(connection.ItemId);
    }

    public async Task<IReadOnlyList<PluggyCardTransaction>> ReadInfiniteTransactionsAsync(CancellationToken cancellationToken)
        => await ReadTransactionsAsync(PluggyCardSource.Itau, cancellationToken);

    public async Task<IReadOnlyList<PluggyCardTransaction>> ReadTransactionsAsync(
        PluggyCardSource source, CancellationToken cancellationToken)
    {
        if (!IsConfiguredFor(source))
            throw new InvalidOperationException("A conexão Pluggy ainda não foi configurada no servidor.");

        var connection = GetConnection(source);
        var key = await GetApiKeyAsync(connection, cancellationToken);
        using var accounts = await GetAsync($"accounts?itemId={Uri.EscapeDataString(connection.ItemId!)}", key, cancellationToken);
        var creditAccounts = accounts.RootElement.GetProperty("results").EnumerateArray()
            .Where(account => ReadString(account, "type") == "CREDIT" && ReadString(account, "subtype") == "CREDIT_CARD")
            .ToList();
        var expectedFinal = source switch
        {
            PluggyCardSource.Itau => "5808",
            PluggyCardSource.BradescoCamila => "6311",
            PluggyCardSource.BradescoDiego => "7937",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        var selectedAccounts = creditAccounts.Where(account =>
                ReadString(account, "number").EndsWith(expectedFinal, StringComparison.Ordinal) &&
                (source != PluggyCardSource.Itau ||
                 ReadString(account, "name").Contains("INFINITE", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (selectedAccounts.Count != 1)
            throw new InvalidOperationException($"Não foi possível identificar com segurança o cartão final {expectedFinal} na Pluggy.");

        var accountId = ReadString(selectedAccounts[0], "id");
        var billMonths = await ReadBillMonthsAsync(accountId, key, cancellationToken);
        var result = new List<PluggyCardTransaction>();
        var next = $"?accountId={Uri.EscapeDataString(accountId)}";
        for (var page = 0; !string.IsNullOrEmpty(next) && page < 30; page++)
        {
            using var document = await GetAsync("v2/transactions" + next, key, cancellationToken);
            foreach (var transaction in document.RootElement.GetProperty("results").EnumerateArray())
            {
                // A Pluggy normaliza estornos e pagamentos da fatura como CREDIT, não como compras.
                if (ReadString(transaction, "type") == "CREDIT") continue;
                var amount = ReadDecimal(transaction, "amount");
                if (ReadString(transaction, "currencyCode") != "BRL")
                    amount = ReadDecimal(transaction, "amountInAccountCurrency");
                if (amount <= 0) continue;
                var metadata = transaction.TryGetProperty("creditCardMetadata", out var card) && card.ValueKind == JsonValueKind.Object
                    ? card : default;
                var dateText = ReadString(transaction, "date");
                if (dateText.Length < 10 || !DateTime.TryParseExact(dateText[..10], "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var reportedDate) ||
                    !TryReadBrazilianDate(dateText, out var date)) continue;
                var purchaseDateText = ReadString(metadata, "purchaseDate");
                DateTime? purchaseDate = purchaseDateText.Length >= 10 &&
                    DateTime.TryParseExact(purchaseDateText[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsedPurchaseDate) ? parsedPurchaseDate : null;
                var billMonth = ReadString(metadata, "billForecastDate");
                if (billMonth.Length != 7)
                    billMonth = billMonths.GetValueOrDefault(ReadString(metadata, "billId")) ?? "";
                var id = ReadString(transaction, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var transactionRecord = new PluggyCardTransaction(
                    id, reportedDate, ReadString(transaction, "description"), amount,
                    ReadString(metadata, "cardNumber"), Math.Max(1, ReadInt(metadata, "installmentNumber")),
                    Math.Max(1, ReadInt(metadata, "totalInstallments")), billMonth, purchaseDate,
                    Source: source, Status: ReadString(transaction, "status"));
                var corrected = source == PluggyCardSource.Itau
                    ? PluggyTransactionCorrection.Apply(transactionRecord) : transactionRecord;
                if (corrected is not null)
                    result.Add(corrected with { Date = date, ReportedDate = date != reportedDate ? reportedDate : null });
            }
            next = ReadString(document.RootElement, "next");
            if (next.Length > 0 && (!next.StartsWith('?') || next.Contains("//", StringComparison.Ordinal)))
                throw new InvalidOperationException("Paginação inválida na resposta da Pluggy.");
        }
        if (!string.IsNullOrEmpty(next))
            throw new InvalidOperationException("A fatura retornou mais páginas do que o limite de segurança. Tente novamente mais tarde.");
        return result;
    }

    public static string? FindOpenBillMonth(IEnumerable<PluggyCardTransaction> transactions, DateTime today)
    {
        var firstPossibleMonth = new DateTime(today.Year, today.Month, 1);
        var lastPossibleMonth = firstPossibleMonth.AddMonths(1);
        return transactions.Where(item => item.Status == "PENDING" &&
                DateTime.TryParseExact(item.BillMonth, "yyyy-MM", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var month) &&
                month >= firstPossibleMonth && month <= lastPossibleMonth)
            .Select(item => item.BillMonth).Distinct().Order(StringComparer.Ordinal).FirstOrDefault();
    }

    private PluggyConnection GetConnection(PluggyCardSource source) => source switch
    {
        PluggyCardSource.Itau => new(_configuration["Pluggy:ClientId"],
            _configuration["Pluggy:ClientSecret"], _configuration["Pluggy:ItemId"]),
        PluggyCardSource.BradescoCamila => new(_configuration["Pluggy:ClientId"],
            _configuration["Pluggy:ClientSecret"], _configuration["Pluggy:BradescoCamila:ItemId"]),
        PluggyCardSource.BradescoDiego => new(_configuration["Pluggy:BradescoDiego:ClientId"],
            _configuration["Pluggy:BradescoDiego:ClientSecret"],
            _configuration["Pluggy:BradescoDiego:ItemId"]),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private sealed record PluggyConnection(string? ClientId, string? ClientSecret, string? ItemId);

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

    private async Task<string> GetApiKeyAsync(PluggyConnection connection, CancellationToken cancellationToken)
    {
        var clientId = connection.ClientId!;
        var cacheKey = $"pluggy-api-key:{clientId}";
        if (_cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrEmpty(cached)) return cached;
        using var response = await _http.PostAsJsonAsync("auth", new
        {
            clientId, clientSecret = connection.ClientSecret
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

    private static bool TryReadBrazilianDate(string value, out DateTime date)
    {
        date = default;
        if (value.Length < 10 || !DateTime.TryParseExact(value[..10], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var reportedDate)) return false;
        var hasOffset = value.EndsWith('Z') || value.Length >= 6 &&
            (value[^6] is '+' or '-') && value[^3] == ':';
        date = hasOffset && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var instant)
            ? instant.ToOffset(TimeSpan.FromHours(-3)).Date : reportedDate;
        return true;
    }
}

public sealed record PluggyCardTransaction(string Id, DateTime Date, string Description, decimal Amount,
    string CardNumber, int InstallmentNumber, int TotalInstallments, string BillMonth, DateTime? PurchaseDate,
    string? ReportedCardNumber = null, DateTime? ReportedDate = null,
    PluggyCardSource Source = PluggyCardSource.Itau, string Status = "")
{
    public string Person => Source switch
    {
        PluggyCardSource.BradescoCamila when HasFinal("6311") || HasFinal("0442") => "Camila",
        PluggyCardSource.BradescoDiego when HasFinal("7937") || HasFinal("2243") => "Diego",
        PluggyCardSource.Itau when HasFinal("8930") || HasFinal("6235") => "Camila",
        PluggyCardSource.Itau when HasFinal("5169") || HasFinal("3008") => "Diego",
        _ => ""
    };

    private bool HasFinal(string final) => CardNumber.Trim().EndsWith(final, StringComparison.Ordinal);

    public DateTime InstallmentDate => PurchaseDate is { } purchaseDate
        ? purchaseDate.AddMonths(TotalInstallments > 1 ? InstallmentNumber - 1 : 0)
        : Date;
};
