using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ec2Switch.DnsWorker;
public sealed class AliDns(HttpClient http, Func<CancellationToken, Task<(string Id, string Secret)>> credentials, string recordId) : IDnsProvider
{
    public static string Sign(IReadOnlyDictionary<string,string> parameters, string secret)
    {
        var canonical = string.Join("&", parameters.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        var text = "POST&%2F&" + Uri.EscapeDataString(canonical);
        return Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret + "&"), Encoding.UTF8.GetBytes(text)));
    }
    private async Task<JsonDocument> Call(string action, Dictionary<string,string> values, CancellationToken ct)
    {
        var key = await credentials(ct);
        values["Action"] = action; values["Version"] = "2015-01-09"; values["Format"] = "JSON";
        values["AccessKeyId"] = key.Id; values["SignatureMethod"] = "HMAC-SHA1"; values["SignatureVersion"] = "1.0";
        values["SignatureNonce"] = Guid.NewGuid().ToString("N");
        values["Timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        values["Signature"] = Sign(values, key.Secret);
        // Signed parameters stay in the POST body, never in a logged URL. TLS verification remains enabled.
        using var body = new FormUrlEncodedContent(values);
        using var response = await http.PostAsync("https://alidns.aliyuncs.com/", body, ct);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode || json.RootElement.TryGetProperty("Code", out _))
        { json.Dispose(); throw new DnsSyncException("ALIYUN_API_ERROR"); }
        return json;
    }
    public async Task<DnsRecord> Read(CancellationToken ct)
    {
        using var json = await Call("DescribeDomainRecordInfo", new() { ["RecordId"] = recordId }, ct);
        var r = json.RootElement;
        string S(string name) => r.GetProperty(name).GetString()!;
        if (S("RecordId") != recordId) throw new DnsSyncException("RECORD_MISMATCH");
        return new(S("RecordId"), S("DomainName"), S("RR"), S("Type"), S("Value"), S("Line"), r.GetProperty("TTL").GetInt32(), S("Status"));
    }
    public async Task Update(DnsRecord record, string ip, CancellationToken ct)
    {
        using var json = await Call("UpdateDomainRecord", new() { ["RecordId"] = recordId, ["RR"] = record.Rr,
            ["Type"] = "A", ["Value"] = ip, ["Line"] = record.Line, ["TTL"] = record.Ttl.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ct);
    }
}
