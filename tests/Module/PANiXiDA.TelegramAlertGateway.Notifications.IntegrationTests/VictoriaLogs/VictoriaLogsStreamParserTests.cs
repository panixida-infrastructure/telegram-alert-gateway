using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.VictoriaLogs;

public sealed class VictoriaLogsStreamParserTests
{
    [Fact(DisplayName = "VictoriaLogs stream fields are parsed without JSON exceptions")]
    public void Parse_Should_Read_Stream_Fields_When_Value_Uses_Logsql_Syntax()
    {
        const string value =
            "{deployment.environment=\"kube-system\",platform.name=\"core-platform\",service.name=\"metrics-server\"}";

        var fields = VictoriaLogsStreamParser.Parse(value);

        fields.Count.ShouldBe(3);
        fields["deployment.environment"].ShouldBe("kube-system");
        fields["platform.name"].ShouldBe("core-platform");
        fields["service.name"].ShouldBe("metrics-server");
    }

    [Fact(DisplayName = "VictoriaLogs quoted stream field values are unescaped")]
    public void Parse_Should_Unescape_Stream_Field_Value_When_Value_Is_Quoted()
    {
        const string value = "{service.name=\"metrics\\\"server\",path=\"C:\\\\logs\",raw=\"\\x2F\"}";

        var fields = VictoriaLogsStreamParser.Parse(value);

        fields["service.name"].ShouldBe("metrics\"server");
        fields["path"].ShouldBe("C:\\logs");
        fields["raw"].ShouldBe("\\x2F");
    }
}
