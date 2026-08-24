namespace ApplyWise.Web.Models.ResumeBuilder;

/// <summary>
/// Creates fresh sample state for the Resume Builder's "Load sample data"
/// action. No returned collection is shared between requests.
/// </summary>
public static class ResumeSampleFactory
{
    public static ResumeDocument Create() => new()
    {
        TemplateSelectionConfirmed = true,
        PersonalInformation = new PersonalInformation
        {
            FullName = "Jordan Lee",
            ProfessionalTitle = "Product-Minded Software Engineer",
            PhoneNumber = "+1 415 555 0142",
            EmailAddress = "jordan.lee@example.com",
            Location = "San Francisco, CA",
            LinkedInUrl = "https://example.com/profiles/jordan-linkedin",
            GitHubUrl = "https://example.com/profiles/jordan-github",
            PortfolioUrl = "https://example.com/portfolio/jordan"
        },
        ProfessionalSummary =
            "[b]Product-minded software engineer[/b] building accessible full-stack applications with React, .NET, and PostgreSQL. Turns ambiguous requirements into reliable features, communicates clearly across teams, and improves delivery through pragmatic testing and automation.",
        Education =
        [
            new EducationEntry
            {
                Id = "education-fast-nuces",
                InstitutionName = "Northbridge University",
                Degree = "Bachelor of Science",
                FieldOfStudy = "Computer Science",
                StartDate = "2023-08",
                EndDate = "2027-06",
                Location = "San Francisco, CA",
                Grade = "CGPA: 3.09 / 4.00",
                DescriptionOrCoursework = "Relevant coursework: [i]Data Structures, Algorithms, Databases, Operating Systems[/i]"
            },
            new EducationEntry
            {
                Id = "education-cadet-intermediate",
                InstitutionName = "Harbor College",
                Degree = "Intermediate",
                FieldOfStudy = "Pre-Engineering",
                StartDate = "2020-08",
                EndDate = "2022-06",
                Location = "Oakland, CA",
                Grade = "90%",
                DescriptionOrCoursework = "Mathematics, Physics, and Chemistry"
            }
        ],
        Experience =
        [
            new ExperienceEntry
            {
                Id = "experience-codesprint",
                CompanyName = "CodeSprint - Northbridge University",
                JobTitle = "Co-Head",
                EmploymentType = "Leadership",
                TechnologySkills = "Full-Stack Development, Contest Management, Team Coordination",
                Location = "San Francisco, CA",
                StartDate = "2025-03",
                EndDate = "2026-01",
                BulletPoints =
                [
                    "Co-led a full-stack competition platform supporting registration, problem access, and [b]real-time results[/b]."
                ]
            },
            new ExperienceEntry
            {
                Id = "experience-github-society",
                CompanyName = "Open Source Society, Northbridge University",
                JobTitle = "Head of Workshop Team",
                EmploymentType = "Volunteer",
                TechnologySkills = "Git, GitHub, Public Speaking, Team Leadership",
                Location = "San Francisco, CA",
                StartDate = "2024-08",
                IsCurrentlyWorking = true,
                BulletPoints =
                [
                    "Lead practical Git and GitHub workshops for student development teams."
                ]
            },
            new ExperienceEntry
            {
                Id = "experience-product-intern",
                CompanyName = "Example Labs",
                JobTitle = "Software Engineering Intern",
                EmploymentType = "Internship",
                TechnologySkills = ".NET, PostgreSQL, Automated Testing",
                Location = "Remote",
                StartDate = "2024-06",
                EndDate = "2024-08",
                BulletPoints = ["Shipped tested API and dashboard improvements that reduced manual reporting work."]
            }
        ],
        Projects =
        [
            new ProjectEntry
            {
                Id = "project-forked-nuces",
                ProjectName = "Campus Collab",
                ProjectUrl = "https://campus-collab.example.com",
                RepositoryUrl = "https://example.com/repos/campus-collab",
                TechnologiesUsed = ["Next.js", "Python", "Django REST Framework", "PostgreSQL", "Redis", "Docker"],
                StartDate = "2025-01",
                IsOngoing = true,
                BulletPoints =
                [
                    "Connected university projects with skill-matched contributors across batches."
                ]
            },
            new ProjectEntry
            {
                Id = "project-investment-recommendation",
                ProjectName = "Investment Recommendation System",
                RepositoryUrl = "https://example.com/repos/investment-recommender",
                TechnologiesUsed = ["Python", "Pandas", "Scikit-learn", "Streamlit"],
                StartDate = "2024-08",
                EndDate = "2024-12",
                BulletPoints =
                [
                    "Built recommendation filters and interactive portfolio visualizations from user preferences."
                ]
            },
            new ProjectEntry
            {
                Id = "project-codeverse-scoreboard",
                ProjectName = "CodeVerse Scoreboard",
                RepositoryUrl = "https://example.com/repos/codeverse-scoreboard",
                TechnologiesUsed = ["React", "TypeScript", "Express", "Socket.IO", "Puppeteer"],
                StartDate = "2024-03",
                EndDate = "2024-07",
                BulletPoints =
                [
                    "Automated live standings and delivered score changes through [u]WebSockets[/u]."
                ]
            }
        ],
        Skills =
        [
            new SkillCategory
            {
                Id = "skills-languages",
                Name = "Languages",
                Skills =
                [
                    new SkillItem { Id = "skill-javascript", Name = "JavaScript", Level = 5 },
                    new SkillItem { Id = "skill-typescript", Name = "TypeScript", Level = 4 },
                    new SkillItem { Id = "skill-python", Name = "Python", Level = 4 },
                    new SkillItem { Id = "skill-c", Name = "C", Level = 3 },
                    new SkillItem { Id = "skill-cpp", Name = "C++", Level = 3 },
                    new SkillItem { Id = "skill-sql", Name = "SQL", Level = 4 }
                ]
            },
            new SkillCategory
            {
                Id = "skills-frameworks",
                Name = "Frameworks & Libraries",
                Skills =
                [
                    new SkillItem { Id = "skill-react", Name = "React", Level = 5 },
                    new SkillItem { Id = "skill-nextjs", Name = "Next.js", Level = 4 },
                    new SkillItem { Id = "skill-nodejs", Name = "Node.js", Level = 4 },
                    new SkillItem { Id = "skill-express", Name = "Express", Level = 4 },
                    new SkillItem { Id = "skill-django-rest", Name = "Django REST Framework", Level = 3 }
                ]
            },
            new SkillCategory
            {
                Id = "skills-databases",
                Name = "Databases",
                Skills =
                [
                    new SkillItem { Id = "skill-postgresql", Name = "PostgreSQL", Level = 4 },
                    new SkillItem { Id = "skill-mongodb", Name = "MongoDB", Level = 3 },
                    new SkillItem { Id = "skill-mysql", Name = "MySQL", Level = 4 },
                    new SkillItem { Id = "skill-redis", Name = "Redis", Level = 3 }
                ]
            },
            new SkillCategory
            {
                Id = "skills-tools",
                Name = "Tools",
                Skills =
                [
                    new SkillItem { Id = "skill-git", Name = "Git", Level = 5 },
                    new SkillItem { Id = "skill-github-actions", Name = "GitHub Actions", Level = 4 },
                    new SkillItem { Id = "skill-linux", Name = "Linux", Level = 4 },
                    new SkillItem { Id = "skill-docker", Name = "Docker", Level = 4 },
                    new SkillItem { Id = "skill-bash", Name = "Bash", Level = 3 },
                    new SkillItem { Id = "skill-figma", Name = "Figma", Level = 3 }
                ]
            },
        ],
        AchievementsAndCertifications =
        [
            new AchievementCertificationEntry
            {
                Id = "achievement-coder-cup",
                Title = "Coder Cup Runner-up",
                IssuingOrganization = "Northbridge University",
                Date = "2025-02",
                CredentialUrl = "https://example.com/credentials/coder-cup",
                Description = "Runner-up in the university programming competition."
            },
            new AchievementCertificationEntry
            {
                Id = "achievement-best-reader",
                Title = "Best Reader",
                IssuingOrganization = "Harbor College",
                Date = "2021-05",
                CredentialUrl = "https://example.com/credentials/best-reader",
                Description = "Recognized for consistent academic contribution."
            },
            new AchievementCertificationEntry
            {
                Id = "certification-responsive-web-design",
                Title = "Responsive Web Design",
                IssuingOrganization = "freeCodeCamp",
                Date = "2024-01",
                CredentialUrl = "https://example.com/credentials/responsive-web-design",
                Description = "Completed accessible HTML and responsive CSS coursework."
            }
        ],
        Languages =
        [
            new LanguageEntry { Id = "language-english", Name = "English", Proficiency = "Professional working proficiency", Level = 4 },
            new LanguageEntry { Id = "language-urdu", Name = "Urdu", Proficiency = "Native proficiency", Level = 5 },
            new LanguageEntry { Id = "language-sindhi", Name = "Sindhi", Proficiency = "Native proficiency", Level = 5 }
        ],
        VolunteerExperience =
        [
            new VolunteerExperienceEntry
            {
                Id = "volunteer-github-society",
                OrganizationName = "Open Source Society, Northbridge University",
                Role = "Workshop Mentor",
                Location = "San Francisco, CA",
                StartDate = "2024-08",
                IsCurrentlyVolunteering = true,
                BulletPoints =
                [
                    "Mentor students on source control, open-source collaboration, and practical software delivery."
                ]
            }
        ],
        References =
        [
            new ReferenceEntry
            {
                Id = "reference-priya-shah",
                FullName = "Priya Shah",
                JobTitle = "Engineering Manager",
                Company = "Example Labs",
                EmailAddress = "priya.shah@example.com",
                PhoneNumber = "+1 415 555 0168"
            },
            new ReferenceEntry
            {
                Id = "reference-daniel-kim",
                FullName = "Daniel Kim",
                JobTitle = "Faculty Advisor",
                Company = "Northbridge University",
                EmailAddress = "daniel.kim@example.com",
                PhoneNumber = "+1 415 555 0181"
            }
        ],
        Interests = ["Open-source software", "Competitive programming", "System design"],
        CustomSections =
        [
            new CustomSection
            {
                Id = "custom-community",
                Title = "Community",
                Entries =
                [
                    new CustomSectionEntry
                    {
                        Id = "custom-community-entry",
                        Heading = "Student Developer Community",
                        Subheading = "Peer Mentor",
                        Location = "San Francisco, CA",
                        StartDate = "2025-01",
                        IsCurrent = true,
                        Url = "https://example.com/profiles/jordan-github",
                        BulletPoints = ["Review student projects and share practical guidance on full-stack engineering."]
                    }
                ]
            }
        ],
        Sections = ResumeSectionCatalog.CreateDefault()
    };
}
