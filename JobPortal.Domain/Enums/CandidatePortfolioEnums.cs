using System.Text.Json.Serialization;

namespace JobPortal.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CandidatePortfolioStatus { Draft = 1, Published }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CandidatePortfolioTemplate { Professional = 1, Developer }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PortfolioSectionType
{
    About = 1,
    Skills,
    Experience,
    Education,
    Projects,
    Certifications,
    ProfessionalLinks,
    Resume,
    CustomSections
}
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProfessionalLinkType
{
    LinkedIn = 1,
    GitHub,
    Portfolio,
    Website,
    Behance,
    Dribbble,
    Kaggle,
    Other
}
