using Elsa.Workflows.Activities.Flowchart.Contracts;
using Elsa.Workflows.Activities.Flowchart.Extensions;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Options;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Activities.Flowchart.Services;

public class DefaultFlowchartScheduler : IFlowchartScheduler
{
    public async ValueTask ScheduleOutboundActivitiesAsync(ActivityExecutionContext flowchartContext, ActivityExecutionContext completedActivityContext, Outcomes outcomes, ActivityCompletionCallback? completionCallback)
    {
        var flowchart = (Activities.Flowchart)flowchartContext.Activity;
        var completedActivity = completedActivityContext.Activity;
        var outboundConnections = flowchart.Connections.MatchingOutboundConnections(completedActivity, outcomes).ToList();
        var children = outboundConnections.Select(x => x.Target.Activity).ToList();
        var scope = flowchartContext.GetProperty(Activities.Flowchart.ScopeProperty, () => new FlowScope());

        scope.RegisterActivityExecution(completedActivity);

        if (children.Any())
        {
            // Schedule each child, but only if all of its left inbound activities have already executed.
            foreach (var activity in children)
            {
                var existingActivity = scope.ContainsActivity(activity);
                scope.AddActivity(activity);

                var inboundActivities = flowchart.Connections.LeftInboundActivities(activity).ToList();

                // If the completed activity is not part of the left inbound path, always allow its children to be scheduled.
                if (!inboundActivities.Contains(completedActivity))
                {
                    await flowchartContext.ScheduleActivityAsync(activity, completionCallback);
                    continue;
                }

                // If the activity is anything but a join activity, only schedule it if all of its left-inbound activities have executed, effectively implementing a "wait all" join. 
                if (activity is not IJoinNode)
                {
                    if (CanWaitAllProceed(activity, inboundActivities, scope))
                    {
                        await flowchartContext.ScheduleActivityAsync(activity, completionCallback);
                    }
                }
                else
                {
                    // Select an existing activity execution context for this activity, if any.
                    var joinContext = flowchartContext.WorkflowExecutionContext.ActivityExecutionContexts.FirstOrDefault(x =>x.ParentActivityExecutionContext == flowchartContext && x.Activity == activity);
                    var scheduleWorkOptions = new ScheduleWorkOptions
                    {
                        CompletionCallback = completionCallback,
                        ExistingActivityExecutionContext = joinContext,
                        PreventDuplicateScheduling = true
                    };

                    var logger = flowchartContext.GetRequiredService<ILogger<DefaultFlowchartScheduler>>();
                    if (joinContext != null)
                        logger.LogDebug("Next activity {ChildActivityId} is a join activity. Attaching to existing join context {JoinContext}", activity.Id, joinContext.Id);
                    else if (!existingActivity)
                        logger.LogDebug("Next activity {ChildActivityId} is a join activity. Creating new join context", activity.Id);
                    else
                    {
                        logger.LogDebug("Next activity {ChildActivityId} is a join activity. Join context was not found, but activity is already being created", activity.Id);
                        continue;
                    }

                    await flowchartContext.ScheduleActivityAsync(activity, scheduleWorkOptions);
                }
            }
        }
    }

    public bool CanWaitAllProceed(ActivityExecutionContext context)
    {
        var flowchartContext = context.ParentActivityExecutionContext!;
        var flowchart = (Activities.Flowchart)flowchartContext.Activity;
        var activity = context.Activity;
        var leftIncomingActivities = flowchart.Connections.LeftInboundActivities(activity).ToList();
        var scope = flowchartContext.GetProperty(Activities.Flowchart.ScopeProperty, () => new FlowScope());

        return CanWaitAllProceed(activity, leftIncomingActivities, scope);
    }

    private bool CanWaitAllProceed(IActivity activity, List<IActivity> leftIncomingActivities, FlowScope scope)
    {
        var executionCount = scope.GetExecutionCount(activity);
        var haveAllInboundActivitiesExecuted = leftIncomingActivities.All(x => scope.GetExecutionCount(x) > executionCount);
        return haveAllInboundActivitiesExecuted;
    }
}
