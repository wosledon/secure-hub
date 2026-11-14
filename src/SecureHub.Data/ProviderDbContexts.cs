using Microsoft.EntityFrameworkCore;

namespace SecureHub.Data;

/// <summary>
/// SQLite 专用上下文：仅用于把迁移与 provider 绑定。
/// 运行时通过 <c>AddDbContext&lt;SecureHubDbContext, SqliteSecureHubDbContext&gt;</c> 注册。
/// </summary>
public sealed class SqliteSecureHubDbContext(DbContextOptions<SqliteSecureHubDbContext> options)
    : SecureHubDbContext(options);

/// <summary>
/// PostgreSQL 专用上下文：与 SQLite 各持一套迁移，避免 provider 绑定的迁移互相污染。
/// </summary>
public sealed class PostgresSecureHubDbContext(DbContextOptions<PostgresSecureHubDbContext> options)
    : SecureHubDbContext(options);
