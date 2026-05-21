namespace Aggregates.Sagas;

/// <summary>
/// Asynchronously persists the saga changes tracked by the given <see cref="UnitOfWork"/>.
/// Register an implementation of this delegate in the DI container to wire up saga persistence.
/// </summary>
/// <param name="unitOfWork">The unit of work containing the saga changes to commit.</param>
public delegate ValueTask SagaCommitDelegate(UnitOfWork unitOfWork);
