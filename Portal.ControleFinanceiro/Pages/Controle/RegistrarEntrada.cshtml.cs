using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portal.ControleFinanceiro.Models;
using Portal.ControleFinanceiro.Models.Response;
using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
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
        public List<SalarioCadastrado> SalariosCadastrados { get; private set; } = new();
        public List<SalarioCadastrado> SalariosNoPeriodo { get; private set; } = new();
        public string? AvisoSalarios { get; private set; }

        public async Task OnGetAsync()
        {
            Input.Pessoa = User.Identity?.Name ?? string.Empty;
            Input.MesAno = DateTime.Today.ToString("MM/yyyy");
            await CarregarSalariosAsync();
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
                    valorExtraCalculado = Math.Round(Convert.ToDecimal(Input.HorasExtras) * Convert.ToDecimal(Input.ValorHora), 2);
                    novosExtras = valorExtraCalculado;
                }

                var Entrada = new EntradaModel
                {
                    Pessoa = Input.Pessoa,
                    ValorHora = Convert.ToDecimal(Input.ValorHora),
                    HorasUteisMes = Convert.ToInt32(Input.HorasUteisMes),
                    MesAno = Input.MesAno,
                    TipoEntrada = Input.TipoEntrada,
                    HorasExtras = Convert.ToDecimal(Input.HorasExtras)
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
                        Input = new EntradaInput
                        {
                            Pessoa = User.Identity?.Name ?? string.Empty,
                            MesAno = DateTime.Today.ToString("MM/yyyy")
                        };
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
            finally
            {
                await CarregarSalariosAsync();
            }
        }

        public async Task<IActionResult> OnPostRegistrarProximosSalariosAsync(string planoJson)
        {
            Input.Pessoa = User.Identity?.Name ?? string.Empty;
            Input.MesAno = DateTime.Today.ToString("MM/yyyy");
            try
            {
                var plano = JsonSerializer.Deserialize<PlanoProximosSalarios>(planoJson);
                if (plano == null)
                {
                    Mensagem = "Não foi possível ler a prévia. Gere-a novamente.";
                    return Page();
                }

                using var httpClient = new HttpClient();
                var urlApi = _configuration["UrlApi"];
                var resposta = await httpClient.PostAsJsonAsync($"{urlApi}Compra/RegistrarProximosSalarios", plano);
                if (!resposta.IsSuccessStatusCode)
                {
                    Mensagem = await resposta.Content.ReadAsStringAsync();
                    return Page();
                }

                Sucesso = true;
                Mensagem = $"Seis salários e os respectivos fixos registrados, " +
                    $"de {plano.Meses[0].MesAno} a {plano.Meses[5].MesAno}.";
                return Page();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                Mensagem = "Não foi possível registrar os salários. Confira a conexão e tente novamente.";
                return Page();
            }
            finally
            {
                await CarregarSalariosAsync();
            }
        }

        private async Task CarregarSalariosAsync()
        {
            try
            {
                using var httpClient = new HttpClient();
                var urlApi = _configuration["UrlApi"];
                SalariosCadastrados = await httpClient.GetFromJsonAsync<List<SalarioCadastrado>>(
                    $"{urlApi}Compra/SalariosCadastrados") ?? new();
                var agora = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
                var periodoAtual = new DateTime(agora.Year, agora.Month, 1);
                var ultimoPeriodo = periodoAtual.AddMonths(6);
                SalariosNoPeriodo = SalariosCadastrados.Where(salario =>
                    DateTime.TryParseExact(salario.MesAno, "MM/yyyy", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var periodo) &&
                    periodo >= periodoAtual && periodo <= ultimoPeriodo).ToList();
                AvisoSalarios = null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                SalariosCadastrados = new();
                SalariosNoPeriodo = new();
                AvisoSalarios = "Não foi possível carregar os salários cadastrados agora.";
            }
        }

        public class SalarioCadastrado
        {
            public string Pessoa { get; set; } = string.Empty;
            public string MesAno { get; set; } = string.Empty;
            public decimal Valor { get; set; }
            public decimal ValorHora { get; set; }
            public decimal Extras { get; set; }
        }

        public class PlanoProximosSalarios
        {
            public string Pessoa { get; set; } = string.Empty;
            public string UltimoMesAno { get; set; } = string.Empty;
            public decimal UltimoValorHora { get; set; }
            public decimal? NovoValorHora { get; set; }
            public List<MesPlanejado> Meses { get; set; } = new();
        }

        public class MesPlanejado
        {
            public string MesAno { get; set; } = string.Empty;
            public int HorasUteis { get; set; }
        }

        public class EntradaInput
        {
            public string TipoEntrada { get; set; } = "Salario";
            public string Pessoa { get; set; } = string.Empty;
            public decimal? ValorHora { get; set; }
            public int? HorasUteisMes { get; set; }
            public decimal? HorasExtras { get; set; }
            public string MesAno { get; set; } = string.Empty;
        }
    }
}
