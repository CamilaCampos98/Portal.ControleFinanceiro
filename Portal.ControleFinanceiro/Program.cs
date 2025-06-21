using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Tempo de expiração da sessão
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true; // para GDPR compliance
});

// AutenticaÃ§Ã£o Cookie
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";          // PÃ¡gina de login
        options.AccessDeniedPath = "/Login";   // PÃ¡gina para acesso negado
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
    });


builder.Services.AddAuthorization();

var app = builder.Build();

var defaultCulture = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;

//// Configure the HTTP request pipeline.
//if (!app.Environment.IsDevelopment())
//{
//    app.UseExceptionHandler("/Error");
//    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
//    app.UseHsts();
//}

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Urls.Add($"http://*:{port}");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
