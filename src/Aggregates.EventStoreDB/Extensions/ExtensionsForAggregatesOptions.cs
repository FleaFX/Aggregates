// ReSharper disable CheckNamespace

using System.Reflection;
using Aggregates.Configuration;
using Aggregates.EventStoreDB.Extensions;
using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;
using Aggregates.EventStoreDB.Workers;
using Aggregates.Projections;
using Aggregates.Types;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Aggregates.EventStoreDB;

public static class ExtensionsForAggregatesOptions {
    /// <summary>
    /// Completes the Aggregates event sourcing infrastructure with a connection to EventStoreDB.
    /// </summary>
    /// <param name="options">The <see cref="AggregatesOptions"/> to configure.</param>
    /// <param name="connectionString">The connection string to use when connecting to EventStoreDB.</param>
    public static AggregatesOptions UseEventStoreDB(this AggregatesOptions options, string connectionString) =>
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
        })
        .AddConfiguration(services => {
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
                        return [];
                    }
                };
            });
            services.TryAddScoped<CreateToAllAsyncDelegate>(sp => sp.GetRequiredService<EventStorePersistentSubscriptionsClient>().CreateToAllAsync);
            services.TryAddScoped<DeleteToAllAsyncDelegate>(sp => sp.GetRequiredService<EventStorePersistentSubscriptionsClient>().DeleteToAllAsync);
            services.TryAddScoped<SubscribeToAll>(sp => sp.GetRequiredService<EventStorePersistentSubscriptionsClient>().SubscribeToAll);
            services.TryAddScoped(typeof(ResolvedEventDeserializer));
            services.TryAddScoped(typeof(MetadataDeserializer));

            // find all implementations of IProjection<,> and register a ProjectionWorker for it
            foreach (var (stateType, eventType) in
                     from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                     where !(assembly.GetName().Name?.Contains("Microsoft.Data.SqlClient") ?? false)
                     from type in assembly.GetTypes()
                     where !type.IsAbstract

                     from iface in type.GetInterfaces()
                     where iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IProjection<,>)
                     let genericArgs = iface.GetGenericArguments()

                     select (stateType: genericArgs[0], eventType: genericArgs[1])) {
                services.AddSingleton(typeof(IHostedService), typeof(ProjectionWorker<,>).MakeGenericType(stateType, eventType));
            }

            // find all classes that are attributed with a ProjectionContract
            foreach (var type in
                     from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                     where !(assembly.GetName().Name?.Contains("Microsoft.Data.SqlClient") ?? false)
                     from type in assembly.GetTypes()
                     where !type.IsAbstract

                     let attr = type.GetCustomAttribute<ProjectionContractAttribute>()
                     where attr is not null
                     select type) {
                services.AddTransient(type);

                // find suitable Projection(Async)Delegate methods and register workers for it
                var sp = services.BuildServiceProvider();
                var target = sp.GetRequiredService(type);
                foreach (var eventType in
                         from method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         where method.GetParameters().Length == 2 && method.ReturnType == typeof(ICommit)
                         let delegateType = typeof(ProjectionDelegate<>).MakeGenericType(method.GetParameters()[0].ParameterType)
                         where method.IsDelegate(delegateType, target)
                         select method.GetParameters()[0].ParameterType
                        ) {
                    services.AddSingleton(typeof(IHostedService), typeof(ProjectionDelegateWorker<,>).MakeGenericType(type, eventType));
                }
                foreach (var eventType in
                         from method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         where method.GetParameters().Length == 2 && method.ReturnType == typeof(ValueTask<ICommit>)
                         let delegateType = typeof(ProjectionAsyncDelegate<>).MakeGenericType(method.GetParameters()[0].ParameterType)
                         where method.IsDelegate(delegateType, target)
                         select method.GetParameters()[0].ParameterType
                        ) {
                    services.AddSingleton(typeof(IHostedService), typeof(ProjectionAsyncDelegateWorker<,>).MakeGenericType(type, eventType));
                }
            }
        })
        .AddConfiguration(services => {
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
                        return [];
                    }
                };
            });
            services.TryAddScoped<CreateToAllAsyncDelegate>(sp => sp.GetRequiredService<EventStorePersistentSubscriptionsClient>().CreateToAllAsync);
            services.TryAddScoped<SubscribeToAll>(sp => sp.GetRequiredService<EventStorePersistentSubscriptionsClient>().SubscribeToAll);
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

                     from @interface in type.GetInterfaces()
                     where @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IReaction<,,,>)
                     let genericArgs = @interface.GetGenericArguments()

                     select (type, reactionType: genericArgs[0], commandType: genericArgs[1], stateType: genericArgs[2], eventType: genericArgs[3])) {
                services.AddSingleton(typeof(IHostedService), typeof(ReactionWorker<,,,,>).MakeGenericType(type, reactionType, commandType, stateType, eventType));
            }

            // find all implementations of IReaction<,,,,> and register a SagaWorker for it
            foreach (var (reactionStateType, reactionEventType, commandType, commandStateType, commandEventType) in
                     from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                     where !(assembly.GetName().Name?.Contains("Microsoft.Data.SqlClient") ?? false)
                     from type in assembly.GetTypes()
                     where !type.IsAbstract

                     from @interface in type.GetInterfaces()
                     where @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IReaction<,,,,>)
                     let genericArgs = @interface.GetGenericArguments()

                     select (reactionStateType: genericArgs[0], reactionEventType: genericArgs[1], commandType: genericArgs[2], commandStateType: genericArgs[3], commandEventType: genericArgs[4])) {
                services.AddSingleton(typeof(IHostedService), typeof(SagaWorker<,,,,>).MakeGenericType(reactionStateType, reactionEventType, commandType, commandStateType, commandEventType));
            }
        });
}