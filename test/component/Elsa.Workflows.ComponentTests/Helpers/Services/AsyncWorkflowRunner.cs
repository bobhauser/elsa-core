using Elsa.Testing.Shared;
using Elsa.Testing.Shared.Services;
using Elsa.Workflows.ComponentTests.Models;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Entities;
using Elsa.Workflows.Runtime.Messages;
using System.Collections.Concurrent;

namespace Elsa.Workflows.ComponentTests.Services;

/// <summary>
/// Provides functionality to execute workflows asynchronously and await their completion for testing purposes.
/// Tracks activity execution records and workflow completion signals.
/// </summary>
public class AsyncWorkflowRunner : IDisposable
{
    private readonly IWorkflowRuntime _workflowRuntime;
    private readonly IIdentityGenerator _identityGenerator;
    private readonly SignalManager _signalManager;
    private readonly WorkflowEvents _workflowEvents;
    private readonly ConcurrentDictionary<string, ActivityExecutionRecord> _activityExecutionRecords = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncWorkflowRunner"/> class.
    /// </summary>
    public AsyncWorkflowRunner(IWorkflowRuntime workflowRuntime, IIdentityGenerator identityGenerator, SignalManager signalManager, WorkflowEvents workflowEvents)
    {
        _workflowRuntime = workflowRuntime;
        _identityGenerator = identityGenerator;
        _signalManager = signalManager;
        _workflowEvents = workflowEvents;

        _workflowEvents.WorkflowStateCommitted += OnWorkflowStateCommitted;
        _workflowEvents.ActivityExecutedLogUpdated += OnActivityExecutedLogUpdated;
    }

    /// <summary>
    /// Runs the specified workflow definition asynchronously and waits for its completion.
    /// Returns the workflow execution context and activity execution records.
    /// </summary>
    /// <param name="workflowDefinitionHandle">The handle of the workflow definition to execute.</param>
    /// <returns>A <see cref="TestWorkflowExecutionResult"/> containing the workflow execution context and activity execution records.</returns>
    public async Task<TestWorkflowExecutionResult> RunAndAwaitWorkflowCompletionAsync(WorkflowDefinitionHandle workflowDefinitionHandle)
    {
        var workflowInstanceId = _identityGenerator.GenerateId();
        var workflowClient = await _workflowRuntime.CreateClientAsync(workflowInstanceId);
        await workflowClient.CreateInstanceAsync(new()
        {
            WorkflowDefinitionHandle = workflowDefinitionHandle
        });
        _activityExecutionRecords.Clear();
        await workflowClient.RunInstanceAsync(RunWorkflowInstanceRequest.Empty);
        var signalName = GetSignalName(workflowInstanceId);
        var workflowExecutionContext = await _signalManager.WaitAsync<WorkflowExecutionContext>(signalName);
        return new(workflowExecutionContext, _activityExecutionRecords.Values.ToList());
    }

    private void OnWorkflowStateCommitted(object? sender, WorkflowStateCommittedEventArgs e)
    {
        if (e.WorkflowExecutionContext.Status != WorkflowStatus.Finished)
            return;

        var signalName = GetSignalName(e.WorkflowExecutionContext.Id);
        _signalManager.Trigger(signalName, e.WorkflowExecutionContext);
    }
    
    private void OnActivityExecutedLogUpdated(object? sender, ActivityExecutedLogUpdatedEventArgs e)
    {
        foreach (var record in e.Records) 
            _activityExecutionRecords[record.Id] = record;
    }

    private static string GetSignalName(string workflowInstanceId) => $"WorkflowInstanceCompleted-{workflowInstanceId}";
    
    /// <summary>
    /// Unsubscribes from workflow events and releases resources.
    /// </summary>
    public void Dispose()
    {
        _workflowEvents.WorkflowStateCommitted -= OnWorkflowStateCommitted;
        _workflowEvents.ActivityExecutedLogUpdated -= OnActivityExecutedLogUpdated;
        GC.SuppressFinalize(this);
    }
}