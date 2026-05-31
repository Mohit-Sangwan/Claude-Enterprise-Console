using ClaudeEnterprise.Application.Chat;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace ClaudeEnterprise.Tests;

public sealed class SendMessageRequestValidatorTests
{
    private readonly SendMessageRequestValidator _validator = new();

    [Fact]
    public void Empty_message_is_invalid()
    {
        var result = _validator.TestValidate(new SendMessageRequest(null, "", null, null, null, null));
        result.ShouldHaveValidationErrorFor(x => x.Message);
    }

    [Fact]
    public void Valid_request_passes()
    {
        var result = _validator.TestValidate(new SendMessageRequest(null, "hello", null, null, null, null));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Negative_temperature_is_invalid()
    {
        var result = _validator.TestValidate(new SendMessageRequest(null, "hi", null, null, null, -0.1));
        result.ShouldHaveValidationErrorFor(x => x.Temperature);
    }
}
