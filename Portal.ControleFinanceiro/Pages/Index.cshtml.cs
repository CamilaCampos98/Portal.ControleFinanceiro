using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portal.ControleFinanceiro.Models;
using System.Text;
using System.Text.Json;
using static ResumoModel;

namespace Portal.ControleFinanceiro.Pages
{
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

        public List<ResultadoResumoGeral>? ResumoGeral { get; set; }

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
                    Sucesso = true;
                    ResumoGeral = JsonSerializer.Deserialize<List<ResultadoResumoGeral>>(content, options);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Mensagem = $"Erro ao buscar resumo: {error}";
                    ResumoGeral = new List<ResultadoResumoGeral>();
                }
            }
            catch (Exception ex)
            {
                Mensagem = $"Erro: {ex.Message}";
                ResumoGeral = new List<ResultadoResumoGeral>();
            }
        }

        public class ResultadoResumoGeral
        {
            public string? Pessoa { get; set; }
            public decimal SaldoRestante { get; set; }
            public string? UltimaCompra { get; set; }
            public string? DescricaoUltimaCompra { get; set; }
        }

    }
}
