using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.EC2;
using Amazon.EC2.Model;

namespace Ec2Switch.Api;

public sealed class AwsRegistry(IAmazonDynamoDB db, string table) : IRegistry
{
    private static Dictionary<string, AttributeValue> Key(string value) => new() { ["pk"] = new() { S = value } };
    public async Task<Device?> GetDevice(string id, CancellationToken ct)
    {
        var r = await db.GetItemAsync(new GetItemRequest { TableName = table, Key = Key("DEVICE#" + id), ConsistentRead = true }, ct);
        return r.Item.TryGetValue("publicKey", out var key)
            ? new(key.S, r.Item.TryGetValue("enabled", out var enabled) && enabled.BOOL) : null;
    }
    public async Task<bool> Consume(string id, string nonce, long expires, CancellationToken ct)
    {
        try
        {
            await db.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = [
                new() { ConditionCheck = new() { TableName = table, Key = Key("DEVICE#" + id),
                    ConditionExpression = "enabled = :yes", ExpressionAttributeValues = new() { [":yes"] = new() { BOOL = true } } } },
                new() { Put = new() { TableName = table, ConditionExpression = "attribute_not_exists(pk)",
                    Item = new() { ["pk"] = new() { S = $"NONCE#{id}#{nonce}" }, ["expires"] = new() { N = expires.ToString() } } } }
            ] }, ct);
            return true;
        }
        catch (TransactionCanceledException e) when (e.CancellationReasons.Any(x => x.Code == "ConditionalCheckFailed")) { return false; }
    }
}

public sealed class AwsInstance(IAmazonEC2 ec2, string id) : IInstance
{
    public async Task<string> State(CancellationToken ct)
    {
        var r = await ec2.DescribeInstancesAsync(new() { InstanceIds = [id] }, ct);
        return r.Reservations.SelectMany(x => x.Instances).SingleOrDefault()?.State.Name.Value ?? "unknown";
    }
    public async Task<string> Change(string action, CancellationToken ct)
    {
        if (action == "start")
        {
            var r = await ec2.StartInstancesAsync(new() { InstanceIds = [id] }, ct);
            return r.StartingInstances.Single().CurrentState.Name.Value;
        }
        // Ordinary OS shutdown only. No TerminateInstances permission or endpoint exists.
        var stopped = await ec2.StopInstancesAsync(new() { InstanceIds = [id], Force = false, Hibernate = false }, ct);
        return stopped.StoppingInstances.Single().CurrentState.Name.Value;
    }
}
