using System.Text.Json;
using DotnetMcpToolkit.Options;

namespace DotnetMcpToolkit.Tests.Options;

public sealed class OptionsValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "options-" + Guid.NewGuid().ToString("N"));

    public OptionsValidationTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FileSystemOptions_EmptyRoot_ThrowsArgumentNamingRoot(string? root)
    {
        var options = new FileSystemOptions { Root = root! };

        var ex = Assert.ThrowsAny<ArgumentException>(options.Validate);

        Assert.Equal("Root", ex.ParamName);
    }

    [Fact]
    public void FileSystemOptions_RootDoesNotExist_ThrowsArgumentNamingRoot()
    {
        var options = new FileSystemOptions { Root = Path.Combine(_root, "missing") };

        var ex = Assert.Throws<ArgumentException>(options.Validate);

        Assert.Equal("Root", ex.ParamName);
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void FileSystemOptions_ExistingRootWithDefaults_DoesNotThrow()
    {
        var options = new FileSystemOptions { Root = _root };

        var ex = Record.Exception(options.Validate);

        Assert.Null(ex);
    }

    [Fact]
    public void FileSystemOptions_MaxListEntriesBelowOne_ThrowsArgumentOutOfRangeNamingProperty()
    {
        var options = new FileSystemOptions { Root = _root, MaxListEntries = 0 };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

        Assert.Equal("MaxListEntries", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void GitOptions_EmptyRepositoryPath_ThrowsArgumentNamingRepositoryPath(string? repositoryPath)
    {
        var options = new GitOptions { RepositoryPath = repositoryPath! };

        var ex = Assert.ThrowsAny<ArgumentException>(options.Validate);

        Assert.Equal("RepositoryPath", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void GitOptions_MaxLogCountOutsideOneToFiveHundred_ThrowsArgumentOutOfRangeNamingProperty(int maxLogCount)
    {
        var options = new GitOptions { RepositoryPath = _root, MaxLogCount = maxLogCount };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

        Assert.Equal("MaxLogCount", ex.ParamName);
    }

    [Fact]
    public void DatabaseOptions_EmptyConnectionString_ThrowsArgumentNamingConnectionString()
    {
        var options = new DatabaseOptions { ConnectionString = "" };

        var ex = Assert.Throws<ArgumentException>(options.Validate);

        Assert.Equal("ConnectionString", ex.ParamName);
    }

    [Fact]
    public void DatabaseOptions_UndefinedProvider_ThrowsArgumentOutOfRangeNamingProvider()
    {
        var options = new DatabaseOptions { ConnectionString = "Data Source=:memory:", Provider = (DatabaseProvider)99 };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

        Assert.Equal("Provider", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public void DatabaseOptions_MaxRowsOutsideOneToFiveThousand_ThrowsArgumentOutOfRangeNamingProperty(int maxRows)
    {
        var options = new DatabaseOptions { ConnectionString = "Data Source=:memory:", MaxRows = maxRows };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

        Assert.Equal("MaxRows", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void DatabaseOptions_CommandTimeoutOutsideOneToSixHundred_ThrowsArgumentOutOfRangeNamingProperty(int timeoutSeconds)
    {
        var options = new DatabaseOptions { ConnectionString = "Data Source=:memory:", CommandTimeoutSeconds = timeoutSeconds };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

        Assert.Equal("CommandTimeoutSeconds", ex.ParamName);
    }

    [Theory]
    [InlineData("localhost:2375")]
    [InlineData("http://localhost:2375")]
    [InlineData("")]
    public void DockerOptions_HostWithoutSupportedScheme_ThrowsArgumentNamingDockerHost(string dockerHost)
    {
        var options = new DockerOptions { DockerHost = dockerHost };

        var ex = Assert.Throws<ArgumentException>(options.Validate);

        Assert.Equal("DockerHost", ex.ParamName);
    }

    [Theory]
    [InlineData("unix:///var/run/docker.sock")]
    [InlineData("tcp://localhost:2375")]
    [InlineData("npipe:////./pipe/docker_engine")]
    [InlineData("ssh://user@host")]
    public void DockerOptions_HostWithSupportedScheme_DoesNotThrow(string dockerHost)
    {
        var options = new DockerOptions { DockerHost = dockerHost };

        var ex = Record.Exception(options.Validate);

        Assert.Null(ex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void DockerOptions_DefaultTimeoutOutsideOneToSixHundred_ThrowsArgumentOutOfRangeNamingProperty(int timeoutSeconds)
    {
        var options = new DockerOptions { DefaultTimeoutSeconds = timeoutSeconds };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

        Assert.Equal("DefaultTimeoutSeconds", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void McpServerOptions_EmptyName_ThrowsArgumentNamingName(string? name)
    {
        var options = new McpServerOptions { Name = name! };

        var ex = Assert.ThrowsAny<ArgumentException>(options.Validate);

        Assert.Equal("Name", ex.ParamName);
    }

    [Fact]
    public void McpServerOptions_InvalidChildPack_CascadesValidationToThePack()
    {
        var options = new McpServerOptions
        {
            Name = "test",
            Database = new DatabaseOptions { ConnectionString = "" },
        };

        var ex = Assert.Throws<ArgumentException>(options.Validate);

        Assert.Equal("ConnectionString", ex.ParamName);
    }

    [Fact]
    public void FromOptions_FileSystemOnly_RegistersFilesystemTools()
    {
        var options = new McpServerOptions
        {
            Name = "test",
            FileSystem = new FileSystemOptions { Root = _root },
        };

        var builder = McpServerBuilder.FromOptions(options);

        Assert.Equal(["fs_list", "fs_read_file", "fs_search", "fs_write_file"], builder.Tools.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task FromOptions_MaxListEntries_LimitsFsListResults()
    {
        for (var i = 0; i < 3; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"file{i}.txt"), "x");
        }

        var options = new McpServerOptions
        {
            Name = "test",
            FileSystem = new FileSystemOptions { Root = _root, MaxListEntries = 2 },
        };
        var list = McpServerBuilder.FromOptions(options).Tools.Single(t => t.Name == "fs_list");

        var result = await list.InvokeAsync(null);

        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Equal(2, doc.RootElement.GetProperty("entries").GetArrayLength());
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
    }
}
