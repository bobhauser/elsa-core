using System.Text.Json.Serialization;

namespace Elsa.Workflows.Activities.Flowchart.Models;

internal class FlowchartSchedulerState
{
    [JsonConstructor]
    public FlowchartSchedulerState()
    {
    }

    private readonly Dictionary<string, long> _activitiesVisitCount = new();
    private readonly Dictionary<string, long> _connectionVisitCount = new();
    private readonly Dictionary<string, bool> _connectionLastVisitFollowed = new();
    private string? _startActivityId = null;

    public void RegisterActivityVisit(IActivity activity)
    {
        string activityId = activity.Id;
        _activitiesVisitCount.TryAdd(activityId, 0);
        _activitiesVisitCount[activityId]++;
        _startActivityId ??= activityId;
    }

    public string? StartActivityId => _startActivityId;

    public long GetActivityVisitCount(IActivity activity) => _activitiesVisitCount.TryGetValue(activity.Id, out var count) ? count : 0;

    public void RegisterConnectionVisit(Connection connection, bool followed)
    {
        string connectionId = GetConnectionId(connection);
        _connectionVisitCount.TryAdd(connectionId, 0);
        _connectionVisitCount[connectionId]++;
        _connectionLastVisitFollowed[connectionId] = followed;
    }

    public long GetConnectionVisitCount(Connection connection) => _connectionVisitCount.TryGetValue(GetConnectionId(connection), out var count) ? count : 0;
    public bool GetConnectionLastVisitFollowed(Connection connection) => _connectionLastVisitFollowed.TryGetValue(GetConnectionId(connection), out var followed) ? followed : false;

    private static string GetConnectionId(Connection connection)
    {
        return $"{connection.Source.Activity.Id}{(string.IsNullOrEmpty(connection.Source.Port) ? "" : $":{connection.Source.Port}")} -> " +
               $"{connection.Target.Activity.Id}{(string.IsNullOrEmpty(connection.Target.Port) ? "" : $":{connection.Target.Port}")}";
    }
}
