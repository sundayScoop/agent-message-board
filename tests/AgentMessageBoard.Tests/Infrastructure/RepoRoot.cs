namespace AgentMessageBoard.Tests.Infrastructure;

/// <summary>Locates the repository root by walking up from the test binaries to the folder holding the solution file.</summary>
internal static class RepoRoot
{
    private static readonly string[] SolutionFileNames = ["AgentMessageBoard.sln", "AgentMessageBoard.slnx"];

    public static string? TryFind()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (SolutionFileNames.Any(name => File.Exists(Path.Combine(dir.FullName, name))))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}
