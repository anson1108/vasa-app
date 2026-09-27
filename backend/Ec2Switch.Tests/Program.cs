using System.Security.Cryptography;
using System.Text;
using Ec2Switch.Api;

const long Now = 1800000000;
const string Audience = "https://test.execute-api.ap-southeast-5.amazonaws.com";
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var der = key.ExportSubjectPublicKeyInfo();
var id = ControlService.Hex(SHA256.HashData(der));
var failures = 0;
SignedRequest Sign(string action = "status", long time = Now, string audience = Audience, string? raw = null)
{
    var r = new SignedRequest(id, time.ToString(), ControlService.Hex(RandomNumberGenerator.GetBytes(16)), "",
        Encoding.UTF8.GetBytes(raw ?? "{\"action\":\"" + action + "\"}"));
    return r with { Signature = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(ControlService.Canonical(audience, r)), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) };
}
(ControlService service, Registry registry, Instance instance) Setup(string state = "stopped")
{
    var registry = new Registry(new(Convert.ToBase64String(der), true));
    var instance = new Instance { Current = state };
    return (new(registry, instance, Audience, "i-056494d14ab6b3dd0", () => Now), registry, instance);
}
async Task Reject(ControlService s, SignedRequest r, string code)
{
    try { await s.Execute(r, default); throw new Exception("Request was accepted"); }
    catch (ApiException e) { if (e.Message != code) throw new Exception($"Expected {code}, got {e.Message}"); }
}
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
async Task Test(string name, Func<Task> body)
{
    try { await body(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
await Test("valid DER ECDSA signature and actual state", async () => { var x = Setup(); Equal("stopped", (await x.service.Execute(Sign(), default)).State); Equal(0, x.instance.Changes); });
await Test("body tamper rejected", async () => { var x = Setup(); await Reject(x.service, Sign() with { Body = Encoding.UTF8.GetBytes("{\"action\":\"start\"}") }, "BAD_SIGNATURE"); });
await Test("cross-deployment audience rejected", async () => { var x = Setup(); await Reject(x.service, Sign(audience: "https://other"), "BAD_SIGNATURE"); });
await Test("expired timestamp rejected", async () => { var x = Setup(); await Reject(x.service, Sign(time: Now - 121), "CLOCK_SKEW"); });
await Test("future timestamp rejected", async () => { var x = Setup(); await Reject(x.service, Sign(time: Now + 121), "CLOCK_SKEW"); });
await Test("malformed signature rejected", async () => { var x = Setup(); await Reject(x.service, Sign() with { Signature = "!!!" }, "BAD_SIGNATURE"); });
await Test("unknown device rejected", async () => { var x = Setup(); x.registry.Device = null; await Reject(x.service, Sign(), "DEVICE_REVOKED_OR_UNKNOWN"); });
await Test("revoked device rejected", async () => { var x = Setup(); x.registry.Device = new(Convert.ToBase64String(der), false); await Reject(x.service, Sign(), "DEVICE_REVOKED_OR_UNKNOWN"); });
await Test("revocation between read and nonce transaction rejected", async () => { var x = Setup(); x.registry.RevokeOnConsume = true; await Reject(x.service, Sign(), "REPLAY_OR_REVOKED"); Equal(0, x.instance.Changes); });
await Test("sequential replay rejected", async () => { var x = Setup(); var r = Sign(); await x.service.Execute(r, default); await Reject(x.service, r, "REPLAY_OR_REVOKED"); });
await Test("concurrent replay permits only one request", async () => {
    var x = Setup(); var r = Sign("start");
    var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ => {
        try { await x.service.Execute(r, default); return true; } catch (ApiException) { return false; }
    })); Equal(1, results.Count(v => v)); Equal(1, x.instance.Changes);
});
await Test("nonce retention exceeds signed validity window", async () => { var x = Setup(); await x.service.Execute(Sign(time: Now + 120), default); Equal(Now + 600, x.registry.Expires); });
await Test("start returns pending", async () => { var x = Setup(); Equal("pending", (await x.service.Execute(Sign("start"), default)).State); Equal("start", x.instance.LastAction); });
await Test("stop returns stopping", async () => { var x = Setup("running"); Equal("stopping", (await x.service.Execute(Sign("stop"), default)).State); Equal("stop", x.instance.LastAction); });
await Test("already in requested state is idempotent", async () => { var x = Setup("running"); await x.service.Execute(Sign("start"), default); Equal(0, x.instance.Changes); });
foreach (var state in new[] { "pending", "stopping" })
    await Test(state + " blocks commands", async () => { var x = Setup(state); await Reject(x.service, Sign("stop"), "TRANSITION_IN_PROGRESS"); });
foreach (var state in new[] { "terminated", "shutting-down" })
    await Test(state + " unavailable", async () => { var x = Setup(state); await Reject(x.service, Sign("start"), "INSTANCE_UNAVAILABLE"); });
await Test("unknown state cannot operate", async () => { var x = Setup("unknown"); await Reject(x.service, Sign("start"), "UNKNOWN_STATE"); });
await Test("terminate action absent", async () => { var x = Setup(); await Reject(x.service, Sign("terminate"), "BAD_ACTION"); Equal(0, x.instance.Changes); });
await Test("caller cannot override instance", async () => { var x = Setup(); await Reject(x.service, Sign(raw: "{\"action\":\"start\",\"instanceId\":\"other\"}"), "BAD_ACTION"); });
await Test("duplicate action fields rejected", async () => { var x = Setup(); await Reject(x.service, Sign(raw: "{\"action\":\"start\",\"action\":\"stop\"}"), "BAD_ACTION"); });
await Test("non-P256 registration rejected", () => { using var other = ECDsa.Create(ECCurve.NamedCurves.nistP384); try { using var imported = ControlService.ImportKey(Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())); throw new Exception("Accepted P384"); } catch (CryptographicException) { } return Task.CompletedTask; });
await DnsTests.Run(Test);
Console.WriteLine($"Failures: {failures}");
return failures == 0 ? 0 : 1;

sealed class Registry(Device device) : IRegistry
{
    public Device? Device = device;
    public bool RevokeOnConsume;
    public long Expires;
    private readonly HashSet<string> nonces = [];
    public Task<Device?> GetDevice(string id, CancellationToken ct) => Task.FromResult(Device);
    public Task<bool> Consume(string id, string nonce, long expires, CancellationToken ct)
    { lock (nonces) { Expires = expires; return Task.FromResult(!RevokeOnConsume && Device is { Enabled: true } && nonces.Add(id + nonce)); } }
}
sealed class Instance : IInstance
{
    public string Current = "stopped", LastAction = "";
    public int Changes;
    public Task<string> State(CancellationToken ct) => Task.FromResult(Current);
    public Task<string> Change(string action, CancellationToken ct)
    { Changes++; LastAction = action; Current = action == "start" ? "pending" : "stopping"; return Task.FromResult(Current); }
}
