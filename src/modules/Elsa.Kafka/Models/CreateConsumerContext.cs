namespace Elsa.Kafka;

public record CreateConsumerContext(ConsumerDefinition ConsumerDefinition, SchemaRegistryDefinition? SchemaRegistryDefinition);