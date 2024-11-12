using System.Reflection;

namespace Aggregates.EventStoreDB.Extensions;

static class ExtensionsForReflection {
    /// <summary>
    /// Returns whether the given <paramref name="methodInfo"/> can be used to create a <typeparamref name="TDelegate"/>.
    /// </summary>
    /// <typeparam name="TDelegate"></typeparam>
    /// <param name="methodInfo"></param>
    /// <param name="target"></param>
    /// <returns></returns>
    public static bool IsDelegate<TDelegate>(this MethodInfo methodInfo, object? target) where TDelegate : Delegate {
        try {
            methodInfo.CreateDelegate<TDelegate>(target);
            return true;
        }  catch (Exception) {
            return false;
        }
    }

    /// <summary>
    /// Returns whether the given <paramref name="methodInfo"/> can be used to create a <typeparamref name="TDelegate"/>.
    /// </summary>
    /// <param name="methodInfo"></param>
    /// <param name="target"></param>
    /// <returns></returns>
    public static bool IsDelegate(this MethodInfo methodInfo, Type delegateType, object target) {
        try {
            methodInfo.CreateDelegate(delegateType, target);
            return true;
        }  catch (Exception) {
            return false;
        }
    }

    public static object? BuildWithStubDependencies(this Type targetType) {
        var constructor = targetType.GetConstructors().MinBy(ctor => ctor.GetParameters().Length);
        var parameters = (
                from parameter in constructor?.GetParameters() ?? []
                select parameter.ParameterType.IsPrimitive
                    ? parameter.DefaultValue
                    : null
            ).ToArray();
        return constructor?.Invoke(parameters);
    }
}