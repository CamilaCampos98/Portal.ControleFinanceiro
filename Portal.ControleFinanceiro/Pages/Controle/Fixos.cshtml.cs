using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Portal.ControleFinanceiro.Models;
using Portal.ControleFinanceiro.Models.Response;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json;
using static Portal.ControleFinanceiro.Pages.Controle.RegistrarCompraModel;
using static ResumoModel;
using static System.Runtime.InteropServices.JavaScript.JSType;

public class Fixos : PageModel
{
    private readonly IConfiguration _configuration;

    public Fixos(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [BindProperty]
    public string Pessoa { get; set; }

    [BindProperty]
    public List<string> TiposSelecionados { get; set; }

    [BindProperty]
    public FixosPayload ListaFixos { get; set; }

    public string Mensagem { get; set; }

    public bool Sucesso { get; set; }
    public string? Erro { get; set; }

    [BindProperty]
    public List<FixoModel> FixosModel { get; set; } = new List<FixoModel>();

    public void OnGet()
    {
        
    }

    public async Task<IActionResult> OnPostGerarAsync()
    {
        if (Pessoa == null)
        {
            Mensagem = "Informe a Pessoa!";
            return Page();
        }

        if (TiposSelecionados == null || !TiposSelecionados.Any())
        {
            Mensagem = "Selecione pelo menos um tipo de gasto fixo.";
            return Page();
        }

        var vencimento = string.Empty;
        var mesAno = await GetMesAnoRefAsync();

        if (DateTime.TryParseExact(mesAno, "MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataRef))
        {
            // Soma 1 mês
            var proximoMes = dataRef.AddMonths(1);

            // Formata no padrão "yyyy-MM-15"
            vencimento = $"{proximoMes.Year}-{proximoMes.Month:D2}-10";

            Console.WriteLine(vencimento); // Ex.: "2025-07-15"
        }
        else
        {
            // Tratar erro se não conseguir converter
            throw new Exception($"Formato de data inválido: {mesAno}");
        }


        var fixosGerados = new List<FixoModel>();

        foreach (var tipo in TiposSelecionados)
        {
            if(FixosModel.Count == 0 || !FixosModel.Any(f => f.Tipo == tipo && f.MesAno == mesAno.ToString()))
            {
                var fixo = new FixoModel
                {
                    Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + new Random().Next(1000, 9999),
                    Tipo = tipo,
                    MesAno = mesAno.ToString(),
                    Vencimento = vencimento,
                    Valor = "",
                    Pago = ""
                };

                fixosGerados.Add(fixo);
                FixosModel.Add(fixo);
            }
            
           
        }

        if (fixosGerados.Any())
        {
            try
            {
                using var httpClient = new HttpClient();
                var urlApi = _configuration["UrlApi"];
                var url = $"{urlApi}Compra/GeraFixos";

                var payload = new FixosPayload
                {
                    Action = "salvarFixos",
                    Pessoa = Pessoa,
                    Data = fixosGerados
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    Sucesso = true;
                    Mensagem = $"⚠️ Gastos fixos para {Pessoa} gerados, preencha os valores.";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Mensagem = $"❌ Erro ao salvar na API: {errorContent}";
                }
            }
            catch (Exception ex)
            {
                Mensagem = $"❌ Erro na comunicação com a API: {ex.Message}";
            }
        }
        else
        {
            Mensagem = $"⚠️ Nenhum gasto fixo novo gerado. Já existem lançamentos para {mesAno}.";
        }

        // Atualiza lista para exibir na tela
        FixosModel = FixosModel.Where(f => f.MesAno == mesAno.ToString() && TiposSelecionados.Contains(f.Tipo)).ToList();

        return Page();
    }


    public async Task<IActionResult> OnPostSalvarAsync()
    {
        try
        {
            var urlApi = _configuration["UrlApi"];
            var url = $"{urlApi}Compra/AtualizarFixo";

            using var http = new HttpClient();

            if (FixosModel.Count > 0)
            {
                foreach (var fixo in FixosModel)
                {
                    var payload = new AtualizaFixoModel
                    {
                        Id = fixo.Id.ToString(),
                        Valor = decimal.TryParse(fixo.Valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : 0,
                        Pago = fixo.Pago?.ToLower() == "true"
                    };

                    var json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await http.PostAsync(url, content);
                    if (!response.IsSuccessStatusCode)
                    {
                        var msg = await response.Content.ReadAsStringAsync();
                        Mensagem = $"Erro ao atualizar gasto fixo {fixo.Id}: {msg}";
                        return Page();
                    }
                }

                Mensagem = "Todos os gastos fixos foram atualizados com sucesso!";
            }
            
        }
        catch (Exception ex)
        {
            Mensagem = $"Erro ao salvar gastos fixos: {ex.Message}";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostListarAsync()
    {
        if (Pessoa == null)
        {
            Mensagem = "⚠️ Informe uma pessoa para listar os gastos fixos.";
            return Page();
        }

        try
        {
            using var httpClient = new HttpClient();

            var urlApi = _configuration["UrlApi"];
            var url = $"{urlApi}Compra/ListarFixos?pessoa={Pessoa}";

            var response = await httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();

                var lista = JsonSerializer.Deserialize<List<FixoModel>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                foreach (var i in lista)
                {
                    i.Pago = i.Pago == "Sim" ? "true" : "false";
                }
                FixosModel = lista ?? new List<FixoModel>();

                if (FixosModel.Any())
                {
                    Mensagem = $"✅ Foram encontrados {FixosModel.Count} gastos fixos para {Pessoa}.";
                }
                else
                {
                    Mensagem = $"⚠️ Nenhum gasto fixo encontrado para {Pessoa}.";
                }
            }
            else
            {
                var erro = await response.Content.ReadAsStringAsync();
                Mensagem = $"❌ Erro: {erro}";
                FixosModel = new List<FixoModel>();
            }
        }
        catch (Exception ex)
        {
            Mensagem = $"❌ Erro ao listar fixos: {ex.Message}";
            FixosModel = new List<FixoModel>();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostDividirGastoFixoAsync(string IdGasto, string DividirCom, string ValorDivisao)
    {
        using var httpClient = new HttpClient();
        var urlApi = _configuration["UrlApi"];
        var url = $"{urlApi}Compra/DividirGasto";

        var valor = ParseDecimal(ValorDivisao);

        var request = new
        {
            IdLinha = IdGasto,
            NomeDestino = DividirCom,
            ValorDividir = valor
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                var resposta = await response.Content.ReadAsStringAsync();
                // Você pode tratar a resposta aqui, exibir mensagem, atualizar a página, etc.

                Sucesso = true;
                Mensagem = "Gasto dividido com sucesso.";
            }
            else
            {
                var erro = await response.Content.ReadAsStringAsync();
                
                Erro = $"Erro ao dividir: {erro}";
            }
        }
        catch (Exception ex)
        {
            Erro = $"Erro ao conectar na API: {ex.Message}";
        }

        return Page(); 
    }

    public decimal ParseDecimal(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return 0;

        input = input.Replace("R$", "").Trim();

        // Remove espaços
        input = input.Replace(" ", "");

        // Conta quantas vírgulas e pontos existem
        int commaCount = input.Count(c => c == ',');
        int dotCount = input.Count(c => c == '.');

        // Cenário 1: ambos existem
        if (commaCount > 0 && dotCount > 0)
        {
            // Assume que o separador decimal é o último deles
            if (input.LastIndexOf(",") > input.LastIndexOf("."))
            {
                // Vírgula é decimal → ponto é milhar
                input = input.Replace(".", "");
                input = input.Replace(",", ".");
            }
            else
            {
                // Ponto é decimal → vírgula é milhar
                input = input.Replace(",", "");
            }
        }
        // Cenário 2: só vírgula
        else if (commaCount > 0)
        {
            if (input.LastIndexOf(",") >= input.Length - 3)
            {
                // vírgula é decimal
                input = input.Replace(".", "");
                input = input.Replace(",", ".");
            }
            else
            {
                // vírgula é milhar (raro, mas possível)
                input = input.Replace(",", "");
            }
        }
        // Cenário 3: só ponto
        else if (dotCount > 0)
        {
            if (input.LastIndexOf(".") >= input.Length - 3)
            {
                // ponto é decimal
                input = input.Replace(",", "");
            }
            else
            {
                // ponto é milhar
                input = input.Replace(".", "");
            }
        }

        if (decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            return result;

        return 0;
    }

    private string GetMesAnoAtual()
    {
        var hoje = DateTime.Today;
        return $"{hoje.Month:D2}/{hoje.Year}";
    }

    public async Task<string> GetMesAnoRefAsync()
    {
        var urlApi = _configuration["UrlApi"];
        var url = $"{urlApi}Compra/BuscaDataRef";

        using var httpClient = new HttpClient();

        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var resultado = JsonSerializer.Deserialize<DataRefResponse>(content, options);

            return resultado?.MesAno ?? string.Empty;
        }
        else
        {
            var erro = await response.Content.ReadAsStringAsync();
            Erro = $"Erro buscar data: {erro}";
            return string.Empty;
        }
    }

    public class DataRefResponse
    {
        public string Status { get; set; }
        public string MesAno { get; set; }
    }
}
