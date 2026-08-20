namespace JobPortal.Domain.Enums;

public enum CandidateSkillProficiency
{
    Beginner = 1,
    Intermediate,
    Advanced,
    Expert
}

public enum CandidateWorkStatus { Fresher = 1, Experienced }
public enum CandidateAvailability
{
    FifteenDaysOrLess = 1,
    OneMonth,
    TwoMonths,
    ThreeMonths,
    MoreThanThreeMonths,
    ServingNoticePeriod
}
public enum CandidateJobType { Permanent = 1, Contractual }
public enum CandidateEmploymentPreference { FullTime = 1, PartTime }
public enum CandidateShiftPreference { Day = 1, Night, Flexible }
public enum EducationCourseType { FullTime = 1, PartTime, CorrespondenceOrDistance }
