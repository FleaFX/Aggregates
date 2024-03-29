﻿// ReSharper disable CheckNamespace

using Aggregates.Configuration;
using Aggregates.EventStoreDB.Serialization;
using Aggregates.Types;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.EventStoreDB;

public static class ExtensionsForAggregatesOptions {
    /// <summary>
    /// Completes the Aggregates event sourcing infrastructure with a connection to EventStoreDB.
    /// </summary>
    /// <param name="options">The <see cref="AggregatesOptions"/> to configure.</param>
    /// <param name="connectionString">The connection string to use when connecting to EventStoreDB.</param>
    public static void UseEventStoreDB(this AggregatesOptions options, string connectionString) {
        options.AddConfiguration(services => {
            services.TryAddSingleton(_ =>
                new EventStoreClient(EventStoreClientSettings.Create(connectionString))
            );

            services.TryAddScoped(typeof(IRepository<,>), typeof(EventStoreDbRepository<,>));
            services.TryAddScoped(typeof(ResolvedEventDeserializer));
            services.TryAddScoped(typeof(MetadataDeserializer));
            services.TryAddScoped(sp =>
                EventStoreDbCommitDelegate.CreateEntityDelegate(
                    sp.GetRequiredService<EventStoreClient>(),
                    EventStoreDbSerialization.CreateEntitySerializer(sp.GetRequiredService<SerializerDelegate>())
                )
            );
        });
    }
}