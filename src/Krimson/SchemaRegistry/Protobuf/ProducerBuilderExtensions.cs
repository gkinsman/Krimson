using Confluent.SchemaRegistry.Serdes;
using Krimson.Producers;

namespace Krimson.SchemaRegistry.Protobuf;

public static class ProducerBuilderExtensions {
    public static KrimsonProducerBuilder UseProtobuf(this KrimsonProducerBuilder builder, Action<ProtobufSerializerConfig>? configureSerializer = null) =>
        builder.Serializer(registry => new ProtobufDynamicSerializer(registry, ProtobufDynamicSerializer.DefaultConfig.With(x => configureSerializer?.Invoke(x))));
}