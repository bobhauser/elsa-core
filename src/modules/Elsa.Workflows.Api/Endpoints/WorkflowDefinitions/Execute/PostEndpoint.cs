using System.Text.Json;
using Elsa.Abstractions;
using Elsa.Workflows.Management;
using Elsa.Workflows.Runtime;
using JetBrains.Annotations;

namespace Elsa.Workflows.Api.Endpoints.WorkflowDefinitions.Execute;

/// <summary>
/// An API endpoint that executes a given workflow definition through POST method.
/// </summary>
[PublicAPI]
internal class PostEndpoint(
    IWorkflowDefinitionService workflowDefinitionService,
    IWorkflowRuntime workflowRuntime,
    IWorkflowStarter workflowStarter,
    IApiSerializer apiSerializer)
    : ElsaEndpointWithoutRequest<Response>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Routes("/workflow-definitions/{definitionId}/execute");
        ConfigurePermissions("exec:workflow-definitions");
        Verbs(FastEndpoints.Http.POST);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        PostRequest? request = null;

        if (HttpContext.Request.ContentType?.Contains("application/json") ?? false)
        {
            using var reader = new StreamReader(HttpContext.Request.Body);
            var body = await reader.ReadToEndAsync();

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    request = JsonSerializer.Deserialize<PostRequest>(body, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch
                {
                    AddError("Invalid request body.");
                }
            }
        }

        request ??= new();
        
        var definitionId = Route<string>("definitionId");
        
        if (string.IsNullOrWhiteSpace(definitionId))
            AddError("Missing workflow definition ID.");
        else
            request.DefinitionId = definitionId;
        
        if (ValidationFailed)
        {
            await SendErrorsAsync(cancellation: cancellationToken);
            return;
        }
        
        await WorkflowExecutionHelper.ExecuteWorkflowAsync(
            request,
            workflowDefinitionService,
            workflowRuntime,
            workflowStarter,
            apiSerializer,
            HttpContext,
            cancellationToken);
    }
}