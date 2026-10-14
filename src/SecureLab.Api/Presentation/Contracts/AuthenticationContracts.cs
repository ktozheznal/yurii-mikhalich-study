namespace SecureLab.Api.Presentation.Contracts;

public sealed record UserRegistrationRequest(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record UserLoginRequest(string? UserName, string? Password);
public sealed record RegisteredUserResponse(Guid Id, string UserName, string DisplayName);
public sealed record CurrentUserResponse(Guid UserId, string UserName, string Role);
