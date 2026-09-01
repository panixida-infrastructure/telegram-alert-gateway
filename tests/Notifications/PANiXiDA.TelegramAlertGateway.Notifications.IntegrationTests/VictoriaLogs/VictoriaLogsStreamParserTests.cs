using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.VictoriaLogs;

public sealed class VictoriaLogsStreamParserTests
{
    [Fact(DisplayName = "Parse should read stream fields when value uses logsql syntax")]
    public void Parse_Should_ReadStreamFields_When_ValueUsesLogsqlSyntax()
    {
        const string value =
            "{deployment.environment=\"kube-system\",platform.name=\"core-platform\",service.name=\"metrics-server\"}";

        var fields = VictoriaLogsStreamParser.Parse(value);

        fields.Count.ShouldBe(3);
        fields["deployment.environment"].ShouldBe("kube-system");
        fields["platform.name"].ShouldBe("core-platform");
        fields["service.name"].ShouldBe("metrics-server");
    }

    [Fact(DisplayName = "Parse should unescape stream field value when value is quoted")]
    public void Parse_Should_UnescapeStreamFieldValue_When_ValueIsQuoted()
    {
        const string value = "{service.name=\"metrics\\\"server\",path=\"C:\\\\logs\",raw=\"\\x2F\"}";

        var fields = VictoriaLogsStreamParser.Parse(value);

        fields["service.name"].ShouldBe("metrics\"server");
        fields["path"].ShouldBe("C:\\logs");
        fields["raw"].ShouldBe("\\x2F");
    }
}
