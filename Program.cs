using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using apiFrankfurter.Entidades;
using apiFrankfurter.Servicios;
using apiFrankfurter.DTO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using BCrypt.Net;
using System.IdentityModel.Tokens.Jwt;
using apiFrankfurter;
using apiFrankfurter.Migrations;

var builder = WebApplication.CreateBuilder(args);

// Configurar servicios de Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "API Frankfurter", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Por favor inserta JWT con Bearer en el campo",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        BearerFormat = "JWT",
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
        {
            new OpenApiSecurityScheme {
                Reference = new OpenApiReference {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
});

// Configurar DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configurar HttpClient para FrankfurterService
builder.Services.AddHttpClient<FrankfurterService>(client =>
{
    client.BaseAddress = new Uri("https://api.frankfurter.app/");
});

// Configurar Cache
builder.Services.AddMemoryCache();

// Configurar JWT
var key = Encoding.ASCII.GetBytes(builder.Configuration["Jwt:Key"]);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"], // Coincide con el Issuer en la configuración
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Issuer"], // Coincide con el Audience en la configuración si es necesario
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/register", async (UserRegister userRegister, ApplicationDbContext db) =>
{
    // Verificar si el usuario ya existe
    var existingUser = await db.Users
        .Where(u => u.Username == userRegister.Username)
        .SingleOrDefaultAsync();

    if (existingUser != null)
        return Results.BadRequest("Usuario ya existe.");

    // Crear y agregar nuevo usuario
    var user = new User
    {
        Username = userRegister.Username,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(userRegister.Password)
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok("Usuario registrado correctamente.");
});

app.MapPost("/login", async (UserLogin userLogin, ApplicationDbContext db, IConfiguration configuration) =>
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Username == userLogin.Username);
    if (user == null || !BCrypt.Net.BCrypt.Verify(userLogin.Password, user.PasswordHash))
        return Results.Unauthorized();

    var tokenHandler = new JwtSecurityTokenHandler();
    var key = Encoding.ASCII.GetBytes(configuration["Jwt:Key"]);
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, user.Username)
        }),
        Expires = DateTime.UtcNow.AddHours(1),
        Issuer = configuration["Jwt:Issuer"], // Coincide con el Issuer en la configuración
        Audience = configuration["Jwt:Issuer"], // Coincide con el Audience en la configuración si es necesario
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    var token = tokenHandler.CreateToken(tokenDescriptor);
    var tokenString = tokenHandler.WriteToken(token);

    return Results.Ok(new { Token = tokenString });
});


// Endpoints CRUD para las tasas de cambio
app.MapGet("/rates", async (ApplicationDbContext db, IMemoryCache cache) =>
{
    if (!cache.TryGetValue("all_rates", out List<ExchangeRate> rates))
    {
        rates = await db.ExchangeRates.ToListAsync();
        var cacheEntryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            SlidingExpiration = TimeSpan.FromMinutes(2)
        };
        cache.Set("all_rates", rates, cacheEntryOptions);
    }
    return Results.Ok(rates);
}).RequireAuthorization();

app.MapGet("/rates/{id}", async (int id, ApplicationDbContext db, IMemoryCache cache) =>
{
    if (!cache.TryGetValue($"rate_{id}", out ExchangeRate rate))
    {
        rate = await db.ExchangeRates.FindAsync(id);
        if (rate is null)
        {
            return Results.NotFound();
        }
        var cacheEntryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            SlidingExpiration = TimeSpan.FromMinutes(2)
        };
        cache.Set($"rate_{id}", rate, cacheEntryOptions);
    }
    return Results.Ok(rate);
}).RequireAuthorization();

app.MapPost("/rates", async (ExchangeRate rate, ApplicationDbContext db, IMemoryCache cache) =>
{
    db.ExchangeRates.Add(rate);
    await db.SaveChangesAsync();
    cache.Remove("all_rates"); // Invalidate the cache
    return Results.Created($"/rates/{rate.Id}", rate);
}).RequireAuthorization();

app.MapPut("/rates/{id}", async (int id, ExchangeRate rate, ApplicationDbContext db, IMemoryCache cache) =>
{
    var existingRate = await db.ExchangeRates.FindAsync(id);
    if (existingRate is null) return Results.NotFound();

    existingRate.BaseCurrency = rate.BaseCurrency;
    existingRate.TargetCurrency = rate.TargetCurrency;
    existingRate.Rate = rate.Rate;
    existingRate.Date = rate.Date;

    await db.SaveChangesAsync();
    cache.Remove("all_rates"); // Invalidate the cache
    cache.Remove($"rate_{id}"); // Invalidate the cache
    return Results.NoContent();
}).RequireAuthorization();

app.MapDelete("/rates/{id}", async (int id, ApplicationDbContext db, IMemoryCache cache) =>
{
    var rate = await db.ExchangeRates.FindAsync(id);
    if (rate is null) return Results.NotFound();

    db.ExchangeRates.Remove(rate);
    await db.SaveChangesAsync();
    cache.Remove("all_rates"); // Invalidate the cache
    cache.Remove($"rate_{id}"); // Invalidate the cache
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/rates/currency/{baseCurrency}", async (string baseCurrency, ApplicationDbContext db) =>
{
    return await db.ExchangeRates.Where(r => r.BaseCurrency == baseCurrency).ToListAsync();
}).RequireAuthorization();

app.MapPut("/rates/currency/{baseCurrency}", async (string baseCurrency, ExchangeRate rate, ApplicationDbContext db) =>
{
    var rates = await db.ExchangeRates.Where(r => r.BaseCurrency == baseCurrency).ToListAsync();
    if (rates.Count == 0) return Results.NotFound();

    foreach (var existingRate in rates)
    {
        existingRate.TargetCurrency = rate.TargetCurrency;
        existingRate.Rate = rate.Rate;
        existingRate.Date = rate.Date;
    }

    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapDelete("/rates/currency/{baseCurrency}", async (string baseCurrency, ApplicationDbContext db) =>
{
    var rates = await db.ExchangeRates.Where(r => r.BaseCurrency == baseCurrency).ToListAsync();
    if (rates.Count == 0) return Results.NotFound();

    db.ExchangeRates.RemoveRange(rates);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

// Endpoints para cálculos de tasas de cambio
app.MapGet("/rates/average", async (string baseCurrency, string targetCurrency, DateTime start, DateTime end, ApplicationDbContext db) =>
{
    var rates = await db.ExchangeRates
        .Where(r => r.BaseCurrency == baseCurrency && r.TargetCurrency == targetCurrency && r.Date >= start && r.Date <= end)
        .ToListAsync();

    if (rates.Count == 0) return Results.NotFound();

    var averageRate = rates.Average(r => r.Rate);
    return Results.Ok(new { AverageRate = averageRate });
}).RequireAuthorization();

app.MapGet("/rates/minmax", async (string baseCurrency, string targetCurrency, DateTime start, DateTime end, ApplicationDbContext db) =>
{
    var rates = await db.ExchangeRates
        .Where(r => r.BaseCurrency == baseCurrency && r.TargetCurrency == targetCurrency && r.Date >= start && r.Date <= end)
        .ToListAsync();

    if (rates.Count == 0) return Results.NotFound();

    var minRate = rates.Min(r => r.Rate);
    var maxRate = rates.Max(r => r.Rate);
    return Results.Ok(new { MinRate = minRate, MaxRate = maxRate });
}).RequireAuthorization();

// Endpoints para consultas a la API de Frankfurter
app.MapGet("/external-rates", async (FrankfurterService frankfurterService) =>
{
    var rates = await frankfurterService.GetLatestRates();
    return Results.Ok(rates);
}).RequireAuthorization();

app.Run();
