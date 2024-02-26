// ReSharper disable CheckNamespace

using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;
using Aggregates.EventStoreDB.Workers;
using Aggregates.Types;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Aggregates.EventStoreDB;

public static class ExtensionsForReactionsOptions {
    /// <summary>
    /// Completes the Aggregates reaction infrastructure with a connection to EventStoreDB.
    /// </summary>
    /// <param name="options">The <see cref="ReactionsOptions"/> to configure.</param>
    /// <param name="connectionString">The connection string to use when connecting to EventStoreDB.</param>
    public static void UseEventStoreDB(this ReactionsOptions options, string connectionString) {
        options.AddConfiguration(services => {
            services.TryAddSingleton(_ =>
                new EventStorePersistentSubscriptionsClient(EventStoreClientSettings.Create(connectionString))
            );

            services.TryAddScoped<ListToAllAsyncDelegate>(sp => {
                return async (deadline, credentials, token) => {
                    // fix for EventStoreDB client; imho, ListToAllAsync doesn't play nice, throwing an exception instead of returning an empty list
                    try {
                        var @delegate = sp.GetRequiredService<EventStorePersistentSubscriptionsClient>().ListToAllAsync;
                        return await @delegate(deadline, credentials, token);
                    } catch (PersistentSubscriptionNotFoundException) {
                        return Enumerable.Empty<PersistentSubscriptionInfo>();
                    }
                };
            });
            services.TryAddScoped<CreateToAllAsyncDelegate>(sp => sp.GetRequiredService<EventStorePersistentSubscriptionsClient>().CreateToAllAsync);
            services.TryAddScoped<SubscribeToAllAsync>(sp => sp.GetRequiredService<EventStorePersistentSubscriptionsClient>().SubscribeToAllAsync);
            services.TryAddScoped(typeof(ResolvedEventDeserializer));
            services.TryAddScoped(typeof(MetadataDeserializer));

            services.TryAddScoped(sp =>
                EventStoreDbCommitDelegate.CreateSagaDelegate(
                    sp.GetRequiredService<EventStoreClient>(),
                    EventStoreDbSerialization.CreateSagaSerializer(sp.GetRequiredService<SerializerDelegate>())
                )
            );

            // find all implementations of IReaction<,,,> and register a ReactionWorker for it
            foreach (var (type, reactionType, commandType, stateType, eventType) in
                     from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                     where !(assembly.GetName().Name?.Contains("Microsoft.Data.SqlClient") ?? false)
                     from type in assembly.GetTypes()
                     where !type.IsAbstract

                     from iface in type.GetInterfaces()
                     where iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IReaction<,,,>)
                     let genericArgs = iface.GetGenericArguments()

                     select (type, reactionType: genericArgs[0], commandType: genericArgs[1], stateType: genericArgs[2], eventType: genericArgs[3])) {
                services.AddSingleton(typeof(IHostedService), typeof(ReactionWorker<,,,,>).MakeGenericType(type, reactionType, commandType, stateType, eventType));
            }

            // find all implementations of IReaction<,,,,> and register a SagaWorker for it
            foreach (var (reactionStateType, reactionEventType, commandType, commandStateType, commandEventType) in
                     from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                     where !(assembly.GetName().Name?.Contains("Microsoft.Data.SqlClient") ?? false)
                     from type in assembly.GetTypes()
                     where !type.IsAbstract

                     from iface in type.GetInterfaces()
                     where iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IReaction<,,,,>)
                     let genericArgs = iface.GetGenericArguments()

                     select (reactionStateType: genericArgs[0], reactionEventType: genericArgs[1], commandType: genericArgs[2], commandStateType: genericArgs[3], commandEventType: genericArgs[4])) {
                services.AddSingleton(typeof(IHostedService), typeof(SagaWorker<,,,,>).MakeGenericType(reactionStateType, reactionEventType, commandType, commandStateType, commandEventType));
            }
        });
    }
}