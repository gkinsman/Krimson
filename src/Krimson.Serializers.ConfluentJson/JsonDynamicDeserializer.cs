using System.Collections.Concurrent;
using System.Runtime.Serialization;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Krimson.SchemaRegistry;
using Krimson.Serializers.ConfluentJson.NJsonSchema;
using Newtonsoft.Json;
using NJsonSchema.Generation;
using static Confluent.Kafka.Deserializers;

namespace Krimson.Serializers.ConfluentJson;

[PublicAPI]
public class JsonDynamicDeserializer : IDynamicDeserializer {
    static readonly Type ConfluentDeserializerType = typeof(JsonDeserializer<>);

    public static readonly JsonDeserializerConfig DefaultConfig = new();

    public JsonDynamicDeserializer(ISchemaRegistryClient registryClient, JsonDeserializerConfig config, JsonSchemaGeneratorSettings generatorSettings, Func<MessageSchema, Type>? resolveMessageType = null) {
        ResolveMessageType = resolveMessageType ?? (schema => {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(a => a.GetType(schema.ClrTypeName))
                .FirstOrDefault(x => x is not null)!;
        });

        Deserializers = new();

        var args = (
            Config: config,
            GeneratorSettings: generatorSettings
        );
        
        GetDeserializer = messageType => Deserializers.GetOrAdd(
            messageType,
            static (type, ctx) => Activator.CreateInstance(
                ConfluentDeserializerType.MakeGenericType(type),
                ctx.Config, ctx.GeneratorSettings
            )!, 
            args
        );
        
        GetMessageSchema = registryClient.GetJsonMessageSchema; 
    }

    public JsonDynamicDeserializer(ISchemaRegistryClient registryClient, JsonSchemaGeneratorSettings generatorSettings)
        : this(registryClient, DefaultConfig, generatorSettings) { }

    public JsonDynamicDeserializer(ISchemaRegistryClient registryClient, JsonSerializerSettings serializerSettings)
        : this(registryClient, DefaultConfig, new JsonSchemaGeneratorSettings().ConfigureNewtonsoftJson(serializerSettings)) { }

    Func<ReadOnlyMemory<byte>, MessageSchema> GetMessageSchema   { get; }
    Func<MessageSchema, Type>                 ResolveMessageType { get; }
    Func<Type, dynamic>                       GetDeserializer    { get; }
    ConcurrentDictionary<Type, dynamic>       Deserializers      { get; }

    public async Task<object?> DeserializeAsync(ReadOnlyMemory<byte> data, bool isNull, SerializationContext context) {
        // bypass if message is bytes
        if (context.Headers.IsBytesMessage()) {
            return isNull || data.IsEmpty
                ? Array.Empty<byte>()
                : ByteArray.Deserialize(data.ToArray(), isNull, context);
        }

        if (isNull || data.IsEmpty)
            return null;

        try {
            var messageSchema = GetMessageSchema(data);
            var messageType   = ResolveMessageType(messageSchema);
            var deserializer  = GetDeserializer(messageType);

            var message = await deserializer
                .DeserializeAsync(data, isNull, context)
                .ConfigureAwait(false);

            return message;
        }
        catch (Exception ex) {
            throw new SerializationException("Failed to deserialize json message!", ex);
        }
    }

    public object? Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context) =>
        DeserializeAsync(new(data.ToArray()), isNull, context).ConfigureAwait(false).GetAwaiter().GetResult();
}