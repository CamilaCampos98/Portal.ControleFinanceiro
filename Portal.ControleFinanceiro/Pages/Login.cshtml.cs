using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly IConfiguration _configuration;

    public LoginModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? MensagemErro { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var urlApi = _configuration["UrlApi"];
        var url = $"{urlApi}Usuarios/Login";

        using var httpClient = new HttpClient();

        var payload = JsonSerializer.Serialize(Input);
        using var requestContent = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(url, requestContent);
        var content = await response.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var loginResponse = JsonSerializer.Deserialize<LoginResponse>(content, options);

        if (response.IsSuccessStatusCode && loginResponse?.Autenticado == true)
        {
            var usuario = string.IsNullOrWhiteSpace(loginResponse.Usuario)
                ? Input.Usuario
                : loginResponse.Usuario;

            var claims = new List<Claim> { new Claim(ClaimTypes.Name, usuario) };
            var identidade = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identidade));

            return RedirectToPage("/Index");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            MensagemErro = loginResponse?.Mensagem ?? "Login incorreto.";
            return Page();
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            MensagemErro = loginResponse?.Mensagem ?? "Usuário e senha são obrigatórios.";
            return Page();
        }

        MensagemErro = loginResponse?.Mensagem ?? "Erro ao comunicar com a API.";
        return Page();
    }

    public class LoginInput
    {
        [Required]
        public string Usuario { get; set; } = string.Empty;

        [Required]
        public string Senha { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public bool Autenticado { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Mensagem { get; set; } = string.Empty;
    }
}
