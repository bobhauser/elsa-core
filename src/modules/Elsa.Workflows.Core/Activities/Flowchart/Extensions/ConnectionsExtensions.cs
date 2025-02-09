using Elsa.Workflows.Activities.Flowchart.Models;

namespace Elsa.Workflows.Activities.Flowchart.Extensions;

/// <summary>
/// Contains extension methods for <see cref="ICollection{Connection}"/>.
/// </summary>
public static class ConnectionsExtensions
{
    /// <summary>
    /// Returns all connections that are descendants of the specified parent activity.
    /// </summary>
    public static IEnumerable<Connection> Descendants(this ICollection<Connection> connections, IActivity parent)
    {
        var visitedConnections = new HashSet<Connection>();
        return connections.Descendants(parent, visitedConnections);
    }

    /// <summary>
    /// Returns all ancestor connections of the specified parent activity.
    /// </summary>
    public static IEnumerable<Connection> Ancestors(this ICollection<Connection> connections, IActivity activity)
    {
        var visitedActivities = new HashSet<IActivity>();
        return connections.Ancestors(activity, visitedActivities);
    }

    /// <summary>
    /// Returns all inbound connections of the specified activity.
    /// </summary>
    public static IEnumerable<Connection> InboundConnections(this ICollection<Connection> connections, IActivity activity) => connections.Where(x => x.Target.Activity == activity).ToList();

    /// <summary>
    /// Returns all "left" inbound connections of the specified activity. "Left" means "not a descendant of the activity".
    /// </summary>
    public static IEnumerable<Connection> LeftInboundConnections(this ICollection<Connection> connections, IActivity activity)
    {
        // We only take "left" inbound connections, which means we exclude descendent connections looping back. 
        var descendantConnections = connections.Descendants(activity).ToList();
        var filteredConnections = connections.InboundConnections(activity).Except(descendantConnections).ToList();

        return filteredConnections;
    }

    /// <summary>
    /// Returns all "left" ancestor connections of the specified activity. "Left" means "not a descendant of the activity".
    /// </summary>
    public static IEnumerable<Connection> LeftAncestorConnections(this ICollection<Connection> connections, IActivity activity)
    {
        // We only take "left" inbound connections, which means we exclude descendent connections looping back. 
        var descendantConnections = connections.Descendants(activity).ToList();
        var filteredConnections = connections.Ancestors(activity).Except(descendantConnections).ToList();

        return filteredConnections;
    }

    /// <summary>
    /// Returns all outbound connections of the specified activity.
    /// </summary>
    public static IEnumerable<Connection> OutboundConnections(this ICollection<Connection> connections, IActivity activity) => connections.Where(x => x.Source.Activity == activity).ToList();

    /// <summary>
    /// Returns all outbound connections of the specified activity which are to be followed based on matching outcome names
    /// </summary>
    public static IEnumerable<Connection> MatchingOutboundConnections(this ICollection<Connection> connections, IActivity activity, Outcomes? outcomes = null)
    {
        outcomes ??= Outcomes.Default;
        return connections.OutboundConnections(activity).Where(c => outcomes.Names.Contains(c.Source.Port));
    }

    /// <summary>
    /// Returns all inbound activities of the specified activity.
    /// </summary>
    public static IEnumerable<IActivity> InboundActivities(this ICollection<Connection> connections, IActivity activity) => connections.InboundConnections(activity).Select(x => x.Source.Activity);

    /// <summary>
    /// Returns all "left" inbound activities of the specified activity. "Left" means "not a descendant of the activity".
    /// </summary>
    public static IEnumerable<IActivity> LeftInboundActivities(this ICollection<Connection> connections, IActivity activity) => connections.LeftInboundConnections(activity).Select(x => x.Source.Activity);

    /// <summary>
    /// Returns all "left" ancestor activities of the specified activity. "Left" means "not a descendant of the activity".
    /// </summary>
    public static IEnumerable<IActivity> LeftAncestorActivities(this ICollection<Connection> connections, IActivity activity) => connections.LeftAncestorConnections(activity).Select(x => x.Source.Activity);

    private static IEnumerable<Connection> Descendants(this ICollection<Connection> connections, IActivity parent, ISet<Connection> visitedConnections)
    {
        var children = connections.Where(x => parent == x.Source.Activity && !visitedConnections.Contains(x)).ToList();

        foreach (var child in children)
        {
            visitedConnections.Add(child);
            yield return child;

            var descendants = connections.Descendants(child.Target.Activity, visitedConnections).ToList();

            foreach (var descendant in descendants)
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<Connection> Ancestors(this ICollection<Connection> connections, IActivity activity, ISet<IActivity> visitedActivities)
    {
        var parents = connections.Where(x => activity == x.Target.Activity && !visitedActivities.Contains(x.Source.Activity)).ToList();

        foreach (var parent in parents)
        {
            visitedActivities.Add(parent.Source.Activity);
            yield return parent;

            var ancestors = connections.Ancestors(parent.Source.Activity, visitedActivities).ToList();

            foreach (var ancestor in ancestors)
            {
                yield return ancestor;
            }
        }
    }

    /// <summary>
    /// Returns inbound connections of the specified activity that have a path back to the start activity without looping. This is similar to LeftInboundActivities 
    /// but ensures that the connections are only on paths between the startActivity and activity without looping through activity. It also excludes connections from
    /// dangling activities which will never be executed (since there is not path to it from startActivity)
    /// </summary>
    public static IEnumerable<Connection> FromStartInboundConnections(this ICollection<Connection> connections, IActivity startActivity, IActivity activity)
    {
        // Find all inbound connections of the activity
        var inboundConnections = connections.InboundConnections(activity);

        // Filter connections where the source activity has a path back to start
        return inboundConnections.Where(conn => connections.HasPathBackToStart(startActivity, conn.Source.Activity, activity)).ToList();
    }

    /// <summary>
    /// Determines if an activity has a path back to the start activity without looping through a specific activity.
    /// </summary>
    private static bool HasPathBackToStart(this ICollection<Connection> connections, IActivity startActivity, IActivity activity, IActivity avoidActivity)
    {
        if (activity == startActivity)
            return true;

        var visited = new HashSet<IActivity> { avoidActivity }; // Mark avoidActivity as visited to prevent looping
        var stack = new Stack<IActivity>();
        stack.Push(activity);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (current == startActivity)
                return true; // Found a valid path back

            if (visited.Contains(current))
                continue;

            visited.Add(current);

            foreach (var parent in connections.InboundActivities(current))
            {
                if (!visited.Contains(parent))
                    stack.Push(parent);
            }
        }

        return false; // No valid path back found
    }
}