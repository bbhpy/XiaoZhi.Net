using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WebAPIDemo.Authority;

namespace WebAPIDemo.Controllers
{
    [ApiVersion("1.0")]
    [ApiExplorerSettings(GroupName = "v1")]
    [ApiController] 
    public class AuthorityController : ControllerBase
    {
        private readonly IConfiguration configuration;

        public AuthorityController(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        [HttpPost("auth")]
        public IActionResult Authenticate([FromBody] AppCredential credential)
        {
            if (Authenticator.Authenticate(credential.ClientId, credential.Secret))
            {
                var expiresAt = DateTime.UtcNow.AddMinutes(10);

                return Ok(new
                {
                    access_token = Authenticator.CreateToken(credential.ClientId, expiresAt, configuration["SecurityKey"] ?? string.Empty),
                    expires_at = expiresAt
                });
            }
            else
            {
                ModelState.AddModelError("Unauthorized", "You are not authorized.");
                var problemDetails = new ValidationProblemDetails(ModelState)
                {
                    Status = StatusCodes.Status401Unauthorized
                };
                return new UnauthorizedObjectResult(problemDetails);
            }
        }        
    }
}
