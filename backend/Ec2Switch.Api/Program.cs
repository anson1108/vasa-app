using Amazon;
using Amazon.DynamoDBv2;
using Amazon.EC2;
using Ec2Switch.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
var region = RegionEndpoint.GetBySystemName("ap-southeast-5");
var table = builder.Configuration["TABLE_NAME"] ?? throw new InvalidOperationException("Set TABLE_NAME");
var audience = builder.Configuration["AUDIENCE"] ?? throw new InvalidOperationException("Set AUDIENCE");
var instanceId = builder.Configuration["INSTANCE_ID"] ?? "i-056494d14ab6b3dd0";
builder.Services.AddSingleton<IAmazonDynamoDB>(new AmazonDynamoDBClient(new AmazonDynamoDBConfig { RegionEndpoint = region, MaxErrorRetry = 1 }));
builder.Services.AddSingleton<IAmazonEC2>(new AmazonEC2Client(new AmazonEC2Config { RegionEndpoint = region, MaxErrorRetry = 1 }));
builder.Services.AddSingleton<IRegistry>(sp => new AwsRegistry(sp.GetRequiredService<IAmazonDynamoDB>(), table));
builder.Services.AddSingleton<IInstance>(sp => new AwsInstance(sp.GetRequiredService<IAmazonEC2>(), instanceId));
builder.Services.AddSingleton(sp => new ControlService(sp.GetRequiredService<IRegistry>(), sp.GetRequiredService<IInstance>(), audience, instanceId));
builder.Services.AddSingleton(sp => new DnsStatusReader(sp.GetRequiredService<IAmazonDynamoDB>(), sp.GetRequiredService<IAmazonEC2>(),
    table, instanceId, builder.Configuration["DNS_FQDN"] ?? "my.contoso1.asia", builder.Configuration["DDNS_ENABLED"] == "true"));
var app = builder.Build();
app.MapPost("/control", async (HttpContext context, ControlService service, DnsStatusReader dnsReader, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    try
    {
        if (context.Request.Path != "/control" || context.Request.QueryString.HasValue) throw new ApiException(404, "ROUTE");
        using var body = new MemoryStream();
        var buffer = new byte[257];
        while (body.Length <= 256)
        {
            var n = await context.Request.Body.ReadAsync(buffer.AsMemory(0, (int)(257 - body.Length)), context.RequestAborted);
            if (n == 0) break;
            body.Write(buffer, 0, n);
        }
        string Header(string name) => context.Request.Headers[name].ToString();
        var result = await service.Execute(new(Header("x-device-id"), Header("x-timestamp"), Header("x-nonce"),
            Header("x-signature"), body.ToArray()), context.RequestAborted);
        return Results.Json(new { result.State, result.InstanceId, result.ObservedAt, dns = await dnsReader.Read(context.RequestAborted) });
    }
    catch (ApiException e) { return Results.Json(new { error = e.Message }, statusCode: e.Status); }
    catch (AmazonEC2Exception e) when (e.ErrorCode == "IncorrectInstanceState")
    { return Results.Json(new { error = "TRANSITION_IN_PROGRESS" }, statusCode: 409); }
    catch (Exception e)
    {
        logger.LogError("Control failed: {Type}; trace={Trace}", e.GetType().Name, context.TraceIdentifier);
        return Results.Json(new { error = "SERVICE_UNAVAILABLE" }, statusCode: 503);
    }
});
app.Run();
