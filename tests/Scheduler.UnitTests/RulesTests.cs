using Scheduler.Application.Dependencies;
using Scheduler.Application.Executions;
using Scheduler.Application.Reports;

namespace Scheduler.UnitTests;

public class RetryPolicyTests
{
    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(5, 900)] // capped
    [InlineData(40, 900)] // no overflow
    public void Delay_DoublesUpToTheMaximum(int attempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryPolicy.GetDelay(attempt, 30, 900));
    }

    [Fact]
    public void Retries_StopAtTheLimit_AndForInactiveTasks()
    {
        Assert.True(RetryPolicy.ShouldRetry(attempt: 2, maxRetries: 3, taskIsActive: true));
        Assert.False(RetryPolicy.ShouldRetry(attempt: 3, maxRetries: 3, taskIsActive: true));
        Assert.False(RetryPolicy.ShouldRetry(attempt: 0, maxRetries: 3, taskIsActive: false));
    }
}

public class DependencyGraphTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    [Fact]
    public void DirectAndIndirectCycles_AreDetected()
    {
        // B runs after A, C runs after B.
        var graph = new DependencyGraph([new DependencyEdge(B, A), new DependencyEdge(C, B)]);

        Assert.True(graph.WouldCreateCycle(A, C));
        Assert.True(graph.WouldCreateCycle(A, B));
        Assert.True(graph.WouldCreateCycle(A, A));
        Assert.False(graph.WouldCreateCycle(C, A));
    }
}

public class CsvWriterTests
{
    [Fact]
    public void Values_AreQuotedWhenNeeded()
    {
        var csv = CsvWriter.Write(["Name", "Message"], [["Backup, nightly", "He said \"done\"\nnext line"]]);

        Assert.Contains("\"Backup, nightly\"", csv);
        Assert.Contains("\"He said \"\"done\"\"\nnext line\"", csv);
    }

    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("+A1")]
    [InlineData("-1+2")]
    [InlineData("@cmd")]
    public void FormulaLikeValues_CannotRunInASpreadsheet(string value)
    {
        Assert.StartsWith("'", CsvWriter.Escape(value).Trim('"'));
    }
}
