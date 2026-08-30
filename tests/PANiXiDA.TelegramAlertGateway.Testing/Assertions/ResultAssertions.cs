namespace PANiXiDA.TelegramAlertGateway.Testing.Assertions;

public static class ResultAssertions
{
    public static Error ShouldHaveSingleError(
        this Result result,
        ErrorType type,
        string message)
    {
        result.IsFailure.ShouldBeTrue();
        result.Errors.Count.ShouldBe(1);

        var error = result.Errors[0];
        error.Type.ShouldBe(type);
        error.Message.ShouldBe(message);

        return error;
    }

    public static void ShouldHaveField(
        this Error error,
        string field)
    {
        error.Metadata.ShouldContainKey(Error.FieldMetadataKey);
        error.Metadata[Error.FieldMetadataKey].ShouldBe(field);
    }
}
