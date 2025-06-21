using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portal.ControleFinanceiro.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using static ResumoModel;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly IConfiguration _configuration;

    public LoginModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [BindProperty]
    public LoginInput Input { get; set; }

    public string MensagemErro { get; set; }


    public async Task<IActionResult> OnPostAsync()
    {
        var urlApi = _configuration["UrlApi"];
        var url = $"{urlApi}Usuarios/Login";

        if (!ModelState.IsValid)
            return Page();

        using var httpClient = new HttpClient();

        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // Desserializar uma lista, não um único objeto
            var usuarios = JsonSerializer.Deserialize<List<UsuarioModel>>(content, options);

            if (usuarios == null || usuarios.Count == 0)
            {
                MensagemErro = "Nenhum usuário encontrado.";
                return Page();
            }

            // Buscar usuário válido na lista (case insensitive)
            var usuarioValido = usuarios.FirstOrDefault(u =>
                u.Usuario.ToUpper().Equals(Input.Usuario.ToUpper(), StringComparison.OrdinalIgnoreCase) &&
                u.Senha == Input.Senha);

            if (usuarioValido != null)
            {
                var claims = new List<Claim> { new Claim(ClaimTypes.Name, Input.Usuario) };
                var identidade = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identidade));

                return RedirectToPage("/Index");
            }

            MensagemErro = "Usuário ou senha inválidos.";
            return Page();
        }

        // Caso resposta não seja sucesso
        MensagemErro = "Erro ao comunicar com a API.";
        return Page();
    }



    public class LoginInput
    {
        [Required]
        public string Usuario { get; set; }

        [Required]
        public string Senha { get; set; }
    }
}
