using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.EC2;
using System.Globalization;

namespace Ec2Switch.Api;
public record PublicInstance(string State, string? Ip, string Generation);
public record DnsStatus(string Status, string Domain, string? PublicIp = null, long CheckedAt = 0, string? Error = null);
public static class PublicInstanceReader
{
    public static async Task<PublicInstance> Read(IAmazonEC2 ec2, string id, CancellationToken ct)
    {
        var response = await ec2.DescribeInstancesAsync(new() { InstanceIds = [id] }, ct);
        var i = response.Reservations.SelectMany(r => r.Instances).Single();
        return new(i.State.Name.Value, i.PublicIpAddress, i.LaunchTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }
}
public sealed class DnsStatusReader(IAmazonDynamoDB db, IAmazonEC2 ec2, string table, string id, string domain, bool enabled)
{
    public async Task<DnsStatus> Read(CancellationToken ct)
    {
        if (!enabled) return new("disabled", domain);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        ct = deadline.Token;
        try
        {
            var current = await PublicInstanceReader.Read(ec2, id, ct);
            if (current.State != "running") return new(current.State == "pending" ? "pending" : "inactive", domain);
            if (string.IsNullOrEmpty(current.Ip)) return new("waiting_ip", domain);
            var r = await db.GetItemAsync(new GetItemRequest { TableName = table, ConsistentRead = true,
                Key = new() { ["pk"] = new() { S = "SYNC#" + id } } }, ct);
            string S(string name) => r.Item.TryGetValue(name, out var value) ? value.S : "";
            long.TryParse(r.Item.TryGetValue("checkedAt", out var time) ? time.N : "0", out var checkedAt);
            if (S("generation") != current.Generation || S("ip") != current.Ip) return new("pending", domain, current.Ip);
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - checkedAt > 180) return new("stale", domain, current.Ip, checkedAt);
            return new(S("status"), domain, current.Ip, checkedAt, S("error"));
        }
        catch { return new("unavailable", domain); } // DNS diagnostics must not turn an accepted EC2 action into a failure.
    }
}
