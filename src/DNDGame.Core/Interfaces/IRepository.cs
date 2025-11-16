#nullable enable
namespace DNDGame.Core.Interfaces;

public interface IRepository<T> where T : class
{
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    Task<T?> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
