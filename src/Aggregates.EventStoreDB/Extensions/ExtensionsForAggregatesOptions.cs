// ReSharper disable CheckNamespace

using System.Reflection;
using Aggregates.Configuration;
using Aggregates.EventStoreDB.Extensions;
using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;
using Aggregates.EventStoreDB.Workers;
using Aggregates.Policies;
using Aggregates.Projections;
using Aggregates.Sagas;
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
                var target = type.BuildWithStubDependencies();
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

            // find all classes that are attributed with a SagaContract
            foreach (var type in
                     from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                     where !(assembly.GetName().Name?.Contains("Microsoft.Data.SqlClient") ?? false)
                     from type in assembly.GetTypes()
                     where !type.IsAbstract

                     let attr = type.GetCustomAttribute<SagaContractAttribute>()
                     where attr is not null
                     select type) {
                
                services.AddTransient(type);

                // find suitable SagaAsyncDelegate methods and register workers for it
                var target = type.BuildWithStubDependencies();
                foreach (var (TSagaState, TSagaEvent, TCommand, TState, TEvent) in
                         from method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         where method.GetParameters().Length >= 3 &&
                               method.ReturnType.IsGenericType &&
                               (method.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>) || method.ReturnType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) &&
                               (method.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>) || method.ReturnType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) &&
                               ((method.ReturnType.GetGenericArguments()[0].IsGenericType && method.ReturnType.GetGenericArguments()[0].GetGenericTypeDefinition() == typeof(ICommand<,>)) ||
                                method.ReturnType.GetGenericArguments()[0].FindInterfaces((t, _) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ICommand<,>), null).Any())
                         let TSagaState = method.GetParameters()[0].ParameterType
                         let TSagaEvent = method.GetParameters()[1].ParameterType
                         let TCommand = method.ReturnType.GetGenericArguments()[0]
                         let returnTypeGenericArguments = TCommand.IsGenericType ? TCommand.GenericTypeArguments : TCommand.FindInterfaces((t, _) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ICommand<,>), null)[0].GenericTypeArguments
                         let TState = returnTypeGenericArguments[0]
                         let TEvent = returnTypeGenericArguments[1]
                         let asyncDelegateType = typeof(SagaAsyncDelegate<,,,,>).MakeGenericType(TSagaState, TSagaEvent, TCommand, TState, TEvent)
                         let delegateType = typeof(SagaDelegate<,,,,>).MakeGenericType(TSagaState, TSagaEvent, TCommand, TState, TEvent)
                         where method.IsDelegate(asyncDelegateType, target) || method.IsDelegate(delegateType, target)
                         select (TSagaState, TSagaEvent, TCommand, TState, TEvent)
                        ) {
                    services.AddSingleton(typeof(IHostedService), typeof(SagaWorker<,,,,,>).MakeGenericType(type, TSagaState, TSagaEvent, TCommand, TState, TEvent));
                }
            }

            // find all classes that are attributed with a PolicyContract
            foreach (var type in
                     from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                     where !(assembly.GetName().Name?.Contains("Microsoft.Data.SqlClient") ?? false)
                     from type in assembly.GetTypes()
                     where !type.IsAbstract

                     let attr = type.GetCustomAttribute<PolicyContractAttribute>()
                     where attr is not null
                     select type) {
                
                services.AddTransient(type);

                // find suitable PolicyAsyncDelegate methods and register workers for it
                var target = type.BuildWithStubDependencies();
                foreach (var (TPolicyEvent, TCommand, TState, TEvent) in
                         from method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         where method.GetParameters().Length >= 2 &&
                               method.ReturnType.IsGenericType &&
                               (method.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>) || method.ReturnType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) &&
                               ((method.ReturnType.GetGenericArguments()[0].IsGenericType && method.ReturnType.GetGenericArguments()[0].GetGenericTypeDefinition() == typeof(ICommand<,>)) ||
                                 method.ReturnType.GetGenericArguments()[0].FindInterfaces((t, _) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ICommand<,>), null).Any())
                         let TPolicyEvent = method.GetParameters()[0].ParameterType
                         let TCommand = method.ReturnType.GetGenericArguments()[0]
                         let returnTypeGenericArguments = TCommand.IsGenericType ? TCommand.GenericTypeArguments : TCommand.FindInterfaces((t, _) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ICommand<,>), null)[0].GenericTypeArguments
                         let TState = returnTypeGenericArguments[0]
                         let TEvent = returnTypeGenericArguments[1]
                         let asyncDelegateType = typeof(PolicyAsyncDelegate<,,,>).MakeGenericType(TPolicyEvent, TCommand, TState, TEvent)
                         let delegateType = typeof(PolicyDelegate<,,,>).MakeGenericType(TPolicyEvent, TCommand, TState, TEvent)
                         where method.IsDelegate(asyncDelegateType, target) || method.IsDelegate(delegateType, target)
                         select (TPolicyEvent, TCommand, TState, TEvent)
                        ) {
                    services.AddSingleton(typeof(IHostedService), typeof(PolicyWorker<,,,,>).MakeGenericType(type, TPolicyEvent, TCommand, TState, TEvent));
                }
            }
        });
}