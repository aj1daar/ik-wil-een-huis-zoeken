namespace IWEHZ.Domain.Models;

public class User
{
    public int Id { get; set; }
    public long TelegramChatId { get; set; }
    public string? TelegramUsername { get; set; }
    public decimal? MinBudget { get; set; }
    public decimal? MaxBudget { get; set; }
    public bool IsActive { get; set; } = false;
    public bool IsPaused { get; set; } = false;
    public PropertyTypeFilter PropertyTypeFilter { get; set; } = PropertyTypeFilter.Any;
    public OnboardingState OnboardingState { get; set; } = OnboardingState.AwaitingApproval;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<UserCity> UserCities { get; set; } = [];
}

public enum PropertyTypeFilter { Any, Apartment, House, Room }

public enum OnboardingState
{
    AwaitingApproval,
    AwaitingBudget,
    AwaitingCities,
    Completed
}
