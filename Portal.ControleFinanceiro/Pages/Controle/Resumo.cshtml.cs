using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Reflection;
using System.Text.Json;
using static ResumoModel;
using static System.Runtime.InteropServices.JavaScript.JSType;

public class ResumoModel : PageModel
{
    private readonly IConfiguration _configuration;

    public ResumoModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    public bool Sucesso { get; set; }
    public string? Erro { get; set; }

    public int PaginaAtual { get; set; } = 1;
    public int TotalPaginas { get; set; }
    public int ItensPorPagina { get; set; } = 20;

    [BindProperty]
    public FiltroResumo Filtro { get; set; }

    public ResultadoResumo Resumo { get; set; }

    public IActionResult OnGet(int pagina = 1)
    {
        PaginaAtual = pagina;

        var resumoJson = HttpContext.Session.GetString("ResumoJson");
        Resumo = resumoJson != null ? JsonSerializer.Deserialize<ResultadoResumo>(resumoJson) : null;

        // Código para buscar Resumo, etc.
        if (Resumo != null)
        {
            if (Resumo.Compras.Count > 0)
            {
                var totalItens = Resumo.Compras.Count;
                TotalPaginas = (int)Math.Ceiling(totalItens / (double)ItensPorPagina);

                Resumo.Compras = Resumo.Compras
                    .OrderByDescending(x => Convert.ToInt32(x["IdLan"].ToString()))
                    .Skip((PaginaAtual - 1) * ItensPorPagina)
                    .Take(ItensPorPagina)
                    .ToList();
            }
        }
       
        return Page();

    }

    public async Task OnPostAsync()
    {
        Resumo = await ObterResumoAsync(Filtro);

        if (Resumo.Compras.Count > 0)
        {
            var totalItens = Resumo.Compras.Count;

            TotalPaginas = (int)Math.Ceiling(totalItens / (double)ItensPorPagina);

            Resumo.Compras = Resumo.Compras
                            .OrderByDescending(x => Convert.ToInt32(x["IdLan"].ToString()))
                            .ToList()
                            .Skip((PaginaAtual - 1) * ItensPorPagina)
                            .Take(ItensPorPagina)
                            .ToList();

        }
    }

    private async Task<ResultadoResumo> ObterResumoAsync(FiltroResumo filtro)
    {
        try
        {
            var urlApi = _configuration["UrlApi"];
            var url = $"{urlApi}Compra/ResumoPessoaPeriodo?pessoa={filtro.Pessoa}&dataInicio={filtro.DataInicio:yyyy-MM-dd}&dataFim={filtro.DataFim:yyyy-MM-dd}";

            using var httpClient = new HttpClient();

            var response = await httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var resultado = JsonSerializer.Deserialize<ResultadoResumo>(content, options);

                // A partir daqui você usa normalmente:
                var saldo = resultado.SaldoRestante;
                var compras = resultado.Compras;
                var totalGasto = resultado.TotalGasto;
                var totalGastoFixos = resultado.GastosFixos;
                var totalComExtras = resultado.Salario + (resultado.Extras);

                bool saldoCritico = saldo < (Convert.ToDecimal(0.2) * totalComExtras); // Considera crítico se saldo for menor que 20% do total com extras
                Sucesso = true;

                HttpContext.Session.SetString("ResumoJson", JsonSerializer.Serialize(resultado));

                return new ResultadoResumo
                {
                    Pessoa = filtro.Pessoa,
                    Periodo = $"{filtro.DataInicio:dd/MM/yyyy} a {filtro.DataFim:dd/MM/yyyy}",
                    Salario = resultado.Salario,
                    Extras = resultado.Extras,
                    TotalGasto = totalGasto,
                    GastosFixos = totalGastoFixos,
                    SaldoRestante = saldo,
                    SaldoCritico = saldoCritico,
                    Compras = compras
                };
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Erro = $"Erro ao buscar resumo: {error}";

                return new ResultadoResumo();
            }



        }
        catch (Exception ex)
        {
            Erro = $"Erro: {ex.Message}";
            return new ResultadoResumo();
        }

    }

    public IActionResult OnPostLimparSessao()
    {
        HttpContext.Session.Remove("ResumoJson");
        return RedirectToPage(); // Redireciona para a primeira página limpa
    }

    public class FiltroResumo
    {
        public string Pessoa { get; set; }
        public DateTime DataInicio { get; set; }
        public DateTime DataFim { get; set; }
    }

    public class ResultadoResumo
    {
        public string Pessoa { get; set; }
        public string Periodo { get; set; }
        public decimal Salario { get; set; }
        public decimal Extras { get; set; }
        public decimal TotalGasto { get; set; }
        public decimal GastosFixos { get; set; }
        public decimal SaldoRestante { get; set; }
        public bool SaldoCritico { get; set; }
        public List<Dictionary<string, object>> Compras { get; set; }
    }

}
