namespace MatchApi.Auth;

/// <summary>
/// Carries verified identity claims extracted from a valid JWT.
/// Passed through the dispatcher to handlers that implement <see cref="IAuthenticatedHandler"/>.
/// Null means the request arrived without a token (anonymous).
/// </summary>
public record AuthContext(string UserId, string Username, string Email);
