using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Dashboard;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="DashboardService"/> — written first (TDD).
/// ICalendarEventRepository, IHolidayProvider, IRepository&lt;PurchaseRequest&gt;,
/// and IRepository&lt;ItemMaster&gt; are all mocked.
/// </summary>
public sealed class DashboardServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static User MakeUser(UserRole role = UserRole.Staff) => new()
    {
        Id       = Guid.NewGuid(),
        FullName = "Test User",
        Email    = "test@ppdo.gov.ph",
        PasswordHash = "hash",
        Role     = role,
        Division = Division.Admin,
        IsActive = true,
    };

    private static CalendarEvent MakeOfficeEvent(DateTime start) => new()
    {
        Id          = Guid.NewGuid(),
        Title       = "Office Meeting",
        StartDate   = start,
        IsAllDay    = false,
        EventType   = "Office",
        CreatedById = Guid.NewGuid(),
    };

    private static CalendarEvent MakePersonalEvent(DateTime start, Guid userId) => new()
    {
        Id          = Guid.NewGuid(),
        Title       = "Personal Appointment",
        StartDate   = start,
        IsAllDay    = false,
        EventType   = "Personal",
        CreatedById = userId,
    };

    private DashboardService BuildSut(
        Mock<ICalendarEventRepository>   eventRepo,
        Mock<IHolidayProvider>           holidays,
        Mock<IRepository<PurchaseRequest>> prRepo,
        Mock<IRepository<ItemMaster>>    itemRepo)
        => new(eventRepo.Object, holidays.Object, prRepo.Object, itemRepo.Object);

    // ── GetEventsAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEventsAsync_ReturnsMergedOfficePersonalAndHolidays()
    {
        User user = MakeUser();
        DateTime start = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        CalendarEvent officeEvent   = MakeOfficeEvent(start.AddDays(5));
        CalendarEvent personalEvent = MakePersonalEvent(start.AddDays(10), user.Id);

        CalendarEventDto holiday = new(null, "Independence Day", null,
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc), null, true, "Holiday", "Static");

        Mock<ICalendarEventRepository>   eventRepo = new();
        Mock<IHolidayProvider>           holidays  = new();
        Mock<IRepository<PurchaseRequest>> prRepo  = new();
        Mock<IRepository<ItemMaster>>    itemRepo  = new();

        eventRepo.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([officeEvent, personalEvent]);

        holidays.Setup(h => h.GetPhHolidaysAsync(2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync([holiday]);

        IReadOnlyList<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .GetEventsAsync(2026, 6, user.Id);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, e => e.EventType == "Office");
        Assert.Contains(result, e => e.EventType == "Personal");
        Assert.Contains(result, e => e.EventType == "Holiday");
    }

    [Fact]
    public async Task GetEventsAsync_FiltersHolidaysToRequestedMonth()
    {
        User user = MakeUser();

        CalendarEventDto juneHoliday = new(null, "Independence Day", null,
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc), null, true, "Holiday", "Static");
        CalendarEventDto julyHoliday = new(null, "Ninoy Aquino Day", null,
            new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc), null, true, "Holiday", "Static");

        Mock<ICalendarEventRepository>     eventRepo = new();
        Mock<IHolidayProvider>             holidays  = new();
        Mock<IRepository<PurchaseRequest>> prRepo    = new();
        Mock<IRepository<ItemMaster>>      itemRepo  = new();

        eventRepo.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        holidays.Setup(h => h.GetPhHolidaysAsync(2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync([juneHoliday, julyHoliday]);

        IReadOnlyList<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .GetEventsAsync(2026, 6, user.Id);

        Assert.Single(result);
        Assert.Equal("Independence Day", result[0].Title);
    }

    [Fact]
    public async Task GetEventsAsync_WhenHolidayProviderFails_StillReturnsDbEvents()
    {
        User user = MakeUser();
        CalendarEvent officeEvent = MakeOfficeEvent(new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc));

        Mock<ICalendarEventRepository>     eventRepo = new();
        Mock<IHolidayProvider>             holidays  = new();
        Mock<IRepository<PurchaseRequest>> prRepo    = new();
        Mock<IRepository<ItemMaster>>      itemRepo  = new();

        eventRepo.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([officeEvent]);

        holidays.Setup(h => h.GetPhHolidaysAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Nager.Date unavailable"));

        IReadOnlyList<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .GetEventsAsync(2026, 6, user.Id);

        Assert.Single(result);
        Assert.Equal("Office Meeting", result[0].Title);
    }

    // ── CreateEventAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateEventAsync_WithValidOfficeEvent_ReturnsOk()
    {
        User user = MakeUser();
        CreateCalendarEventDto dto = new(
            "Team Meeting", null,
            new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc), null,
            false, "Office");

        Mock<ICalendarEventRepository>     eventRepo = new();
        Mock<IHolidayProvider>             holidays  = new();
        Mock<IRepository<PurchaseRequest>> prRepo    = new();
        Mock<IRepository<ItemMaster>>      itemRepo  = new();

        eventRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        eventRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .CreateEventAsync(user, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("Team Meeting", result.Value!.Title);
        Assert.Equal("Office", result.Value.EventType);
    }

    [Fact]
    public async Task CreateEventAsync_WithPersonalEvent_SetsCreatorId()
    {
        User user = MakeUser();
        CreateCalendarEventDto dto = new(
            "Doctor Appointment", null,
            new DateTime(2026, 6, 20, 8, 0, 0, DateTimeKind.Utc), null,
            false, "Personal");

        CalendarEvent? saved = null;
        Mock<ICalendarEventRepository>     eventRepo = new();
        Mock<IHolidayProvider>             holidays  = new();
        Mock<IRepository<PurchaseRequest>> prRepo    = new();
        Mock<IRepository<ItemMaster>>      itemRepo  = new();

        eventRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CalendarEvent, CancellationToken>((e, _) => saved = e)
            .Returns(Task.CompletedTask);
        eventRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await BuildSut(eventRepo, holidays, prRepo, itemRepo).CreateEventAsync(user, dto);

        Assert.NotNull(saved);
        Assert.Equal(user.Id, saved!.CreatedById);
    }

    [Fact]
    public async Task CreateEventAsync_WithInvalidEventType_ReturnsBadRequest()
    {
        User user = MakeUser();
        CreateCalendarEventDto dto = new(
            "Unknown", null,
            new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc), null,
            false, "Holiday"); // "Holiday" is not a valid create type

        Mock<ICalendarEventRepository>     eventRepo = new();
        Mock<IHolidayProvider>             holidays  = new();
        Mock<IRepository<PurchaseRequest>> prRepo    = new();
        Mock<IRepository<ItemMaster>>      itemRepo  = new();

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .CreateEventAsync(user, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateEventAsync_WithEmptyTitle_ReturnsBadRequest()
    {
        User user = MakeUser();
        CreateCalendarEventDto dto = new(
            "", null,
            new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc), null,
            false, "Office");

        Mock<ICalendarEventRepository>     eventRepo = new();
        Mock<IHolidayProvider>             holidays  = new();
        Mock<IRepository<PurchaseRequest>> prRepo    = new();
        Mock<IRepository<ItemMaster>>      itemRepo  = new();

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .CreateEventAsync(user, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── GetStatsAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_ReturnsPRCountsByStatus()
    {
        List<PurchaseRequest> prs =
        [
            new() { Id = Guid.NewGuid(), Status = PRStatus.Open },
            new() { Id = Guid.NewGuid(), Status = PRStatus.Open },
            new() { Id = Guid.NewGuid(), Status = PRStatus.PartiallyDelivered },
            new() { Id = Guid.NewGuid(), Status = PRStatus.FullyDelivered },
        ];

        Mock<ICalendarEventRepository>     eventRepo = new();
        Mock<IHolidayProvider>             holidays  = new();
        Mock<IRepository<PurchaseRequest>> prRepo    = new();
        Mock<IRepository<ItemMaster>>      itemRepo  = new();

        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(prs);
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>());

        DashboardStatsDto stats =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo).GetStatsAsync();

        Assert.Equal(4, stats.TotalPRs);
        Assert.Equal(2, stats.OpenPRs);
        Assert.Equal(1, stats.PartiallyDeliveredPRs);
        Assert.Equal(1, stats.FullyDeliveredPRs);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsItemCounts()
    {
        List<ItemMaster> items =
        [
            new() { Id = Guid.NewGuid(), IsNewItem = false },
            new() { Id = Guid.NewGuid(), IsNewItem = false },
            new() { Id = Guid.NewGuid(), IsNewItem = true },
        ];

        Mock<ICalendarEventRepository>     eventRepo = new();
        Mock<IHolidayProvider>             holidays  = new();
        Mock<IRepository<PurchaseRequest>> prRepo    = new();
        Mock<IRepository<ItemMaster>>      itemRepo  = new();

        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest>());
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        DashboardStatsDto stats =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo).GetStatsAsync();

        Assert.Equal(3, stats.TotalItems);
        Assert.Equal(1, stats.NewItemsPendingReview);
    }
}
