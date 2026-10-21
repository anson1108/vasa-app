using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ec2Switch.Api;

// The schedule name is fixed: clients cannot select an arbitrary schedule or EC2 target.
public sealed class ScheduleService(IAmazonScheduler scheduler, string scheduleName, string roleArn, string instanceId)
{
    private const string TimeZone = "Asia/Shanghai"; // UTC+08:00, no DST
    public async Task<object> Execute(byte[] bytes, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("action", out var actionNode)
            || actionNode.ValueKind != JsonValueKind.String) throw new ApiException(400, "BAD_ACTION");
        var action = actionNode.GetString();
        if (action == "schedule-status") return await Status(ct);
        if (action == "schedule-disable")
        {
            var old = await GetOrNull(ct);
            if (old != null) await scheduler.UpdateScheduleAsync(new UpdateScheduleRequest
            {
                Name = scheduleName, GroupName = "default", ScheduleExpression = old.ScheduleExpression,
                ScheduleExpressionTimezone = old.ScheduleExpressionTimezone ?? TimeZone,
                FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF },
                Target = old.Target, State = ScheduleState.DISABLED,
                Description = "Daily EC2 stop (disabled)"
            }, ct);
            return new { enabled = false, time = ParseTime(old?.ScheduleExpression), timezone = TimeZone };
        }
        if (action != "schedule-set" || !root.TryGetProperty("time", out var timeNode)
            || timeNode.ValueKind != JsonValueKind.String) throw new ApiException(400, "BAD_ACTION");
        var time = timeNode.GetString()!;
        if (!Regex.IsMatch(time, @"\A(?:[01][0-9]|2[0-3]):[0-5][0-9]\z")) throw new ApiException(400, "BAD_TIME");
        var hour = int.Parse(time[..2]); var minute = int.Parse(time[3..]);
        var expression = $"cron({minute} {hour} * * ? *)";
        var target = new Target
        {
            Arn = "arn:aws:scheduler:::aws-sdk:ec2:stopInstances", RoleArn = roleArn,
            Input = JsonSerializer.Serialize(new { InstanceIds = new[] { instanceId } }),
            RetryPolicy = new RetryPolicy { MaximumRetryAttempts = 2, MaximumEventAgeInSeconds = 3600 }
        };
        var current = await GetOrNull(ct);
        if (current == null)
            await scheduler.CreateScheduleAsync(new CreateScheduleRequest
            {
                Name = scheduleName, GroupName = "default", ScheduleExpression = expression,
                ScheduleExpressionTimezone = TimeZone, FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF },
                Target = target, State = ScheduleState.ENABLED, Description = "Daily stop for one EC2 instance"
            }, ct);
        else
            await scheduler.UpdateScheduleAsync(new UpdateScheduleRequest
            {
                Name = scheduleName, GroupName = "default", ScheduleExpression = expression,
                ScheduleExpressionTimezone = TimeZone, FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF },
                Target = target, State = ScheduleState.ENABLED, Description = "Daily stop for one EC2 instance"
            }, ct);
        return new { enabled = true, time, timezone = TimeZone };
    }
    public async Task<object> Status(CancellationToken ct)
    {
        var schedule = await GetOrNull(ct);
        return new { enabled = schedule?.State == ScheduleState.ENABLED,
            time = ParseTime(schedule?.ScheduleExpression), timezone = TimeZone };
    }
    private async Task<GetScheduleResponse?> GetOrNull(CancellationToken ct)
    {
        try { return await scheduler.GetScheduleAsync(new GetScheduleRequest { Name = scheduleName, GroupName = "default" }, ct); }
        catch (ResourceNotFoundException) { return null; }
    }
    private static string ParseTime(string? expression)
    {
        var match = Regex.Match(expression ?? "", @"\Acron\((\d+) (\d+) \* \* \? \*\)\z");
        return match.Success ? $"{int.Parse(match.Groups[2].Value):D2}:{int.Parse(match.Groups[1].Value):D2}" : "23:00";
    }
}
