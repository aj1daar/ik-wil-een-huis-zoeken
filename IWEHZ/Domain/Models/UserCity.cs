namespace IWEHZ.Domain.Models;

public class UserCity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int CityId { get; set; }
    public City City { get; set; } = null!;
}
