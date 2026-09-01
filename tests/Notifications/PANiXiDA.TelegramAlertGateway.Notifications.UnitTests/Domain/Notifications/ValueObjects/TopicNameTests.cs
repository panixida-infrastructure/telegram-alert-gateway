using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Domain.Notifications.ValueObjects;

public sealed class TopicNameTests
{
    [Fact(DisplayName = "Topic name should preserve lower kebab case when value is valid")]
    public void Create_Should_ReturnTopic_When_ValueIsValid()
    {
        var result = TopicName.Create(value: "tactical-heroes");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe("tactical-heroes");
    }

    [Fact(DisplayName = "Topic name should reject uppercase input when value is invalid")]
    public void Create_Should_ReturnFailure_When_ValueIsInvalid()
    {
        var result = TopicName.Create(value: "TacticalHeroes");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact(DisplayName = "Topic name should return its value when converted to string")]
    public void ToString_Should_ReturnValue_When_ConvertedToString()
    {
        var topic = TopicName.Create(value: "tactical-heroes").Value;

        var result = topic.ToString();

        result.ShouldBe("tactical-heroes");
    }
}
