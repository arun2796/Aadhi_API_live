using Microsoft.Extensions.Configuration;

namespace AadhiCrackers.Infrastructure.Identity;


public static class JwtSecretProvider
{
    
    public const string DefaultSecret = "AadhiCrackers_Secure_Enterprise_JWT_Secret_Key_2026_!@#$999_SUPER_SECURE";

    public static string Resolve(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = configuration["JwtSettings:SecretKey"];
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = DefaultSecret;
        }

        return secret;
    }

    public static bool IsDefaultSecret(IConfiguration configuration)
        => Resolve(configuration) == DefaultSecret;
}
