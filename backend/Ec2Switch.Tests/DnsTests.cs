using Ec2Switch.Api;
using Ec2Switch.DnsWorker;
internal static class DnsTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        static void Check(bool value) { if (!value) throw new Exception("DNS assertion failed"); }
        static async Task Fails(Task task) { try { await task; } catch (DnsSyncException) { return; } throw new Exception("Expected failure"); }
        await test("AliDNS signature matches independent HMAC vector", () => {
            Check(AliDns.Sign(new Dictionary<string,string> { ["RecordId"] = "123", ["Action"] = "DescribeDomainRecordInfo" }, "testsecret") == "f/Hu18UkrTK3oUxN0CU6D0a/bNw=");
            return Task.CompletedTask;
        });
        await test("DDNS updates one A record and verifies result", async () => {
            var f = new Fixture(); await f.Service.Run(default); Check(f.Dns.Updates == 1 && f.Status == "synced" && f.Dns.Record.Value == "8.8.4.4");
            Check(f.Dns.Record.Ttl == 600 && f.Dns.Record.Line == "default");
        });
        await test("DDNS unchanged IP produces no writes", async () => { var f = new Fixture(); f.Dns.Record = f.Dns.Record with { Value = "8.8.4.4" }; await f.Service.Run(default); Check(f.Dns.Updates == 0 && f.Status == "synced"); });
        await test("DDNS waits when no public IP assigned", async () => { var f = new Fixture(); f.Instance = f.Instance with { Ip = null }; await f.Service.Run(default); Check(f.Status == "waiting_ip" && f.Dns.Reads == 0); });
        await test("DDNS stopped instance does not alter DNS", async () => { var f = new Fixture(); f.Instance = f.Instance with { State = "stopped" }; await f.Service.Run(default); Check(f.Status == "inactive" && f.Dns.Reads == 0); });
        await test("DDNS rejects a different zone", async () => { var f = new Fixture(); f.Dns.Record = f.Dns.Record with { Domain = "other.example" }; await Fails(f.Service.Run(default)); Check(f.Error == "RECORD_MISMATCH" && f.Dns.Updates == 0); });
        await test("DDNS rejects different host and non-A type", async () => { foreach(var record in new[] { "host", "type", "line" }) { var f = new Fixture(); f.Dns.Record = record switch { "host" => f.Dns.Record with { Rr = "www" }, "type" => f.Dns.Record with { Type = "AAAA" }, _ => f.Dns.Record with { Line = "telecom" } }; await Fails(f.Service.Run(default)); Check(f.Dns.Updates == 0); } });
        await test("DDNS does not silently enable disabled records", async () => { var f = new Fixture(); f.Dns.Record = f.Dns.Record with { Status = "Disable" }; await Fails(f.Service.Run(default)); Check(f.Error == "RECORD_DISABLED" && f.Dns.Updates == 0); });
        await test("DDNS rechecks current instance before writing", async () => { var f = new Fixture(); f.Dns.OnRead = () => f.Instance = f.Instance with { Ip = "1.1.1.1", Generation = "new" }; await f.Service.Run(default); Check(f.Status == "pending" && f.Dns.Updates == 0); });
        await test("DDNS rejects unverified update", async () => { var f = new Fixture(); f.Dns.IgnoreWrite = true; await Fails(f.Service.Run(default)); Check(f.Status == "error" && f.Error == "VERIFY_FAILED"); });
        await test("DDNS retry converges after transient failure", async () => { var f = new Fixture(); f.Dns.FailRead = true; await Fails(f.Service.Run(default)); f.Dns.FailRead = false; await f.Service.Run(default); Check(f.Status == "synced"); });
        await test("DDNS filters private IPv4 and IPv6", () => { foreach (var ip in new[] { "10.0.0.1", "172.31.1.1", "192.168.1.1", "169.254.1.1", "100.64.0.1", "127.0.0.1", "::1", "" }) Check(!SyncService.IsPublicV4(ip)); Check(SyncService.IsPublicV4("8.8.4.4")); return Task.CompletedTask; });
    }
    sealed class Fixture
    {
        public PublicInstance Instance = new("running", "8.8.4.4", "boot1");
        public FakeDns Dns = new();
        public string Status = "", Error = "";
        public SyncService Service => new(_ => Task.FromResult(Instance), Dns, (i,s,e,ct) => { Status=s; Error=e; return Task.CompletedTask; }, "contoso1.asia", "my");
    }
    sealed class FakeDns : IDnsProvider
    {
        public DnsRecord Record = new("record1", "contoso1.asia", "my", "A", "1.1.1.1", "default", 600, "Enable");
        public int Updates, Reads;
        public bool FailRead, IgnoreWrite;
        public Action? OnRead;
        public Task<DnsRecord> Read(CancellationToken ct) { Reads++; OnRead?.Invoke(); if (FailRead) throw new HttpRequestException(); return Task.FromResult(Record); }
        public Task Update(DnsRecord r, string ip, CancellationToken ct) { Updates++; if (!IgnoreWrite) Record = Record with { Value = ip }; return Task.CompletedTask; }
    }
}
