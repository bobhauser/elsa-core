using System.ComponentModel;
using System.Runtime.CompilerServices;
using Elsa.Extensions;
using Elsa.Workflows.Activities.Flowchart.Contracts;
using Elsa.Workflows.Activities.Flowchart.Extensions;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Options;
using Elsa.Workflows.Signals;

namespace Elsa.Workflows.Activities.Flowchart.Activities;

/// <summary>
/// A flowchart consists of a collection of activities and connections between them.
/// </summary>
[Activity("Elsa", "Flow", "A flowchart is a collection of activities and connections between them.")]
[Browsable(false)]
public class Flowchart : Container
{
    public const string ScopeProperty = "Scope";

    /// <inheritdoc />
    public Flowchart([CallerFilePath] string? source = default, [CallerLineNumber] int? line = default) : base(source, line)
    {
        OnSignalReceived<ScheduleActivityOutcomes>(OnScheduleOutcomesAsync);
        OnSignalReceived<ScheduleChildActivity>(OnScheduleChildActivityAsync);
        OnSignalReceived<CancelSignal>(OnActivityCanceledAsync);
    }

    /// <summary>
    /// The activity to execute when the flowchart starts.
    /// </summary>
    [Port] [Browsable(false)] public IActivity? Start { get; set; }

    /// <summary>
    /// A list of connections between activities.
    /// </summary>
    public ICollection<Connection> Connections { get; set; } = new List<Connection>();

    /// <inheritdoc />
    protected override async ValueTask ScheduleChildrenAsync(ActivityExecutionContext context)
    {
        var startActivity = GetStartActivity(context);

        if (startActivity == null)
        {
            // Nothing else to execute.
            await context.CompleteActivityAsync();
            return;
        }

        // Schedule the start activity.
        await context.ScheduleActivityAsync(startActivity, OnChildCompletedAsync);
    }

    private IActivity? GetStartActivity(ActivityExecutionContext context)
    {
        // If there's a trigger that triggered this workflow, use that.
        var triggerActivityId = context.WorkflowExecutionContext.TriggerActivityId;
        var triggerActivity = triggerActivityId != null ? Activities.FirstOrDefault(x => x.Id == triggerActivityId) : default;

        if (triggerActivity != null)
            return triggerActivity;

        // If an explicit Start activity was provided, use that.
        if (Start != null)
            return Start;

        // If there is a Start activity on the flowchart, use that.
        var startActivity = Activities.FirstOrDefault(x => x is Start);

        if (startActivity != null)
            return startActivity;

        // If there's an activity marked as "Can Start Workflow", use that.
        var canStartWorkflowActivity = Activities.FirstOrDefault(x => x.GetCanStartWorkflow());

        if (canStartWorkflowActivity != null)
            return canStartWorkflowActivity;

        // If there is a single activity that has no inbound connections, use that.
        var root = GetRootActivity();

        if (root != null)
            return root;

        // If no start activity found, return the first activity.
        return Activities.FirstOrDefault();
    }

    /// <summary>
    /// Checks if there is any pending work for the flowchart.
    /// </summary>
    private bool HasPendingWork(ActivityExecutionContext context)
    {
        var workflowExecutionContext = context.WorkflowExecutionContext;
        var activityIds = Activities.Select(x => x.Id).ToList();
        var children = context.Children;
        var hasRunningActivityInstances = children.Where(x => activityIds.Contains(x.Activity.Id)).Any(x => x.Status == ActivityStatus.Running);

        var hasPendingWork = workflowExecutionContext.Scheduler.List().Any(workItem =>
        {
            var ownerInstanceId = workItem.Owner?.Id;

            if (ownerInstanceId == null)
                return false;

            if (ownerInstanceId == context.Id)
                return true;

            var ownerContext = context.WorkflowExecutionContext.ActivityExecutionContexts.First(x => x.Id == ownerInstanceId);
            var ancestors = ownerContext.GetAncestors().ToList();

            return ancestors.Any(x => x == context);
        });

        return hasRunningActivityInstances || hasPendingWork;
    }

    private IActivity? GetRootActivity()
    {
        // Get the first activity that has no inbound connections.
        var query =
            from activity in Activities
            let inboundConnections = Connections.Any(x => x.Target.Activity == activity)
            where !inboundConnections
            select activity;

        var rootActivity = query.FirstOrDefault();
        return rootActivity;
    }

    private async ValueTask OnChildCompletedAsync(ActivityCompletedContext context)
    {
        var flowchartContext = context.TargetContext;
        var completedActivityContext = context.ChildContext;
        var completedActivity = completedActivityContext.Activity;
        var result = context.Result;

        if (flowchartContext.Activity != this)
        {
            throw new Exception("Target context activity must be this flowchart");
        }

        // If the completed activity's status is anything but "Completed", do not schedule its outbound activities.
        if (completedActivityContext.Status != ActivityStatus.Completed)
        {
            return;
        }

        // If the complete activity is a terminal node, complete the flowchart immediately.
        if (completedActivity is ITerminalNode)
        {
            await flowchartContext.CompleteActivityAsync();
            return;
        }

        // Determine the outcomes from the completed activity
        var outcomes = result is Outcomes o ? o : Outcomes.Default;

        // Schedule the outbound activities
        var flowchartHandler = context.GetRequiredService<IFlowchartScheduler>();
        await flowchartHandler.ScheduleOutboundActivitiesAsync(flowchartContext, completedActivityContext, outcomes, OnChildCompletedAsync);

        // If there are not any outbound connections, complete the flowchart activity if there is no other pending work
        if (!Connections.MatchingOutboundConnections(completedActivity, outcomes).Any())
        {
            await CompleteIfNoPendingWorkAsync(flowchartContext);
        }
    }

    private async Task CompleteIfNoPendingWorkAsync(ActivityExecutionContext context)
    {
        var hasPendingWork = HasPendingWork(context);

        if (!hasPendingWork)
        {
            var hasFaultedActivities = context.Children.Any(x => x.Status == ActivityStatus.Faulted);

            if (!hasFaultedActivities)
            {
                await context.CompleteActivityAsync();
            }
        }
    }

    private async ValueTask OnScheduleOutcomesAsync(ScheduleActivityOutcomes signal, SignalContext context)
    {
        var flowchartContext = context.ReceiverActivityExecutionContext;
        var schedulingActivityContext = context.SenderActivityExecutionContext;
        var schedulingActivity = schedulingActivityContext.Activity;
        var outcomes = signal.Outcomes;
        var outboundConnections = Connections.Where(connection => connection.Source.Activity == schedulingActivity && outcomes.Contains(connection.Source.Port!)).ToList();
        var outboundActivities = outboundConnections.Select(x => x.Target.Activity).ToList();

        if (outboundActivities.Any())
        {
            // Schedule each child.
            foreach (var activity in outboundActivities) await flowchartContext.ScheduleActivityAsync(activity, OnChildCompletedAsync);
        }
    }

    private async ValueTask OnScheduleChildActivityAsync(ScheduleChildActivity signal, SignalContext context)
    {
        var flowchartContext = context.ReceiverActivityExecutionContext;
        var activity = signal.Activity;
        var activityExecutionContext = signal.ActivityExecutionContext;

        if (activityExecutionContext != null)
        {
            await flowchartContext.ScheduleActivityAsync(activityExecutionContext.Activity, new ScheduleWorkOptions
            {
                ExistingActivityExecutionContext = activityExecutionContext,
                CompletionCallback = OnChildCompletedAsync,
                Input = signal.Input
            });
        }
        else
        {
            await flowchartContext.ScheduleActivityAsync(activity, new ScheduleWorkOptions
            {
                CompletionCallback = OnChildCompletedAsync,
                Input = signal.Input
            });
        }
    }

    private async ValueTask OnActivityCanceledAsync(CancelSignal signal, SignalContext context)
    {
        await CompleteIfNoPendingWorkAsync(context.ReceiverActivityExecutionContext);
    }
}