using Ec2Switch.Api;
using System.Net;
using System.Net.Sockets;

namespace Ec2Switch.DnsWorker;
public record DnsRecord(string Id, string Domain, string Rr, string Type, string Value, string Line, int Ttl, string Status);
public interface IDnsProvider
{
    Task<DnsRecord> Read(CancellationToken ct);
    Task Update(DnsRecord record, string ip, CancellationToken ct);
}
public sealed class DnsSyncException(string code) : Exception(code);
public sealed class SyncService(Func<CancellationToken, Task<PublicInstance>> instance, IDnsProvider dns,
    Func<PublicInstance, string, string, CancellationToken, Task> save, string domain, string rr)
{
    public static bool IsPublicV4(string? value)
    {
        if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] is not (0 or 10 or 127) && b[0] < 224 && !(b[0] == 169 && b[1] == 254)
            && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) && !(b[0] == 192 && b[1] == 168)
            && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }
    public async Task Run(CancellationToken ct)
    {
        var current = await instance(ct);
        if (current.State != "running") { await save(current, "inactive", "", ct); return; }
        if (!IsPublicV4(current.Ip)) { await save(current, "waiting_ip", "", ct); return; }
        try
        {
            var record = await dns.Read(ct);
            if (!string.Equals(record.Domain, domain, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(record.Rr, rr, StringComparison.OrdinalIgnoreCase) || record.Type != "A" || record.Line != "default")
                throw new DnsSyncException("RECORD_MISMATCH");
            if (!record.Status.Equals("ENABLE", StringComparison.OrdinalIgnoreCase)) throw new DnsSyncException("RECORD_DISABLED");
            // Never apply an IP from an old EventBridge event: check the current EC2 generation again.
            if (await instance(ct) != current) { await save(current, "pending", "INSTANCE_CHANGED", ct); return; }
            if (record.Value != current.Ip)
            {
                await dns.Update(record, current.Ip!, ct);
                var verified = await dns.Read(ct);
                if (verified.Value != current.Ip || verified.Type != "A" || verified.Rr != record.Rr || verified.Domain != record.Domain
                    || verified.Line != record.Line || !verified.Status.Equals("ENABLE", StringComparison.OrdinalIgnoreCase))
                    throw new DnsSyncException("VERIFY_FAILED");
            }
            if (await instance(ct) != current) { await save(current, "pending", "INSTANCE_CHANGED", ct); return; }
            await save(current, "synced", "", ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            await save(current, "error", e is DnsSyncException ? e.Message : "DNS_REQUEST_FAILED", ct);
            // Schedule runs every minute, independently of whether the phone remains open.
            throw new DnsSyncException("DNS_SYNC_FAILED");
        }
    }
}
