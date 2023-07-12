// ReSharper disable CheckNamespace

using Aggregates.Configuration;
using Aggregates.Types;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;

namespace Aggregates.Json;

public static class ExtensionsForAggregateOptions {
    /// <summary>
    /// Completes the Aggregates event sourcing infrastructure with serialization logic using Json.
    /// </summary>
    /// <param name="options">The <see cref="AggregatesOptions"/> to configure.</param>
    /// <param name="jsonSerializerOptions">An optional <see cref="JsonSerializerOptions"/> to use.</param>
    public static void UseJson(this AggregatesOptions options, JsonSerializerOptions? jsonSerializerOptions = null) {
        options.AddConfiguration(services => {
            services.TryAddTransient<SerializerDelegate>(_ => (destination, @event) => JsonSerializer.Serialize(destination, @event, @event.GetType(), jsonSerializerOptions));
            services.TryAddTransient<DeserializerDelegate>(_ => (source, target) => JsonSerializer.Deserialize(source, target, jsonSerializerOptions)!);
        });
    }
}