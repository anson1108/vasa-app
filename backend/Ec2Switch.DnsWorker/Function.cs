using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.EC2;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Ec2Switch.Api;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
namespace Ec2Switch.DnsWorker;
public sealed class Function
{
    private static readonly RegionEndpoint Region = RegionEndpoint.GetBySystemName("ap-southeast-5");
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly AmazonEC2Client Ec2 = new(new AmazonEC2Config { RegionEndpoint = Region, MaxErrorRetry = 1 });
    private static readonly AmazonDynamoDBClient Db = new(new AmazonDynamoDBConfig { RegionEndpoint = Region, MaxErrorRetry = 1 });
    private static readonly AmazonSecretsManagerClient Secrets = new(new AmazonSecretsManagerConfig { RegionEndpoint = Region, MaxErrorRetry = 1 });
    private static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Missing configuration: " + name);
    public async Task Handler(JsonElement unusedEvent, ILambdaContext context)
    {
        var id = Env("INSTANCE_ID");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        string? cachedToken = null;
        async Task<string> CloudflareToken(CancellationToken ct)
        {
            if (cachedToken is not null) return cachedToken;
            var response = await Secrets.GetSecretValueAsync(new GetSecretValueRequest { SecretId = Env("DNS_SECRET_ARN") }, ct);
            using var json = JsonDocument.Parse(response.SecretString);
            cachedToken = json.RootElement.GetProperty("CloudflareApiToken").GetString();
            if (string.IsNullOrWhiteSpace(cachedToken)) throw new DnsSyncException("CLOUDFLARE_TOKEN_MISSING");
            return cachedToken;
        }
        async Task Save(PublicInstance instance, string status, string error, CancellationToken ct)
        {
            await Db.PutItemAsync(new PutItemRequest { TableName = Env("TABLE_NAME"), Item = new() {
                ["pk"] = new() { S = "SYNC#" + id }, ["status"] = new() { S = status },
                ["ip"] = new() { S = instance.Ip ?? "" }, ["generation"] = new() { S = instance.Generation },
                ["checkedAt"] = new() { N = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }, ["error"] = new() { S = error }
            } }, ct);
        }
        var dns = new CloudflareDns(Http, CloudflareToken, Env("DNS_ZONE_ID"), Env("DNS_RECORD_ID"), Env("DNS_ZONE"), Env("DNS_RR"));
        var service = new SyncService(ct => PublicInstanceReader.Read(Ec2, id, ct), dns, Save, Env("DNS_ZONE"), Env("DNS_RR"));
        try { await service.Run(timeout.Token); }
        catch { context.Logger.LogLine("DNS sync failed; next scheduled run will retry. Inspect App sync status and configuration."); throw new Exception("DNS_SYNC_FAILED"); }
    }
}
