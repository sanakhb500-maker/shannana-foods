using Erp.Application.Accounting;
using Erp.Domain.Entities;
using FluentValidation;

namespace Erp.Application.Validators;

public class AccountValidator : AbstractValidator<Account>
{
    public AccountValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9._-]+$").WithMessage("Code may contain letters, digits, '.', '_' and '-' only.");
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).IsInEnum();
    }
}

public class JournalEntryInputValidator : AbstractValidator<JournalEntryInput>
{
    public JournalEntryInputValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.Description).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least two lines are required.");
        RuleFor(x => x.Lines).Must(l => l.Count >= 2).When(x => x.Lines is not null).WithMessage("At least two lines are required.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.AccountId).GreaterThan(0);
            l.RuleFor(i => i.Debit).GreaterThanOrEqualTo(0);
            l.RuleFor(i => i.Credit).GreaterThanOrEqualTo(0);
            l.RuleFor(i => i).Must(i => (i.Debit > 0) ^ (i.Credit > 0)).WithName("Amount").WithMessage("Each line needs exactly one of debit or credit, greater than zero.");
            l.RuleFor(i => i.Description).MaximumLength(200);
            l.RuleFor(i => i.PartyType).IsInEnum();
            l.RuleFor(i => i.PartyId).NotNull().When(i => i.PartyType != PartyType.None).WithMessage("A party id is required with a party type.");
        });
    }
}
