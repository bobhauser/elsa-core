﻿using System.Text.Json;
using Elsa.Connections.Attributes;
using Elsa.Connections.Models;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Pipelines.ActivityExecution;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Elsa.Connections.Core.Extensions;
using Elsa.Connections.Converters;
using Elsa.Connections.Persistence.Contracts;

namespace Elsa.Connections.Middleware;

/// <summary>
/// An activity execution middleware component that inject connection property from Connection Name.
/// </summary>
[UsedImplicitly]
public class ConnectionMiddleware(ActivityMiddlewareDelegate next
    , IConnectionStore connectionStore
    ,ILogger<ConnectionMiddleware> logger) 
    : IActivityExecutionMiddleware
{
    private static JsonSerializerOptions? _serializerOptions;
    private readonly ILogger<ConnectionMiddleware> _logger = logger;

    private static JsonSerializerOptions SerializerOptions =>
        _serializerOptions ??= new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }.WithConverters(new Int32Converter());

    /// <inheritdoc />
    public async ValueTask InvokeAsync(ActivityExecutionContext context)
    {
        var activityDescriptor = context.ActivityDescriptor;

        if (activityDescriptor.Attributes.Any(attr => attr.GetType() == typeof(ConnectionActivityAttribute)))
        {
            var inputDescriptors = activityDescriptor.Inputs.Where(x => x?.PropertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(ConnectionProperties<>));

            if (inputDescriptors != null && inputDescriptors.Any())
            {
                var input = inputDescriptors.First();
                var propertyType = input?.PropertyInfo?.PropertyType.GetGenericArguments()[0];

                dynamic inputValue = input.ValueGetter(context.Activity);

                var connectionName = (string)inputValue?.ConnectionName;

                if (connectionName == null)
                    LogConnectionExtensions.LogConnectionIsNull(_logger);
                else
                {
                    //Get connection from store, if exist,
                    var connectionConfiguration = await connectionStore.FindAsync(new Persistence.Filters.ConnectionDefinitionFilter() { Name = connectionName });
                    if (connectionConfiguration != null)
                    {
                        dynamic deserializedjson =
                            JsonSerializer.Deserialize(connectionConfiguration.ConnectionConfiguration, propertyType, SerializerOptions);

                        inputValue.Properties = deserializedjson;

                        input.ValueSetter(context.Activity, inputValue);
                    }
                    else
                        LogConnectionExtensions.LogConnectionNotFound(_logger, connectionName);
                }
            }
        }

        await next(context);
    }

    private static bool IsActivityBookmarked(ActivityExecutionContext context)
    {
        return context.WorkflowExecutionContext.Bookmarks.Any(b => b.ActivityNodeId.Equals(context.ActivityNode.NodeId));
    }
}
