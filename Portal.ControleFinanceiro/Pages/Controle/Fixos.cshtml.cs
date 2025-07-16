using Microsoft.AspNetCore.Authorization;
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

[Authorize]
public class Fixos : PageModel
{
    private readonly IConfiguration _configuration;
    public string UrlApi { get; set; }
    public Fixos(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    [BindProperty]
    public List<string> MesesNaoGerados { get; set; } = new();

    [BindProperty]
    public List<string> MesesSelecionados { get; set; } = new();

    [BindProperty]
    public string Pessoa { get; set; }
    [BindProperty]
    public string Periodo { get; set; }

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
        UrlApi = _configuration["UrlApi"];
    }

    public async Task<IActionResult> OnPostGerarAsync()
    {
        UrlApi = _configuration["UrlApi"];

        if (string.IsNullOrWhiteSpace(Pessoa))
        {
            Mensagem = "Informe a Pessoa!";
            return Page();
        }

        var vencimento = string.Empty;
        var mesAno = await GetMesAnoRefAsync(Periodo);

        if (!DateTime.TryParseExact(mesAno, "MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataRef))
            throw new Exception($"Formato de data inválido: {mesAno}");

        vencimento = $"{dataRef.Year}-{dataRef.Month:D2}-10";

        var fixosGerados = new List<FixoModel>();

        // Se veio da cópia do mês anterior, FixosModel já está preenchido
        var fixosDoMesAtual = FixosModel.Where(f => f.MesAno == mesAno).ToList();

        if (!fixosDoMesAtual.Any())
        {
            if (TiposSelecionados == null || !TiposSelecionados.Any())
            {
                Mensagem = "Selecione pelo menos um tipo de gasto fixo ou copie de um mês anterior.";
                return Page();
            }

            foreach (var tipo in TiposSelecionados)
            {
                var fixo = new FixoModel
                {
                    Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + new Random().Next(1000, 9999),
                    Tipo = tipo,
                    MesAno = mesAno,
                    Vencimento = vencimento,
                    Valor = "",
                    Pago = "",
                    Dividido = "Não"
                };

                fixosGerados.Add(fixo);
                FixosModel.Add(fixo);
            }
        }
        else
        {
            // Se já temos fixos na tela para o mês atual, envia todos
            fixosGerados = fixosDoMesAtual;
        }

        if (fixosGerados.Any())
        {
            try
            {
                foreach (var i in fixosGerados)
                {
                    i.MesAno = mesAno;
                    i.Vencimento = vencimento;
                }
                using var httpClient = new HttpClient();
                var url = $"{UrlApi}Compra/GeraFixos";

                var payload = new FixosPayload
                {
                    Action = "salvarFixos",
                    Pessoa = Pessoa,
                    Data = fixosGerados
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(url, content);

                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var resultado = JsonSerializer.Deserialize<RetornoApiFixos>(responseContent, options);

                    if (resultado != null)
                    {
                        Sucesso = true;
                        var msg = $"✔️ {resultado.Message}";

                        if (resultado.Inseridos?.Any() == true)
                            msg += $"\n✅ Inseridos: {string.Join(", ", resultado.Inseridos.Select(i => $"{i.Tipo} ({i.MesAno})"))}";

                        if (resultado.Ignorados?.Any() == true)
                            msg += $"\n⚠️ Ignorados (já existiam): {string.Join(", ", resultado.Ignorados.Select(i => $"{i.Tipo} ({i.MesAno})"))}";

                        Mensagem = msg;
                    }
                    else
                    {
                        Mensagem = "⚠️ Resposta da API vazia.";
                    }
                }
                else
                {
                    Mensagem = $"❌ Erro ao salvar na API: {responseContent}";
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
        FixosModel = FixosModel
            .Where(f => f.MesAno == mesAno && (TiposSelecionados?.Contains(f.Tipo) ?? true))
            .ToList();

        return Page();
    }


    public async Task<IActionResult> OnPostExcluirAsync(string Id, string MesAno, string Pessoa)
    {
        UrlApi = _configuration["UrlApi"];
        using var httpClient = new HttpClient();
        var urlApi = _configuration["UrlApi"];
        var url = $"{urlApi}Compra/DeletarFixo";

        var payload = new DeletarFixoPayload
        {
            Id = Id,
            MesAno = MesAno,
            Pessoa = Pessoa
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(url, content);

        var responseContent = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            Sucesso = true;
            Mensagem = "Gasto fixo excluído com sucesso.";
        }
        else
        {
            Erro = $"❌ Erro ao excluir o gasto fixo.";
        }

        return Page(); 
    }
    public async Task<IActionResult> OnPostSalvarAsync()
    {
        UrlApi = _configuration["UrlApi"];
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
                        Pago = fixo.Pago?.ToLower() == "true",
                        Dividido = fixo.Dividido?.ToLower() == "true"
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

                Sucesso = true;
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
        UrlApi = _configuration["UrlApi"];
        if (Pessoa == null)
        {
            Mensagem = "⚠️ Informe uma pessoa para listar os gastos fixos.";
            return Page();
        }

        try
        {
            using var httpClient = new HttpClient();

            var urlApi = _configuration["UrlApi"];
            var url = $"{urlApi}Compra/ListarFixos?pessoa={Pessoa}&periodo={Periodo}";

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
                    Sucesso = true;
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
        UrlApi = _configuration["UrlApi"];
        using var httpClient = new HttpClient();
        var urlApi = _configuration["UrlApi"];
        var url = $"{urlApi}Compra/DividirGasto";

        var valor = ParseDecimal(ValorDivisao);

        var request = new
        {
            IdLinha = IdGasto,
            NomeDestino = DividirCom,
            ValorDividir = valor,
            Dividido = "Sim"
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

    public async Task<IActionResult> OnPostCopiarMesAnteriorAsync()
    {
        UrlApi = _configuration["UrlApi"];

        if (string.IsNullOrEmpty(Pessoa) || string.IsNullOrEmpty(Periodo))
        {
            Mensagem = "Pessoa ou período não informado.";
            return Page();
        }

        if (!DateTime.TryParseExact(Periodo, "MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataAtual))
        {
            Mensagem = "Período inválido.";
            return Page();
        }

        var mesAnterior = dataAtual.AddMonths(-1).ToString("MM/yyyy");

        var payload = new
        {
            Pessoa,
            MesAnoOrigem = mesAnterior,
            MesAnoDestino = Periodo
        };

        try
        {
            using var httpClient = new HttpClient();
            var url = $"{UrlApi}Compra/CopiarFixos";

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Sucesso = true;
                Mensagem = $"✅ {responseText}";
                // Agora recarrega os fixos do novo mês copiado
                await CarregarFixosDoPeriodoAsync();

                // E chama o Gerar com o FixosModel atualizado
                return await OnPostListarAsync();

            }
            else
            {
                Mensagem = $"⚠️ Falha ao copiar: {responseText}";
            }
        }
        catch (Exception ex)
        {
            Mensagem = $"❌ Erro na requisição: {ex.Message}";
        }

        // Recarrega os fixos para o período atual (destino da cópia)
        await CarregarFixosDoPeriodoAsync();

        // Agora sim, chama o gerar usando FixosModel preenchido
        return Page();

    }

    private async Task CarregarFixosDoPeriodoAsync()
    {
        try
        {
            using var httpClient = new HttpClient();

            var url = $"{UrlApi}Compra/ListarFixos?pessoa={Pessoa}&periodo={Periodo}";
            var response = await httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();

                FixosModel = JsonSerializer.Deserialize<List<FixoModel>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<FixoModel>();
            }
            else
            {
                FixosModel = new List<FixoModel>();
            }
        }
        catch
        {
            FixosModel = new List<FixoModel>();
        }
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

    public async Task<string> GetMesAnoRefAsync(string mesAno)
    {
        var urlApi = _configuration["UrlApi"];
        var url = $"{urlApi}Compra/BuscaDataRef?mesAno={mesAno}";

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
            Erro = $"Erro ao validar mês {mesAno}: {erro}";
            return string.Empty;
        }
    }


    public class DataRefResponse
    {
        public string Status { get; set; }
        public string MesAno { get; set; }
    }

    public class RetornoApiFixos
    {
        public string Status { get; set; } = "";
        public string Message { get; set; } = "";
        public List<ItemFixo> Inseridos { get; set; }
        public List<ItemFixo> Ignorados { get; set; }
    }

    public class ItemFixo
    {
        public string Tipo { get; set; } = "";
        public string MesAno { get; set; } = "";
        public string Pessoa { get; set; } = "";
    }

    public class DeletarFixoPayload
    {
        public string Id { get; set; } = string.Empty;
        public string MesAno { get; set; } = string.Empty;
        public string Pessoa { get; set; } = string.Empty;
    }

}
