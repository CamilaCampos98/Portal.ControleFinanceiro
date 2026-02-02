using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portal.ControleFinanceiro.Models;
using Portal.ControleFinanceiro.Models.Response;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;

namespace Portal.ControleFinanceiro.Pages.Controle
{
    [Authorize]
    public class RegistrarCompraModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public RegistrarCompraModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [BindProperty]
        public CompraInputModel Input { get; set; } = new();

        [BindProperty]
        public IFormFile? ArquivoCsv { get; set; }

        [BindProperty]
        public string? CartaoImportacao { get; set; }

        [BindProperty]
        public string? PessoaImportacao { get; set; }

        [BindProperty]
        public string CsvGerado { get; set; }

        [BindProperty]
        public string MesFatura { get; set; }
        public string UrlApi { get; set; }

        public bool Sucesso { get; set; }
        public string? Erro { get; set; }
        public int ResultadoId { get; set; }
        public string? Mensagem { get; set; }
        public void OnGet()
        {
            UrlApi = _configuration["UrlApi"];
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                UrlApi = _configuration["UrlApi"];

                if (string.IsNullOrWhiteSpace(Input.Pessoa) ||
                    string.IsNullOrWhiteSpace(Input.Descricao) ||
                    Input.ValorTotal == null)
                {
                    Mensagem = "Pelo menos Pessoa, Descricao e Valor Total devem ser preenchidos!";
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
                        ValorTotal = Convert.ToDecimal(Input.ValorTotal),
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

        public async Task<IActionResult> OnPostImportarFaturaAsync()
        {
            var urlApi = _configuration["UrlApi"];

            if (CsvGerado == null || CsvGerado.Length == 0)
            {
                Mensagem = "Arquivo CSV não informado.";
                Sucesso = false;
                return Page();
            }

            if (string.IsNullOrWhiteSpace(CartaoImportacao))
            {
                Mensagem = "Selecione o cartão para importação.";
                Sucesso = false;
                return Page();
            }

            if (string.IsNullOrWhiteSpace(PessoaImportacao))
            {
                Mensagem = "Selecione a pessoa para importação.";
                Sucesso = false;
                return Page();
            }

            var mesAnoDate = DateTime.ParseExact(
                                                    MesFatura,
                                                    "yyyy-MM",
                                                    CultureInfo.InvariantCulture
                                                );

            var (inicioFatura, fimFatura) =
                ObterPeriodoCartao(CartaoImportacao, mesAnoDate);

            using var httpClient = new HttpClient();

            // ----------------------------------------------------
            // 👉 busca as compras já existentes via API
            // (Pessoa + Data + Valor)  => chave
            // ----------------------------------------------------
            var urlGetCompras = $"{urlApi}Compra/GetCompras?pessoa={Uri.EscapeDataString(PessoaImportacao)}";

            var responseGet = await httpClient.GetAsync(urlGetCompras);

            if (!responseGet.IsSuccessStatusCode)
            {
                Mensagem = "Erro ao buscar compras existentes na API.";
                Sucesso = false;
                return Page();
            }

            var jsonGet = await responseGet.Content.ReadAsStringAsync();

            var comprasExistentes =
                JsonSerializer.Deserialize<HashSet<string>>(jsonGet)
                ?? new HashSet<string>();

            int totalImportadas = 0;
            int totalIgnoradas = 0;

            var erros = new StringBuilder();

            using var reader = new StringReader(CsvGerado);

            // pula cabeçalho
            await reader.ReadLineAsync();

            var urlPost = $"{urlApi}Compra/RegistrarCompra";

            string? linha;

            while ((linha = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(linha))
                    continue;

                var colunas = linha.Split(',');

                if (colunas.Length < 3)
                {
                    erros.AppendLine($"Linha inválida: {linha}");
                    continue;
                }

                if (!DateTime.TryParse(colunas[0], out var dataCsv))
                {
                    erros.AppendLine($"Data inválida: {linha}");
                    continue;
                }

                if (!decimal.TryParse(
                        colunas[2],
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var valor))
                {
                    erros.AppendLine($"Valor inválido: {linha}");
                    continue;
                }

                var descricao = colunas[1];
                bool ehParcela = descricao.ToLower().Contains("parcela"); // ou outra lógica para identificar

                // ---------------------------------------
                // Ajusta a data da parcela para fatura
                // ---------------------------------------
                DateTime dataFatura = dataCsv;

                if (ehParcela)
                {
                    // calcula quantos meses adicionar para cair na fatura atual
                    int mesesAdicionar = 0;
                    DateTime parcelaData = dataCsv;

                    while (parcelaData < inicioFatura)
                    {
                        mesesAdicionar++;
                        parcelaData = dataCsv.AddMonths(mesesAdicionar);
                    }

                    // se após adicionar meses ainda estiver fora do período, ignora
                    if (parcelaData > fimFatura)
                        continue;

                    dataFatura = parcelaData; // agora é a data real dentro da fatura
                }
                else
                {
                    // para compras únicas, ignora se está fora da fatura
                    if (dataCsv < inicioFatura || dataCsv > fimFatura)
                        continue;
                }

                // -----------------------------
                // chave: Pessoa + Data + Valor
                // -----------------------------
                var chave =
                    $"{PessoaImportacao.Trim().ToUpperInvariant()}|" +
                    $"{dataFatura:yyyy-MM-dd}|" +
                    $"{valor.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

                if (comprasExistentes.Contains(chave))
                {
                    totalIgnoradas++;
                    continue;
                }

                var compra = new CompraModel
                {
                    Pessoa = PessoaImportacao,
                    Descricao = descricao,
                    ValorTotal = valor,
                    Data = dataFatura,
                    FormaPgto = "C",
                    TotalParcelas = 1,
                    Fonte = Input.Fonte,
                    Cartao = CartaoImportacao
                };

                var json = JsonSerializer.Serialize(compra);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(urlPost, content);

                if (response.IsSuccessStatusCode)
                {
                    totalImportadas++;
                    comprasExistentes.Add(chave);
                }
                else
                {
                    var msg = await response.Content.ReadAsStringAsync();
                    erros.AppendLine($"Erro ao importar '{descricao}': {msg}");
                }
            }


            Sucesso = totalImportadas > 0;

            Mensagem =
                $"Importação finalizada.\n" +
                $"Importadas: {totalImportadas}\n" +
                $"Ignoradas (já existentes): {totalIgnoradas}";

            if (erros.Length > 0)
                Mensagem += "\n\nErros:\n" + erros.ToString();

            return Page();
        }

        (DateTime inicio, DateTime fim) ObterPeriodoCartao(string cartaoBase, DateTime mesAnoDate)
        {
            cartaoBase = RemoverPalavras(cartaoBase?.ToUpper() ?? "");

            if (!fechamentoCartoes.ContainsKey(cartaoBase))
                cartaoBase = "OUTROS";

            int diaFechamento = fechamentoCartoes[cartaoBase];

            var fechamentoTeoricoAnterior = new DateTime(
                mesAnoDate.Year,
                mesAnoDate.Month,
                Math.Min(diaFechamento, DateTime.DaysInMonth(mesAnoDate.Year, mesAnoDate.Month))
            );

            var mesSeguinte = mesAnoDate.AddMonths(1);

            var fechamentoTeoricoAtual = new DateTime(
                mesSeguinte.Year,
                mesSeguinte.Month,
                Math.Min(diaFechamento, DateTime.DaysInMonth(mesSeguinte.Year, mesSeguinte.Month))
            );

            bool fimDeSemana =
                fechamentoTeoricoAtual.DayOfWeek is DayOfWeek.Saturday
                or DayOfWeek.Sunday;

            var fechamentoAjustado = fimDeSemana
                ? fechamentoTeoricoAtual.AddDays(
                    ((int)DayOfWeek.Monday - (int)fechamentoTeoricoAtual.DayOfWeek + 7) % 7)
                : fechamentoTeoricoAtual;

            var inicio = fechamentoTeoricoAnterior.AddDays(1);
            var fim = fimDeSemana
                ? fechamentoAjustado.AddDays(-3)
                : fechamentoAjustado;

            return (inicio, fim);
        }

        public static string RemoverPalavras(string texto)
        {
            if (string.IsNullOrEmpty(texto))
                return texto;

            var palavrasParaRemover = new List<string> { "Titular", "Adicional" };

            foreach (var palavra in palavrasParaRemover)
            {
                texto = System.Text.RegularExpressions.Regex.Replace(
                    texto,
                    System.Text.RegularExpressions.Regex.Escape(palavra),
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            return texto.Trim();
        }

        Dictionary<string, int> fechamentoCartoes = new()
                                                {
                                                    { "ITAU", 8 },
                                                    { "SANTANDER", 9 },
                                                    { "BRADESCO", 3 },
                                                    { "C&A", 5 },
                                                    { "RIACHUELO", 10 }
                                                };

    }
}
