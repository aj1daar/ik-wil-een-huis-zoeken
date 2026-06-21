namespace IWEHZ.Domain.Models;

public class City
{
    public int Id { get; set; }
    public string NameNl { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<UserCity> UserCities { get; set; } = [];
}
