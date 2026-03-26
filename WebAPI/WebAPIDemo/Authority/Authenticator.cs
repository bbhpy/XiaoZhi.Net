using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using static System.Net.Mime.MediaTypeNames;

namespace WebAPIDemo.Authority
{
    public static class Authenticator
    {
        public static bool Authenticate(string clientId, string secret)
        {
            var app = AppRepository.GetApplicationByClientId(clientId);
            if (app == null) return false;

            return (app.ClientId == clientId && app.Secret == secret);
        }

        public static string CreateToken(string clientId, DateTime expiresAt, string strSecretKey)
        {
            // Algo
            // Signing Key
            // Payload (claims)

            // Algorithm
            var signingCredentials = new SigningCredentials(
                new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(strSecretKey)),
                SecurityAlgorithms.HmacSha256Signature);

            // Payload (claims)
            var app = AppRepository.GetApplicationByClientId(clientId);
            var claimsDictionary = new Dictionary<string, object>
            {
                { "AppName", app?.ApplicationName??string.Empty },                
            };

            var scopes = app?.Scopes?.Split(',') ?? Array.Empty<string>();
            if (scopes.Length > 0)
            {
                foreach(var scope in scopes)
                {
                    claimsDictionary.Add(scope.Trim().ToLower(), "true");
                }
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                SigningCredentials = signingCredentials,
                Claims = claimsDictionary,
                Expires = expiresAt,
                NotBefore = DateTime.UtcNow,              
            };

            var tokenHandler = new JsonWebTokenHandler();
            return tokenHandler.CreateToken(tokenDescriptor);
        }

        public static async Task<IEnumerable<Claim>?> VerifyTokenAsync(string tokenString, string securityKey)
        {
            if (string.IsNullOrWhiteSpace(tokenString) || string.IsNullOrWhiteSpace(securityKey))
            {
                return null;
            }

            var keyBytes = System.Text.Encoding.UTF8.GetBytes(securityKey);
            var tokenHander = new JsonWebTokenHandler();

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero // No clock skew
            };

            try
            {
                var result = await tokenHander.ValidateTokenAsync(tokenString, validationParameters);
                
                if (result.SecurityToken != null)
                {                    
                    var tokenObject = tokenHander.ReadJsonWebToken(tokenString);
                    return tokenObject.Claims ?? Enumerable.Empty<Claim>();
                }
                else
                {
                    // Token is not valid
                    return null;
                }
                
            }
            catch(SecurityTokenMalformedException)
            {
                // Token is malformed
                return null;
            }
            catch(SecurityTokenExpiredException)
            {
                // Token is expired
                return null;
            }
            catch(SecurityTokenInvalidSignatureException)
            {
                // Token signature is invalid
                return null;
            }
            catch(Exception)
            {
                // Other exceptions
                throw;
            }
        }
    }
}
