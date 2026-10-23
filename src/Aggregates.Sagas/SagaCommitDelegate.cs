namespace Aggregates.Sagas;

/// <summary>
/// Asynchronously persists the saga changes tracked by the given <see cref="UnitOfWork"/>.
/// Register an implementation of this delegate in the DI container to wire up saga persistence.
/// </summary>
/// <param name="unitOfWork">The unit of work containing the saga changes to commit.</param>
/// <param name="cancellationToken">
/// Cancels the commit; pass it to the store's append. When it is cancelled while the append is in
/// flight, the outcome is unknown: the changes may have been stored.
/// </param>
public delegate ValueTask SagaCommitDelegate(UnitOfWork unitOfWork, CancellationToken cancellationToken);
