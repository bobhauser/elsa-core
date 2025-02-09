using System.ComponentModel;
using Elsa.Extensions;
using Elsa.Features.Abstractions;
using Elsa.Features.Services;
using Elsa.Workflows.Activities.Flowchart.Contracts;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Activities.Flowchart.Serialization;
using Elsa.Workflows.Activities.Flowchart.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Features;

/// <summary>
/// Adds support for the Flowchart activity.
/// </summary>
public class FlowchartFeature : FeatureBase
{
    /// <inheritdoc />
    public FlowchartFeature(IModule module) : base(module)
    {
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services.AddSerializationOptionsConfigurator<FlowchartSerializationOptionConfigurator>();
        Services.AddScoped<IFlowchartScheduler, DefaultFlowchartScheduler>();
    }

    public override void Configure()
    {
        Module.AddTypeAlias<FlowScope>("FlowScope");
    }
}