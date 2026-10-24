using DotnetMcpToolkit.Database;

namespace DotnetMcpToolkit.Tests.Database;

public sealed class SqlStatementGuardTests
{
    [Theory]
    [InlineData("SELECT 1", SqlStatementKind.Read)]
    [InlineData("  select * from users", SqlStatementKind.Read)]
    [InlineData("-- note\nSELECT 1", SqlStatementKind.Read)]
    [InlineData("/* note */ SELECT 1", SqlStatementKind.Read)]
    [InlineData("EXPLAIN SELECT 1", SqlStatementKind.Read)]
    [InlineData("SHOW TABLES", SqlStatementKind.Read)]
    [InlineData("DESCRIBE users", SqlStatementKind.Read)]
    [InlineData("VALUES (1)", SqlStatementKind.Read)]
    [InlineData("PRAGMA table_list", SqlStatementKind.Read)]
    [InlineData("PRAGMA = 1", SqlStatementKind.Write)]
    [InlineData("PRAGMA journal_mode = WAL", SqlStatementKind.Write)]
    [InlineData("PRAGMA foreign_keys(1)", SqlStatementKind.Write)]
    [InlineData("INSERT INTO t VALUES (1)", SqlStatementKind.Write)]
    [InlineData("update t set a = 1", SqlStatementKind.Write)]
    [InlineData("DELETE FROM t", SqlStatementKind.Write)]
    [InlineData("MERGE INTO t USING s ON 1 = 1", SqlStatementKind.Write)]
    [InlineData("REPLACE INTO t VALUES (1)", SqlStatementKind.Write)]
    [InlineData("TRUNCATE t", SqlStatementKind.Write)]
    [InlineData("CALL proc()", SqlStatementKind.Write)]
    [InlineData("EXEC sp_who", SqlStatementKind.Write)]
    [InlineData("CREATE TABLE t (a int)", SqlStatementKind.Ddl)]
    [InlineData("alter table t add column b int", SqlStatementKind.Ddl)]
    [InlineData("DROP TABLE t", SqlStatementKind.Ddl)]
    [InlineData("GRANT SELECT ON t TO u", SqlStatementKind.Ddl)]
    [InlineData("VACUUM", SqlStatementKind.Ddl)]
    [InlineData("", SqlStatementKind.Unknown)]
    [InlineData("-- only a comment", SqlStatementKind.Unknown)]
    [InlineData("(SELECT 1)", SqlStatementKind.Unknown)]
    [InlineData("BEGIN", SqlStatementKind.Unknown)]
    public void Classify_LeadingKeyword_ReturnsExpectedKind(string sql, SqlStatementKind expected)
    {
        var kind = SqlStatementGuard.Classify(sql);

        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData("WITH c AS (SELECT 1) SELECT * FROM c", SqlStatementKind.Read)]
    [InlineData("WITH RECURSIVE c(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM c WHERE n < 3) SELECT n FROM c", SqlStatementKind.Read)]
    [InlineData("WITH a AS (SELECT 1), b AS MATERIALIZED (SELECT 2) SELECT * FROM a, b", SqlStatementKind.Read)]
    [InlineData("WITH a AS (SELECT 1) DELETE FROM t", SqlStatementKind.Write)]
    [InlineData("WITH a AS (SELECT 1) DROP TABLE t", SqlStatementKind.Ddl)]
    [InlineData("WITH a AS (SELECT 1) BOGUS", SqlStatementKind.Unknown)]
    [InlineData("WITH a AS (SELECT 1", SqlStatementKind.Unknown)]
    public void Classify_WithStatement_UsesMainStatementKeyword(string sql, SqlStatementKind expected)
    {
        var kind = SqlStatementGuard.Classify(sql);

        Assert.Equal(expected, kind);
    }

    [Fact]
    public void Classify_KeywordInsideStringLiteral_IgnoresLiteral()
    {
        var kind = SqlStatementGuard.Classify("'DELETE' || 'x'");

        Assert.Equal(SqlStatementKind.Unknown, kind);
    }

    [Fact]
    public void Classify_NullSql_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SqlStatementGuard.Classify(null!));
    }

    [Theory]
    [InlineData("SELECT 1;")]
    [InlineData("SELECT 1; -- trailing comment")]
    [InlineData("SELECT 1 /* end */;\n  ")]
    [InlineData("SELECT ';' AS semi")]
    [InlineData("SELECT \"a;b\" FROM t")]
    [InlineData("SELECT 'it''s; fine'")]
    [InlineData("SELECT 1 -- a; b")]
    [InlineData("SELECT 1 /* a; b */")]
    public void ContainsMultipleStatements_SingleStatement_ReturnsFalse(string sql)
    {
        var result = SqlStatementGuard.ContainsMultipleStatements(sql);

        Assert.False(result);
    }

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1;;")]
    [InlineData("SELECT 1; DROP TABLE t;")]
    [InlineData("SELECT 'a' ; ; DELETE FROM t")]
    public void ContainsMultipleStatements_SecondStatement_ReturnsTrue(string sql)
    {
        var result = SqlStatementGuard.ContainsMultipleStatements(sql);

        Assert.True(result);
    }

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("WITH c AS (SELECT 1) SELECT * FROM c")]
    [InlineData("SELECT 'DELETE FROM t' AS text")]
    [InlineData("SELECT \"update\" FROM t")]
    [InlineData("SELECT [order] FROM [dbo].[Orders]")]
    [InlineData("SELECT `order` FROM `orders`")]
    [InlineData("SELECT 'it''s' AS quoted")]
    [InlineData("SELECT 1 -- DROP TABLE t")]
    [InlineData("SELECT 1 /* INSERT INTO t */")]
    [InlineData("SELECT $1, $2")]
    [InlineData("SELECT replace(name, 'a', 'b') FROM t")]
    [InlineData("SELECT 1;")]
    [InlineData("EXPLAIN QUERY PLAN SELECT * FROM t")]
    [InlineData("PRAGMA table_list")]
    public void IsReadOnly_ReadStatement_ReturnsTrue(string sql)
    {
        var result = SqlStatementGuard.IsReadOnly(sql);

        Assert.True(result);
    }

    [Theory]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("DELETE FROM t")]
    [InlineData("DROP TABLE t")]
    [InlineData("SELECT * INTO new_table FROM t")]
    [InlineData("SELECT * FROM t FOR UPDATE")]
    [InlineData("SELECT * FROM t FOR SHARE")]
    [InlineData("SELECT * FROM t FOR NO KEY UPDATE")]
    [InlineData("SELECT * FROM t LOCK IN SHARE MODE")]
    [InlineData("SELECT 1; DROP TABLE t")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("WITH d AS (DELETE FROM t RETURNING *) SELECT * FROM d")]
    [InlineData("EXPLAIN ANALYZE DELETE FROM t")]
    [InlineData("EXPLAIN (ANALYZE) UPDATE t SET a = 1")]
    [InlineData("PRAGMA journal_mode = DELETE")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsReadOnly_WriteOrAmbiguousStatement_ReturnsFalse(string sql)
    {
        var result = SqlStatementGuard.IsReadOnly(sql);

        Assert.False(result);
    }

    [Theory]
    [InlineData("SELECT 'unterminated")]
    [InlineData("SELECT 1 /* never closed")]
    [InlineData("SELECT 1 /*! ; DROP TABLE t */")]
    [InlineData("SELECT 1 /* a /* b */ ; DROP TABLE t */")]
    [InlineData("SELECT 1 --; DROP TABLE t")]
    [InlineData("SELECT 'C:\\' ; DROP TABLE t; --'")]
    [InlineData("SELECT E'\\'' ; DROP TABLE t --'")]
    [InlineData("SELECT $$ it's $$; DROP TABLE t")]
    [InlineData("SELECT $body$ x $body$")]
    [InlineData("SELECT [unterminated")]
    public void IsReadOnly_AmbiguousLexicalForm_ReturnsFalse(string sql)
    {
        var result = SqlStatementGuard.IsReadOnly(sql);

        Assert.False(result);
    }

    [Fact]
    public void EnsureAllowed_ReadStatementWithoutWrites_DoesNotThrow()
    {
        SqlStatementGuard.EnsureAllowed("SELECT 1", allowWrites: false);
    }

    [Fact]
    public void EnsureAllowed_WriteStatementWithoutWrites_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => SqlStatementGuard.EnsureAllowed("DELETE FROM t", allowWrites: false));
    }

    [Fact]
    public void EnsureAllowed_WriteStatementWithWrites_DoesNotThrow()
    {
        SqlStatementGuard.EnsureAllowed("DELETE FROM t", allowWrites: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureAllowed_EmptySql_ThrowsArgumentException(string? sql)
    {
        Assert.ThrowsAny<ArgumentException>(() => SqlStatementGuard.EnsureAllowed(sql!, allowWrites: true));
    }

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("  select * from t")]
    [InlineData("WITH x AS (SELECT 1) SELECT * FROM x")]
    [InlineData("EXPLAIN SELECT 1")]
    [InlineData("-- comment\nSELECT 1")]
    [InlineData("/* block */ SELECT 1")]
    [InlineData("VALUES (1)")]
    [InlineData("SHOW TABLES")]
    public void Classify_ReadStatements_ReturnsRead(string sql)
    {
        var kind = SqlStatementGuard.Classify(sql);

        Assert.Equal(SqlStatementKind.Read, kind);
    }

    [Theory]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("DELETE FROM t")]
    [InlineData("MERGE INTO t USING s ON 1 = 1")]
    [InlineData("REPLACE INTO t VALUES (1)")]
    [InlineData("TRUNCATE t")]
    [InlineData("WITH x AS (SELECT 1) INSERT INTO t SELECT * FROM x")]
    [InlineData("PRAGMA journal_mode = WAL")]
    [InlineData("PRAGMA table_info(t)")]
    [InlineData("EXEC sp_who")]
    public void Classify_WriteStatements_ReturnsWrite(string sql)
    {
        var kind = SqlStatementGuard.Classify(sql);

        Assert.Equal(SqlStatementKind.Write, kind);
    }

    [Theory]
    [InlineData("CREATE TABLE t (a INT)")]
    [InlineData("ALTER TABLE t ADD COLUMN b INT")]
    [InlineData("DROP TABLE t")]
    [InlineData("GRANT SELECT ON t TO u")]
    [InlineData("ATTACH DATABASE 'x.db' AS x")]
    [InlineData("VACUUM")]
    public void Classify_DdlStatements_ReturnsDdl(string sql)
    {
        var kind = SqlStatementGuard.Classify(sql);

        Assert.Equal(SqlStatementKind.Ddl, kind);
    }

    [Theory]
    [InlineData("FOO BAR")]
    [InlineData("123")]
    [InlineData(";")]
    public void Classify_Garbage_ReturnsUnknown(string sql)
    {
        var kind = SqlStatementGuard.Classify(sql);

        Assert.Equal(SqlStatementKind.Unknown, kind);
    }

    [Theory]
    [InlineData("SELECT ';' FROM t")]
    [InlineData("SELECT 'it''s; fine'")]
    [InlineData("SELECT \"a;b\" FROM t")]
    public void ContainsMultipleStatements_SemicolonInStringLiteral_ReturnsFalse(string sql)
    {
        var result = SqlStatementGuard.ContainsMultipleStatements(sql);

        Assert.False(result);
    }

    [Theory]
    [InlineData("SELECT 1 -- ; DROP TABLE t")]
    [InlineData("SELECT 1 /* ; */")]
    public void ContainsMultipleStatements_SemicolonInComment_ReturnsFalse(string sql)
    {
        var result = SqlStatementGuard.ContainsMultipleStatements(sql);

        Assert.False(result);
    }

    [Theory]
    [InlineData("SELECT 1;")]
    [InlineData("SELECT 1;   \n-- done")]
    public void ContainsMultipleStatements_TrailingSemicolon_ReturnsFalse(string sql)
    {
        var result = SqlStatementGuard.ContainsMultipleStatements(sql);

        Assert.False(result);
    }

    [Theory]
    [InlineData("SELECT 1; DROP TABLE t")]
    [InlineData("SELECT 1;SELECT 2")]
    public void ContainsMultipleStatements_TwoStatements_ReturnsTrue(string sql)
    {
        var result = SqlStatementGuard.ContainsMultipleStatements(sql);

        Assert.True(result);
    }

    [Theory]
    [InlineData("SELECT * FROM t FOR UPDATE")]
    [InlineData("SELECT * FROM t FOR SHARE")]
    public void IsReadOnly_SelectForUpdate_ReturnsFalse(string sql)
    {
        var result = SqlStatementGuard.IsReadOnly(sql);

        Assert.False(result);
    }

    [Fact]
    public void IsReadOnly_SelectInto_ReturnsFalse()
    {
        var result = SqlStatementGuard.IsReadOnly("SELECT * INTO new_table FROM t");

        Assert.False(result);
    }

    [Fact]
    public void IsReadOnly_PlainSelect_ReturnsTrue()
    {
        var result = SqlStatementGuard.IsReadOnly("SELECT * FROM t");

        Assert.True(result);
    }

    [Fact]
    public void IsReadOnly_SelectThenDrop_ReturnsFalse()
    {
        var result = SqlStatementGuard.IsReadOnly("SELECT 1; DROP TABLE t");

        Assert.False(result);
    }

    [Fact]
    public void EnsureAllowed_WriteWithoutAllowWrites_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SqlStatementGuard.EnsureAllowed("DELETE FROM t", allowWrites: false));

        Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureAllowed_WriteWithAllowWrites_DoesNotThrow()
    {
        var exception = Record.Exception(() => SqlStatementGuard.EnsureAllowed("DELETE FROM t", allowWrites: true));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureAllowed_ReadWithoutAllowWrites_DoesNotThrow()
    {
        var exception = Record.Exception(() => SqlStatementGuard.EnsureAllowed("SELECT * FROM t", allowWrites: false));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void EnsureAllowed_Whitespace_ThrowsArgumentException(string sql)
    {
        Assert.Throws<ArgumentException>(() => SqlStatementGuard.EnsureAllowed(sql, allowWrites: false));
    }

    [Fact]
    public void Classify_UnterminatedStringLiteral_DoesNotThrow()
    {
        var kind = SqlStatementGuard.Classify("SELECT 'abc");

        Assert.Equal(SqlStatementKind.Unknown, kind);
    }
}
