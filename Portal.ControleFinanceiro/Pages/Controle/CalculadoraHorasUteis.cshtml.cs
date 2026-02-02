using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;

[Authorize]
public class CalculadoraHorasUteisModel : PageModel
{
    private static readonly HttpClient _http = new();

    [BindProperty]
    public PeriodoInput Input { get; set; }

    public ResultadoCalculo Resultado { get; set; }

    public void OnGet()
    {
        var hoje = DateTime.Today;
        Input = new PeriodoInput
        {
            MesReferencia = $"{hoje:yyyy-MM}",
            HorasPorDia = 8
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(Input.MesReferencia))
        {
            ModelState.AddModelError("", "Mês de referência inválido.");
            return Page();
        }

        // 🎯 Define início e fim forçados com base no mês de referência
        var mesRefParts = Input.MesReferencia.Split('-');
        int ano = int.Parse(mesRefParts[0]);
        int mes = int.Parse(mesRefParts[1]);

        var dataInicio = new DateTime(ano, mes, 1).AddMonths(-1).AddDays(25); // 26 do mês anterior
        var dataFim = new DateTime(ano, mes, 25);                             // 25 do mês atual

        var feriados = await ObterFeriadosAsync(dataInicio, dataFim);

        var diasTotais = (dataFim - dataInicio).Days + 1;
        var diasUteis = Enumerable.Range(0, diasTotais)
            .Select(i => dataInicio.AddDays(i))
            .Where(d =>
                d.DayOfWeek != DayOfWeek.Saturday &&
                d.DayOfWeek != DayOfWeek.Sunday &&
                !feriados.Any(f => f.Data == d.Date))
            .ToList();

        Resultado = new ResultadoCalculo
        {
            Periodo = $"{dataInicio:dd/MM/yyyy} a {dataFim:dd/MM/yyyy}",
            DiasTotais = diasTotais,
            DiasUteis = diasUteis.Count,
            HorasPorDia = Input.HorasPorDia,
            TotalHorasUteis = diasUteis.Count * Input.HorasPorDia,
            Feriados = feriados
                        .Where(d => d.Data >= dataInicio && d.Data <= dataFim)
                        .OrderBy(d => d.Data)
                        .ToList()
        };

        return Page();
    }

    private async Task<List<FeriadoInfo>> ObterFeriadosAsync(DateTime inicio, DateTime fim)
    {
        var feriados = new List<FeriadoInfo>();
        var anos = Enumerable.Range(inicio.Year, fim.Year - inicio.Year + 1);

        foreach (var ano in anos)
        {
            var lista = await _http.GetFromJsonAsync<List<BrasilFeriado>>(
                $"https://brasilapi.com.br/api/feriados/v1/{ano}");

            if (lista != null)
            {
                foreach (var item in lista)
                {
                    if (DateTime.TryParse(item.date, out var data))
                    {
                        var dataDate = data.Date;
                        if (dataDate >= inicio && dataDate <= fim)
                            feriados.Add(new FeriadoInfo { Data = dataDate, Nome = item.name ?? "Feriado Nacional" });

                        // Se for Carnaval (terça), inclui a segunda-feira também
                        if (!string.IsNullOrWhiteSpace(item.name) &&
                            item.name.Contains("Carnaval", StringComparison.OrdinalIgnoreCase))
                        {
                            var segundaCarnaval = dataDate.AddDays(-1);

                            if (segundaCarnaval >= inicio && segundaCarnaval <= fim)
                            {
                                feriados.Add(new FeriadoInfo
                                {
                                    Data = segundaCarnaval,
                                    Nome = "Carnaval (segunda-feira)"
                                });
                            }
                        }
                    }

                }
            }

            // Feriado estadual de SP – 09 de julho
            var sp = new DateTime(ano, 7, 9);
            if (sp >= inicio && sp <= fim)
                feriados.Add(new FeriadoInfo { Data = sp, Nome = "Revolução Constitucionalista (SP)" });

            // Feriados móveis - cuidado para não duplicar os que já vieram da API
            foreach (var movel in CalculaFeriadosMoveis(ano))
            {
                if (movel.Data >= inicio && movel.Data <= fim)
                    feriados.Add(movel);
            }

            // Barueri – 26 de março
            var barueri = new DateTime(ano, 3, 26);
            if (barueri >= inicio && barueri <= fim)
                feriados.Add(new FeriadoInfo { Data = barueri, Nome = "Aniversário de Barueri" });
        }

        // Remove duplicados por Data (mantém o primeiro encontrado)
        return feriados
            .GroupBy(f => f.Data)
            .Select(g => g.First())
            .OrderBy(f => f.Data)
            .ToList();
    }


    private List<FeriadoInfo> CalculaFeriadosMoveis(int ano)
    {
        int a = ano % 19, b = ano / 100, c = ano % 100;
        int d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int mes = (h + l - 7 * m + 114) / 31;
        int dia = ((h + l - 7 * m + 114) % 31) + 1;
        var pascoa = new DateTime(ano, mes, dia);

        return new List<FeriadoInfo>
    {
        new() { Data = pascoa, Nome = "Páscoa" },
        new() { Data = pascoa.AddDays(-2), Nome = "Sexta-feira Santa" },
        new() { Data = pascoa.AddDays(-47), Nome = "Carnaval" },
        new() { Data = pascoa.AddDays(60), Nome = "Corpus Christi" }
    };
    }


    public class BrasilFeriado
    {
        public string date { get; set; }
        public string name { get; set; }
    }

    public class PeriodoInput
    {
        [Required]
        public string MesReferencia { get; set; }

        [Required]
        [Range(1, 24)]
        public int HorasPorDia { get; set; }
    }

    public class ResultadoCalculo
    {
        public string Periodo { get; set; }
        public int DiasTotais { get; set; }
        public int DiasUteis { get; set; }
        public int HorasPorDia { get; set; }
        public int TotalHorasUteis { get; set; }
        public List<FeriadoInfo> Feriados { get; set; } = new();
    }

    public class FeriadoInfo
    {
        public DateTime Data { get; set; }
        public string Nome { get; set; }
    }
}
