using Elsa.Extensions;
using Elsa.Workflows.Activities.Flowchart.Contracts;
using Elsa.Workflows.Activities.Flowchart.Extensions;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Options;

namespace Elsa.Workflows.Activities.Flowchart.Services;

public class ConnectionBasedFlowchartScheduler : IFlowchartScheduler
{
    private const string FlowchartSchedulerStateProperty = "FlowchartSchedulerState";
    private const string LoopbackActivityInput = "Loopback";

    public async ValueTask ScheduleOutboundActivitiesAsync(ActivityExecutionContext flowchartContext, ActivityExecutionContext completedActivityContext, Outcomes outcomes, ActivityCompletionCallback? completionCallback)
    {
        var flowchart = (Activities.Flowchart)flowchartContext.Activity;
        var completedActivity = completedActivityContext.Activity;
        var flowchartSchedulerState = flowchartContext.GetProperty(FlowchartSchedulerStateProperty, () => new FlowchartSchedulerState());
        var completedActivityWasLoopback = completedActivityContext.ActivityInput.GetValueOrDefault<bool>(LoopbackActivityInput);
        await ScheduleOutboundActivitiesAsync(flowchart, flowchartContext, completedActivity, outcomes, flowchartSchedulerState, completedActivityWasLoopback, completionCallback);
    }

    private static async ValueTask ScheduleOutboundActivitiesAsync(Activities.Flowchart flowchart, ActivityExecutionContext flowchartContext, IActivity activity, Outcomes outcomes, FlowchartSchedulerState flowchartSchedulerState, bool completedActivityWasLoopback, ActivityCompletionCallback? completionCallback)
    {
        // Register the activity as being visited (unless the completed activity was scheduled by a loopback)
        if (!completedActivityWasLoopback)
        {
            flowchartSchedulerState.RegisterActivityVisit(activity);
        }

        var startActivity = flowchart.Activities.First(a => a.Id == flowchartSchedulerState.StartActivityId);
        var outboundConnections = flowchart.Connections.OutboundConnections(activity).ToList();

        // update all outbound connections with whether the connection was followed or skipped
        foreach (var outboundConnection in outboundConnections)
        {
            bool connectionFollowed = outcomes.Names.Contains(outboundConnection.Source.Port);
            flowchartSchedulerState.RegisterConnectionVisit(outboundConnection, connectionFollowed);

            var outboundActivity = outboundConnection.Target.Activity;
            var outboundActivityFromStartInboundConnections = flowchart.Connections.FromStartInboundConnections(startActivity, outboundActivity).ToList();

            // If the connection is a loopback (the connection is not one of the outbound activities "from start" inbound connections) then always schedule it if the connection was followed
            var isLoopback = !outboundActivityFromStartInboundConnections.Contains(outboundConnection);
            if (isLoopback)
            {
                if (connectionFollowed)
                {
                    // Pass a flag indicating that this is a loopback so that we don't "visit" the activity when the scheduled activity is completed
                    // as this will mess with the WaitAll behavior for regular inbound connections 
                    var scheduleWorkOptions = new ScheduleWorkOptions
                    {
                        CompletionCallback = completionCallback,
                        Input = new Dictionary<string, object>() { { LoopbackActivityInput, true } }
                    };

                    await flowchartContext.ScheduleActivityAsync(outboundActivity, scheduleWorkOptions);
                }
            }

            // If the activity is anything but a join activity, only schedule it if all of its "from start" inbound connections have been visited (implicit Wait All join). 
            else if (outboundActivity is not IJoinNode)
            {
                // Wait until all "from start" inbound activities have been visited, either by being followed or skipped
                if (AllFromStartInboundActivitiesVisited(outboundActivity, outboundActivityFromStartInboundConnections, flowchartSchedulerState))
                {
                    var hasFollowedInboundConnection = outboundActivityFromStartInboundConnections.Any(c => flowchartSchedulerState.GetConnectionLastVisitFollowed(c));
                    if (hasFollowedInboundConnection)
                    {
                        // The outboundActivity has at least one followed inbound connection, so schedule the outboundActivity
                        await flowchartContext.ScheduleActivityAsync(outboundActivity, completionCallback);
                    }
                    else
                    {
                        // All inbound connections to the outboundActivity were skipped, so we want to mark the outboundActivity's outbound connections being visited, but
                        // skipped. Passing Outcomes.Empty will ensure that the outboundActivites outbound connections are registered as being visited, but skipped.
                        await ScheduleOutboundActivitiesAsync(flowchart, flowchartContext, outboundActivity, Outcomes.Empty, flowchartSchedulerState, completedActivityWasLoopback: false, completionCallback);
                    }
                }
            }

            // Delegate the scheduling to the Join activity
            else
            {
                var outboundActivityVisitCount = flowchartSchedulerState.GetActivityVisitCount(outboundActivity);
                var outgoingConnectionVisitCount = flowchartSchedulerState.GetConnectionVisitCount(outboundConnection);
                if (outgoingConnectionVisitCount <= outboundActivityVisitCount)
                {
                    // If the connection visit count is less than or equal to activity visit count it means that the Join was a JoinAny and the 
                    // Join activity has already completed. This connection should just be ignored.
                    continue;
                }

                // We only want to schedule the Join activity if at least one "from start" inbound connection was followed
                var hasFollowedInboundConnection = outboundActivityFromStartInboundConnections.Any(c => flowchartSchedulerState.GetConnectionLastVisitFollowed(c));
                if (!hasFollowedInboundConnection)
                {
                    if (AllFromStartInboundActivitiesVisited(outboundActivity, outboundActivityFromStartInboundConnections, flowchartSchedulerState))
                    {
                        // All inbound connections to the outboundActivity were skipped, so we want to mark the outboundActivity's outbound connections being visited, but
                        // skipped. Passing Outcomes.Empty will ensure that the outboundActivities outbound connections are registered as being visited, but skipped.
                        await ScheduleOutboundActivitiesAsync(flowchart, flowchartContext, outboundActivity, Outcomes.Empty, flowchartSchedulerState, completedActivityWasLoopback: false, completionCallback);
                    }
                    continue;
                }

                // Select an existing activity execution context for this activity, if any.
                var joinContext = flowchartContext.WorkflowExecutionContext.ActivityExecutionContexts.LastOrDefault(x =>
                    x.ParentActivityExecutionContext == flowchartContext &&
                    x.Activity == outboundActivity &&
                    x.Status is ActivityStatus.Pending or ActivityStatus.Running);

                // If there is not an existing joinContext, see if the activity has already been scheduled, and if so, do not schedule it again
                if (joinContext == null)
                {
                    var activityScheduled = flowchartContext.WorkflowExecutionContext.Scheduler.List().Any(workItem => workItem.Owner == flowchartContext && workItem.Activity == outboundActivity);
                    if (activityScheduled)
                    {
                        continue;
                    }
                }

                var scheduleWorkOptions = new ScheduleWorkOptions
                {
                    CompletionCallback = completionCallback,
                    ExistingActivityExecutionContext = joinContext
                };
                await flowchartContext.ScheduleActivityAsync(outboundActivity, scheduleWorkOptions);
            }
        }
    }

    public bool CanWaitAllProceed(ActivityExecutionContext context)
    {
        var flowchartContext = context.ParentActivityExecutionContext!;
        var flowchart = (Activities.Flowchart)flowchartContext.Activity;
        var activity = context.Activity;
        var flowchartSchedulerState = flowchartContext.GetProperty(FlowchartSchedulerStateProperty, () => new FlowchartSchedulerState());

        var startActivity = flowchart.Activities.First(a => a.Id == flowchartSchedulerState.StartActivityId);
        var fromStartInboundConnections = flowchart.Connections.FromStartInboundConnections(startActivity, activity).ToList();

        return AllFromStartInboundActivitiesVisited(activity, fromStartInboundConnections, flowchartSchedulerState);
    }

    private static bool AllFromStartInboundActivitiesVisited(IActivity activity, List<Connection> fromStartInboundConnections, FlowchartSchedulerState flowchartSchedulerState)
    {
        var outboundActivityVisitCount = flowchartSchedulerState.GetActivityVisitCount(activity);
        var minConnectionVisitCount = fromStartInboundConnections.Min(c => flowchartSchedulerState.GetConnectionVisitCount(c));
        return minConnectionVisitCount > outboundActivityVisitCount;
    }
}
