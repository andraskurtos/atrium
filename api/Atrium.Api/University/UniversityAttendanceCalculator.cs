using Atrium.Api.Data.Entities;

namespace Atrium.Api.University;

public static class UniversityAttendanceCalculator
{
    public static AttendanceSummaryDto Calculate(UniversityCourseActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var supportsAttendance = UniversityConstants.SupportsAttendance(activity.ActivityType);
        var items = activity.Items;

        var attendedCount = 0;
        var missedCount = 0;
        var excusedCount = 0;
        var pendingCount = 0;

        foreach (var item in items)
        {
            switch (item.AttendanceStatus)
            {
                case "attended":
                    attendedCount++;
                    break;
                case "missed":
                    missedCount++;
                    break;
                case "excused":
                    excusedCount++;
                    break;
                default:
                    pendingCount++;
                    break;
            }
        }

        var totalItemsCount = items.Count;
        var eligibleScheduledCount = attendedCount + missedCount + pendingCount;
        var recordedCount = attendedCount + missedCount;

        int effectiveTotalCount;
        if (activity.PlannedTotalCount.HasValue)
        {
            var plannedMinusExcused = Math.Max(0, activity.PlannedTotalCount.Value - excusedCount);
            effectiveTotalCount = Math.Max(plannedMinusExcused, eligibleScheduledCount);
        }
        else
        {
            effectiveTotalCount = eligibleScheduledCount;
        }

        var remainingOpportunities = activity.PlannedTotalCount.HasValue
            ? Math.Max(0, effectiveTotalCount - recordedCount)
            : pendingCount;

        decimal? currentRatePercent = effectiveTotalCount > 0
            ? Math.Round((decimal)attendedCount / effectiveTotalCount * 100m, 1, MidpointRounding.AwayFromZero)
            : null;

        decimal? recordedRatePercent = recordedCount > 0
            ? Math.Round((decimal)attendedCount / recordedCount * 100m, 1, MidpointRounding.AwayFromZero)
            : null;

        if (!supportsAttendance)
        {
            return new AttendanceSummaryDto(
                SupportsAttendance: false,
                HasRequirement: false,
                AttendedCount: attendedCount,
                MissedCount: missedCount,
                ExcusedCount: excusedCount,
                PendingCount: pendingCount,
                TotalItemsCount: totalItemsCount,
                EffectiveTotalCount: effectiveTotalCount,
                MinAttendanceCount: null,
                MinAttendancePercent: null,
                PlannedTotalCount: null,
                RequiredCount: null,
                RemainingRequired: null,
                RemainingOpportunities: remainingOpportunities,
                MaxAllowedMissesLeft: null,
                CurrentRatePercent: currentRatePercent,
                RecordedRatePercent: recordedRatePercent,
                Status: "not_applicable");
        }

        var hasCountReq = activity.MinAttendanceCount.HasValue;
        var hasPercentReq = activity.MinAttendancePercent.HasValue;
        var hasRequirement = hasCountReq || hasPercentReq;

        if (!hasRequirement)
        {
            return new AttendanceSummaryDto(
                SupportsAttendance: true,
                HasRequirement: false,
                AttendedCount: attendedCount,
                MissedCount: missedCount,
                ExcusedCount: excusedCount,
                PendingCount: pendingCount,
                TotalItemsCount: totalItemsCount,
                EffectiveTotalCount: effectiveTotalCount,
                MinAttendanceCount: null,
                MinAttendancePercent: null,
                PlannedTotalCount: activity.PlannedTotalCount,
                RequiredCount: null,
                RemainingRequired: null,
                RemainingOpportunities: remainingOpportunities,
                MaxAllowedMissesLeft: null,
                CurrentRatePercent: currentRatePercent,
                RecordedRatePercent: recordedRatePercent,
                Status: "not_configured");
        }

        int? requiredFromPercent = null;
        if (hasPercentReq && effectiveTotalCount > 0)
        {
            requiredFromPercent = (int)Math.Ceiling(effectiveTotalCount * (activity.MinAttendancePercent!.Value / 100m));
        }

        int? requiredCount;
        if (hasCountReq && requiredFromPercent.HasValue)
        {
            requiredCount = Math.Max(activity.MinAttendanceCount!.Value, requiredFromPercent.Value);
        }
        else if (hasCountReq)
        {
            requiredCount = activity.MinAttendanceCount!.Value;
        }
        else
        {
            requiredCount = requiredFromPercent;
        }

        if (!requiredCount.HasValue)
        {
            // Only percentage is configured, and there are no planned or non-excused items yet.
            return new AttendanceSummaryDto(
                SupportsAttendance: true,
                HasRequirement: true,
                AttendedCount: attendedCount,
                MissedCount: missedCount,
                ExcusedCount: excusedCount,
                PendingCount: pendingCount,
                TotalItemsCount: totalItemsCount,
                EffectiveTotalCount: effectiveTotalCount,
                MinAttendanceCount: activity.MinAttendanceCount,
                MinAttendancePercent: activity.MinAttendancePercent,
                PlannedTotalCount: activity.PlannedTotalCount,
                RequiredCount: null,
                RemainingRequired: null,
                RemainingOpportunities: remainingOpportunities,
                MaxAllowedMissesLeft: null,
                CurrentRatePercent: currentRatePercent,
                RecordedRatePercent: recordedRatePercent,
                Status: "no_data");
        }

        var remainingRequired = Math.Max(0, requiredCount.Value - attendedCount);

        int? maxAllowedMissesLeft = null;
        if (effectiveTotalCount > 0 && remainingOpportunities >= remainingRequired)
        {
            maxAllowedMissesLeft = remainingOpportunities - remainingRequired;
        }

        string status;
        if (remainingRequired == 0)
        {
            status = "met";
        }
        else if (activity.PlannedTotalCount.HasValue && remainingRequired > remainingOpportunities)
        {
            status = "impossible";
        }
        else if (!activity.PlannedTotalCount.HasValue && effectiveTotalCount > 0 && remainingRequired > remainingOpportunities)
        {
            // Not enough scheduled items exist yet to reach the count, or too many were missed.
            // If all existing items were already marked (no pending left) and some were missed, still warn at_risk
            // rather than claiming impossible because more items may not have been added yet.
            status = "at_risk";
        }
        else if (effectiveTotalCount > 0 && remainingOpportunities > 0 && remainingRequired == remainingOpportunities)
        {
            // Zero absences left — must attend every remaining opportunity.
            status = "at_risk";
        }
        else
        {
            status = "on_track";
        }

        return new AttendanceSummaryDto(
            SupportsAttendance: true,
            HasRequirement: true,
            AttendedCount: attendedCount,
            MissedCount: missedCount,
            ExcusedCount: excusedCount,
            PendingCount: pendingCount,
            TotalItemsCount: totalItemsCount,
            EffectiveTotalCount: effectiveTotalCount,
            MinAttendanceCount: activity.MinAttendanceCount,
            MinAttendancePercent: activity.MinAttendancePercent,
            PlannedTotalCount: activity.PlannedTotalCount,
            RequiredCount: requiredCount,
            RemainingRequired: remainingRequired,
            RemainingOpportunities: remainingOpportunities,
            MaxAllowedMissesLeft: maxAllowedMissesLeft,
            CurrentRatePercent: currentRatePercent,
            RecordedRatePercent: recordedRatePercent,
            Status: status);
    }
}
