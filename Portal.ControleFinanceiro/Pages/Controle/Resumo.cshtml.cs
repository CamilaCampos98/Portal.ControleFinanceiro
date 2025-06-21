using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using static Fixos;
using static ResumoModel;
using static System.Runtime.InteropServices.JavaScript.JSType;

[Authorize]
public class ResumoModel : PageModel
{
    private readonly IConfiguration _configuration;

    public ResumoModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    public bool Sucesso { get; set; }
    public string? Mensagem { get; set; }

    public int PaginaAtual { get; set; } = 1;
    public int TotalPaginas { get; set; }
    public int ItensPorPagina { get; set; } = 20;

    [BindProperty]
    public FiltroResumo Filtro { get; set; }

    public ResultadoResumo Resumo { get; set; }
    [BindProperty]
    public EditarCompraRequest Compra { get; set; } = new();

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
     .OrderByDescending(x => DateTime.Parse(x["Data"].ToString()))
     .ThenByDescending(x => Convert.ToInt32(x["IdLan"].ToString()))
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

        if (!Sucesso)
        {
            Resumo.Compras = new List<Dictionary<string, object>>();
            Mensagem = $"Erro ao obter resumo: {Mensagem}";
            return;
        }

        if (Resumo?.Compras?.Count > 0)
        {
            var totalItens = Resumo.Compras.Count;

            TotalPaginas = (int)Math.Ceiling(totalItens / (double)ItensPorPagina);

            Resumo.Compras = Resumo.Compras
     .OrderByDescending(x => DateTime.Parse(x["Data"].ToString()))
     .ThenByDescending(x => Convert.ToInt32(x["IdLan"].ToString()))
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
            var mesAnoParam = filtro.Periodo;
            var url = $"{urlApi}Compra/ResumoPessoaPeriodo?pessoa={filtro.Pessoa}&mesAno={mesAnoParam}";

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

                if (resultado == null)
                    return new ResultadoResumo();

                // Calcula saldo crítico no front
                var totalComExtras = resultado.Salario + resultado.Extras;
                var saldoCritico = resultado.SaldoRestante < (0.2m * totalComExtras);

                // Atualiza propriedades importantes antes de retornar
                resultado.SaldoCritico = saldoCritico;

                Sucesso = true;
                HttpContext.Session.SetString("ResumoJson", JsonSerializer.Serialize(resultado));

                return resultado;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Mensagem = $"Erro ao buscar resumo: {error}";

                return new ResultadoResumo();
            }
        }
        catch (Exception ex)
        {
            Mensagem = $"Erro: {ex.Message}";
            return new ResultadoResumo();
        }
    }

    public async Task<IActionResult> OnPostEditarAsync()
    {
        using var httpClient = new HttpClient();

        var urlApi = _configuration["UrlApi"];
        var url = $"{urlApi}Compra/EditarCompra";

        Compra.MesAno = Compra.Data?.ToString("MM/yyyy");
        Compra.Parcela = Compra.Parcela ?? "";

        var json = JsonSerializer.Serialize(Compra, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PutAsync(url, content);

        if (response.IsSuccessStatusCode)
        {
            Mensagem = "Compra atualizada com sucesso.";
            Sucesso = true;
            // Retornar para a página de pesquisa, mantendo os filtros preenchidos
            return RedirectToPage("./Resumo", new
            {
                pagina = 1
            });
        }
        else
        {
            var erro = await response.Content.ReadAsStringAsync();
            Mensagem = $"Erro ao editar: {erro}";
            return Page();
        }
    }


    public async Task<IActionResult> OnPostExcluirAsync(string Id, string MesAno)
    {
        using var httpClient = new HttpClient();
        var urlApi = _configuration["UrlApi"];
        var url = $"{urlApi}Compra/DeletarPorId";

        var payload = new
        {
            Id = Id,
            NomeBase = "Controle"
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(url, content);

        var responseContent = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            Sucesso = true;
            Mensagem = "✔️ Lançamento excluído com sucesso.";
        }
        else
        {
            Mensagem = $"❌ Erro ao excluir lançamento.";
        }

        return Page();
    }
    public IActionResult OnPostLimparSessao()
    {
        HttpContext.Session.Remove("ResumoJson");
        return RedirectToPage(); // Redireciona para a primeira página limpa
    }


    #region classes
    public class EditarCompraRequest
    {
        public string IdLan { get; set; } = string.Empty;
        public string Compra { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public string FormaPgto { get; set; } = string.Empty;
        public string Cartao { get; set; } = string.Empty;
        public DateTime? Data { get; set; }

        public string Parcela { get; set; } = string.Empty;  // se quiser editar
        public string MesAno { get; set; } = string.Empty;   // se quiser editar
        public string Fonte { get; set; } = string.Empty;    // se quiser editar

        public string Pessoa { get; set; } = string.Empty;   // só se precisar mesmo
    }

    public class FiltroResumo
    {
        public string Pessoa { get; set; }
        public string Periodo { get; set; }
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
        public Dictionary<string, decimal> ResumoPorCartao { get; set; }
        public List<CartaoTipoResumo> ResumoPorCartaoTipo { get; set; } = new();
    }



    public class CartaoTipoResumo
    {
        public string? Cartao { get; set; }
        public string? Tipo { get; set; }
        public decimal Valor { get; set; }
    }
    public class DeletarPorIdModel
    {
        public string Id { get; set; }
        public string NomeBase { get; set; }
    }

    #endregion
}