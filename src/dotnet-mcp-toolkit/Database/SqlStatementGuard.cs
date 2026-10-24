namespace DotnetMcpToolkit.Database;

/// <summary>
/// Decides whether a SQL text is safe to run in read-only mode. The analysis is purely textual:
/// string literals, quoted identifiers and comments are skipped, and constructs whose meaning
/// differs between SQL dialects (backslash escapes, dollar quotes, MySQL executable comments,
/// nested block comments) are rejected rather than guessed at.
/// </summary>
public static class SqlStatementGuard
{
    private static readonly string[] _lockingFollowers = ["UPDATE", "SHARE", "NO", "KEY"];

    private static readonly HashSet<string> _statementKeywords = new(StringComparer.Ordinal)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "CREATE", "EXEC", "EXECUTE", "CALL", "COPY",
    };

    /// <summary>
    /// Determines the kind of a SQL statement from its first keyword, after skipping whitespace and comments.
    /// A <c>WITH</c> statement is classified by the keyword of its main statement, after the common table expressions.
    /// </summary>
    /// <param name="sql">The SQL text.</param>
    /// <returns>The statement kind, or <see cref="SqlStatementKind.Unknown"/> when it cannot be determined.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sql"/> is <see langword="null"/>.</exception>
    public static SqlStatementKind Classify(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        return ClassifyTokens(Tokenize(sql));
    }

    /// <summary>
    /// Determines whether <paramref name="sql"/> contains more than one statement. A single semicolon
    /// at the end of the text, optionally followed by whitespace or comments, does not count.
    /// </summary>
    /// <param name="sql">The SQL text.</param>
    /// <returns><see langword="true"/> if a semicolon outside literals and comments is followed by more SQL.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sql"/> is <see langword="null"/>.</exception>
    public static bool ContainsMultipleStatements(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        return HasMultipleStatements(Tokenize(sql));
    }

    /// <summary>
    /// Determines whether <paramref name="sql"/> is a single statement that only reads data. The text must
    /// classify as <see cref="SqlStatementKind.Read"/>, contain no second statement, no <c>INTO</c> clause,
    /// no <c>FOR UPDATE</c>/<c>FOR SHARE</c> locking clause, no <c>LOCK</c> keyword, and no data-modifying
    /// keyword (for example inside a <c>WITH</c> body or after <c>EXPLAIN ANALYZE</c>).
    /// </summary>
    /// <param name="sql">The SQL text.</param>
    /// <returns><see langword="true"/> if the statement can run in read-only mode; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sql"/> is <see langword="null"/>.</exception>
    public static bool IsReadOnly(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var tokens = Tokenize(sql);

        return ClassifyTokens(tokens) == SqlStatementKind.Read
            && !HasMultipleStatements(tokens)
            && !tokens.Any(token => IsWord(token, "INTO"))
            && !HasLockingClause(tokens)
            && !HasStatementKeyword(tokens);
    }

    /// <summary>
    /// Throws unless <paramref name="sql"/> may run under the current access mode.
    /// </summary>
    /// <param name="sql">The SQL text.</param>
    /// <param name="allowWrites"><see langword="true"/> when the connection may modify data or schema.</param>
    /// <exception cref="ArgumentException"><paramref name="sql"/> is <see langword="null"/>, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The statement is not read-only and <paramref name="allowWrites"/> is <see langword="false"/>.</exception>
    public static void EnsureAllowed(string sql, bool allowWrites)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        if (allowWrites || IsReadOnly(sql))
        {
            return;
        }

        throw new InvalidOperationException(
            "Read-only mode only allows a single statement that reads data (SELECT, WITH, EXPLAIN, SHOW, DESCRIBE, PRAGMA without assignment, VALUES). " +
            "Writes are not allowed on this connection.");
    }

    private static SqlStatementKind ClassifyTokens(List<SqlToken> tokens)
    {
        if (tokens.Count == 0 || tokens.Any(token => token.Kind == TokenKind.Unsafe))
        {
            return SqlStatementKind.Unknown;
        }

        return ClassifyAt(tokens, 0);
    }

    private static SqlStatementKind ClassifyAt(List<SqlToken> tokens, int index) =>
        KeywordAt(tokens, index) switch
        {
            "SELECT" or "EXPLAIN" or "SHOW" or "DESCRIBE" or "VALUES" => SqlStatementKind.Read,
            "PRAGMA" => HasPragmaAssignment(tokens, index + 1) ? SqlStatementKind.Write : SqlStatementKind.Read,
            "WITH" => ClassifyWith(tokens, index),
            "INSERT" or "UPDATE" or "DELETE" or "MERGE" or "REPLACE" or "UPSERT" or "TRUNCATE" or "COPY" or "CALL" or "EXEC" or "EXECUTE"
                => SqlStatementKind.Write,
            "CREATE" or "ALTER" or "DROP" or "GRANT" or "REVOKE" or "ATTACH" or "DETACH" or "VACUUM" => SqlStatementKind.Ddl,
            _ => SqlStatementKind.Unknown,
        };

    private static SqlStatementKind ClassifyWith(List<SqlToken> tokens, int index)
    {
        var i = index + 1;

        if (IsWordAt(tokens, i, "RECURSIVE"))
        {
            i++;
        }

        // Each common table expression reads "name [(columns)] AS [NOT] [MATERIALIZED] (body)" and they are comma-separated.
        while (true)
        {
            while (i < tokens.Count && !IsWordAt(tokens, i, "AS"))
            {
                i = IsSymbolAt(tokens, i, '(') ? SkipGroup(tokens, i) : i + 1;
            }

            i++;

            if (IsWordAt(tokens, i, "NOT"))
            {
                i++;
            }

            if (IsWordAt(tokens, i, "MATERIALIZED"))
            {
                i++;
            }

            if (!IsSymbolAt(tokens, i, '('))
            {
                return SqlStatementKind.Unknown;
            }

            i = SkipGroup(tokens, i);

            if (!IsSymbolAt(tokens, i, ','))
            {
                return ClassifyAt(tokens, i);
            }

            i++;
        }
    }

    private static bool HasPragmaAssignment(List<SqlToken> tokens, int start) =>
        tokens.Skip(start).Any(token => token is { Kind: TokenKind.Symbol, Text: "=" or "(" });

    private static bool HasMultipleStatements(List<SqlToken> tokens)
    {
        var semicolons = 0;
        var lastIndex = -1;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (IsSymbolAt(tokens, i, ';'))
            {
                semicolons++;
                lastIndex = i;
            }
        }

        return semicolons > 1 || (semicolons == 1 && lastIndex != tokens.Count - 1);
    }

    private static bool HasLockingClause(List<SqlToken> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (IsWord(tokens[i], "LOCK"))
            {
                return true;
            }

            if (IsWord(tokens[i], "FOR") && _lockingFollowers.Any(word => IsWordAt(tokens, i + 1, word)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStatementKeyword(List<SqlToken> tokens) =>
        tokens.Any(token => token.Kind == TokenKind.Word && _statementKeywords.Contains(token.Text.ToUpperInvariant()));

    /// <summary>
    /// Splits SQL into words, symbols and opaque quoted sections. Comments and whitespace are dropped.
    /// Whenever the text contains something whose lexing differs between dialects, the list ends with an
    /// <see cref="TokenKind.Unsafe"/> token, which makes every check fail closed.
    /// </summary>
    private static List<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>();
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '-' && next == '-')
            {
                // MySQL only treats "--" as a comment when whitespace follows it, so anything else is ambiguous.
                if (i + 2 < sql.Length && !char.IsWhiteSpace(sql[i + 2]))
                {
                    return Abort(tokens);
                }

                var eol = sql.IndexOf('\n', i);
                i = eol < 0 ? sql.Length : eol + 1;
            }
            else if (c == '/' && next == '*')
            {
                // "/*!" is executed by MySQL, and block comments nest in some dialects but not others.
                if (i + 2 < sql.Length && sql[i + 2] == '!')
                {
                    return Abort(tokens);
                }

                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);

                if (end < 0 || sql.IndexOf("/*", i + 2, end - i - 2, StringComparison.Ordinal) >= 0)
                {
                    return Abort(tokens);
                }

                i = end + 2;
            }
            else if (c == '\'' || c == '"')
            {
                // Backslash escapes are not portable (standard SQL reads 'C:\' as a complete literal, MySQL does not).
                var end = SkipDelimited(sql, i, c);

                if (end < 0 || sql.IndexOf('\\', i, end - i) >= 0)
                {
                    return Abort(tokens);
                }

                tokens.Add(new SqlToken(TokenKind.Quoted, string.Empty));
                i = end;
            }
            else if (c == '[' || c == '`')
            {
                var end = SkipDelimited(sql, i, c == '[' ? ']' : '`');

                if (end < 0)
                {
                    return Abort(tokens);
                }

                tokens.Add(new SqlToken(TokenKind.Quoted, string.Empty));
                i = end;
            }
            else if (c == '$' && IsDollarQuoteStart(sql, i))
            {
                return Abort(tokens);
            }
            else if (char.IsLetterOrDigit(c) || c == '_')
            {
                var end = i + 1;

                while (end < sql.Length && (char.IsLetterOrDigit(sql[end]) || sql[end] is '_' or '$'))
                {
                    end++;
                }

                tokens.Add(new SqlToken(TokenKind.Word, sql[i..end]));
                i = end;
            }
            else
            {
                tokens.Add(new SqlToken(TokenKind.Symbol, c.ToString()));
                i++;
            }
        }

        return tokens;
    }

    /// <summary>
    /// Returns the index just past the closing delimiter of the quoted section that starts at <paramref name="start"/>,
    /// treating a doubled delimiter as an escaped one, or <c>-1</c> when the section is not terminated.
    /// </summary>
    private static int SkipDelimited(string sql, int start, char close)
    {
        var i = start + 1;

        while (i < sql.Length)
        {
            if (sql[i] == close)
            {
                if (i + 1 < sql.Length && sql[i + 1] == close)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return -1;
    }

    /// <summary>
    /// Detects the start of a dollar-quoted string (<c>$$</c> or <c>$tag$</c>). Positional parameters such as <c>$1</c> do not match.
    /// </summary>
    private static bool IsDollarQuoteStart(string sql, int index)
    {
        var j = index + 1;

        if (j < sql.Length && sql[j] == '$')
        {
            return true;
        }

        if (j >= sql.Length || !(char.IsLetter(sql[j]) || sql[j] == '_'))
        {
            return false;
        }

        while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_'))
        {
            j++;
        }

        return j < sql.Length && sql[j] == '$';
    }

    /// <summary>
    /// Returns the index just past the parenthesis that closes the one at <paramref name="open"/>, or the token count when unbalanced.
    /// </summary>
    private static int SkipGroup(List<SqlToken> tokens, int open)
    {
        var depth = 0;

        for (var i = open; i < tokens.Count; i++)
        {
            if (IsSymbolAt(tokens, i, '('))
            {
                depth++;
            }
            else if (IsSymbolAt(tokens, i, ')'))
            {
                depth--;

                if (depth == 0)
                {
                    return i + 1;
                }
            }
        }

        return tokens.Count;
    }

    private static List<SqlToken> Abort(List<SqlToken> tokens)
    {
        tokens.Add(new SqlToken(TokenKind.Unsafe, string.Empty));

        return tokens;
    }

    private static string? KeywordAt(List<SqlToken> tokens, int index) =>
        index < tokens.Count && tokens[index].Kind == TokenKind.Word
            ? tokens[index].Text.ToUpperInvariant()
            : null;

    private static bool IsWord(SqlToken token, string keyword) =>
        token.Kind == TokenKind.Word && token.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase);

    private static bool IsWordAt(List<SqlToken> tokens, int index, string keyword) =>
        index < tokens.Count && IsWord(tokens[index], keyword);

    private static bool IsSymbolAt(List<SqlToken> tokens, int index, char symbol) =>
        index < tokens.Count && tokens[index] is { Kind: TokenKind.Symbol, Text.Length: 1 } token && token.Text[0] == symbol;

    private enum TokenKind
    {
        Word,
        Symbol,
        Quoted,
        Unsafe,
    }

    private readonly record struct SqlToken(TokenKind Kind, string Text);
}
