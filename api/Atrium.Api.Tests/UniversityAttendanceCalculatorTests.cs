using Atrium.Api.Data.Entities;
using Atrium.Api.University;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class UniversityAttendanceCalculatorTests
{
    private static UniversityCourseActivity CreateActivity(
        string type,
        int? minCount = null,
        decimal? minPercent = null,
        int? plannedTotal = null,
        params string[] itemStatuses)
    {
        var activity = new UniversityCourseActivity
        {
            Id = Guid.NewGuid(),
            CourseId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ActivityType = type,
            MinAttendanceCount = minCount,
            MinAttendancePercent = minPercent,
            PlannedTotalCount = plannedTotal,
        };

        foreach (var status in itemStatuses)
        {
            activity.Items.Add(new UniversityActivityItem
            {
                Id = Guid.NewGuid(),
                ActivityId = activity.Id,
                CourseId = activity.CourseId,
                UserId = activity.UserId,
                Title = "Item",
                AttendanceStatus = status,
            });
        }

        return activity;
    }

    [Theory]
    [InlineData("semester_assignment")]
    [InlineData("midterm")]
    [InlineData("exam")]
    public void NonAttendanceActivityTypes_ReturnNotApplicable(string activityType)
    {
        var activity = CreateActivity(activityType, itemStatuses: ["pending", "attended"]);
        var summary = UniversityAttendanceCalculator.Calculate(activity);

        Assert.False(summary.SupportsAttendance);
        Assert.False(summary.HasRequirement);
        Assert.Equal("not_applicable", summary.Status);
        Assert.Null(summary.RequiredCount);
        Assert.Null(summary.RemainingRequired);
        Assert.Equal(1, summary.AttendedCount);
        Assert.Equal(1, summary.PendingCount);
    }

    [Fact]
    public void HomeworkWithoutRequirement_ReturnsNotConfigured()
    {
        var activity = CreateActivity("homework_assignment", itemStatuses: ["pending", "attended"]);
        var summary = UniversityAttendanceCalculator.Calculate(activity);

        Assert.True(summary.SupportsAttendance);
        Assert.False(summary.HasRequirement);
        Assert.Equal("not_configured", summary.Status);
        Assert.Null(summary.RequiredCount);
        Assert.Null(summary.RemainingRequired);
        Assert.Equal(1, summary.AttendedCount);
        Assert.Equal(1, summary.PendingCount);
        Assert.Equal(50m, summary.CurrentRatePercent);
        Assert.Equal(100m, summary.RecordedRatePercent);
    }

    [Fact]
    public void PercentageRequirement_WithNoItemsAndNoPlannedTotal_ReturnsNoData()
    {
        var activity = CreateActivity("lecture", minPercent: 70m);
        var summary = UniversityAttendanceCalculator.Calculate(activity);

        Assert.True(summary.SupportsAttendance);
        Assert.True(summary.HasRequirement);
        Assert.Equal("no_data", summary.Status);
        Assert.Null(summary.RequiredCount);
        Assert.Null(summary.RemainingRequired);
        Assert.Equal(0, summary.EffectiveTotalCount);
    }

    [Fact]
    public void PercentageRequirement_UsesExactCeilingAndExcludesExcusedSessions()
    {
        // 10 items, 2 excused -> 8 effective; 70% of 8 = 5.6 -> ceil = 6
        var activity = CreateActivity(
            "laboratory",
            minPercent: 70m,
            itemStatuses: ["attended", "attended", "missed", "excused", "excused", "pending", "pending", "pending", "pending", "pending"]);

        var summary = UniversityAttendanceCalculator.Calculate(activity);

        Assert.Equal(8, summary.EffectiveTotalCount);
        Assert.Equal(6, summary.RequiredCount);
        Assert.Equal(4, summary.RemainingRequired);
        Assert.Equal(5, summary.RemainingOpportunities);
        Assert.Equal(1, summary.MaxAllowedMissesLeft);
        Assert.Equal("on_track", summary.Status);
    }

    [Fact]
    public void PercentageRequirement_ExactDecimalBoundary_DoesNotRoundUp()
    {
        // 70% of 10 = exactly 7.0 -> 7 (not 8)
        var activity = CreateActivity("practice", minPercent: 70m, plannedTotal: 10);
        var summary = UniversityAttendanceCalculator.Calculate(activity);

        Assert.Equal(10, summary.EffectiveTotalCount);
        Assert.Equal(7, summary.RequiredCount);
        Assert.Equal(7, summary.RemainingRequired);
        Assert.Equal(3, summary.MaxAllowedMissesLeft);
        Assert.Equal("on_track", summary.Status);
    }

    [Fact]
    public void BothCountAndPercentSet_TakesStricterRequirement()
    {
        // Planned 14, 70% -> ceil(9.8) = 10; minCount = 11 -> required = 11
        var activity = CreateActivity("lecture", minCount: 11, minPercent: 70m, plannedTotal: 14, "attended", "attended");
        var summary = UniversityAttendanceCalculator.Calculate(activity);

        Assert.Equal(11, summary.RequiredCount);
        Assert.Equal(9, summary.RemainingRequired);
        Assert.Equal(12, summary.RemainingOpportunities);
        Assert.Equal(3, summary.MaxAllowedMissesLeft);
        Assert.Equal("on_track", summary.Status);
    }

    [Fact]
    public void ZeroMissesLeft_ReturnsAtRisk()
    {
        // Planned 5, required 4, 2 attended, 1 missed -> 2 left, need 2 -> at_risk
        var activity = CreateActivity("practice", minCount: 4, plannedTotal: 5, itemStatuses: ["attended", "attended", "missed", "pending", "pending"]);
        var summary = UniversityAttendanceCalculator.Calculate(activity);

        Assert.Equal(2, summary.RemainingRequired);
        Assert.Equal(2, summary.RemainingOpportunities);
        Assert.Equal(0, summary.MaxAllowedMissesLeft);
        Assert.Equal("at_risk", summary.Status);
    }

    [Fact]
    public void PlannedTotal_TooManyMissed_ReturnsImpossible()
    {
        // Planned 5, required 4, 1 attended, 2 missed -> only 2 opportunities left, need 3 -> impossible
        var activity = CreateActivity("laboratory", minCount: 4, plannedTotal: 5, itemStatuses: ["attended", "missed", "missed", "pending", "pending"]);
        var summary = UniversityAttendanceCalculator.Calculate(activity);

        Assert.Equal(3, summary.RemainingRequired);
        Assert.Equal(2, summary.RemainingOpportunities);
        Assert.Null(summary.MaxAllowedMissesLeft);
        Assert.Equal("impossible", summary.Status);
    }

    [Fact]
    public void NoPlannedTotal_FewItemsAddedSoFar_DoesNotFalselyClaimImpossible()
    {
        // MinCount = 5, no planned total, only 2 items added so far (both pending) -> at_risk (not impossible)
        var activity = CreateActivity("homework_assignment", minCount: 5, itemStatuses: ["pending", "pending"]);
        var summary = UniversityAttendanceCalculator.Calculate(activity);

        Assert.Equal(5, summary.RequiredCount);
        Assert.Equal(5, summary.RemainingRequired);
        Assert.Equal("at_risk", summary.Status);

        // And with 0 items added yet -> on_track (user hasn't entered items yet)
        var emptyActivity = CreateActivity("homework_assignment", minCount: 5);
        var emptySummary = UniversityAttendanceCalculator.Calculate(emptyActivity);
        Assert.Equal(5, emptySummary.RequiredCount);
        Assert.Equal(5, emptySummary.RemainingRequired);
        Assert.Equal("on_track", emptySummary.Status);
    }

    [Fact]
    public void RequirementMet_AndZeroRequirement_ReturnMet()
    {
        var met = CreateActivity("lecture", minCount: 3, plannedTotal: 5, itemStatuses: ["attended", "attended", "attended", "missed", "pending"]);
        var metSummary = UniversityAttendanceCalculator.Calculate(met);
        Assert.Equal(0, metSummary.RemainingRequired);
        Assert.Equal("met", metSummary.Status);

        var zeroReq = CreateActivity("lecture", minCount: 0);
        var zeroSummary = UniversityAttendanceCalculator.Calculate(zeroReq);
        Assert.Equal(0, zeroSummary.RequiredCount);
        Assert.Equal(0, zeroSummary.RemainingRequired);
        Assert.Equal("met", zeroSummary.Status);
    }
}
