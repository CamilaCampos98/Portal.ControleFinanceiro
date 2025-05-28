namespace Portal.ControleFinanceiro.Models.Response
{
    public class EntradaResponseModel
    {
        public string Message { get; set; }
        public decimal ValorHoraExtra { get; set; }
        public int HorasExtras { get; set; }
        public decimal ValorExtraCalculado { get; set; }
        public decimal NovosExtras { get; set; }
    }
}
