using System.ComponentModel.DataAnnotations;

namespace Portal.ControleFinanceiro.Models
{
    public class CompraInputModel
    {
        public string Pessoa { get; set; } = string.Empty;
        public string Descricao { get; set; } = string.Empty;
        public decimal ValorTotal { get; set; }
        public DateTime Data { get; set; } = DateTime.Today;
        public string FormaPgto { get; set; } = "C";
        public int TotalParcelas { get; set; } = 1;
        public string Fonte { get; set; } = "Salario";
        public string Cartao { get; set; }
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (FormaPgto == "C" && string.IsNullOrWhiteSpace(Cartao))
            {
                yield return new ValidationResult(
                    "O campo Cartão é obrigatório quando a forma de pagamento é Crédito.",
                    new[] { nameof(Cartao) });
            }
        }
    }
}
