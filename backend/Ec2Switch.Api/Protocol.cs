using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ec2Switch.Api;

public record Device(string PublicKey, bool Enabled);
public record SignedRequest(string DeviceId, string Timestamp, string Nonce, string Signature, byte[] Body);
public record ControlResult(string State, string InstanceId, long ObservedAt);
public sealed class ApiException(int status, string code) : Exception(code) { public int Status { get; } = status; }
public interface IRegistry
{
    Task<Device?> GetDevice(string id, CancellationToken ct);
    Task<bool> Consume(string id, string nonce, long expires, CancellationToken ct);
}
public interface IInstance
{
    Task<string> State(CancellationToken ct);
    Task<string> Change(string action, CancellationToken ct);
}
public sealed class ControlService(IRegistry registry, IInstance instance, string audience, string instanceId, Func<long>? clock = null)
{
    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    public static string Canonical(string audience, SignedRequest r) => string.Join('\n',
        "EC2SWITCH1", audience, "POST", "/control", r.DeviceId, r.Timestamp, r.Nonce, Hex(SHA256.HashData(r.Body)));

    public static ECDsa ImportKey(string publicKey)
    {
        var der = Convert.FromBase64String(publicKey);
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(der, out var read);
            if (read != der.Length || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
                throw new CryptographicException("P-256 required");
            return key;
        }
        catch { key.Dispose(); throw; }
    }

    public async Task<ControlResult> Execute(SignedRequest r, CancellationToken ct)
    {
        var now = clock?.Invoke() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!Regex.IsMatch(r.DeviceId, "\\A[a-f0-9]{64}\\z") || !Regex.IsMatch(r.Timestamp, "\\A[0-9]{10}\\z")
            || !Regex.IsMatch(r.Nonce, "\\A[a-f0-9]{32}\\z")) throw new ApiException(401, "AUTH_FORMAT");
        if (Math.Abs(now - long.Parse(r.Timestamp)) > 120) throw new ApiException(401, "CLOCK_SKEW");
        if (r.Body.Length is 0 or > 256) throw new ApiException(400, "BODY_SIZE");
        var device = await registry.GetDevice(r.DeviceId, ct);
        if (device is not { Enabled: true }) throw new ApiException(401, "DEVICE_REVOKED_OR_UNKNOWN");
        var valid = false;
        try
        {
            using var key = ImportKey(device.PublicKey);
            if (r.Signature.Length <= 104)
                valid = key.VerifyData(Encoding.UTF8.GetBytes(Canonical(audience, r)), Convert.FromBase64String(r.Signature),
                    HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException) { }
        if (!valid) throw new ApiException(401, "BAD_SIGNATURE");
        string action;
        try
        {
            using var json = JsonDocument.Parse(r.Body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
                || !root.TryGetProperty("action", out var value) || value.ValueKind != JsonValueKind.String)
                throw new ApiException(400, "BAD_ACTION");
            action = value.GetString()!;
            if (action is not ("status" or "start" or "stop")) throw new ApiException(400, "BAD_ACTION");
        }
        catch (JsonException) { throw new ApiException(400, "BAD_JSON"); }
        // Atomic enabled-device condition and unique nonce insertion; TTL is garbage collection only.
        if (!await registry.Consume(r.DeviceId, r.Nonce, now + 600, ct)) throw new ApiException(409, "REPLAY_OR_REVOKED");
        var state = await instance.State(ct);
        if (action != "status")
        {
            if (state is "terminated" or "shutting-down") throw new ApiException(409, "INSTANCE_UNAVAILABLE");
            if (state is "pending" or "stopping") throw new ApiException(409, "TRANSITION_IN_PROGRESS");
            if (state is not ("running" or "stopped")) throw new ApiException(409, "UNKNOWN_STATE");
            var desired = action == "start" ? "running" : "stopped";
            if (state != desired) state = await instance.Change(action, ct);
        }
        return new(state, instanceId, now);
    }
}
