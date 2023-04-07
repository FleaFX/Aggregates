// ReSharper disable CheckNamespace

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Json; 

public static class ExtensionsForAggregateOptions {
    /// <summary>
    /// Completes the Aggregates event sourcing infrastructure with serialization logic using Json.
    /// </summary>
    /// <param name="options">The <see cref="AggregatesOptions"/> to configure.</param>
    public static void UseJson(this AggregatesOptions options) {
        options.AddConfiguration(services => {
            services.TryAddTransient<EventSerializerDelegate>(_ => (destination, @event) => JsonSerializer.Serialize(destination, @event, @event.GetType()));
            services.TryAddTransient<EventDeserializerDelegate>(_ => (source, target) => JsonSerializer.Deserialize(source, target)!);
        });
    }
}