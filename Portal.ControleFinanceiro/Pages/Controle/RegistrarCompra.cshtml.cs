using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portal.ControleFinanceiro.Models;
using Portal.ControleFinanceiro.Models.Response;
using System.ComponentModel.DataAnnotations;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;

namespace Portal.ControleFinanceiro.Pages.Controle
{
    public class RegistrarCompraModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public RegistrarCompraModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [BindProperty]
        public CompraInputModel Input { get; set; } = new();

        public bool Sucesso { get; set; }
        public string? Erro { get; set; }
        public int ResultadoId { get; set; }
        public string? Mensagem { get; set; }
        public void OnGet()
        {
            // Inicialização se necessário
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Input.Pessoa) ||
                    string.IsNullOrWhiteSpace(Input.Descricao) ||
                    Input.ValorTotal <= 0)
                {
                    Erro = "Preencha todos os campos obrigatórios corretamente.";
                    return Page();
                }

                using (var httpClient = new HttpClient())
                {
                    var urlApi = _configuration["UrlApi"];
                    var url = $"{urlApi}Compra/RegistrarCompra";

                    // Monta o objeto que será enviado para a API
                    var compra = new CompraModel
                    {
                        Pessoa = Input.Pessoa,
                        Descricao = Input.Descricao,
                        ValorTotal = Input.ValorTotal,
                        Data = Input.Data,
                        FormaPgto = Input.FormaPgto,
                        TotalParcelas = Input.TotalParcelas,
                        Fonte = Input.Fonte,
                        Cartao = Input.Cartao
                    };

                    var json = JsonSerializer.Serialize(compra);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await httpClient.PostAsync(url, content);

                    if (response.IsSuccessStatusCode)
                    {
                        // Se desejar capturar um retorno da API (como ID, mensagem, etc)
                        var retorno = await response.Content.ReadAsStringAsync();
                        ResultadoId = JsonSerializer.Deserialize<CompraResponseModel>(retorno).id;

                        Sucesso = true;
                        Mensagem = "Compra Registrar Com Sucesso!";
                        Input = new CompraInputModel();
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Mensagem = $"{errorContent}";
                    }
                }

                return Page();
            }
            catch (Exception ex)
            {
                Erro = $"Erro: {ex.Message}";
                return Page();
            }
        }

    }
}
