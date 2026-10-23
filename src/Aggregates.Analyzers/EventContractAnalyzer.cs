using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aggregates.Analyzers;

/// <summary>
/// Reports an event type without <c>EventContractAttribute</c>: such a type is stored under its full type name,
/// so renaming or moving it makes its stored events unreadable.
/// </summary>
/// <remarks>
/// An event type is a concrete type assignable to the <c>TEvent</c> of a command, saga, policy or projection
/// declared in the compilation, or, for a command, a concrete type in the assembly that declares its <c>TEvent</c>.
/// A <c>TEvent</c> of <see cref="object"/> is ignored.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventContractAnalyzer : DiagnosticAnalyzer {
    /// <summary>
    /// The id of the diagnostic.
    /// </summary>
    internal const string DiagnosticId = "AGG0001";

    /// <summary>
    /// An event type has no <c>EventContractAttribute</c>.
    /// </summary>
    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Event type without EventContractAttribute",
        messageFormat: "Event type '{0}'{1} has no [EventContract]: it is stored under its full type name, so renaming or moving it makes its stored events unreadable",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Without [EventContract], the class name is the contract of an event: renaming the class or moving it to another namespace makes its stored events unreadable, and loading the aggregate fails.",
        helpLinkUri: "https://github.com/FleaFX/Aggregates/wiki/API-Reference#aggregatesanalyzers",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    static void OnCompilationStart(CompilationStartAnalysisContext context) {
        var compilation = context.Compilation;
        if (compilation.GetTypeByMetadataName(KnownTypes.EventContractAttribute) is not { } contract)
            return;

        // The handler interfaces the compilation can see, with the position of TEvent among their type
        // arguments. Only a command's events are also looked up in the assembly that declares its TEvent:
        // sagas, policies and projections often read events of other teams, which the reader can't attribute.
        var handlers = (
            from handler in new[] {
                (Name: KnownTypes.Command, EventIndex: 1, IsCommand: true),
                (Name: KnownTypes.Saga, EventIndex: 1, IsCommand: false),
                (Name: KnownTypes.Policy, EventIndex: 0, IsCommand: false),
                (Name: KnownTypes.Projection, EventIndex: 0, IsCommand: false),
            }
            let definition = compilation.GetTypeByMetadataName(handler.Name)
            where definition is not null
            select (Definition: definition!, handler.EventIndex, handler.IsCommand)
        ).ToArray();
        if (handlers.Length == 0)
            return;

        var declaredTypes = new ConcurrentBag<INamedTypeSymbol>();
        var eventTypes = new ConcurrentDictionary<ITypeSymbol, byte>(SymbolEqualityComparer.Default);
        var commands = new ConcurrentBag<(ITypeSymbol EventType, INamedTypeSymbol Command)>();

        context.RegisterSymbolAction(symbolContext => {
            var type = (INamedTypeSymbol)symbolContext.Symbol;
            declaredTypes.Add(type);
            foreach (var @interface in type.AllInterfaces)
                foreach (var handler in handlers) {
                    if (!SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, handler.Definition))
                        continue;
                    var eventType = @interface.TypeArguments[handler.EventIndex];
                    if (eventType.SpecialType == SpecialType.System_Object || eventType.TypeKind is TypeKind.TypeParameter or TypeKind.Error)
                        continue;
                    eventTypes.TryAdd(eventType, 0);
                    if (handler.IsCommand)
                        commands.Add((eventType, type));
                }
        }, SymbolKind.NamedType);

        context.RegisterCompilationEndAction(endContext => {
            if (eventTypes.IsEmpty)
                return;

            foreach (var type in declaredTypes)
                if (IsUncontracted(type, contract) && eventTypes.Keys.Any(eventType => IsAssignable(compilation, type, eventType)))
                    endContext.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.ToDisplayString(), ""));

            // An event in a referenced assembly has no source location: report it on the first command in source
            // order whose TEvent it is assignable to, once per compilation.
            var typesByAssembly = new Dictionary<IAssemblySymbol, INamedTypeSymbol[]>(SymbolEqualityComparer.Default);
            var reported = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var ordered = commands
                .OrderBy(entry => entry.Command.Locations[0].SourceTree?.FilePath, StringComparer.Ordinal)
                .ThenBy(entry => entry.Command.Locations[0].SourceSpan.Start);
            foreach (var (eventType, command) in ordered) {
                if (eventType.ContainingAssembly is not { } assembly || SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly))
                    continue;
                if (!typesByAssembly.TryGetValue(assembly, out var types))
                    typesByAssembly[assembly] = types = [.. TypesIn(assembly.GlobalNamespace)];
                foreach (var type in types)
                    if (IsUncontracted(type, contract) && IsAssignable(compilation, type, eventType) && reported.Add(type))
                        endContext.ReportDiagnostic(Diagnostic.Create(Rule, command.Locations[0], type.ToDisplayString(), $" in assembly '{assembly.Name}'"));
            }
        });
    }

    // A type that EventTypeRegistry would store under its full type name: concrete, and without the attribute on
    // the type itself (EventContractAttribute is not inherited).
    static bool IsUncontracted(INamedTypeSymbol type, INamedTypeSymbol contract) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct
        && !type.IsAbstract
        && !type.IsStatic
        && !type.IsImplicitlyDeclared
        && !type.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, contract));

    // Boxing covers a struct event that implements a marker interface; user-defined conversions don't count.
    // CommonConversion has no IsBoxing: an implicit, non-user-defined conversion from a value type to a
    // reference type is a boxing conversion.
    static bool IsAssignable(Compilation compilation, ITypeSymbol type, ITypeSymbol eventType) {
        var conversion = compilation.ClassifyCommonConversion(type, eventType);
        return conversion.IsImplicit
            && !conversion.IsUserDefined
            && (conversion.IsIdentity || conversion.IsReference || (type.IsValueType && eventType.IsReferenceType));
    }

    static IEnumerable<INamedTypeSymbol> TypesIn(INamespaceSymbol @namespace) {
        foreach (var member in @namespace.GetMembers())
            switch (member) {
                case INamespaceSymbol nested:
                    foreach (var type in TypesIn(nested))
                        yield return type;
                    break;
                case INamedTypeSymbol type:
                    foreach (var nestedType in WithNestedTypes(type))
                        yield return nestedType;
                    break;
            }
    }

    static IEnumerable<INamedTypeSymbol> WithNestedTypes(INamedTypeSymbol type) {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
            foreach (var nestedType in WithNestedTypes(nested))
                yield return nestedType;
    }
}
