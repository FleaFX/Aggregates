// ReSharper disable CheckNamespace

using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Sql; 

public static class ExtensionsForProjectionOptions {
    public static void UseSql(this ProjectionsOptions options) {
        options.AddConfiguration(services => {
            // find all implementations of SqlProjection and register them
            foreach (var (implType, stateType, eventType) in
                     from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                     from type in assembly.GetTypes()
                     where !type.IsAbstract && (type.BaseType?.IsGenericType ?? false) && type.BaseType.GetGenericTypeDefinition() == typeof(SqlProjection<,>)

                     let genericArgs = type.BaseType.GetGenericArguments()

                     select (type, genericArgs[0], genericArgs[1])) {
                services.AddScoped(typeof(IProjection<,>).MakeGenericType(stateType, eventType), implType);
            }
        });
    }
}