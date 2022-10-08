using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using static System.Formats.Asn1.AsnWriter;

namespace OnBehalfOfAuth
{
    public class Function1
    {
        public Function1(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        [FunctionName("Function1")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
        
            string token = req.Headers["Authorization"];
            token = token.Replace("Bearer ", "");

            var isTokenValid = await ValidateToken(token);

            var options = new TokenCredentialOptions
            {
                AuthorityHost = AzureAuthorityHosts.AzurePublicCloud
            };

            //var clientSecretCredential = new ClientSecretCredential(
            //           Configuration["AzureAd:TenantId"], 
            //           Configuration["AzureAd:ClientId"],
            //           Configuration["AzureAd:ClientSecret"],
            //options);

            var onBehalfOfCredentials = new OnBehalfOfCredential(
                Configuration["AzureAd:TenantId"],
                       Configuration["AzureAd:ClientId"],
                       Configuration["AzureAd:ClientSecret"],
                       token
                );

            string[] scopes = new string[] { "https://graph.microsoft.com/.default" };

            try
            {
                var graphClient = new GraphServiceClient(onBehalfOfCredentials, scopes);
                var groups = graphClient.Users["f32f3633-dab2-4828-93d7-57f28b39e6f1"].MemberOf.Request().GetAsync().Result;

            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex.Message);
            }


            return new OkObjectResult(token);
        }

        private async Task<bool> ValidateToken(
           string token
           )
        {
            string tenantId = Configuration["AzureAd:TenantId"];
            string issuer =$"https://sts.windows.net/{tenantId}/"; // using v1 tokens
                //$"https://login.microsoftonline.com/{tenantId}/";  for v2 tokens
            string audience = Configuration["AzureAd:ClientId"];

            var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                   issuer + "/.well-known/openid-configuration",
                   new OpenIdConnectConfigurationRetriever(),
                   new HttpDocumentRetriever());

            if (string.IsNullOrEmpty(token)) throw new ArgumentNullException(nameof(token));
            if (string.IsNullOrEmpty(issuer)) throw new ArgumentNullException(nameof(issuer));

            var discoveryDocument = await configurationManager.GetConfigurationAsync(default(CancellationToken));
            var signingKeys = discoveryDocument.SigningKeys;

            var validationParameters = new TokenValidationParameters
            {
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = $"api://{audience}" ,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            };

            try
            {
                new JwtSecurityTokenHandler().ValidateToken(token, validationParameters, out var rawValidatedToken);
                return true;
            }
            catch (SecurityTokenValidationException)
            {
                return false;
            }
        }
    }
}
