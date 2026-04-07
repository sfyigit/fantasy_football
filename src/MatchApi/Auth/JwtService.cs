using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MatchApi.Auth;

/// <summary>
/// Generates and validates JWT access tokens.
/// Config is read once at construction from IConfiguration (environment variables):
///   JWT_SECRET, JWT_ISSUER, JWT_AUDIENCE, JWT_EXPIRES_IN_SECONDS.
/// </summary>
public class JwtService
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiresInSeconds;
    private readonly JwtSecurityTokenHandler _handler = new();
    private readonly TokenValidationParameters _validationParams;

    public JwtService(IConfiguration config)
    {
        var secret = config["JWT_SECRET"]
            ?? throw new InvalidOperationException("JWT_SECRET environment variable is required (min 32 chars)");

        if (secret.Length < 32)
            throw new InvalidOperationException("JWT_SECRET must be at least 32 characters");

        _signingKey       = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _issuer           = config["JWT_ISSUER"]   ?? "MatchApi";
        _audience         = config["JWT_AUDIENCE"] ?? "FantasyFootballClient";
        _expiresInSeconds = int.TryParse(config["JWT_EXPIRES_IN_SECONDS"], out var exp) ? exp : 3600;

        _validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = _signingKey,
            ValidateIssuer           = true,
            ValidIssuer              = _issuer,
            ValidateAudience         = true,
            ValidAudience            = _audience,
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>Issues a signed JWT for the given user.</summary>
    public (string Token, int ExpiresIn) GenerateToken(string userId, string username, string email)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   userId),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new Claim("username", username),
            new Claim("email",    email)
        };

        var token = new JwtSecurityToken(
            issuer:             _issuer,
            audience:           _audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddSeconds(_expiresInSeconds),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return (_handler.WriteToken(token), _expiresInSeconds);
    }

    /// <summary>
    /// Validates a raw Bearer token string (without the "Bearer " prefix).
    /// Returns an <see cref="AuthContext"/> on success, null on failure.
    /// </summary>
    public AuthContext? ValidateToken(string? rawToken)
    {
        if (string.IsNullOrEmpty(rawToken)) return null;

        try
        {
            var principal = _handler.ValidateToken(rawToken, _validationParams, out _);
            var userId    = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var username  = principal.FindFirstValue("username");
            var email     = principal.FindFirstValue("email");

            if (userId is null || username is null || email is null) return null;

            return new AuthContext(userId, username, email);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts and validates the token from an "Authorization: Bearer &lt;token&gt;" header value.
    /// </summary>
    public AuthContext? ValidateAuthorizationHeader(string? headerValue)
    {
        if (string.IsNullOrEmpty(headerValue)) return null;
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

        return ValidateToken(headerValue["Bearer ".Length..].Trim());
    }
}
