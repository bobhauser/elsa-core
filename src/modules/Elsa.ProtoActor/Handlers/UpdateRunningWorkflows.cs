using Elsa.Extensions;
using Elsa.Mediator.Contracts;
using Elsa.ProtoActor.Extensions;
using Elsa.ProtoActor.Grains;
using Elsa.ProtoActor.Protos;
using Elsa.Workflows.Core.Notifications;
using Proto.Cluster;
using WorkflowStatus = Elsa.Workflows.Core.WorkflowStatus;

namespace Elsa.ProtoActor.Handlers;

/// <summary>
/// Updates the <see cref="RunningWorkflowsGrain"/> with running workflow instances.
/// </summary>
internal class UpdateRunningWorkflowsHandler : INotificationHandler<WorkflowExecuted>
{
    private readonly Cluster _cluster;

    public UpdateRunningWorkflowsHandler(Cluster cluster)
    {
        _cluster = cluster;
    }

    public async Task HandleAsync(WorkflowExecuted notification, CancellationToken cancellationToken)
    {
        var client = _cluster.GetNamedRunningWorkflowsGrain();
        var workflowState = notification.WorkflowState;

        if (workflowState.Status == WorkflowStatus.Running)
        {
            var registerRequest = new RegisterRunningWorkflowRequest
            {
                DefinitionId = workflowState.DefinitionId,
                Version = workflowState.DefinitionVersion,
                CorrelationId = workflowState.CorrelationId.EmptyIfNull(),
                InstanceId = workflowState.Id
            };
            
            await client.Register(registerRequest, cancellationToken);
        }
        else
        {
            var unregisterRequest = new UnregisterRunningWorkflowRequest
            {
                InstanceId = workflowState.Id
            };
            
            await client.Unregister(unregisterRequest, cancellationToken);
        }
    }
}