using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portal.ControleFinanceiro.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;
using static ResumoModel;

namespace Portal.ControleFinanceiro.Pages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }
        public string Mensagem { get; set; }

        public bool Sucesso { get; set; }
        public string? Erro { get; set; }

        public List<ResumoPessoaMesDTO>? ResumoGeral { get; set; }
        public string? PeriodoAtual { get; set; }
        public UltimaCompraDTO? UltimaCompraGlobal { get; set; }

        public async Task OnGetAsync()
        {
            try
            {
                var urlApi = _configuration["UrlApi"];
                var url = $"{urlApi}Compra/ResumoGeral";

                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    ResumoGeral = JsonSerializer.Deserialize<List<ResumoPessoaMesDTO>>(content, options) ?? new();
                    await CarregarPeriodoEUltimaCompraAsync(httpClient, urlApi, options);
                    Sucesso = true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Mensagem = $"Erro ao buscar resumo: {error}";
                    ResumoGeral = new List<ResumoPessoaMesDTO>();
                }
            }
            catch (Exception ex)
            {
                Mensagem = $"Erro: {ex.Message}";
                ResumoGeral = new List<ResumoPessoaMesDTO>();
            }
        }

        private async Task CarregarPeriodoEUltimaCompraAsync(
            HttpClient httpClient,
            string? urlApi,
            JsonSerializerOptions options)
        {
            var pessoas = ResumoGeral?
                .Select(x => x.Pessoa)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList() ?? new List<string>();

            if (!pessoas.Any() || string.IsNullOrWhiteSpace(urlApi))
                return;

            PeriodoAtual = await ObterPeriodoAtualItauAsync(httpClient, urlApi, pessoas[0]);
            if (string.IsNullOrWhiteSpace(PeriodoAtual))
                return;

            var compras = new List<UltimaCompraDTO>();

            foreach (var pessoa in pessoas)
            {
                var url = $"{urlApi}Compra/ResumoPessoaPeriodo" +
                          $"?pessoa={Uri.EscapeDataString(pessoa)}" +
                          $"&mesAno={Uri.EscapeDataString(PeriodoAtual)}";

                var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    continue;

                var json = await response.Content.ReadAsStringAsync();
                var resumo = JsonSerializer.Deserialize<ResumoPeriodoDTO>(json, options);

                if (resumo?.Compras == null)
                    continue;

                foreach (var compra in resumo.Compras)
                {
                    if (!TryGetString(compra, "Data", out var dataTexto) ||
                        !DateTime.TryParse(dataTexto, new CultureInfo("pt-BR"), DateTimeStyles.None, out var data))
                        continue;

                    if (data.Date > DateTime.Today)
                        continue;

                    TryGetString(compra, "Compra", out var descricao);
                    TryGetString(compra, "Cartao", out var cartao);
                    TryGetString(compra, "IdLan", out var idLanTexto);
                    TryGetDecimal(compra, "Valor", out var valor);
                    long.TryParse(idLanTexto, out var idLan);

                    compras.Add(new UltimaCompraDTO
                    {
                        Pessoa = pessoa,
                        Descricao = descricao ?? "-",
                        Cartao = cartao ?? "-",
                        Data = data,
                        Valor = valor,
                        IdLan = idLan
                    });
                }
            }

            UltimaCompraGlobal = compras
                .OrderByDescending(x => x.Data)
                .ThenByDescending(x => x.IdLan)
                .FirstOrDefault();
        }

        private static async Task<string?> ObterPeriodoAtualItauAsync(
            HttpClient httpClient,
            string urlApi,
            string pessoa)
        {
            var hoje = DateTime.Today;
            var candidatos = new[] { hoje, hoje.AddMonths(-1) };

            foreach (var candidato in candidatos)
            {
                var mesAno = candidato.ToString("MM/yyyy", CultureInfo.InvariantCulture);
                var url = $"{urlApi}Compra/PeriodoFatura" +
                          $"?pessoa={Uri.EscapeDataString(pessoa)}" +
                          $"&mesAno={Uri.EscapeDataString(mesAno)}" +
                          $"&cartao=ITAU";

                var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    continue;

                var json = await response.Content.ReadAsStringAsync();
                var periodo = JsonSerializer.Deserialize<PeriodoFaturaDTO>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (periodo != null && hoje >= periodo.Inicio.Date && hoje <= periodo.Fim.Date)
                    return mesAno;
            }

            return null;
        }

        private static bool TryGetString(
            Dictionary<string, JsonElement> compra,
            string propriedade,
            out string? valor)
        {
            var item = compra.FirstOrDefault(x =>
                string.Equals(x.Key, propriedade, StringComparison.OrdinalIgnoreCase));

            valor = item.Key == null ? null : item.Value.ToString();
            return !string.IsNullOrWhiteSpace(valor);
        }

        private static bool TryGetDecimal(
            Dictionary<string, JsonElement> compra,
            string propriedade,
            out decimal valor)
        {
            var item = compra.FirstOrDefault(x =>
                string.Equals(x.Key, propriedade, StringComparison.OrdinalIgnoreCase));

            if (item.Key != null && item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetDecimal(out valor))
                return true;

            return decimal.TryParse(
                item.Value.ToString(),
                NumberStyles.Any,
                new CultureInfo("pt-BR"),
                out valor);
        }

        public class ResumoPessoaMesDTO
        {
            public string Pessoa { get; set; }
            public string MesAno { get; set; } // Ex: "07/2025"
            public decimal SaldoRestante { get; set; }
            public decimal ValorGuardado { get; set; }

        }

        private sealed class ResumoPeriodoDTO
        {
            public List<Dictionary<string, JsonElement>> Compras { get; set; } = new();
        }

        private sealed class PeriodoFaturaDTO
        {
            public DateTime Inicio { get; set; }
            public DateTime Fim { get; set; }
        }

        public sealed class UltimaCompraDTO
        {
            public string Pessoa { get; set; } = string.Empty;
            public string Descricao { get; set; } = string.Empty;
            public string Cartao { get; set; } = string.Empty;
            public DateTime Data { get; set; }
            public decimal Valor { get; set; }
            public long IdLan { get; set; }
        }

    }
}
