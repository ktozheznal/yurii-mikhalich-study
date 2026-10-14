using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", RegisterAsync);
        app.MapPost("/api/auth/login", LoginAsync).RequireRateLimiting(LoginRateLimit.Policy);
        app.MapPost("/api/auth/logout", async (HttpContext context,
            SignInManager<ApplicationUser> signIn, ILogger<AuthenticationEvents> logger) =>
        {
            await signIn.SignOutAsync();
            AuthenticationEvents.Write(logger, context, "logout", "success",
                context.User.FindFirstValue(ClaimTypes.NameIdentifier));
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapGet("/api/me", (HttpContext context) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
            return Results.Ok(new CurrentUserResponse(userId,
                context.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                context.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty));
        }).RequireAuthorization();
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }

    private static async Task<IResult> RegisterAsync(UserRegistrationRequest input,
        UserManager<ApplicationUser> users, SecureLabDbContext db)
    {
        if (!ValidRegistration(input))
            return RegistrationFailed();

        // The role and account are committed together; client role/id fields are absent from the DTO.
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = input.UserName!.Trim(), Email = input.Email!.Trim(),
                DisplayName = input.DisplayName!.Trim()
            };
            var result = await users.CreateAsync(user, input.Password!);
            if (!result.Succeeded)
                return result.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail")
                    ? DuplicateRegistration() : RegistrationFailed();
            var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
            if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");
            await transaction.CommitAsync();
            return Results.Created("/api/me", new RegisteredUserResponse(user.Id,
                user.UserName!, user.DisplayName));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return DuplicateRegistration();
        }
    }

    private static async Task<IResult> LoginAsync(UserLoginRequest input, HttpContext context,
        SignInManager<ApplicationUser> signIn, ILogger<AuthenticationEvents> logger)
    {
        var succeeded = false;
        string? verifiedUserId = null;
        if (!string.IsNullOrWhiteSpace(input.UserName) && input.UserName.Length <= 64
            && !string.IsNullOrEmpty(input.Password) && input.Password.Length <= 128)
        {
            var userName = input.UserName.Trim();
            var result = await signIn.PasswordSignInAsync(userName, input.Password,
                isPersistent: false, lockoutOnFailure: false);
            succeeded = result.Succeeded;
            if (succeeded)
                verifiedUserId = (await signIn.UserManager.FindByNameAsync(userName))?.Id.ToString();
        }
        AuthenticationEvents.Write(logger, context, "login", succeeded ? "success" : "failure", verifiedUserId);
        return succeeded ? Results.NoContent() : Results.Unauthorized();
    }

    private static bool ValidRegistration(UserRegistrationRequest input) =>
        !string.IsNullOrWhiteSpace(input.UserName) && input.UserName.Length <= 64
        && !string.IsNullOrWhiteSpace(input.DisplayName) && input.DisplayName.Length <= 120
        && !string.IsNullOrWhiteSpace(input.Email) && input.Email.Length <= 254
        && new EmailAddressAttribute().IsValid(input.Email.Trim())
        && input.Password is { Length: >= 10 and <= 128 };

    private static IResult RegistrationFailed() => Results.Problem(
        title: "Реєстрацію відхилено", statusCode: StatusCodes.Status400BadRequest);

    private static IResult DuplicateRegistration() => Results.Problem(
        title: "Обліковий запис уже існує", statusCode: StatusCodes.Status409Conflict);
}
