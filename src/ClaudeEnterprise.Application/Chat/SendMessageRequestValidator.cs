using FluentValidation;

namespace ClaudeEnterprise.Application.Chat;

public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message is required.")
            .MaximumLength(32_000);
        RuleFor(x => x.MaxTokens)
            .InclusiveBetween(1, 16_000)
            .When(x => x.MaxTokens.HasValue);
        RuleFor(x => x.Temperature)
            .InclusiveBetween(0.0, 1.0)
            .When(x => x.Temperature.HasValue);
        RuleFor(x => x.Model)
            .MaximumLength(128);
    }
}
