using IWEHZ.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace IWEHZ.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<UserCity> UserCities => Set<UserCity>();
    public DbSet<RentalListing> RentalListings => Set<RentalListing>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.TelegramChatId).HasColumnName("telegram_chat_id");
            e.Property(x => x.TelegramUsername).HasColumnName("telegram_username");
            e.Property(x => x.MinBudget).HasColumnName("min_budget").HasColumnType("numeric(10,2)");
            e.Property(x => x.MaxBudget).HasColumnName("max_budget").HasColumnType("numeric(10,2)");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.IsPaused).HasColumnName("is_paused");
            e.Property(x => x.OnboardingState).HasColumnName("onboarding_state").HasConversion<string>();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.TelegramChatId).IsUnique();
        });

        modelBuilder.Entity<City>(e =>
        {
            e.ToTable("cities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.NameNl).HasColumnName("name_nl").HasMaxLength(100);
            e.Property(x => x.NameEn).HasColumnName("name_en").HasMaxLength(100);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => x.NameNl);
            e.HasIndex(x => x.NameEn);
        });

        modelBuilder.Entity<UserCity>(e =>
        {
            e.ToTable("user_cities");
            e.HasKey(x => new { x.UserId, x.CityId });
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.CityId).HasColumnName("city_id");
            e.HasOne(x => x.User).WithMany(x => x.UserCities).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.City).WithMany(x => x.UserCities).HasForeignKey(x => x.CityId);
        });

        modelBuilder.Entity<RentalListing>(e =>
        {
            e.ToTable("rental_listings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(500);
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(100);
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(500);
            e.Property(x => x.City).HasColumnName("city").HasMaxLength(100);
            e.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(10,2)");
            e.Property(x => x.PreviousPrice).HasColumnName("previous_price").HasColumnType("numeric(10,2)");
            e.Property(x => x.SourceUrl).HasColumnName("source_url").HasMaxLength(2000);
            e.Property(x => x.ScrapedAt).HasColumnName("scraped_at");
            e.HasIndex(x => new { x.ExternalId, x.Source }).IsUnique();
        });

        modelBuilder.Entity<NotificationLog>(e =>
        {
            e.ToTable("notification_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.ListingId).HasColumnName("listing_id");
            e.Property(x => x.SentAt).HasColumnName("sent_at");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Listing).WithMany(x => x.NotificationLogs).HasForeignKey(x => x.ListingId);
            e.HasIndex(x => new { x.UserId, x.ListingId }).IsUnique();
        });

        SeedCities(modelBuilder);
    }

    private static void SeedCities(ModelBuilder modelBuilder)
    {
        var cities = new[]
        {
            new City { Id = 1,  NameNl = "Amsterdam",    NameEn = "Amsterdam" },
            new City { Id = 2,  NameNl = "Rotterdam",    NameEn = "Rotterdam" },
            new City { Id = 3,  NameNl = "Den Haag",     NameEn = "The Hague" },
            new City { Id = 4,  NameNl = "Utrecht",      NameEn = "Utrecht" },
            new City { Id = 5,  NameNl = "Eindhoven",    NameEn = "Eindhoven" },
            new City { Id = 6,  NameNl = "Groningen",    NameEn = "Groningen" },
            new City { Id = 7,  NameNl = "Tilburg",      NameEn = "Tilburg" },
            new City { Id = 8,  NameNl = "Almere",       NameEn = "Almere" },
            new City { Id = 9,  NameNl = "Breda",        NameEn = "Breda" },
            new City { Id = 10, NameNl = "Nijmegen",     NameEn = "Nijmegen" },
            new City { Id = 11, NameNl = "Enschede",     NameEn = "Enschede" },
            new City { Id = 12, NameNl = "Apeldoorn",    NameEn = "Apeldoorn" },
            new City { Id = 13, NameNl = "Haarlem",      NameEn = "Haarlem" },
            new City { Id = 14, NameNl = "Arnhem",       NameEn = "Arnhem" },
            new City { Id = 15, NameNl = "Zaanstad",     NameEn = "Zaanstad" },
            new City { Id = 16, NameNl = "Amersfoort",   NameEn = "Amersfoort" },
            new City { Id = 17, NameNl = "Maastricht",   NameEn = "Maastricht" },
            new City { Id = 18, NameNl = "Dordrecht",    NameEn = "Dordrecht" },
            new City { Id = 19, NameNl = "Leiden",       NameEn = "Leiden" },
            new City { Id = 20, NameNl = "Zoetermeer",   NameEn = "Zoetermeer" },
            new City { Id = 21, NameNl = "Zwolle",       NameEn = "Zwolle" },
            new City { Id = 22, NameNl = "Deventer",     NameEn = "Deventer" },
            new City { Id = 23, NameNl = "Delft",        NameEn = "Delft" },
            new City { Id = 24, NameNl = "Alkmaar",      NameEn = "Alkmaar" },
            new City { Id = 25, NameNl = "s-Hertogenbosch", NameEn = "Den Bosch" },
        };

        modelBuilder.Entity<City>().HasData(cities);
    }
}
