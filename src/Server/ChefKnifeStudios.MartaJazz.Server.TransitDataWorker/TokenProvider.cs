using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;

public sealed class TokenProvider
{
    private readonly string[] _scopes;
    private readonly DefaultAzureCredential _credential;

    public TokenProvider(IConfiguration configuration)
    {
        var scope = configuration["AzureAd:Scope"]
            ?? throw new InvalidOperationException("Missing AzureAd:Scope");
        
        _scopes = new[] { scope };
        
        var managedIdentityClientId = configuration["AzureAd:ManagedIdentityClientId"];
        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = managedIdentityClientId
        });
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext(_scopes), ct);
        return token.Token;
    }
}
