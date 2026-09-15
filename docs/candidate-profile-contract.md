# Candidate profile backend contract

All examples use camelCase JSON. Responses are wrapped in the API's `data` envelope. `GET /api/candidate/profile/basic-details` and its PUT response share the same DTO. The PUT merges omitted values with the stored profile. A new profile requires `workStatus`, `isOutsideIndia`, `currentCountry`, and `currentCity`; completion also needs the stored first and last name and `resumeHeadline`. `mobileVerified` is read only.

```json
{"workStatus":"Experienced","isOutsideIndia":false,"currentCountry":"India","currentCity":"Pune","currentArea":"Kothrud","resumeHeadline":".NET 8 Developer","skills":["C#",".NET"],"mobileNumber":"9765433544","noticePeriod":"OneMonth","currentAnnualSalary":1300000}
```

```json
{"email":"candidate@example.com","mobileNumber":"9765433544","mobileVerified":false,"workStatus":"Experienced","isOutsideIndia":false,"currentCountry":"India","currentCity":"Pune","currentArea":"Kothrud","resumeHeadline":".NET 8 Developer","skills":["C#",".NET"],"noticePeriod":"OneMonth","availabilityToJoin":"OneMonth","currentAnnualSalary":1300000,"currentFixedAnnualSalary":null,"currentVariableAnnualSalary":null,"salaryUnit":"INR per annum"}
```

`PUT /api/candidate/profile/career-preferences` accepts individual selections. A comma in an incoming item is split for compatibility with older UI input. The GET and PUT response return the stored arrays.

```json
{"preferredJobRoles":[".NET Developer","Product Manager"],"preferredCities":["Pune","Bengaluru"],"expectedAnnualSalary":2500000,"jobTypes":[],"employmentTypes":[],"preferredShifts":[]}
```

```json
{"preferredJobRoles":[".NET Developer","Product Manager"],"preferredCities":["Pune","Bengaluru"],"expectedAnnualSalary":2500000,"jobTypes":[],"employmentTypes":[],"preferredShifts":[],"salaryUnit":"INR per annum"}
```

The portfolio education contract retains existing wire names: `qualification` = education level, `institution` = university/institute, `fieldOfStudy` = course/specialization, `grade` = score, `isCurrentlyStudying` = currently studying. `startYear` and `endYear` are integer years. `endYear` is required for completed study and cleared for current study. `gradingSystem` is stored as free text for compatibility; new clients should send `Percentage`, `Cgpa10`, `Gpa4`, `Grade`, `PassFail`, or `Other`. Existing values such as `10` and `CGPA` remain readable without a migration. Course types: `FullTime`, `PartTime`, `CorrespondenceOrDistance`.

```json
{"qualification":"B.Tech","institution":"K. K. Wagh","fieldOfStudy":"Mechanical Engineering","courseType":"FullTime","startYear":2015,"endYear":2020,"isCurrentlyStudying":false,"gradingSystem":"Grade","grade":"A","description":null,"displayOrder":0}
```

The education response repeats these fields and adds `id`, for example `"id":"11111111-1111-1111-1111-111111111111"`.

The portfolio experience contract retains `isCurrent`, `startDate`, `endDate`, `annualSalary`, and `description` as wire names for the UI concepts current company, joining date, leaving date, current annual salary, and responsibilities. Dates use ISO `yyyy-MM-dd`. `endDate` is required for previous employment and cleared for current employment. `employmentType` uses the existing domain `EmploymentType` enum. Salary fields are private.

```json
{"jobTitle":".NET 8 Developer","companyName":"AT&T","location":"Pune","employmentType":1,"startDate":"2025-10-16","endDate":"2026-05-11","isCurrent":false,"annualSalary":1300000,"skillsUsed":[".NET","C#"],"description":"Built APIs and supported releases.","displayOrder":0,"noticePeriod":null}
```

The private experience response repeats these fields and adds `id`. The public response omits `annualSalary` and `noticePeriod`.

Canonical candidate values: `workStatus`: `Fresher`, `Experienced`; `noticePeriod`: `ImmediateJoiner`, `FifteenDaysOrLess`, `OneMonth`, `ThreeMonths`, `Other`. `availabilityToJoin` additionally supports `TwoMonths`, `MoreThanThreeMonths`, and `ServingNoticePeriod`. `jobTypes`: `Permanent`, `Contractual`; `employmentTypes`: `FullTime`, `PartTime`; `preferredShifts`: `Day`, `Night`, `Flexible`.

Completion section identifiers remain `BasicDetails`, `ProfileSummary`, `Skills`, `CareerPreferences`, `Education`, `Employment`, `Resume`. Display them in that order. Basic Details requires names, headline, work status, country and city. Profile Summary requires bio. Skills requires one persisted skill. Career Preferences requires at least one role and city, or completed legacy onboarding choices. Education requires one owned record. Experienced Employment requires one owned record; it is omitted from Fresher required sections. Resume requires a stored upload. The weights sum to 100: Fresher 25/15/15/20/10/0/15, Experienced 15/15/15/20/10/10/15 in display order.

New salary values must be whole INR amounts between 0 and 1,000,000,000. Role and city selections are limited to 3 and 5 respectively; skill names to 100 characters, cities to 150, headline to 180, company and job title to 200, responsibilities to 4,000. Markup, control characters and punctuation-only values are rejected in professional labels. No country or city allowlist or database-backed dropdown is needed. Public portfolio uses an allowlisted DTO and excludes mobile, verification state, salary and private location details.

Portfolio `employmentType` uses its existing numeric wire values: `FullTime` = 1, `PartTime` = 2, `Contract` = 3, `Internship` = 4, `Freelance` = 5, `Temporary` = 6. New education years are 1950 through the current UTC year plus 10. Percentage scores range from 0 to 100, `Cgpa10` and legacy `CGPA` or `10` from 0 to 10, and `Gpa4` from 0 to 4. `PassFail` accepts `Pass` or `Fail`; `Grade` and `Other` accept safe short text up to 100 characters. Name fields allow Unicode letters, spaces, apostrophes and hyphens up to 100 characters per part. A current employment entry ignores a stale `endDate`; a current education entry ignores a stale `endYear`.
