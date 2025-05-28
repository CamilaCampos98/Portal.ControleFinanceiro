using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portal.ControleFinanceiro.Models;
using Portal.ControleFinanceiro.Models.Response;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json;
using static Portal.ControleFinanceiro.Pages.Controle.RegistrarCompraModel;
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

        var mesAno = GetMesAnoAtual();
        var vencimento = $"{DateTime.Today.Year}-{DateTime.Today.Month.ToString("D2")}-15";

        var fixosGerados = new List<FixoModel>();

        foreach (var tipo in TiposSelecionados)
        {
            if(FixosModel.Count == 0 || !FixosModel.Any(f => f.Tipo == tipo && f.MesAno == mesAno))
            {
                var fixo = new FixoModel
                {
                    Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + new Random().Next(1000, 9999),
                    Tipo = tipo,
                    MesAno = mesAno,
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
        FixosModel = FixosModel.Where(f => f.MesAno == mesAno && TiposSelecionados.Contains(f.Tipo)).ToList();

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

    private string GetMesAnoAtual()
    {
        var hoje = DateTime.Today;
        return $"{hoje.Month:D2}/{hoje.Year}";
    }

    
}
