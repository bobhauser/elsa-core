using Elsa.Workflows.Activities.Flowchart.Models;

namespace Elsa.Workflows.Activities.Flowchart.Contracts;
public interface IFlowchartScheduler
{
    ValueTask ScheduleOutboundActivitiesAsync(ActivityExecutionContext flowchartContext, ActivityExecutionContext completedActivityContext, Outcomes outcomes, ActivityCompletionCallback? completionCallback);
    bool CanWaitAllProceed(ActivityExecutionContext context);
}
