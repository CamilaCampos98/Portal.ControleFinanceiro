using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Portal.ControleFinanceiro.Pages.Controle;

[Authorize]
public sealed class PlanejarFestaModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IConfiguration _configuration;
    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    public PlanejarFestaModel(IConfiguration configuration) => _configuration = configuration;

    public PartySnapshot Snapshot { get; private set; } = new();
    public string? Error { get; private set; }
    public bool GuestsOpen { get; private set; }
    public bool HousesOpen { get; private set; }
    [TempData] public string? Notice { get; set; }

    [BindProperty] public string Category { get; set; } = "";
    [BindProperty] public string ItemName { get; set; } = "";
    [BindProperty] public string AmountText { get; set; } = "";
    [BindProperty] public string ItemId { get; set; } = "";
    [BindProperty] public string ScheduleTarget { get; set; } = "";
    [BindProperty] public List<string> SelectedMonths { get; set; } = new();
    [BindProperty] public string MinimumText { get; set; } = "0";
    [BindProperty] public int DueDay { get; set; } = 15;
    [BindProperty] public string InstallmentId { get; set; } = "";
    [BindProperty] public string PaymentId { get; set; } = "";
    [BindProperty] public DateTime PaidAt { get; set; } = BrazilToday();
    [BindProperty] public string GuestId { get; set; } = "";
    [BindProperty] public string GuestName { get; set; } = "";
    [BindProperty] public bool GuestIsChild { get; set; }
    [BindProperty] public string GuestGender { get; set; } = "";
    [BindProperty] public string GuestNotes { get; set; } = "";
    [BindProperty] public string GuestHouseId { get; set; } = "";
    [BindProperty] public string HouseId { get; set; } = "";
    [BindProperty] public string HouseName { get; set; } = "";
    [BindProperty] public List<string> HouseGuestIds { get; set; } = new();
    [BindProperty] public int? BoysOrdered { get; set; }
    [BindProperty] public int? GirlsOrdered { get; set; }
    [BindProperty] public int? AdultsOrdered { get; set; }
    public DateTime TodayBrazil => BrazilToday();

    public decimal KnownTotal => Snapshot.Items.Sum(item => item.Amount ?? 0);
    public decimal PaidTotal => Snapshot.Payments.Sum(item => item.Amount);
    public decimal Remaining => Math.Max(0, KnownTotal - PaidTotal);
    public decimal UnplannedKnown => Math.Max(0, KnownTotal - Snapshot.Payments
        .Where(payment => payment.InstallmentId == null).Sum(payment => payment.Amount) -
        Snapshot.Installments.Sum(item => item.Amount));
    public int UnpricedCount => Snapshot.Items.Count(item => !item.Amount.HasValue);
    public int AdultSouvenirCount => Snapshot.Houses.Count(house => Snapshot.Guests.Any(guest =>
        guest.HouseId == house.Id && !guest.IsChild));
    public int BoySouvenirCount => Snapshot.Guests.Count(guest => guest.IsChild &&
        string.Equals(guest.Gender, "Menino", StringComparison.OrdinalIgnoreCase));
    public int GirlSouvenirCount => Snapshot.Guests.Count(guest => guest.IsChild &&
        string.Equals(guest.Gender, "Menina", StringComparison.OrdinalIgnoreCase));
    public int ChildSouvenirCount => Snapshot.Guests.Count(guest => guest.IsChild);
    public int UnclassifiedChildSouvenirs => ChildSouvenirCount - BoySouvenirCount - GirlSouvenirCount;
    public int TotalSouvenirCount => AdultSouvenirCount + ChildSouvenirCount;
    public int BoysOverOrder => Math.Max(0, BoySouvenirCount - (Snapshot.Event?.BoysOrdered ?? int.MaxValue));
    public int GirlsOverOrder => Math.Max(0, GirlSouvenirCount - (Snapshot.Event?.GirlsOrdered ?? int.MaxValue));
    public int AdultsOverOrder => Math.Max(0, AdultSouvenirCount - (Snapshot.Event?.AdultsOrdered ?? int.MaxValue));
    public int UnassignedAdults => Snapshot.Guests.Count(guest => !guest.IsChild &&
        string.IsNullOrWhiteSpace(guest.HouseId));

    public IReadOnlyList<DateTime> AvailableMonths
    {
        get
        {
            if (Snapshot.Event == null) return Array.Empty<DateTime>();
            var today = BrazilToday();
            var first = new DateTime(today.Year, today.Month, 1);
            var last = new DateTime(Snapshot.Event.Date.Year, Snapshot.Event.Date.Month, 1);
            var months = new List<DateTime>();
            for (var month = first; month <= last; month = month.AddMonths(1)) months.Add(month);
            return months;
        }
    }

    public IEnumerable<PartyItem> UnscheduledItems => Snapshot.Items.Where(item =>
        item.Amount is > 0 && !Snapshot.Installments.Any(part => part.Category == item.Category && part.ItemId == null) &&
        item.Amount > Snapshot.Payments.Where(payment => payment.ItemId == item.Id && payment.InstallmentId == null)
            .Sum(payment => payment.Amount) + Snapshot.Installments.Where(part => part.ItemId == item.Id).Sum(part => part.Amount));

    public IEnumerable<(string Category, decimal Amount)> CategoriesWithUnplannedBalance => Snapshot.Items
        .GroupBy(item => item.Category)
        .Where(group => group.All(item => item.Amount.HasValue) &&
            Snapshot.Installments.Any(part => part.Category == group.Key && part.ItemId == null))
        .Select(group => (Category: group.Key,
            Amount: group.Sum(item => item.Amount ?? 0) - Snapshot.Payments.Where(payment =>
                payment.Category == group.Key && payment.InstallmentId == null).Sum(payment => payment.Amount) -
                Snapshot.Installments.Where(part => part.Category == group.Key).Sum(part => part.Amount)))
        .Where(group => group.Amount > 0);

    public decimal PaidFor(PartyInstallment installment) => Snapshot.Payments
        .Where(payment => payment.InstallmentId == installment.Id).Sum(payment => payment.Amount);

    public decimal PaidFor(PartyItem item) => Snapshot.Payments
        .Where(payment => payment.ItemId == item.Id).Sum(payment => payment.Amount);

    public decimal RemainingFor(PartyItem item) => Math.Max(0, (item.Amount ?? 0) - PaidFor(item));

    public bool HasUnallocatedCategoryPayments(PartyItem item) => Snapshot.Payments
        .Any(payment => payment.Category == item.Category && payment.ItemId == null);

    public decimal CategoryPaid(string category) => Snapshot.Payments
        .Where(payment => payment.Category == category).Sum(payment => payment.Amount);

    public async Task OnGetAsync() => await LoadAsync(importIfMissing: true);

    public async Task<IActionResult> OnPostAddItemAsync()
    {
        if (!TryMoney(AmountText, out var amount, allowEmpty: true)) return await InvalidAsync("Informe um valor válido.");
        return await SendAsync(HttpMethod.Post, "Itens", new { category = Category, name = ItemName, amount });
    }

    public async Task<IActionResult> OnPostEditItemAsync()
    {
        if (!TryMoney(AmountText, out var amount, allowEmpty: true)) return await InvalidAsync("Informe um valor válido.");
        return await SendAsync(HttpMethod.Put, $"Itens/{Uri.EscapeDataString(ItemId)}",
            new { category = Category, name = ItemName, amount });
    }

    public async Task<IActionResult> OnPostScheduleAsync()
    {
        if (!TryMoney(MinimumText, out var minimum, allowEmpty: false) || minimum is < 0)
            return await InvalidAsync("Informe a parcela mínima válida.");
        var months = new List<DateTime>();
        foreach (var month in SelectedMonths)
        {
            if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return await InvalidAsync("Escolha meses válidos.");
            months.Add(parsed);
        }
        if (months.Count == 0) return await InvalidAsync("Escolha ao menos um mês para pagar.");
        await LoadAsync();
        string? itemId = null;
        string category;
        if (ScheduleTarget.StartsWith("item:", StringComparison.Ordinal))
        {
            itemId = ScheduleTarget[5..];
            category = Snapshot.Items.FirstOrDefault(item => item.Id == itemId)?.Category ?? "";
        }
        else if (ScheduleTarget.StartsWith("category:", StringComparison.Ordinal))
            category = ScheduleTarget[9..];
        else return await InvalidAsync("Escolha o item a parcelar.");
        return await SendAsync(HttpMethod.Post, "Parcelas", new
        {
            category, itemId, months, minimumInstallment = minimum, dueDay = DueDay
        });
    }

    public async Task<IActionResult> OnPostPayAsync()
    {
        await LoadAsync();
        var installment = Snapshot.Installments.FirstOrDefault(item => item.Id == InstallmentId);
        if (installment == null) return await InvalidAsync("Parcela não encontrada.");
        var remaining = installment.Amount - PaidFor(installment);
        if (!TryMoney(AmountText, out var amount, allowEmpty: true)) return await InvalidAsync("Informe um valor válido.");
        amount ??= remaining;
        return await SendAsync(HttpMethod.Post, $"Parcelas/{Uri.EscapeDataString(InstallmentId)}/Pagar",
            new { paymentId = PaymentId, paidAt = PaidAt.Date, amount });
    }

    public Task<IActionResult> OnPostAddGuestAsync() => SendAsync(HttpMethod.Post, "Convidados", GuestPayload(), guests: true);
    public Task<IActionResult> OnPostEditGuestAsync() => SendAsync(HttpMethod.Put,
        $"Convidados/{Uri.EscapeDataString(GuestId)}", GuestPayload(), guests: true);
    public Task<IActionResult> OnPostDeleteGuestAsync() => SendAsync(HttpMethod.Delete,
        $"Convidados/{Uri.EscapeDataString(GuestId)}", null, guests: true);
    public Task<IActionResult> OnPostAddHouseAsync() => SendAsync(HttpMethod.Post, "Casas",
        new { name = HouseName, guestIds = HouseGuestIds }, guests: true, houses: true);
    public Task<IActionResult> OnPostEditHouseAsync() => SendAsync(HttpMethod.Put,
        $"Casas/{Uri.EscapeDataString(HouseId)}", new { name = HouseName, guestIds = HouseGuestIds }, guests: true, houses: true);
    public Task<IActionResult> OnPostDeleteHouseAsync() => SendAsync(HttpMethod.Delete,
        $"Casas/{Uri.EscapeDataString(HouseId)}", null, guests: true, houses: true);
    public async Task<IActionResult> OnPostSaveSouvenirOrderAsync()
    {
        if (BoysOrdered is < 0 or > 10000 || GirlsOrdered is < 0 or > 10000 ||
            AdultsOrdered is < 0 or > 10000 || BoysOrdered == null || GirlsOrdered == null || AdultsOrdered == null)
            return await InvalidAsync("Informe as quantidades encomendadas para adultos, meninos e meninas.", guests: true, houses: true);
        return await SendAsync(HttpMethod.Put, "Lembrancinhas",
            new { boysOrdered = BoysOrdered.Value, girlsOrdered = GirlsOrdered.Value, adultsOrdered = AdultsOrdered.Value },
            guests: true, houses: true);
    }

    private object GuestPayload() => new { name = GuestName, isChild = GuestIsChild,
        gender = GuestGender, notes = GuestNotes, houseId = GuestHouseId };

    private async Task<IActionResult> SendAsync(HttpMethod method, string path, object? payload,
        bool guests = false, bool houses = false)
    {
        try
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(method, ApiUrl(path));
            if (payload != null) request.Content = JsonContent.Create(payload);
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return await InvalidAsync(await ReadErrorAsync(response), guests, houses);
            Notice = guests ? "Lista de convidados atualizada." : "Festa atualizada.";
            return guests ? RedirectToPage(new { guests = houses ? "houses" : "guests" }) : RedirectToPage();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        { return await InvalidAsync("Não foi possível conectar ao financeiro. Tente novamente.", guests, houses); }
    }

    private async Task<IActionResult> InvalidAsync(string message, bool guests = false, bool houses = false)
    {
        Error = message;
        GuestsOpen = guests;
        HousesOpen = houses;
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync(bool importIfMissing = false)
    {
        try
        {
            using var http = new HttpClient();
            Snapshot = await http.GetFromJsonAsync<PartySnapshot>(ApiUrl(""), JsonOptions) ?? new();
            if (importIfMissing && Snapshot.Event == null)
            {
                using var response = await http.PostAsync(ApiUrl("ImportarLivia"), null);
                if (!response.IsSuccessStatusCode) { Error = await ReadErrorAsync(response); return; }
                Snapshot = await response.Content.ReadFromJsonAsync<PartySnapshot>(JsonOptions) ?? new();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        { Error = "Não foi possível carregar a festa. Confira a conexão com a API."; }
    }

    private string ApiUrl(string path) => $"{_configuration["UrlApi"]}Festas/{path}";

    private static DateTime BrazilToday() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).Date;

    private static bool TryMoney(string? text, out decimal? value, bool allowEmpty)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return allowEmpty;
        var cleaned = text.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Trim();
        var culture = cleaned.Contains(',') || cleaned.Contains('.') && cleaned.Split('.').Last().Length == 3
            ? Brazil : CultureInfo.InvariantCulture;
        if (!decimal.TryParse(cleaned, NumberStyles.Number, culture, out var parsed) || parsed < 0 ||
            Math.Round(parsed, 2) != parsed) return false;
        value = parsed;
        return true;
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions);
            if (!string.IsNullOrWhiteSpace(error?.Message)) return error.Message;
        }
        catch (JsonException) { }
        return "Não foi possível salvar. Revise os dados e tente novamente.";
    }

    public sealed class PartySnapshot
    {
        public PartyEvent? Event { get; set; }
        public List<PartyItem> Items { get; set; } = new();
        public List<PartyInstallment> Installments { get; set; } = new();
        public List<PartyPayment> Payments { get; set; } = new();
        public List<PartyGuest> Guests { get; set; } = new();
        public List<PartyHouse> Houses { get; set; } = new();
    }
    public sealed class PartyEvent
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime Date { get; set; }
        public int? BoysOrdered { get; set; }
        public int? GirlsOrdered { get; set; }
        public int? AdultsOrdered { get; set; }
    }
    public sealed class PartyItem
    {
        public string Id { get; set; } = "";
        public string EventId { get; set; } = "";
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal? Amount { get; set; }
    }
    public sealed class PartyInstallment
    {
        public string Id { get; set; } = "";
        public string EventId { get; set; } = "";
        public string Category { get; set; } = "";
        public string? ItemId { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
    }
    public sealed class PartyPayment
    {
        public string Id { get; set; } = "";
        public string EventId { get; set; } = "";
        public string Category { get; set; } = "";
        public string? ItemId { get; set; }
        public string? InstallmentId { get; set; }
        public string Description { get; set; } = "";
        public DateTime PaidAt { get; set; }
        public decimal Amount { get; set; }
    }
    public sealed class PartyGuest
    {
        public string Id { get; set; } = "";
        public string EventId { get; set; } = "";
        public string HouseId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsChild { get; set; }
        public string Gender { get; set; } = "";
        public string Notes { get; set; } = "";
    }
    public sealed class PartyHouse
    {
        public string Id { get; set; } = "";
        public string EventId { get; set; } = "";
        public string Name { get; set; } = "";
    }
    private sealed class ApiError { public string Message { get; set; } = ""; }
}
