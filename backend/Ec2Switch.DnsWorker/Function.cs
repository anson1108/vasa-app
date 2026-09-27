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
        (string Id, string Secret)? cached = null; // Per invocation, so rotated secrets take effect at next run.
        async Task<(string Id, string Secret)> Credentials(CancellationToken ct)
        {
            if (cached.HasValue) return cached.Value;
            var r = await Secrets.GetSecretValueAsync(new GetSecretValueRequest { SecretId = Env("DNS_SECRET_ARN") }, ct);
            using var json = JsonDocument.Parse(r.SecretString);
            cached = (json.RootElement.GetProperty("AccessKeyId").GetString()!, json.RootElement.GetProperty("AccessKeySecret").GetString()!);
            return cached.Value;
        }
        async Task Save(PublicInstance instance, string status, string error, CancellationToken ct)
        {
            await Db.PutItemAsync(new PutItemRequest { TableName = Env("TABLE_NAME"), Item = new() {
                ["pk"] = new() { S = "SYNC#" + id }, ["status"] = new() { S = status },
                ["ip"] = new() { S = instance.Ip ?? "" }, ["generation"] = new() { S = instance.Generation },
                ["checkedAt"] = new() { N = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }, ["error"] = new() { S = error }
            } }, ct);
        }
        var service = new SyncService(ct => PublicInstanceReader.Read(Ec2, id, ct), new AliDns(Http, Credentials, Env("DNS_RECORD_ID")), Save, Env("DNS_ZONE"), Env("DNS_RR"));
        try { await service.Run(timeout.Token); }
        catch { context.Logger.LogLine("DNS sync failed; next scheduled run will retry. Inspect App sync status and configuration."); throw new Exception("DNS_SYNC_FAILED"); }
    }
}
