using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portal.ControleFinanceiro.Models;
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

        public class ResumoPessoaMesDTO
        {
            public string Pessoa { get; set; }
            public string MesAno { get; set; } // Ex: "07/2025"
            public decimal SaldoRestante { get; set; }
            public decimal ValorGuardado { get; set; }

        }

    }
}
