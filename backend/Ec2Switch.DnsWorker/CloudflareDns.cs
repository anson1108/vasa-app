using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Ec2Switch.DnsWorker;

public sealed class CloudflareDns(
    HttpClient http,
    Func<CancellationToken, Task<string>> token,
    string zoneId,
    string recordId,
    string zone,
    string rr) : IDnsProvider
{
    private string Fqdn => $"{rr}.{zone}";

    private async Task<JsonDocument> Send(HttpMethod method, object? payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method,
            $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records/{recordId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(ct));
        if (payload is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new DnsSyncException("CLOUDFLARE_API_ERROR");
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { throw new DnsSyncException("CLOUDFLARE_INVALID_RESPONSE"); }
        if (!doc.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
        {
            doc.Dispose();
            throw new DnsSyncException("CLOUDFLARE_API_ERROR");
        }
        return doc;
    }

    public async Task<DnsRecord> Read(CancellationToken ct)
    {
        using var doc = await Send(HttpMethod.Get, null, ct);
        try
        {
            var record = doc.RootElement.GetProperty("result");
            string Get(string field) => record.GetProperty(field).GetString()!;
            var name = Get("name");
            var actualId = Get("id");
            var type = Get("type");
            var proxied = record.GetProperty("proxied").GetBoolean();
            if (actualId != recordId || !name.Equals(Fqdn, StringComparison.OrdinalIgnoreCase) || type != "A")
                throw new DnsSyncException("RECORD_MISMATCH");
            if (proxied) throw new DnsSyncException("CLOUDFLARE_PROXY_ENABLED");
            var ttl = record.GetProperty("ttl").GetInt32();
            return new DnsRecord(actualId, zone, rr, type, Get("content"), "default", ttl, "ENABLE");
        }
        catch (KeyNotFoundException) { throw new DnsSyncException("CLOUDFLARE_INVALID_RESPONSE"); }
        catch (InvalidOperationException) { throw new DnsSyncException("CLOUDFLARE_INVALID_RESPONSE"); }
    }

    public async Task Update(DnsRecord record, string ip, CancellationToken ct)
    {
        if (record.Id != recordId || record.Domain != zone || record.Rr != rr || record.Type != "A")
            throw new DnsSyncException("RECORD_MISMATCH");
        // PATCH keeps unrelated record settings unchanged. Force DNS-only for VPN traffic.
        using var doc = await Send(HttpMethod.Patch,
            new { type = "A", name = Fqdn, content = ip, proxied = false }, ct);
    }
}
