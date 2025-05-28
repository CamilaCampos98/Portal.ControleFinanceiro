namespace Portal.ControleFinanceiro.Models
{
    public class FixosPayload
    {
        public string Action { get; set; }
        public string Pessoa { get; set; }
        public List<FixoModel> Data { get; set; }
    }
}
