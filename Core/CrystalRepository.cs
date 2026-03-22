using Ardalis.Specification.EntityFrameworkCore;

namespace Server.Core;

public sealed class CrystalRepository<T>(CrystalDb db)
    : RepositoryBase<T>(db), IRepository<T> where T
    : class, IAggregateRoot;