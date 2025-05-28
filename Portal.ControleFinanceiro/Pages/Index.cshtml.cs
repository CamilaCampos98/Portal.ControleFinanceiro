using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portal.ControleFinanceiro.Models;
using System.Text;
using System.Text.Json;

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

        public async Task OnGetAsync()
        {
            try
            {
                using var httpClient = new HttpClient();
                var urlApi = _configuration["UrlApi"];
                var url = $"{urlApi}Compra/Get";

              

                var response = await httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    Sucesso = true;
                    //Mensagem = $"⚠️ Gastos fixos para {Pessoa} gerados, preencha os valores.";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Erro = $"❌ Erro ao salvar na API: {errorContent}";
                }
            }
            catch (Exception ex)
            {
                Mensagem = $"❌ Erro na comunicação com a API: {ex.Message}";
            }
        }
    }
}
