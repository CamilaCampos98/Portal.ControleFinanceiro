using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Portal.ControleFinanceiro.Pages.Controle;

[Authorize]
public class PlanejarFeriasModel : PageModel
{
    private readonly IConfiguration _configuration;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PlanejarFeriasModel(IConfiguration configuration) => _configuration = configuration;

    [BindProperty]
    public VacationInput Input { get; set; } = new();
    public VacationPreview? Preview { get; private set; }
    public string? Message { get; private set; }
    public bool Saved { get; private set; }

    public async Task OnGetAsync()
    {
        Input.StartDate = DateTime.Today.AddMonths(2);
        Input.EndDate = Input.StartDate.AddDays(6);
        await LoadPeopleAsync();
    }

    public async Task OnPostPreviewAsync()
    {
        NormalizePostedSelections();
        await LoadPeopleAsync();
        var request = BuildRequest();
        if (request == null) return;
        try
        {
            using var http = new HttpClient();
            using var response = await http.PostAsJsonAsync($"{_configuration["UrlApi"]}Compra/Ferias/Previa", request);
            if (!response.IsSuccessStatusCode)
            {
                Message = await ReadErrorAsync(response);
                return;
            }
            Preview = await response.Content.ReadFromJsonAsync<VacationPreview>(JsonOptions);
            if (Preview != null)
            {
                Input.ExpectedGuardadoTransfers = Preview.GuardadoTransfers;
                Input.ExpectedLostIncome = Preview.Selected.LostIncome;
                Input.ExpectedLowestPeriodBalanceAfter = Preview.Selected.LowestPeriodBalanceAfter;
                foreach (var key in ModelState.Keys.Where(key =>
                    key.StartsWith("Input.ExpectedGuardadoTransfers[", StringComparison.Ordinal) ||
                    key is "Input.ExpectedLostIncome" or "Input.ExpectedLowestPeriodBalanceAfter").ToList())
                    ModelState.Remove(key);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            Message = "Não foi possível calcular a prévia. Confira a conexão e tente novamente.";
        }
    }

    public async Task OnPostConfirmAsync()
    {
        NormalizePostedSelections();
        await LoadPeopleAsync();
        var request = BuildRequest();
        if (request == null) return;
        try
        {
            using var http = new HttpClient();
            using var response = await http.PostAsJsonAsync($"{_configuration["UrlApi"]}Compra/Ferias/Confirmar", request);
            if (!response.IsSuccessStatusCode)
            {
                Message = await ReadErrorAsync(response);
                return;
            }
            Saved = true;
            Message = "Férias planejadas. O Guardado foi distribuído proporcionalmente, se escolhido, preservando ao menos 30% em cada mês. As metas restantes foram provisionadas.";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            Message = "Não foi possível confirmar o planejamento. Confira a conexão antes de tentar novamente.";
        }
    }

    private async Task LoadPeopleAsync()
    {
        var posted = Input.People
            .Where(person => !string.IsNullOrWhiteSpace(person.Person))
            .GroupBy(person => person.Person.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var signedIn = User.Identity?.Name?.Trim();
        if (!string.IsNullOrWhiteSpace(signedIn) && !posted.ContainsKey(signedIn))
            posted[signedIn] = new SelectedPerson { Person = signedIn, Selected = true };
        try
        {
            using var http = new HttpClient();
            var salaries = await http.GetFromJsonAsync<List<RegisteredSalary>>(
                $"{_configuration["UrlApi"]}Compra/SalariosCadastrados", JsonOptions) ?? new();
            var available = salaries.Select(salary => salary.Pessoa)
                .Where(person => !string.IsNullOrWhiteSpace(person))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            Input.People = posted.Values.Concat(available.Where(person => !posted.ContainsKey(person))
                .OrderBy(person => person)
                .Select(person => new SelectedPerson { Person = person }))
                .ToList();
            if (available.Count == 0)
                Message = "Nenhuma pessoa com salário cadastrado foi encontrada. Confira a tela Entradas.";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            Input.People = posted.Values.ToList();
            Message = "A lista de salários não carregou. Confira a conexão com a API e tente novamente.";
        }
    }

    private void NormalizePostedSelections()
    {
        for (var index = 0; index < Input.People.Count; index++)
        {
            var key = $"Input.People[{index}].Selected";
            if (!Request.Form.TryGetValue(key, out var value) ||
                !value.Any(item => string.Equals(item, "on", StringComparison.OrdinalIgnoreCase))) continue;
            Input.People[index].Selected = true;
            ModelState.Remove(key);
        }
    }

    private VacationRequest? BuildRequest()
    {
        if (!TryParseMoney(Input.BudgetText, out var budget) || budget <= 0)
        {
            Message = "Informe o orçamento total em reais.";
            return null;
        }
        var people = Input.People.Where(person => person.Selected).ToList();
        if (people.Count == 0 || Math.Abs(people.Sum(person => person.Percent) - 100) > 0.01m)
        {
            Message = "Selecione pessoas e divida 100% do orçamento entre elas.";
            return null;
        }
        if (Input.EndDate < Input.StartDate)
        {
            Message = "A data de fim deve ser igual ou posterior à de início.";
            return null;
        }
        return new VacationRequest
        {
            StartDate = Input.StartDate, EndDate = Input.EndDate, Budget = budget,
            UseGuardado = Input.UseGuardado,
            ExpectedLostIncome = Input.ExpectedLostIncome,
            ExpectedLowestPeriodBalanceAfter = Input.ExpectedLowestPeriodBalanceAfter,
            ExpectedGuardadoTransfers = Input.ExpectedGuardadoTransfers,
            Participants = people.Select(person => new VacationPerson { Person = person.Person, Percent = person.Percent }).ToList()
        };
    }

    private static bool TryParseMoney(string? text, out decimal amount)
    {
        if (string.IsNullOrWhiteSpace(text)) { amount = 0; return true; }
        var value = text.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Trim();
        var culture = value.Contains(',') || (value.Contains('.') && value.Split('.').Last().Length == 3)
            ? CultureInfo.GetCultureInfo("pt-BR") : CultureInfo.InvariantCulture;
        return decimal.TryParse(value, NumberStyles.Number, culture, out amount);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions);
            if (!string.IsNullOrWhiteSpace(error?.Message)) return error.Message;
        }
        catch (JsonException) { }
        return "Não foi possível concluir o planejamento. Revise os dados e tente novamente.";
    }

    public sealed class VacationInput
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string BudgetText { get; set; } = "";
        public bool UseGuardado { get; set; }
        public decimal? ExpectedLostIncome { get; set; }
        public decimal? ExpectedLowestPeriodBalanceAfter { get; set; }
        public List<GuardadoTransfer> ExpectedGuardadoTransfers { get; set; } = new();
        public List<SelectedPerson> People { get; set; } = new();
    }
    public sealed class SelectedPerson
    {
        public string Person { get; set; } = "";
        public bool Selected { get; set; }
        public decimal Percent { get; set; }
    }
    public sealed class RegisteredSalary { public string Pessoa { get; set; } = ""; }
    private sealed class ApiError { public string Message { get; set; } = ""; }
    public sealed class VacationRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal Budget { get; set; }
        public bool UseGuardado { get; set; }
        public decimal? ExpectedLostIncome { get; set; }
        public decimal? ExpectedLowestPeriodBalanceAfter { get; set; }
        public List<GuardadoTransfer> ExpectedGuardadoTransfers { get; set; } = new();
        public List<VacationPerson> Participants { get; set; } = new();
    }
    public sealed class GuardadoOption
    {
        public string Id { get; set; } = "";
        public int RowNumber { get; set; }
        public string Person { get; set; } = "";
        public string Period { get; set; } = "";
        public decimal Available { get; set; }
    }
    public sealed class VacationPerson
    {
        public string Person { get; set; } = "";
        public decimal Percent { get; set; }
    }
    public sealed class VacationPreview
    {
        public VacationOption Selected { get; set; } = new();
        public VacationOption Recommended { get; set; } = new();
        public List<VacationOption> Alternatives { get; set; } = new();
        public string? Warning { get; set; }
        public List<GuardadoOption> AvailableGuardado { get; set; } = new();
        public List<GuardadoTransfer> GuardadoTransfers { get; set; } = new();
    }
    public sealed class GuardadoTransfer
    {
        public string Id { get; set; } = "";
        public int RowNumber { get; set; }
        public string Person { get; set; } = "";
        public string Period { get; set; } = "";
        public decimal VacationAmount { get; set; }
        public decimal IncomeAmount { get; set; }
    }
    public sealed class MonthlyVacationSaving
    {
        public string Period { get; set; } = "";
        public decimal FromGuardado { get; set; }
        public decimal Additional { get; set; }
        public decimal ForVacation { get; set; }
        public decimal ForIncome { get; set; }
        public decimal Total => FromGuardado + Additional;
    }
    public List<MonthlyVacationSaving> BuildMonthlySavings(PersonImpact person)
    {
        if (Preview?.Selected is not { SavingPeriods: > 0 } selected) return new();
        var vacationPeriod = new DateTime(selected.StartDate.Year, selected.StartDate.Month, 1)
            .AddMonths(selected.StartDate.Day >= 26 ? 1 : 0);
        var firstPeriod = vacationPeriod.AddMonths(-selected.SavingPeriods);
        var remainingVacation = person.BudgetShare - person.GuardadoAllocated;
        var remainingIncome = person.LostIncome - person.IncomeGuardadoAllocated;
        var schedule = new List<MonthlyVacationSaving>();
        for (var index = 0; index < selected.SavingPeriods; index++)
        {
            var period = firstPeriod.AddMonths(index).ToString("MM/yyyy", CultureInfo.InvariantCulture);
            var transfers = Preview.GuardadoTransfers.Where(transfer =>
                string.Equals(transfer.Person, person.Person, StringComparison.OrdinalIgnoreCase) &&
                DateTime.TryParseExact(transfer.Period, new[] { "M/yyyy", "MM/yyyy" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var transferPeriod) &&
                transferPeriod.ToString("MM/yyyy", CultureInfo.InvariantCulture) == period).ToList();
            var newVacation = index == selected.SavingPeriods - 1
                ? remainingVacation - person.MonthlySaving * index : person.MonthlySaving;
            var newIncome = index == selected.SavingPeriods - 1
                ? remainingIncome - person.IncomeMonthlySaving * index : person.IncomeMonthlySaving;
            schedule.Add(new MonthlyVacationSaving
            {
                Period = period,
                FromGuardado = transfers.Sum(transfer => transfer.VacationAmount + transfer.IncomeAmount),
                Additional = newVacation + newIncome,
                ForVacation = transfers.Sum(transfer => transfer.VacationAmount) + newVacation,
                ForIncome = transfers.Sum(transfer => transfer.IncomeAmount) + newIncome
            });
        }
        return schedule;
    }
    public sealed class VacationOption
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int BusinessDays { get; set; }
        public decimal Budget { get; set; }
        public decimal LostIncome { get; set; }
        public decimal BalanceBefore { get; set; }
        public decimal BalanceAfter { get; set; }
        public decimal LowestPeriodBalanceAfter { get; set; }
        public string TightestPeriod { get; set; } = "";
        public int SavingPeriods { get; set; }
        public bool HasEstimatedSalary { get; set; }
        public List<PersonImpact> People { get; set; } = new();
    }
    public sealed class PersonImpact
    {
        public string Person { get; set; } = "";
        public decimal BudgetShare { get; set; }
        public decimal Percent { get; set; }
        public int UnpaidHours { get; set; }
        public decimal LostIncome { get; set; }
        public decimal MonthlySaving { get; set; }
        public decimal GuardadoAllocated { get; set; }
        public decimal IncomeGuardadoAllocated { get; set; }
        public decimal IncomeMonthlySaving { get; set; }
        public decimal IncomeReserveFunded { get; set; }
        public decimal BalanceBefore { get; set; }
        public decimal BalanceAfter { get; set; }
        public decimal LowestPeriodBalanceAfter { get; set; }
        public string TightestPeriod { get; set; } = "";
        public Dictionary<string, decimal> LossByPeriod { get; set; } = new();
    }
}
