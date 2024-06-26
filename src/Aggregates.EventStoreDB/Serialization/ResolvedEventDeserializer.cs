﻿using Aggregates.Types;
using EventStore.Client;
using System.Reflection;
using Aggregates.Configuration;

namespace Aggregates.EventStoreDB.Serialization;

class ResolvedEventDeserializer(DeserializerDelegate deserializer, AggregatesOptions options) {
    readonly Lazy<Type[]> _eventContracts = new(() => (
        from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
        from type in assembly.GetTypes()
        let attr = type.GetCustomAttribute<EventContractAttribute>()
        where attr != null
        select type
    ).ToArray());

    /// <summary>
    /// Deserializes the event contained in the given <paramref name="resolvedEvent"/>.
    /// </summary>
    /// <param name="resolvedEvent">The <see cref="ResolvedEvent"/> that holds all the information about this event.</param>
    /// <returns>The deserialized event.</returns>
    public object Deserialize(ResolvedEvent resolvedEvent) {
        // try to find the contract
        var contract = _eventContracts.Value.FirstOrDefault(c => GetEventType(c) == resolvedEvent.Event.EventType);
        if (contract == null) throw new ArgumentOutOfRangeException(nameof(resolvedEvent), $"No contract found for event type {resolvedEvent.Event.EventType}");

        // deserialization works using streams, read the payload byte array into a memory stream
        using var stream = new MemoryStream();
        stream.Write(resolvedEvent.Event.Data.ToArray(), 0, resolvedEvent.Event.Data.Length);
        stream.Seek(0, SeekOrigin.Begin);

        // attempt to upgrade the event when necessary
        return UpgradeEvent(deserializer(stream, contract));
    }

    static string GetEventType(Type contractType) {
        var eventContract = contractType.GetCustomAttribute<EventContractAttribute>();
        return eventContract?.ToString() ?? contractType.Name;
    }

    static object UpgradeEvent(object @event) {
        while (true) {
            var contract = @event.GetType().GetCustomAttribute<EventContractAttribute>();
            if (contract == null || !contract.TryUpgrade(@event, out var upgradedEvent))
                return @event;

            @event = upgradedEvent;
        }
    }
}