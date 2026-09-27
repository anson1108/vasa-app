using System.Security.Cryptography;
using System.Text.Json;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Ec2Switch.Api;

if (args.Length != 3 || args[0] is not ("register" or "revoke"))
{
    Console.Error.WriteLine("Usage: register TABLE device.json | revoke TABLE DEVICE_ID");
    return 1;
}
using var db = new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName("ap-southeast-5"));
var table = args[1];
if (args[0] == "register")
{
    using var json = JsonDocument.Parse(await File.ReadAllTextAsync(args[2]));
    var root = json.RootElement;
    if (root.GetProperty("version").GetInt32() != 1) throw new Exception("Unsupported device file");
    using var key = ControlService.ImportKey(root.GetProperty("publicKey").GetString()!);
    var der = key.ExportSubjectPublicKeyInfo();
    var id = ControlService.Hex(SHA256.HashData(der));
    if (root.GetProperty("deviceId").GetString() != id) throw new Exception("Device fingerprint mismatch");
    await db.PutItemAsync(new PutItemRequest {
        TableName = table, ConditionExpression = "attribute_not_exists(pk)",
        Item = new() { ["pk"] = new() { S = "DEVICE#" + id }, ["publicKey"] = new() { S = Convert.ToBase64String(der) }, ["enabled"] = new() { BOOL = true } }
    });
    Console.WriteLine("Registered device: " + id);
}
else
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(args[2], "\\A[a-f0-9]{64}\\z")) throw new Exception("Invalid device ID");
    await db.UpdateItemAsync(new UpdateItemRequest { TableName = table,
        Key = new() { ["pk"] = new() { S = "DEVICE#" + args[2] } },
        ConditionExpression = "attribute_exists(pk)", UpdateExpression = "SET enabled = :no",
        ExpressionAttributeValues = new() { [":no"] = new() { BOOL = false } }
    });
    Console.WriteLine("Revoked device: " + args[2]);
}
return 0;
