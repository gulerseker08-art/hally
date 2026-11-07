using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console());

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.Section));
builder.Services.Configure<ExternalAuthOptions>(builder.Configuration.GetSection(ExternalAuthOptions.Section));
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (jwt.SigningKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters. Set it via environment (Jwt__SigningKey).");
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));

var connectionString = builder.Configuration.GetConnectionString("Hally")
                       ?? throw new InvalidOperationException("ConnectionStrings:Hally is not configured.");
builder.Services.AddDbContext<HallyDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAnalytics, DbAnalytics>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton<IExternalIdentityVerifier, GoogleIdentityVerifier>();
builder.Services.AddSingleton<IExternalIdentityVerifier, AppleIdentityVerifier>();
builder.Services.AddSingleton<IExternalIdentityVerifier, DevIdentityVerifier>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = TokenService.KeyFrom(jwt),
        NameClaimType = "sub",
        ClockSkew = TimeSpan.FromSeconds(30),
    };
});
builder.Services.AddScoped<IAuthorizationHandler, FamilyMemberHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.FamilyMember, p => p.RequireAuthenticatedUser().AddRequirements(new FamilyMemberRequirement()));

builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = $"{builder.Configuration["App:Name"] ?? "Hally"} API", Version = "v1" }));
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<HallyDbContext>().Database.MigrateAsync();
}

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });

app.Run();

public partial class Program;
