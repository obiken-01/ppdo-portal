namespace PPDO.Application.DTOs.Notifications;

/// <summary>One feed entry (PPDO-221, <c>Notification_Centre_Spec.md</c> §4). Times are UTC.</summary>
public sealed record NotificationDto(
    long Id, string Type, string Text, string Link, DateTime CreatedAt, DateTime? ReadAt);

/// <summary>A page of the caller's feed, newest first, and the total for the same filter.</summary>
public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, int Total);

/// <summary>The bell's unread count.</summary>
public sealed record NotificationCountDto(int Count);
