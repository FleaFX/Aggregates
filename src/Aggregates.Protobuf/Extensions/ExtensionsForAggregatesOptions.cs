// ReSharper disable CheckNamespace

using Aggregates.Configuration;
using Aggregates.Types;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Aggregates.Protobuf;

public static class ExtensionsForAggregatesOptions {
    /// <summary>
    /// Completes the Aggregates event sourcing infrastructure with serialization logic using ProtoBuf.
    /// </summary>
    /// <param name="options">The <see cref="AggregatesOptions"/> to configure.</param>
    public static AggregatesOptions UseProtobuf(this AggregatesOptions options) {
        // shim for DateTimeOffset, cref. https://stackoverflow.com/questions/7046506/can-i-serialize-arbitrary-types-with-protobuf-net/7046868#7046868
        RuntimeTypeModel.Default.Add<DateTimeOffset>(false).SetSurrogate(typeof(DateTimeOffsetSurrogate));

        return options.AddConfiguration(services => {
            services.TryAddTransient<SerializerDelegate>(_ => Serializer.Serialize);
            services.TryAddTransient<DeserializerDelegate>(_ => (source, target) => Serializer.Deserialize(target, source));
        });
    }
}