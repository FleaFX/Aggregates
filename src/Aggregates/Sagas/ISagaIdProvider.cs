using Aggregates.Util;

namespace Aggregates.Sagas;

/// <summary>
/// Provides a <see cref="string"/> that is to be used to identify a saga.
/// </summary>
/// <typeparam name="TReactionEvent">The type of the event that is handled by the saga.</typeparam>
public interface ISagaIdProvider<in TReactionEvent> {
    /// <summary>
    /// Gets the value for a saga identifier.
    /// </summary>
    /// <param name="context">The context that may provide more information to generate the saga identifier.</param>
    /// <returns>A <see cref="string"/>>.</returns>
    string GetSagaId(TReactionEvent context) =>
        GetSagaIdAsync(context, CancellationToken.None).RunSynchronously();

    /// <summary>
    /// Asynchronously gets the value for a saga identifier.
    /// </summary>
    /// <param name="context">The context that may provide more information to generate the saga identifier.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous operation.</returns>
    ValueTask<string> GetSagaIdAsync(TReactionEvent context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetSagaId(context));
    
}