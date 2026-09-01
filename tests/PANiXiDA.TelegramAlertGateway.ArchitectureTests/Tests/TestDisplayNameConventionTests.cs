using System.Text.RegularExpressions;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Tests;

public sealed partial class TestDisplayNameConventionTests
{
    [GeneratedRegex(
        @"^[A-Za-z][\x20-\x5B\x5D-\x7E]* should " +
        @"[a-z0-9][\x20-\x5B\x5D-\x7E]* when " +
        @"[a-z0-9][\x20-\x5B\x5D-\x7E]*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EnglishBehaviorDescriptionPattern();

    [GeneratedRegex(
        @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
        RegexOptions.CultureInvariant)]
    private static partial Regex PascalCaseBoundaryPattern();

    [Fact(DisplayName = "Fact and Theory tests should declare display names when a test is declared")]
    public void FactsAndTheories_Should_DeclareDisplayName_When_ATestIsDeclared()
    {
        var testMethods = TestSourceDiscovery.GetTestMethods();

        Assert.NotEmpty(testMethods);

        var violations = testMethods
            .Where(testMethod =>
                string.IsNullOrWhiteSpace(testMethod.DisplayName))
            .Select(testMethod =>
                $"{testMethod.Location}: {testMethod.Name}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Fact and Theory attributes without a string-literal DisplayName:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Fact and Theory display names should describe test conditions when a test is declared")]
    public void DisplayNames_Should_DescribeTestCondition_When_ATestIsDeclared()
    {
        var violations = TestSourceDiscovery.GetTestMethods()
            .Where(testMethod => testMethod.DisplayName is not null)
            .Select(testMethod => new
            {
                TestMethod = testMethod,
                ExpectedCondition = GetExpectedCondition(testMethod.Name)
            })
            .Where(item =>
                item.ExpectedCondition is null ||
                !EnglishBehaviorDescriptionPattern().IsMatch(
                    item.TestMethod.DisplayName!) ||
                !item.TestMethod.DisplayName!.EndsWith(
                    item.ExpectedCondition,
                    StringComparison.Ordinal))
            .Select(item =>
                $"{item.TestMethod.Location}: \"{item.TestMethod.DisplayName}\"; " +
                $"expected an English '<subject> should <behavior>" +
                $"{item.ExpectedCondition ?? " when <condition>"}' description.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"DisplayName convention violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static string? GetExpectedCondition(string methodName)
    {
        const string conditionSeparator = "_When_";
        var separatorIndex = methodName.IndexOf(
            conditionSeparator,
            StringComparison.Ordinal);

        if (separatorIndex < 0)
        {
            return null;
        }

        var condition = methodName[
            (separatorIndex + conditionSeparator.Length)..];
        var words = PascalCaseBoundaryPattern().Split(condition);

        return " when " + string.Join(" ", words).ToLowerInvariant();
    }
}
