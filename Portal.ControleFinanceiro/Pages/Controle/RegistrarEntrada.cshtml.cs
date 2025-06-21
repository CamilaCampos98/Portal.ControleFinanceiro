using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portal.ControleFinanceiro.Models;
using Portal.ControleFinanceiro.Models.Response;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using static Portal.ControleFinanceiro.Pages.Controle.RegistrarCompraModel;

namespace Portal.ControleFinanceiro.Pages.Controle
{
    [Authorize]
    public class RegistrarEntradaModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public RegistrarEntradaModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [BindProperty]
        public EntradaInput Input { get; set; } = new();

        public bool Sucesso { get; set; }
        public string ResultadoTexto { get; set; } = "";
        public string? Mensagem { get; set; }

        public void OnGet()
        {
            // Inicialização
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Input.Pessoa) || string.IsNullOrWhiteSpace(Input.MesAno))
                {
                    Mensagem = "Pessoa e Mês/Ano são obrigatórios.";
                    return Page();
                }

                if (Input.TipoEntrada == "Extra")
                {
                    if (Input.HorasExtras <= 0)
                    {
                        Mensagem = "Informe as Horas Extras corretamente.";
                        return Page();
                    }
                }
                else
                {
                    if (Input.ValorHora <= 0)
                    {
                        Mensagem = "Informe o Valor Hora corretamente.";
                        return Page();
                    }
                    if (Input.HorasUteisMes <= 0)
                    {
                        Mensagem = "Informe as Horas Úteis no Mês corretamente.";
                        return Page();
                    }
                }

                decimal valorExtraCalculado = 0;
                decimal novosExtras = 0;

                if (Input.TipoEntrada == "Extra")
                {
                    valorExtraCalculado = Math.Round(Convert.ToInt32(Input.HorasExtras) * Convert.ToDecimal(Input.ValorHora), 2);
                    novosExtras = valorExtraCalculado;
                }

                var Entrada = new EntradaModel
                {
                    Pessoa = Input.Pessoa,
                    ValorHora = Convert.ToDecimal(Input.ValorHora),
                    HorasUteisMes = Convert.ToInt32(Input.HorasUteisMes),
                    MesAno = Input.MesAno,
                    TipoEntrada = Input.TipoEntrada,
                    HorasExtras = Convert.ToInt32(Input.HorasExtras)
                };

                using (var httpClient = new HttpClient())
                {
                    var urlApi = _configuration["UrlApi"];
                    var url = $"{urlApi}Compra/RegistrarEntrada";

                    var json = JsonSerializer.Serialize(Entrada);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");


                    var response = await httpClient.PostAsync(url, content);

                    if (response.IsSuccessStatusCode)
                    {
                        // Se desejar capturar um retorno da API (como ID, mensagem, etc)
                        var retorno = await response.Content.ReadAsStringAsync();

                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };

                        var dados = JsonSerializer.Deserialize<EntradaResponseModel>(retorno, options);

                        ResultadoTexto = $"""
                                        Entrada registrada com sucesso!

                                        Valor da Hora Extra: R$ {dados.ValorHoraExtra:N2}
                                        Horas Extras: {dados.HorasExtras}
                                        Valor Extra Calculado: R$ {dados.ValorExtraCalculado:N2}
                                        Total de Extras: R$ {dados.NovosExtras:N2}
                                      """;

                        Sucesso = true;
                        Mensagem = ResultadoTexto;
                        // Limpa os campos após submit
                        Input = new EntradaInput();
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
                Mensagem = ex.Message;
                return Page();
            }
        }

        public class EntradaInput
        {
            public string TipoEntrada { get; set; } = "Salario";
            public string Pessoa { get; set; } = string.Empty;
            public decimal? ValorHora { get; set; }
            public int? HorasUteisMes { get; set; }
            public int? HorasExtras { get; set; }
            public string MesAno { get; set; } = string.Empty;
        }
    }
}
