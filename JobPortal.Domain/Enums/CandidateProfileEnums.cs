using System.Text.Json.Serialization;

namespace JobPortal.Domain.Enums;

public enum CandidateSkillProficiency
{
    Beginner = 1,
    Intermediate,
    Advanced,
    Expert
}

public enum CandidateWorkStatus { Fresher = 1, Experienced }
[JsonConverter(typeof(JsonStringEnumConverter<CandidateAvailability>))]
public enum CandidateAvailability
{
    FifteenDaysOrLess = 1,
    OneMonth,
    TwoMonths,
    ThreeMonths,
    MoreThanThreeMonths,
    ServingNoticePeriod,
    ImmediateJoiner,
    Other
}
[JsonConverter(typeof(JsonStringEnumConverter<CandidateJobType>))]
public enum CandidateJobType { Permanent = 1, Contractual }
[JsonConverter(typeof(JsonStringEnumConverter<CandidateEmploymentPreference>))]
public enum CandidateEmploymentPreference { FullTime = 1, PartTime }
[JsonConverter(typeof(JsonStringEnumConverter<CandidateShiftPreference>))]
public enum CandidateShiftPreference { Day = 1, Night, Flexible }
[JsonConverter(typeof(JsonStringEnumConverter<EducationCourseType>))]
public enum EducationCourseType { FullTime = 1, PartTime, CorrespondenceOrDistance }
