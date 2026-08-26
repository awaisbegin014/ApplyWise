/* ApplyWise Resume Builder
 * Pure state/PDF helpers are exported through CommonJS for dependency-free tests.
 * In a browser the same module is exposed as ApplyWiseResumeBuilder and starts itself.
 */
(function (globalObject, factory) {
    'use strict';

    const templateRenderers = typeof module === 'object' && module.exports
        ? require('./resume-template-renderers.js')
        : globalObject.ApplyWiseResumeTemplates;
    const api = factory(templateRenderers);
    if (typeof module === 'object' && module.exports) {
        module.exports = api;
        return;
    }

    globalObject.ApplyWiseResumeBuilder = api;
    const start = function () { api.autoInitialize(); };
    if (globalObject.document && globalObject.document.readyState === 'loading') {
        globalObject.document.addEventListener('DOMContentLoaded', start, { once: true });
    } else if (globalObject.document) {
        start();
    }
}(typeof globalThis !== 'undefined' ? globalThis : this, function (templateRenderers) {
    'use strict';

    const SCHEMA_VERSION = 5;
    const LIMITS = Object.freeze({
        draftBytes: 512000,
        photoDataUrl: 320000,
        photoFileBytes: 8000000,
        shortText: 180,
        name: 120,
        email: 254,
        url: 2048,
        summary: 2500,
        longText: 4000,
        bullet: 1000,
        tag: 80,
        entries: 25,
        customSections: 10,
        customEntries: 20,
        bullets: 20,
        tags: 50
    });
    const ONE_PAGE_RECOMMENDATIONS = Object.freeze({
        professionalSummary: 3,
        education: 2,
        experience: 3,
        projects: 3,
        achievementsAndCertifications: 3
    });
    const DEFAULT_TEMPLATE_ID = 'classic';
    const LEGACY_TEMPLATE_CATALOG = Object.freeze([
        Object.freeze({ id: 'classic', name: 'Editorial', font: 'Roboto', description: 'A refined photo-free resume with formal two-column structure.', accent: '#4b5563', layout: 'editorial-columns', hasPhoto: false }),
        Object.freeze({ id: 'emerald', name: 'Emerald', font: 'Poppins', description: 'A confident portrait resume with a deep green career sidebar.', accent: '#17633a', layout: 'emerald-sidebar', hasPhoto: true }),
        Object.freeze({ id: 'modern', name: 'Slate', font: 'Poppins', description: 'A polished portrait resume with a structured navy sidebar.', accent: '#17324d', layout: 'slate-sidebar', hasPhoto: true }),
        Object.freeze({ id: 'executive', name: 'Executive', font: 'Poppins', description: 'A leadership-focused portrait resume with a bold olive banner.', accent: '#777517', layout: 'executive-banner', hasPhoto: true }),
        Object.freeze({ id: 'compact', name: 'Azure', font: 'Poppins', description: 'A bright portrait resume with a compact blue profile column.', accent: '#5b86b5', layout: 'azure-profile', hasPhoto: true }),
        Object.freeze({ id: 'minimal', name: 'Minimal', font: 'Roboto', description: 'A clean single-column resume with generous spacing and simple rules.', accent: '#2563eb', layout: 'minimal-single-column', hasPhoto: false }),
        Object.freeze({ id: 'corporate', name: 'Corporate', font: 'Poppins', description: 'A polished blue nameplate with a focused credentials column.', accent: '#243b64', layout: 'corporate-header', hasPhoto: false }),
        Object.freeze({ id: 'timeline', name: 'Timeline', font: 'Roboto', description: 'A modern teal-accent layout that keeps the career story easy to scan.', accent: '#0f766e', layout: 'timeline-accent-rail', hasPhoto: false }),
        Object.freeze({ id: 'studio', name: 'Studio', font: 'Poppins', description: 'A refined portrait layout with a plum header and balanced columns.', accent: '#6d4c7d', layout: 'studio-photo-header', hasPhoto: true })
    ]);
    const TEMPLATE_CATALOG = Object.freeze(LEGACY_TEMPLATE_CATALOG.concat(
        templateRenderers && Array.isArray(templateRenderers.CATALOG) ? templateRenderers.CATALOG : []
    ));
    const TEMPLATE_IDS = new Set(TEMPLATE_CATALOG.map(function (template) { return template.id; }));

    const SECTION_DEFAULTS = Object.freeze([
        Object.freeze({ key: 'professionalSummary', title: 'Professional Summary', isVisible: true }),
        Object.freeze({ key: 'education', title: 'Education', isVisible: true }),
        Object.freeze({ key: 'skills', title: 'Technical Skills', isVisible: true }),
        Object.freeze({ key: 'experience', title: 'Experience', isVisible: true }),
        Object.freeze({ key: 'projects', title: 'Projects', isVisible: true }),
        Object.freeze({ key: 'achievementsAndCertifications', title: 'Achievements & Certifications', isVisible: true }),
        Object.freeze({ key: 'languages', title: 'Languages', isVisible: false }),
        Object.freeze({ key: 'volunteerExperience', title: 'Volunteer Experience', isVisible: false }),
        Object.freeze({ key: 'references', title: 'References', isVisible: false }),
        Object.freeze({ key: 'interests', title: 'Interests', isVisible: false }),
        Object.freeze({ key: 'customSections', title: 'Custom Sections', isVisible: false })
    ]);
    const SECTION_KEYS = new Set(SECTION_DEFAULTS.map(function (section) { return section.key; }));
    const BUILDER_SECTION_ALIASES = new Map([
        ['personalinformation', 'personalInformation'],
        ['professionalsummary', 'professionalSummary'],
        ['experience', 'experience'],
        ['projects', 'projects'],
        ['education', 'education'],
        ['skills', 'skills'],
        ['references', 'references'],
        ['languages', 'languages'],
        ['volunteerexperience', 'volunteerExperience'],
        ['interests', 'interests'],
        ['customsections', 'customSections'],
        ['achievements', 'achievementsAndCertifications'],
        ['certifications', 'achievementsAndCertifications'],
        ['achievementsandcertifications', 'achievementsAndCertifications']
    ]);
    const EDITOR_TAB_GROUPS = Object.freeze({
        personal: Object.freeze(['personalInformation']),
        summary: Object.freeze(['professionalSummary']),
        experience: Object.freeze(['experience']),
        education: Object.freeze(['education']),
        skills: Object.freeze(['skills']),
        projects: Object.freeze(['projects']),
        more: Object.freeze(['achievementsAndCertifications', 'languages', 'volunteerExperience', 'references', 'interests', 'customSections']),
        arrange: Object.freeze(['arrange'])
    });
    const EDITOR_TAB_ORDER = Object.freeze(Object.keys(EDITOR_TAB_GROUPS));
    const MONTHS = Object.freeze(['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']);
    const PROFICIENCY_LEVEL_OPTIONS = Object.freeze([
        Object.freeze({ value: '', label: 'Not rated' }),
        Object.freeze({ value: 1, label: '1 - Basic' }),
        Object.freeze({ value: 2, label: '2 - Working knowledge' }),
        Object.freeze({ value: 3, label: '3 - Proficient' }),
        Object.freeze({ value: 4, label: '4 - Advanced' }),
        Object.freeze({ value: 5, label: '5 - Expert / native' })
    ]);
    const CONTROL_CHARACTERS = /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g;
    const GALLERY_PHOTO_DATA_URL = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAIAAAD8GO2jAAAAK0lEQVR4nGPYd+w8TRHDqAWjFoxaMGrBqAWjFoxaMGrBqAWjFoxaMFQsAADVsUyIpsy4AwAAAABJRU5ErkJggg==';
    let runtimeId = 0;

    function isRecord(value) {
        if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
        const prototype = Object.getPrototypeOf(value);
        return prototype === Object.prototype || prototype === null;
    }

    function text(value, maximum, trim) {
        if (typeof value !== 'string' && typeof value !== 'number') return '';
        let result = String(value).replace(/\r\n?/g, '\n').replace(CONTROL_CHARACTERS, '');
        if (trim !== false) result = result.trim();
        return result.slice(0, maximum);
    }

    function normalizeBuilderSection(value) {
        const candidate = text(value, 64).toLocaleLowerCase();
        return BUILDER_SECTION_ALIASES.get(candidate) || '';
    }

    function builderSectionSelector(value) {
        const section = normalizeBuilderSection(value);
        return section ? '[data-editor-section="' + section + '"]' : '';
    }

    function editorTabForSection(value) {
        const section = normalizeBuilderSection(value) || text(value, 64);
        const tab = Object.keys(EDITOR_TAB_GROUPS).find(function (key) {
            return EDITOR_TAB_GROUPS[key].includes(section);
        });
        return tab || 'personal';
    }

    function boolean(value) {
        return value === true;
    }

    function normalizeTemplateId(value) {
        const candidate = text(value, 40).toLocaleLowerCase();
        return TEMPLATE_IDS.has(candidate) ? candidate : DEFAULT_TEMPLATE_ID;
    }

    function templateMetadata(value) {
        const id = normalizeTemplateId(value);
        return TEMPLATE_CATALOG.find(function (template) { return template.id === id; }) || TEMPLATE_CATALOG[0];
    }

    function normalizeProfilePhotoDataUrl(value) {
        if (typeof value !== 'string') return '';
        const candidate = value.trim();
        if (!candidate || candidate.length > LIMITS.photoDataUrl) return '';
        const match = /^data:image\/(?:png|jpeg);base64,([A-Za-z0-9+/]+={0,2})$/.exec(candidate);
        return match && match[1].length % 4 === 0 ? candidate : '';
    }

    function array(value, maximum) {
        return Array.isArray(value) ? value.slice(0, maximum) : [];
    }

    function safeId(value, prefix, index) {
        const candidate = text(value, 64);
        if (/^[A-Za-z0-9_-]{1,64}$/.test(candidate)) return candidate;
        return prefix + '-' + String(index + 1);
    }

    function newId(prefix) {
        runtimeId += 1;
        return prefix + '-' + Date.now().toString(36) + '-' + runtimeId.toString(36);
    }

    function uniqueStrings(value, maximumItems, maximumLength) {
        const result = [];
        const seen = new Set();
        array(value, maximumItems).forEach(function (item) {
            const normalized = text(item, maximumLength);
            const key = normalized.toLocaleLowerCase();
            if (normalized && !seen.has(key)) {
                seen.add(key);
                result.push(normalized);
            }
        });
        return result;
    }

    function stringList(value, maximumItems, maximumLength) {
        return array(value, maximumItems).map(function (item) { return text(item, maximumLength); });
    }

    function normalizeLevel(value) {
        if (value === null || value === undefined || value === '') return null;
        const candidate = Number(value);
        return Number.isInteger(candidate) && candidate >= 1 && candidate <= 5 ? candidate : null;
    }

    function createEmptyState() {
        return {
            schemaVersion: SCHEMA_VERSION,
            templateId: DEFAULT_TEMPLATE_ID,
            templateSelectionConfirmed: false,
            personalInformation: {
                fullName: '',
                professionalTitle: '',
                phoneNumber: '',
                emailAddress: '',
                location: '',
                linkedInUrl: '',
                gitHubUrl: '',
                portfolioUrl: '',
                profilePhotoDataUrl: ''
            },
            professionalSummary: '',
            education: [],
            experience: [],
            projects: [],
            skills: [],
            achievementsAndCertifications: [],
            languages: [],
            volunteerExperience: [],
            references: [],
            interests: [],
            customSections: [],
            sections: SECTION_DEFAULTS.map(function (section) { return Object.assign({}, section); })
        };
    }

    function normalizePersonal(value) {
        const source = isRecord(value) ? value : {};
        return {
            fullName: text(source.fullName, LIMITS.name),
            professionalTitle: text(source.professionalTitle, LIMITS.shortText),
            phoneNumber: text(source.phoneNumber, 40),
            emailAddress: text(source.emailAddress, LIMITS.email),
            location: text(source.location, LIMITS.shortText),
            linkedInUrl: text(source.linkedInUrl, LIMITS.url),
            gitHubUrl: text(source.gitHubUrl, LIMITS.url),
            portfolioUrl: text(source.portfolioUrl, LIMITS.url),
            profilePhotoDataUrl: normalizeProfilePhotoDataUrl(source.profilePhotoDataUrl)
        };
    }

    function normalizeEducation(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'education', index),
            institutionName: text(source.institutionName, LIMITS.shortText),
            degree: text(source.degree, LIMITS.shortText),
            fieldOfStudy: text(source.fieldOfStudy, LIMITS.shortText),
            startDate: text(source.startDate, 7),
            endDate: text(source.endDate, 7),
            isCurrentlyStudying: boolean(source.isCurrentlyStudying),
            location: text(source.location, LIMITS.shortText),
            grade: text(source.grade, LIMITS.shortText),
            descriptionOrCoursework: text(source.descriptionOrCoursework, LIMITS.longText)
        };
    }

    function normalizeExperience(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'experience', index),
            companyName: text(source.companyName, LIMITS.shortText),
            jobTitle: text(source.jobTitle, LIMITS.shortText),
            employmentType: text(source.employmentType, LIMITS.shortText),
            technologySkills: text(source.technologySkills, LIMITS.longText),
            location: text(source.location, LIMITS.shortText),
            startDate: text(source.startDate, 7),
            endDate: text(source.endDate, 7),
            isCurrentlyWorking: boolean(source.isCurrentlyWorking),
            bulletPoints: stringList(source.bulletPoints, LIMITS.bullets, LIMITS.bullet)
        };
    }

    function normalizeProject(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'project', index),
            projectName: text(source.projectName, LIMITS.shortText),
            projectUrl: text(source.projectUrl, LIMITS.url),
            repositoryUrl: text(source.repositoryUrl, LIMITS.url),
            technologiesUsed: uniqueStrings(source.technologiesUsed, LIMITS.tags, LIMITS.tag),
            startDate: text(source.startDate, 7),
            endDate: text(source.endDate, 7),
            isOngoing: boolean(source.isOngoing),
            bulletPoints: stringList(source.bulletPoints, LIMITS.bullets, LIMITS.bullet)
        };
    }

    function normalizeSkill(item, index) {
        const source = isRecord(item) ? item : {};
        const skills = [];
        const seen = new Set();
        array(source.skills, LIMITS.tags).forEach(function (skill, skillIndex) {
            const skillSource = isRecord(skill) ? skill : { name: skill };
            const name = text(skillSource.name, LIMITS.tag);
            const key = name.toLocaleLowerCase();
            if (!name || seen.has(key)) return;
            seen.add(key);
            skills.push({
                id: safeId(skillSource.id, 'skill-' + String(index + 1), skillIndex),
                name: name,
                level: normalizeLevel(skillSource.level)
            });
        });
        return {
            id: safeId(source.id, 'skills', index),
            name: text(source.name, LIMITS.shortText),
            skills: skills
        };
    }

    function normalizeAchievement(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'achievement', index),
            title: text(source.title, LIMITS.shortText),
            issuingOrganization: text(source.issuingOrganization, LIMITS.shortText),
            date: text(source.date, 7),
            credentialUrl: text(source.credentialUrl, LIMITS.url),
            description: text(source.description, LIMITS.longText)
        };
    }

    function normalizeLanguage(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'language', index),
            name: text(source.name, LIMITS.shortText),
            proficiency: text(source.proficiency, LIMITS.shortText),
            level: normalizeLevel(source.level)
        };
    }

    function normalizeVolunteer(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'volunteer', index),
            organizationName: text(source.organizationName, LIMITS.shortText),
            role: text(source.role, LIMITS.shortText),
            location: text(source.location, LIMITS.shortText),
            startDate: text(source.startDate, 7),
            endDate: text(source.endDate, 7),
            isCurrentlyVolunteering: boolean(source.isCurrentlyVolunteering),
            bulletPoints: stringList(source.bulletPoints, LIMITS.bullets, LIMITS.bullet)
        };
    }

    function normalizeReference(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'reference', index),
            fullName: text(source.fullName, LIMITS.name),
            jobTitle: text(source.jobTitle, LIMITS.shortText),
            company: text(source.company, LIMITS.shortText),
            emailAddress: text(source.emailAddress, LIMITS.email),
            phoneNumber: text(source.phoneNumber, 40)
        };
    }

    function normalizeCustomEntry(item, index, sectionIndex) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'custom-' + String(sectionIndex + 1), index),
            heading: text(source.heading, LIMITS.shortText),
            subheading: text(source.subheading, LIMITS.shortText),
            location: text(source.location, LIMITS.shortText),
            startDate: text(source.startDate, 7),
            endDate: text(source.endDate, 7),
            isCurrent: boolean(source.isCurrent),
            url: text(source.url, LIMITS.url),
            bulletPoints: stringList(source.bulletPoints, LIMITS.bullets, LIMITS.bullet)
        };
    }

    function normalizeCustomSection(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'custom-section', index),
            title: text(source.title, LIMITS.shortText),
            entries: array(source.entries, LIMITS.customEntries).map(function (entry, entryIndex) {
                return normalizeCustomEntry(entry, entryIndex, index);
            })
        };
    }

    function normalizeSections(value) {
        const byKey = new Map(SECTION_DEFAULTS.map(function (section) { return [section.key, section]; }));
        const result = [];
        const used = new Set();
        array(value, SECTION_DEFAULTS.length).forEach(function (item) {
            if (!isRecord(item)) return;
            const key = text(item.key, 60);
            if (!SECTION_KEYS.has(key) || used.has(key)) return;
            const fallback = byKey.get(key);
            used.add(key);
            result.push({
                key: key,
                title: text(item.title, LIMITS.shortText) || fallback.title,
                isVisible: typeof item.isVisible === 'boolean' ? item.isVisible : fallback.isVisible
            });
        });
        SECTION_DEFAULTS.forEach(function (section) {
            if (!used.has(section.key)) result.push(Object.assign({}, section));
        });
        return result;
    }

    function normalizeState(value) {
        const source = isRecord(value) ? value : {};
        const sourceVersion = Number(source.schemaVersion);
        return {
            schemaVersion: SCHEMA_VERSION,
            templateId: normalizeTemplateId(source.templateId),
            templateSelectionConfirmed: typeof source.templateSelectionConfirmed === 'boolean'
                ? source.templateSelectionConfirmed
                : Number.isInteger(sourceVersion) && sourceVersion > 0 && sourceVersion < SCHEMA_VERSION,
            personalInformation: normalizePersonal(source.personalInformation),
            professionalSummary: text(source.professionalSummary, LIMITS.summary),
            education: array(source.education, LIMITS.entries).map(normalizeEducation),
            experience: array(source.experience, LIMITS.entries).map(normalizeExperience),
            projects: array(source.projects, LIMITS.entries).map(normalizeProject),
            skills: array(source.skills, LIMITS.entries).map(normalizeSkill),
            achievementsAndCertifications: array(source.achievementsAndCertifications, LIMITS.entries).map(normalizeAchievement),
            languages: array(source.languages, LIMITS.entries).map(normalizeLanguage),
            volunteerExperience: array(source.volunteerExperience, LIMITS.entries).map(normalizeVolunteer),
            references: array(source.references, LIMITS.entries).map(normalizeReference),
            interests: uniqueStrings(source.interests, LIMITS.tags, LIMITS.tag),
            customSections: array(source.customSections, LIMITS.customSections).map(normalizeCustomSection),
            sections: normalizeSections(source.sections)
        };
    }

    function parseDraft(serialized) {
        if (typeof serialized !== 'string' || !serialized.trim()) {
            return { state: createEmptyState(), recovered: false, reason: 'empty' };
        }
        if (serialized.length > LIMITS.draftBytes) {
            return { state: createEmptyState(), recovered: true, reason: 'too-large' };
        }
        try {
            const parsed = JSON.parse(serialized);
            if (!isRecord(parsed)) {
                return { state: createEmptyState(), recovered: true, reason: 'invalid-shape' };
            }
            return { state: normalizeState(parsed), recovered: false, reason: null };
        } catch (_error) {
            return { state: createEmptyState(), recovered: true, reason: 'invalid-json' };
        }
    }

    function isEmail(value) {
        const candidate = text(value, LIMITS.email);
        if (!candidate || candidate.length > LIMITS.email || candidate.includes('..')) return false;
        const parts = candidate.split('@');
        if (parts.length !== 2) return false;
        const local = parts[0];
        const domain = parts[1];
        if (!local || local.length > 64 || local.startsWith('.') || local.endsWith('.') || !/^[A-Za-z0-9!#$%&'*+/=?^_`{|}~.-]+$/.test(local)) return false;
        if (!domain || domain.length > 253 || !domain.includes('.')) return false;
        const labels = domain.split('.');
        if (labels.some(function (label) { return !/^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$/.test(label); })) return false;
        const topLevel = labels[labels.length - 1];
        return /^[A-Za-z]{2,63}$/.test(topLevel) || /^xn--[A-Za-z0-9-]{2,59}$/i.test(topLevel);
    }

    function isPhone(value) {
        const candidate = text(value, 40);
        if (!candidate || !/^\+?[0-9().\-\s]+$/.test(candidate)) return false;
        const digits = candidate.replace(/\D/g, '');
        return digits.length >= 7 && digits.length <= 15;
    }

    function parseHttpUrl(value) {
        const candidate = text(value, LIMITS.url);
        if (!candidate) return null;
        try {
            const parsed = new URL(candidate);
            if ((parsed.protocol !== 'http:' && parsed.protocol !== 'https:') || !parsed.hostname || parsed.username || parsed.password) return null;
            return parsed.href;
        } catch (_error) {
            return null;
        }
    }

    function isHttpUrl(value) {
        return parseHttpUrl(value) !== null;
    }

    function isMonth(value) {
        const match = /^(\d{4})-(\d{2})$/.exec(text(value, 7));
        if (!match) return false;
        const month = Number(match[2]);
        return Number(match[1]) >= 1900 && Number(match[1]) <= 2200 && month >= 1 && month <= 12;
    }

    function addDateRangeErrors(errors, basePath, entry, currentProperty) {
        if (entry.startDate && !isMonth(entry.startDate)) errors[basePath + '.startDate'] = 'Choose a valid start month.';
        if (!entry[currentProperty] && entry.endDate && !isMonth(entry.endDate)) errors[basePath + '.endDate'] = 'Choose a valid end month.';
        if (!entry[currentProperty] && isMonth(entry.startDate) && isMonth(entry.endDate) && entry.endDate < entry.startDate) {
            errors[basePath + '.endDate'] = 'End month cannot be before the start month.';
        }
    }

    function validateState(value) {
        const state = normalizeState(value);
        const errors = {};
        const personal = state.personalInformation;
        const selectedTemplate = templateMetadata(state.templateId);
        const visibleSections = new Set(state.sections.filter(function (section) { return section.isVisible; }).map(function (section) { return section.key; }));
        if (!state.templateSelectionConfirmed) errors.templateSelectionConfirmed = 'Choose a resume template to continue.';
        if (selectedTemplate.photoRequired && !personal.profilePhotoDataUrl) {
            errors['personalInformation.profilePhotoDataUrl'] = selectedTemplate.name + ' requires a professional portrait.';
        }
        if (!personal.fullName) errors['personalInformation.fullName'] = 'Full name is required.';
        if (!personal.professionalTitle) errors['personalInformation.professionalTitle'] = 'Professional title is required.';
        if (!personal.emailAddress) errors['personalInformation.emailAddress'] = 'Email address is required.';
        else if (!isEmail(personal.emailAddress)) errors['personalInformation.emailAddress'] = 'Enter a valid email address.';
        if (personal.phoneNumber && !isPhone(personal.phoneNumber)) errors['personalInformation.phoneNumber'] = 'Enter a valid phone number with 7 to 15 digits.';
        ['linkedInUrl', 'gitHubUrl', 'portfolioUrl'].forEach(function (field) {
            if (personal[field] && !isHttpUrl(personal[field])) errors['personalInformation.' + field] = 'Enter a complete http:// or https:// URL.';
        });

        state.education.forEach(function (entry, index) { addDateRangeErrors(errors, 'education.' + index, entry, 'isCurrentlyStudying'); });
        state.experience.forEach(function (entry, index) { addDateRangeErrors(errors, 'experience.' + index, entry, 'isCurrentlyWorking'); });
        state.projects.forEach(function (entry, index) {
            const base = 'projects.' + index;
            addDateRangeErrors(errors, base, entry, 'isOngoing');
            ['projectUrl', 'repositoryUrl'].forEach(function (field) {
                if (entry[field] && !isHttpUrl(entry[field])) errors[base + '.' + field] = 'Enter a complete http:// or https:// URL.';
            });
        });
        if (selectedTemplate.requiresSkillLevels && visibleSections.has('skills')) {
            state.skills.forEach(function (category, categoryIndex) {
                category.skills.forEach(function (skill, skillIndex) {
                    if (skill.name && !skill.level) {
                        errors['skills.' + categoryIndex + '.skills.' + skillIndex + '.level'] = 'Choose a 1-5 level for this template.';
                    }
                });
            });
        }
        if (selectedTemplate.requiresLanguageLevels && visibleSections.has('languages')) {
            state.languages.forEach(function (language, index) {
                if ((language.name || language.proficiency) && !language.level) {
                    errors['languages.' + index + '.level'] = 'Choose a 1-5 language level for this template.';
                }
            });
        }
        state.achievementsAndCertifications.forEach(function (entry, index) {
            const base = 'achievementsAndCertifications.' + index;
            if (entry.date && !isMonth(entry.date)) errors[base + '.date'] = 'Choose a valid month.';
            if (entry.credentialUrl && !isHttpUrl(entry.credentialUrl)) errors[base + '.credentialUrl'] = 'Enter a complete http:// or https:// URL.';
        });
        state.volunteerExperience.forEach(function (entry, index) { addDateRangeErrors(errors, 'volunteerExperience.' + index, entry, 'isCurrentlyVolunteering'); });
        state.references.forEach(function (entry, index) {
            const base = 'references.' + index;
            if (entry.emailAddress && !isEmail(entry.emailAddress)) errors[base + '.emailAddress'] = 'Enter a valid email address.';
            if (entry.phoneNumber && !isPhone(entry.phoneNumber)) errors[base + '.phoneNumber'] = 'Enter a valid phone number with 7 to 15 digits.';
        });
        state.customSections.forEach(function (section, sectionIndex) {
            section.entries.forEach(function (entry, entryIndex) {
                const base = 'customSections.' + sectionIndex + '.entries.' + entryIndex;
                addDateRangeErrors(errors, base, entry, 'isCurrent');
                if (entry.url && !isHttpUrl(entry.url)) errors[base + '.url'] = 'Enter a complete http:// or https:// URL.';
            });
        });
        return errors;
    }

    function formatMonth(value) {
        if (!isMonth(value)) return '';
        const parts = value.split('-');
        return MONTHS[Number(parts[1]) - 1] + ' ' + parts[0];
    }

    function formatDateRange(startDate, endDate, isCurrent) {
        const start = formatMonth(startDate);
        const end = isCurrent ? 'Present' : formatMonth(endDate);
        if (start && end) return start + ' - ' + end;
        return start || end;
    }

    function compact(values, separator) {
        return values.map(function (value) { return text(value, LIMITS.longText); }).filter(Boolean).join(separator || ' · ');
    }

    function hasText(value) {
        return typeof value === 'string' && value.trim().length > 0;
    }

    function hasEntryContent(entry) {
        if (!isRecord(entry)) return false;
        return Object.keys(entry).some(function (key) {
            if (key === 'id' || key.startsWith('isCurrent') || key === 'isOngoing') return false;
            const value = entry[key];
            return hasText(value) || (Array.isArray(value) && value.some(function (item) { return hasText(item) || hasEntryContent(item); }));
        });
    }

    function hasSectionContent(state, key) {
        if (key === 'professionalSummary') return hasText(state.professionalSummary);
        if (key === 'interests') return state.interests.some(hasText);
        const value = state[key];
        return Array.isArray(value) && value.some(hasEntryContent);
    }

    function richTextRuns(value, maximum) {
        const source = text(value, maximum || LIMITS.longText);
        const runs = [];
        const active = { b: 0, i: 0, u: 0 };
        const marker = /\[(\/?)(b|i|u)\]/gi;
        let position = 0;
        let match;

        function append(content) {
            if (!content) return;
            const run = { text: content };
            if (active.b > 0) run.bold = true;
            if (active.i > 0) run.italics = true;
            if (active.u > 0) run.decoration = 'underline';
            runs.push(run);
        }

        while ((match = marker.exec(source)) !== null) {
            append(source.slice(position, match.index));
            const tag = match[2].toLowerCase();
            if (match[1]) {
                if (active[tag] > 0) active[tag] -= 1;
                else append(match[0]);
            } else {
                active[tag] += 1;
            }
            position = marker.lastIndex;
        }
        append(source.slice(position));
        return runs;
    }

    function plainRichText(value, maximum) {
        return richTextRuns(value, maximum).map(function (run) { return run.text; }).join('');
    }

    function pdfRichText(value, options, maximum) {
        return Object.assign({ text: richTextRuns(value, maximum || LIMITS.longText) }, options || {});
    }

    function estimatedSummaryLines(value) {
        const plain = plainRichText(value, LIMITS.summary);
        if (!plain.trim()) return 0;
        return plain.split('\n').reduce(function (total, line) {
            const length = line.trim().length;
            return total + Math.max(1, Math.ceil(length / 105));
        }, 0);
    }

    function onePageGuidance(value) {
        const state = normalizeState(value);
        const visible = new Set(state.sections.filter(function (section) { return section.isVisible; }).map(function (section) { return section.key; }));
        const metrics = {
            professionalSummary: { value: visible.has('professionalSummary') ? estimatedSummaryLines(state.professionalSummary) : 0, maximum: ONE_PAGE_RECOMMENDATIONS.professionalSummary, label: 'summary lines' },
            education: { value: visible.has('education') ? state.education.filter(hasEntryContent).length : 0, maximum: ONE_PAGE_RECOMMENDATIONS.education, label: 'education entries' },
            experience: { value: visible.has('experience') ? state.experience.filter(hasEntryContent).length : 0, maximum: ONE_PAGE_RECOMMENDATIONS.experience, label: 'experience entries' },
            projects: { value: visible.has('projects') ? state.projects.filter(hasEntryContent).length : 0, maximum: ONE_PAGE_RECOMMENDATIONS.projects, label: 'projects' },
            achievementsAndCertifications: { value: visible.has('achievementsAndCertifications') ? state.achievementsAndCertifications.filter(hasEntryContent).length : 0, maximum: ONE_PAGE_RECOMMENDATIONS.achievementsAndCertifications, label: 'achievements' }
        };
        const exceeded = Object.keys(metrics).filter(function (key) { return metrics[key].value > metrics[key].maximum; });
        return { metrics: metrics, exceeded: exceeded };
    }

    function largestContentSections(value, maximum) {
        const state = normalizeState(value);
        const visible = new Set(state.sections.filter(function (section) { return section.isVisible; }).map(function (section) { return section.key; }));
        const scores = {
            professionalSummary: plainRichText(state.professionalSummary, LIMITS.summary).length,
            education: state.education.reduce(function (total, entry) { return total + JSON.stringify(entry).length; }, 0),
            experience: state.experience.reduce(function (total, entry) { return total + JSON.stringify(entry).length; }, 0),
            projects: state.projects.reduce(function (total, entry) { return total + JSON.stringify(entry).length; }, 0),
            skills: state.skills.reduce(function (total, entry) { return total + JSON.stringify(entry).length; }, 0),
            achievementsAndCertifications: state.achievementsAndCertifications.reduce(function (total, entry) { return total + JSON.stringify(entry).length; }, 0),
            languages: state.languages.reduce(function (total, entry) { return total + JSON.stringify(entry).length; }, 0),
            volunteerExperience: state.volunteerExperience.reduce(function (total, entry) { return total + JSON.stringify(entry).length; }, 0),
            references: state.references.reduce(function (total, entry) { return total + JSON.stringify(entry).length; }, 0),
            interests: state.interests.join(' ').length,
            customSections: state.customSections.reduce(function (total, entry) { return total + JSON.stringify(entry).length; }, 0)
        };
        const titles = new Map(state.sections.map(function (section) { return [section.key, section.title]; }));
        return Object.keys(scores)
            .filter(function (key) { return visible.has(key) && scores[key] > 0; })
            .sort(function (left, right) { return scores[right] - scores[left]; })
            .slice(0, Math.max(1, Number(maximum) || 3))
            .map(function (key) { return { key: key, title: titles.get(key) || key, score: scores[key] }; });
    }

    async function countPdfPages(blob) {
        if (!blob || typeof blob.arrayBuffer !== 'function') throw new Error('A PDF Blob is required.');
        const bytes = new Uint8Array(await blob.arrayBuffer());
        let source = '';
        const chunkSize = 32768;
        for (let index = 0; index < bytes.length; index += chunkSize) {
            source += String.fromCharCode.apply(null, bytes.subarray(index, Math.min(index + chunkSize, bytes.length)));
        }
        const pages = source.match(/\/Type\s*\/Page\b/g);
        if (!pages || !pages.length) throw new Error('The generated PDF page count could not be read.');
        return pages.length;
    }

    function pdfText(value, options) {
        return Object.assign({ text: text(value, LIMITS.longText) }, options || {});
    }

    function linkedText(label, url, options) {
        const safeUrl = parseHttpUrl(url);
        const node = pdfText(label || url, Object.assign({ color: '#0000EE', decoration: 'underline' }, options || {}));
        if (safeUrl) node.link = safeUrl;
        return node;
    }

    function rightDate(value) {
        return pdfText(value, { alignment: 'right', italics: true, color: '#333333', noWrap: true });
    }

    function headingColumns(left, right) {
        const columns = [{ width: '*', text: left || '' }];
        if (right) columns.push({ width: 'auto', text: right, alignment: 'right', italics: true, color: '#333333', noWrap: true });
        return { columns: columns, columnGap: 8, margin: [0, 0, 0, 1] };
    }

    function bulletList(items) {
        const bullets = items.map(function (item) { return text(item, LIMITS.bullet); }).filter(function (item) { return plainRichText(item, LIMITS.bullet).trim(); });
        return bullets.length ? {
            stack: bullets.map(function (item) {
                return {
                    columns: [
                        { width: 8, text: '-', alignment: 'right' },
                        { width: '*', text: richTextRuns(item, LIMITS.bullet) }
                    ],
                    columnGap: 4,
                    margin: [0, 0, 0, 0.5]
                };
            }),
            margin: [12, 0.5, 0, 1],
            lineHeight: 1.03
        } : null;
    }

    function sectionHeading(title, profile) {
        const theme = profile || {};
        if (theme.headingType === 'bar') {
            return {
                table: {
                    widths: ['*'],
                    body: [[{ text: text(title, LIMITS.shortText).toLocaleUpperCase(), style: 'sectionHeading', color: '#ffffff', fillColor: theme.accent, margin: [6, 1.5, 6, 1.5] }]]
                },
                layout: 'noBorders',
                margin: [0, 2, 0, 1]
            };
        }
        if (theme.headingType === 'compact') {
            return pdfText(title.toLocaleUpperCase(), {
                style: 'sectionHeading',
                color: theme.accent,
                margin: [0, 4, 0, 2]
            });
        }
        return {
            stack: [
                pdfText(theme.uppercaseHeadings ? title.toLocaleUpperCase() : title, { style: 'sectionHeading', color: theme.accent }),
                { canvas: [{ type: 'line', x1: 0, y1: 0, x2: theme.ruleWidth || 523, y2: 0, lineWidth: theme.ruleWeight || 0.55, lineColor: theme.accent || '#111111' }], margin: [0, 0.5, 0, 2.5] }
            ],
            margin: [0, 2.5, 0, 0]
        };
    }

    function educationNodes(entries) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const degree = compact([entry.degree, entry.fieldOfStudy], ', ');
            const title = degree || entry.institutionName;
            const secondary = degree ? entry.institutionName : '';
            const details = compact([entry.location, entry.grade], ' | ');
            const stack = [headingColumns(pdfText(title, { bold: true }), formatDateRange(entry.startDate, entry.endDate, entry.isCurrentlyStudying))];
            if (secondary) stack.push(pdfText(secondary, { italics: true }));
            if (details) stack.push(pdfText(details, { color: '#333333' }));
            if (entry.descriptionOrCoursework) stack.push(pdfRichText(entry.descriptionOrCoursework));
            return { stack: stack, margin: [0, 0, 0, 2.5] };
        });
    }

    function experienceNodes(entries, volunteer) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const organization = volunteer ? entry.organizationName : entry.companyName;
            const role = volunteer ? entry.role : entry.jobTitle;
            const current = volunteer ? entry.isCurrentlyVolunteering : entry.isCurrentlyWorking;
            const titleParts = [];
            if (role) titleParts.push({ text: role, bold: true });
            if (role && organization) titleParts.push({ text: ' - ' });
            if (organization) titleParts.push({ text: organization, bold: !role });
            const stack = [headingColumns(titleParts, formatDateRange(entry.startDate, entry.endDate, current))];
            const metadata = compact(volunteer ? [entry.location] : [entry.employmentType, entry.location], ' | ');
            if (metadata) stack.push(pdfText(metadata, { italics: true, color: '#333333' }));
            const bullets = bulletList(entry.bulletPoints);
            if (bullets) stack.push(bullets);
            return { stack: stack, margin: [0, 0, 0, 2] };
        });
    }

    function projectNodes(entries) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const technology = entry.technologiesUsed.filter(hasText).join(', ');
            const left = [];
            if (entry.projectName) left.push({ text: entry.projectName, bold: true });
            if (technology) left.push({ text: (entry.projectName ? ' | ' : '') + technology, italics: true });
            const stack = [headingColumns(left, formatDateRange(entry.startDate, entry.endDate, entry.isOngoing))];
            const bullets = bulletList(entry.bulletPoints);
            if (bullets) stack.push(bullets);
            const links = [];
            if (parseHttpUrl(entry.projectUrl)) links.push(linkedText('Live project', entry.projectUrl));
            if (parseHttpUrl(entry.repositoryUrl)) links.push(linkedText('Repository', entry.repositoryUrl));
            if (links.length) {
                const joined = [];
                links.forEach(function (link, index) {
                    if (index) joined.push({ text: ' | ', color: '#333333' });
                    joined.push(link);
                });
                stack.push({ text: joined, margin: [12, 0, 0, 2] });
            }
            return { stack: stack, margin: [0, 0, 0, 2] };
        });
    }

    function skillNodes(entries) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const line = [];
            if (entry.name) line.push({ text: entry.name + (entry.skills.length ? ': ' : ''), bold: true });
            if (entry.skills.length) {
                line.push({
                    text: entry.skills.filter(hasEntryContent).map(function (skill) {
                        return skill.name + (skill.level ? ' (' + skill.level + '/5)' : '');
                    }).join(', ')
                });
            }
            return { text: line, margin: [0, 0, 0, 1] };
        });
    }

    function achievementNodes(entries) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const left = [];
            if (entry.title) left.push({ text: entry.title, bold: true });
            if (entry.issuingOrganization) left.push({ text: (entry.title ? ' - ' : '') + entry.issuingOrganization });
            const stack = [headingColumns(left, formatMonth(entry.date))];
            if (entry.description) stack.push(pdfRichText(entry.description));
            if (parseHttpUrl(entry.credentialUrl)) stack.push(linkedText('View credential', entry.credentialUrl));
            return { stack: stack, margin: [0, 0, 0, 2] };
        });
    }

    function languageNodes(entries) {
        const values = entries.filter(hasEntryContent).map(function (entry) {
            const label = compact([entry.name, entry.proficiency], ' - ');
            return label + (entry.level ? ' (' + entry.level + '/5)' : '');
        }).filter(Boolean);
        return values.length ? [{ text: values.join(' | '), margin: [0, 0, 0, 2] }] : [];
    }

    function referenceNodes(entries) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const role = compact([entry.jobTitle, entry.company], ' - ');
            const stack = [];
            if (entry.fullName) stack.push(pdfText(entry.fullName, { bold: true }));
            if (role) stack.push(pdfText(role, { italics: true, color: '#475569' }));
            const contact = [];
            if (isEmail(entry.emailAddress)) contact.push(contactLink(entry.emailAddress, 'mailto:' + entry.emailAddress, 'left'));
            if (isPhone(entry.phoneNumber)) {
                if (contact.length) contact.push({ text: ' | ', color: '#64748b' });
                contact.push(contactLink(entry.phoneNumber, 'tel:' + entry.phoneNumber.replace(/[^+\d]/g, ''), 'left'));
            }
            if (contact.length) stack.push({ text: contact });
            return { stack: stack, margin: [0, 0, 0, 3] };
        });
    }

    function customEntryNodes(entries) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const left = [];
            if (entry.heading) left.push({ text: entry.heading, bold: true });
            if (entry.subheading) left.push({ text: (entry.heading ? ' - ' : '') + entry.subheading });
            const stack = [headingColumns(left, formatDateRange(entry.startDate, entry.endDate, entry.isCurrent))];
            if (entry.location) stack.push(pdfText(entry.location, { italics: true, color: '#333333' }));
            if (parseHttpUrl(entry.url)) stack.push(linkedText('Link', entry.url));
            const bullets = bulletList(entry.bulletPoints);
            if (bullets) stack.push(bullets);
            return { stack: stack, margin: [0, 0, 0, 3] };
        });
    }

    function nodesForSection(state, descriptor) {
        switch (descriptor.key) {
            case 'professionalSummary':
                return state.professionalSummary.split(/\n\s*\n/).map(function (paragraph) { return text(paragraph, LIMITS.summary); }).filter(Boolean).map(function (paragraph) {
                    return pdfRichText(paragraph, { margin: [0, 0, 0, 2] }, LIMITS.summary);
                });
            case 'education': return educationNodes(state.education);
            case 'skills': return skillNodes(state.skills);
            case 'experience': return experienceNodes(state.experience, false);
            case 'projects': return projectNodes(state.projects);
            case 'achievementsAndCertifications': return achievementNodes(state.achievementsAndCertifications);
            case 'languages': return languageNodes(state.languages);
            case 'volunteerExperience': return experienceNodes(state.volunteerExperience, true);
            case 'references': return referenceNodes(state.references);
            case 'interests': return state.interests.length ? [pdfText(state.interests.join(' | '), { margin: [0, 0, 0, 2] })] : [];
            default: return [];
        }
    }

    function contactLink(label, link, alignment, color) {
        return { text: label, link: link, color: color || '#0000EE', decoration: 'underline', alignment: alignment || 'right' };
    }

    function contactStack(personal, alignment, color) {
        const contact = [];
        const textAlignment = alignment || 'right';
        const linkColor = color || '#0000EE';
        if (personal.phoneNumber || personal.location) {
            const phoneLocation = [];
            if (personal.phoneNumber) {
                const phone = { text: personal.phoneNumber };
                if (isPhone(personal.phoneNumber)) {
                    phone.link = 'tel:' + personal.phoneNumber.replace(/[^+\d]/g, '');
                    phone.color = linkColor;
                    phone.decoration = 'underline';
                }
                phoneLocation.push(phone);
            }
            if (personal.phoneNumber && personal.location) phoneLocation.push({ text: ' | ' });
            if (personal.location) phoneLocation.push({ text: personal.location });
            contact.push({ text: phoneLocation, alignment: textAlignment });
        }
        if (isEmail(personal.emailAddress)) contact.push(contactLink(personal.emailAddress, 'mailto:' + personal.emailAddress, textAlignment, linkColor));
        [
            ['LinkedIn', personal.linkedInUrl],
            ['GitHub', personal.gitHubUrl],
            ['Portfolio', personal.portfolioUrl]
        ].forEach(function (entry) {
            const url = parseHttpUrl(entry[1]);
            if (url) contact.push(contactLink(entry[0], url, textAlignment, linkColor));
        });
        return contact;
    }

    function contactLine(personal, alignment, color) {
        const linkColor = color || '#0000EE';
        const runs = [];
        function add(run) {
            if (runs.length) runs.push({ text: '  |  ', color: color || '#475569' });
            runs.push(run);
        }
        if (personal.phoneNumber) {
            const phone = { text: personal.phoneNumber, color: color || '#111827' };
            if (isPhone(personal.phoneNumber)) {
                phone.link = 'tel:' + personal.phoneNumber.replace(/[^+\d]/g, '');
                phone.color = linkColor;
                phone.decoration = 'underline';
            }
            add(phone);
        }
        if (personal.location) add({ text: personal.location, color: color || '#475569' });
        if (isEmail(personal.emailAddress)) add(contactLink(personal.emailAddress, 'mailto:' + personal.emailAddress, alignment, linkColor));
        [
            ['LinkedIn', personal.linkedInUrl],
            ['GitHub', personal.gitHubUrl],
            ['Portfolio', personal.portfolioUrl]
        ].forEach(function (entry) {
            const url = parseHttpUrl(entry[1]);
            if (url) add(contactLink(entry[0], url, alignment, linkColor));
        });
        return { text: runs, alignment: alignment || 'left', margin: [0, 3, 0, 0] };
    }

    function identityStack(personal, alignment, color) {
        const identity = [];
        const options = alignment ? { alignment: alignment } : {};
        if (color) options.color = color;
        if (personal.fullName) identity.push(pdfText(personal.fullName, Object.assign({ style: 'name' }, options)));
        if (personal.professionalTitle) identity.push(pdfText(personal.professionalTitle, Object.assign({ style: 'professionalTitle' }, options)));
        return identity.length ? identity : [{ text: '' }];
    }

    function profilePhotoNode(personal, size, margin) {
        const photo = normalizeProfilePhotoDataUrl(personal && personal.profilePhotoDataUrl);
        if (!photo) return null;
        return {
            image: photo,
            width: size,
            height: size,
            alignment: 'center',
            margin: margin || [0, 0, 0, 8]
        };
    }

    function sectionContent(state, profile, include) {
        const content = [];
        state.sections.forEach(function (descriptor) {
            if (include && !include(descriptor.key)) return;
            if (!descriptor.isVisible || !hasSectionContent(state, descriptor.key)) return;
            if (descriptor.key === 'customSections') {
                state.customSections.filter(hasEntryContent).forEach(function (section) {
                    const nodes = customEntryNodes(section.entries);
                    if (!nodes.length) return;
                    content.push(sectionHeading(section.title || descriptor.title, profile));
                    Array.prototype.push.apply(content, nodes);
                });
                return;
            }
            const nodes = nodesForSection(state, descriptor);
            if (!nodes.length) return;
            const templateTitle = profile.sectionTitles && profile.sectionTitles[descriptor.key];
            content.push(sectionHeading(templateTitle || descriptor.title, profile));
            Array.prototype.push.apply(content, nodes);
        });
        return content;
    }

    function createDefinition(state, profile, content) {
        const personal = state.personalInformation;
        return {
            pageSize: 'A4',
            pageMargins: profile.pageMargins,
            info: {
                title: (personal.fullName || 'ApplyWise') + ' Resume',
                author: personal.fullName || 'ApplyWise Resume Builder',
                subject: 'Professional resume',
                keywords: 'ApplyWise resume template ' + profile.id
            },
            content: content,
            defaultStyle: { font: profile.font, fontSize: profile.fontSize, color: profile.textColor, lineHeight: profile.lineHeight },
            styles: {
                name: { fontSize: profile.nameSize, bold: true, characterSpacing: profile.nameSpacing, lineHeight: 1, color: profile.nameColor || profile.textColor },
                professionalTitle: { fontSize: profile.titleSize, color: profile.mutedColor, margin: [0, 1, 0, 0] },
                sectionHeading: { fontSize: profile.headingSize, bold: true, characterSpacing: profile.headingSpacing, color: profile.accent }
            }
        };
    }

    const TEMPLATE_PROFILES = Object.freeze({
        classic: Object.freeze({
            id: 'classic', font: 'Roboto', pageMargins: [30, 22, 30, 22], fontSize: 7.55, lineHeight: 1.04,
            textColor: '#292929', mutedColor: '#5b5b5b', accent: '#4b5563', nameColor: '#343434', nameSize: 24,
            titleSize: 8.8, headingSize: 8.6, nameSpacing: 2.4, headingSpacing: 1.3, uppercaseHeadings: true,
            sectionTitles: Object.freeze({ professionalSummary: 'Profile Summary', experience: 'Work Experience', skills: 'Skills', achievementsAndCertifications: 'Certifications & Awards' })
        }),
        emerald: Object.freeze({
            id: 'emerald', font: 'Poppins', pageMargins: [18, 18, 18, 18], fontSize: 7.05, lineHeight: 1.02,
            textColor: '#27312b', mutedColor: '#66716a', accent: '#17633a', nameColor: '#ffffff', nameSize: 22,
            titleSize: 8.2, headingSize: 8.4, nameSpacing: 0.2, headingSpacing: 0.5, headingType: 'compact',
            sectionTitles: Object.freeze({ professionalSummary: 'Professional Profile', experience: 'Work History', skills: 'Core Skills', achievementsAndCertifications: 'Certifications' })
        }),
        modern: Object.freeze({
            id: 'modern', font: 'Poppins', pageMargins: [18, 18, 18, 18], fontSize: 7.05, lineHeight: 1.02,
            textColor: '#263440', mutedColor: '#607181', accent: '#17324d', nameColor: '#17324d', nameSize: 22,
            titleSize: 8.5, headingSize: 8.4, nameSpacing: 0.15, headingSpacing: 0.55, headingType: 'compact',
            sectionTitles: Object.freeze({ professionalSummary: 'Profile', experience: 'Work Experience', skills: 'Professional Skills', achievementsAndCertifications: 'Certificates' })
        }),
        executive: Object.freeze({
            id: 'executive', font: 'Poppins', pageMargins: [20, 18, 20, 18], fontSize: 7.15, lineHeight: 1.025,
            textColor: '#302f2c', mutedColor: '#6f6b63', accent: '#777517', nameColor: '#ffffff', nameSize: 25,
            titleSize: 8.8, headingSize: 8.6, nameSpacing: 1.1, headingSpacing: 0.7, headingType: 'compact', uppercaseHeadings: true,
            sectionTitles: Object.freeze({ professionalSummary: 'About Me', experience: 'Professional Experience', skills: 'Key Skills', achievementsAndCertifications: 'Certifications' })
        }),
        compact: Object.freeze({
            id: 'compact', font: 'Poppins', pageMargins: [18, 18, 18, 18], fontSize: 7.05, lineHeight: 1.02,
            textColor: '#33404c', mutedColor: '#66798a', accent: '#5b86b5', nameColor: '#3d638c', nameSize: 20,
            titleSize: 8.4, headingSize: 8.4, nameSpacing: 0.1, headingSpacing: 0.45, headingType: 'compact',
            sectionTitles: Object.freeze({ professionalSummary: 'About Me', experience: 'Experience', skills: 'Skills', achievementsAndCertifications: 'Credentials' })
        }),
        minimal: Object.freeze({
            id: 'minimal', font: 'Roboto', pageMargins: [32, 23, 32, 23], fontSize: 7.25, lineHeight: 1.025,
            textColor: '#1f2937', mutedColor: '#64748b', accent: '#2563eb', nameColor: '#111827', nameSize: 23,
            titleSize: 8.8, headingSize: 8.4, nameSpacing: 0.25, headingSpacing: 0.85, headingType: 'compact', uppercaseHeadings: true,
            sectionTitles: Object.freeze({ professionalSummary: 'Summary', experience: 'Experience', skills: 'Skills', achievementsAndCertifications: 'Certifications & Awards' })
        }),
        corporate: Object.freeze({
            id: 'corporate', font: 'Poppins', pageMargins: [20, 18, 20, 18], fontSize: 6.95, lineHeight: 1.015,
            textColor: '#243044', mutedColor: '#60718d', accent: '#243b64', nameColor: '#ffffff', nameSize: 23,
            titleSize: 8.4, headingSize: 8.25, nameSpacing: 0.45, headingSpacing: 0.55, headingType: 'compact', uppercaseHeadings: true,
            sectionTitles: Object.freeze({ professionalSummary: 'Executive Profile', experience: 'Professional Experience', skills: 'Core Competencies', achievementsAndCertifications: 'Credentials' })
        }),
        timeline: Object.freeze({
            id: 'timeline', font: 'Roboto', pageMargins: [24, 20, 24, 20], fontSize: 7.15, lineHeight: 1.02,
            textColor: '#233238', mutedColor: '#627378', accent: '#0f766e', nameColor: '#163b3a', nameSize: 23,
            titleSize: 8.7, headingSize: 8.35, nameSpacing: 0.3, headingSpacing: 0.7, headingType: 'compact', uppercaseHeadings: true,
            sectionTitles: Object.freeze({ professionalSummary: 'Profile', experience: 'Career Timeline', skills: 'Capabilities', achievementsAndCertifications: 'Credentials' })
        }),
        studio: Object.freeze({
            id: 'studio', font: 'Poppins', pageMargins: [20, 18, 20, 18], fontSize: 6.95, lineHeight: 1.015,
            textColor: '#342f38', mutedColor: '#746b79', accent: '#6d4c7d', nameColor: '#ffffff', nameSize: 23,
            titleSize: 8.5, headingSize: 8.3, nameSpacing: 0.7, headingSpacing: 0.5, headingType: 'compact',
            sectionTitles: Object.freeze({ professionalSummary: 'Profile', experience: 'Selected Experience', skills: 'Expertise', achievementsAndCertifications: 'Recognition' })
        })
    });

    function templateProfile(profile, changes) {
        return Object.assign({}, profile, changes || {});
    }

    function sectionContentFor(state, profile, keys) {
        const accepted = new Set(keys);
        return sectionContent(state, profile, function (key) { return accepted.has(key); });
    }

    function remainingSectionContent(state, profile, excludedKeys) {
        const excluded = new Set(excludedKeys);
        return sectionContent(state, profile, function (key) { return !excluded.has(key); });
    }

    function contactSection(personal, profile, color) {
        const content = [sectionHeading('Contact', profile)];
        Array.prototype.push.apply(content, contactStack(personal, 'left', color || profile.accent));
        return content;
    }

    function twoCellResume(sidebarWidth, sidebar, main, sidebarOptions) {
        const options = sidebarOptions || {};
        return {
            table: {
                widths: [sidebarWidth, '*'],
                body: [[
                    { stack: sidebar, fillColor: options.fillColor, color: options.color, margin: options.margin || [10, 10, 10, 10] },
                    { stack: main, margin: options.mainMargin || [13, 8, 7, 8] }
                ]]
            },
            layout: {
                hLineWidth: function () { return 0; },
                vLineWidth: function () { return 0; },
                paddingLeft: function () { return 0; },
                paddingRight: function () { return 0; },
                paddingTop: function () { return 0; },
                paddingBottom: function () { return 0; }
            }
        };
    }

    function twoCellResumeRight(sidebarWidth, main, sidebar, sidebarOptions) {
        const options = sidebarOptions || {};
        return {
            table: {
                widths: ['*', sidebarWidth],
                body: [[
                    { stack: main, margin: options.mainMargin || [7, 8, 13, 8] },
                    { stack: sidebar, fillColor: options.fillColor, color: options.color, margin: options.margin || [10, 10, 10, 10] }
                ]]
            },
            layout: {
                hLineWidth: function () { return 0; },
                vLineWidth: function () { return 0; },
                paddingLeft: function () { return 0; },
                paddingRight: function () { return 0; },
                paddingTop: function () { return 0; },
                paddingBottom: function () { return 0; }
            }
        };
    }

    function buildClassicDefinition(state, profile) {
        const personal = state.personalInformation;
        const sidebarKeys = ['education', 'skills', 'languages', 'achievementsAndCertifications', 'interests'];
        const sidebarProfile = templateProfile(profile, { headingType: 'compact', headingSize: 8, headingSpacing: 1.1 });
        const mainProfile = templateProfile(profile, { ruleWidth: 337, ruleWeight: 0.55 });
        const sidebar = contactSection(personal, sidebarProfile, '#475569');
        Array.prototype.push.apply(sidebar, sectionContentFor(state, sidebarProfile, sidebarKeys));
        const main = remainingSectionContent(state, mainProfile, sidebarKeys);
        const content = [{
            stack: identityStack(personal, 'center'),
            margin: [0, 0, 0, 4]
        }, contactLine(personal, 'center', '#4b5563'), {
            canvas: [
                { type: 'line', x1: 0, y1: 0, x2: 535, y2: 0, lineWidth: 0.7, lineColor: '#9ca3af' },
                { type: 'line', x1: 0, y1: 4, x2: 535, y2: 4, lineWidth: 0.35, lineColor: '#d1d5db' }
            ],
            margin: [0, 6, 0, 6]
        }, {
            table: { widths: [150, '*'], body: [[{ stack: sidebar, margin: [0, 0, 13, 0] }, { stack: main, margin: [15, 0, 0, 0] }]] },
            layout: {
                hLineWidth: function () { return 0; },
                vLineWidth: function (index) { return index === 1 ? 0.55 : 0; },
                vLineColor: function () { return '#b8b8b8'; },
                paddingLeft: function () { return 0; }, paddingRight: function () { return 0; },
                paddingTop: function () { return 0; }, paddingBottom: function () { return 0; }
            }
        }];
        return createDefinition(state, profile, content);
    }

    function buildEmeraldDefinition(state, profile) {
        const personal = state.personalInformation;
        const sidebarKeys = ['skills', 'languages', 'interests', 'references'];
        const sidebarProfile = templateProfile(profile, { accent: '#e9f7ee', headingType: 'compact', mutedColor: '#d7eee0' });
        const mainProfile = templateProfile(profile, { accent: '#17633a', ruleWidth: 344 });
        const sidebar = [];
        const photo = profilePhotoNode(personal, 72, [0, 0, 0, 7]);
        if (photo) sidebar.push(photo);
        Array.prototype.push.apply(sidebar, identityStack(personal, 'left', '#ffffff'));
        sidebar.push({ canvas: [{ type: 'line', x1: 0, y1: 0, x2: 129, y2: 0, lineWidth: 0.65, lineColor: '#7eb692' }], margin: [0, 7, 0, 5] });
        Array.prototype.push.apply(sidebar, contactSection(personal, sidebarProfile, '#d8f3e1'));
        Array.prototype.push.apply(sidebar, sectionContentFor(state, sidebarProfile, sidebarKeys));
        const main = remainingSectionContent(state, mainProfile, sidebarKeys);
        return createDefinition(state, profile, [twoCellResume(142, sidebar, main, { fillColor: '#17633a', color: '#ffffff', margin: [11, 11, 10, 11], mainMargin: [16, 7, 8, 7] })]);
    }

    function buildModernDefinition(state, profile) {
        const personal = state.personalInformation;
        const sidebarKeys = ['skills', 'achievementsAndCertifications', 'languages', 'interests', 'references'];
        const sidebarProfile = templateProfile(profile, { accent: '#d9edf7', headingType: 'compact', mutedColor: '#d7e5ef' });
        const mainProfile = templateProfile(profile, { accent: '#17324d', ruleWidth: 350 });
        const sidebar = [];
        const photo = profilePhotoNode(personal, 70, [0, 0, 0, 8]);
        if (photo) sidebar.push(photo);
        Array.prototype.push.apply(sidebar, contactSection(personal, sidebarProfile, '#cbe7f7'));
        Array.prototype.push.apply(sidebar, sectionContentFor(state, sidebarProfile, sidebarKeys));
        const main = identityStack(personal, 'left', '#17324d');
        main.push({ canvas: [{ type: 'line', x1: 0, y1: 0, x2: 350, y2: 0, lineWidth: 1.4, lineColor: '#5b94b8' }], margin: [0, 6, 0, 4] });
        Array.prototype.push.apply(main, remainingSectionContent(state, mainProfile, sidebarKeys));
        return createDefinition(state, profile, [twoCellResume(145, sidebar, main, { fillColor: '#17324d', color: '#ffffff', margin: [11, 11, 10, 11], mainMargin: [16, 8, 7, 8] })]);
    }

    function buildExecutiveDefinition(state, profile) {
        const personal = state.personalInformation;
        const sidebarKeys = ['skills', 'achievementsAndCertifications', 'languages', 'interests', 'references'];
        const sidebarProfile = templateProfile(profile, { accent: '#777517', headingType: 'compact', headingSize: 8.1 });
        const mainProfile = templateProfile(profile, { accent: '#777517', ruleWidth: 347 });
        const photo = profilePhotoNode(personal, 82, [0, 0, 0, 0]);
        const identity = identityStack(personal, 'left', '#ffffff');
        const headerCells = photo
            ? [{ stack: [photo], fillColor: '#777517', margin: [6, 5, 6, 5] }, { stack: identity, fillColor: '#777517', margin: [14, 20, 8, 12] }]
            : [{ stack: identity, fillColor: '#777517', margin: [18, 18, 18, 14] }];
        const header = {
            table: { widths: photo ? [102, '*'] : ['*'], body: [headerCells] },
            layout: 'noBorders',
            margin: [0, 0, 0, 7]
        };
        const sidebar = contactSection(personal, sidebarProfile, '#777517');
        Array.prototype.push.apply(sidebar, sectionContentFor(state, sidebarProfile, sidebarKeys));
        const main = remainingSectionContent(state, mainProfile, sidebarKeys);
        return createDefinition(state, profile, [header, twoCellResume(144, sidebar, main, { fillColor: '#f5f2e9', color: '#33312b', margin: [10, 8, 11, 8], mainMargin: [16, 5, 7, 7] })]);
    }

    function buildCompactDefinition(state, profile) {
        const personal = state.personalInformation;
        const sidebarKeys = ['professionalSummary', 'skills', 'languages', 'interests', 'references'];
        const sidebarProfile = templateProfile(profile, { accent: '#4e789f', headingType: 'compact', headingSize: 8.1 });
        const mainProfile = templateProfile(profile, { accent: '#5b86b5', ruleWidth: 350 });
        const sidebar = [];
        const photo = profilePhotoNode(personal, 74, [0, 0, 0, 7]);
        if (photo) sidebar.push(photo);
        Array.prototype.push.apply(sidebar, identityStack(personal, 'center', '#3d638c'));
        sidebar.push({ canvas: [{ type: 'line', x1: 0, y1: 0, x2: 136, y2: 0, lineWidth: 0.8, lineColor: '#87a9c9' }], margin: [0, 6, 0, 4] });
        Array.prototype.push.apply(sidebar, contactSection(personal, sidebarProfile, '#3d638c'));
        Array.prototype.push.apply(sidebar, sectionContentFor(state, sidebarProfile, sidebarKeys));
        const main = remainingSectionContent(state, mainProfile, sidebarKeys);
        return createDefinition(state, profile, [twoCellResume(150, sidebar, main, { fillColor: '#edf3f8', color: '#33404c', margin: [11, 9, 11, 9], mainMargin: [16, 5, 7, 7] })]);
    }

    function buildMinimalDefinition(state, profile) {
        const personal = state.personalInformation;
        const content = [{
            stack: identityStack(personal, 'left'),
            margin: [0, 0, 0, 2]
        }, contactLine(personal, 'left', '#2563eb'), {
            canvas: [{ type: 'line', x1: 0, y1: 0, x2: 531, y2: 0, lineWidth: 1.6, lineColor: '#2563eb' }],
            margin: [0, 7, 0, 6]
        }];
        Array.prototype.push.apply(content, sectionContent(state, templateProfile(profile, { ruleWidth: 531, ruleWeight: 0.45 })));
        return createDefinition(state, profile, content);
    }

    function buildCorporateDefinition(state, profile) {
        const personal = state.personalInformation;
        const sidebarKeys = ['education', 'skills', 'achievementsAndCertifications', 'languages', 'interests', 'references'];
        const sidebarProfile = templateProfile(profile, { accent: '#243b64', headingType: 'compact', headingSize: 7.9 });
        const mainProfile = templateProfile(profile, { accent: '#243b64', ruleWidth: 350 });
        const headerStack = identityStack(personal, 'left', '#ffffff');
        headerStack.push(contactLine(personal, 'left', '#e6efff'));
        const header = {
            table: { widths: ['*'], body: [[{ stack: headerStack, fillColor: '#243b64', margin: [16, 12, 16, 11] }]] },
            layout: 'noBorders',
            margin: [0, 0, 0, 7]
        };
        const main = remainingSectionContent(state, mainProfile, sidebarKeys);
        const sidebar = sectionContentFor(state, sidebarProfile, sidebarKeys);
        return createDefinition(state, profile, [header, twoCellResumeRight(150, main, sidebar, {
            fillColor: '#eef3fa', color: '#243044', margin: [11, 8, 10, 8], mainMargin: [4, 5, 16, 7]
        })]);
    }

    function buildTimelineDefinition(state, profile) {
        const personal = state.personalInformation;
        const bodyProfile = templateProfile(profile, { accent: '#0f766e', ruleWidth: 495, ruleWeight: 0.55 });
        const body = sectionContent(state, bodyProfile);
        const name = personal.fullName ? [pdfText(personal.fullName, { style: 'name' })] : [{ text: '' }];
        const timelineBody = {
            table: {
                widths: [9, '*'],
                body: [[
                    { text: '', fillColor: '#0f766e', margin: [0, 0, 0, 0] },
                    { stack: body, margin: [13, 2, 0, 2] }
                ]]
            },
            layout: {
                hLineWidth: function () { return 0; }, vLineWidth: function () { return 0; },
                paddingLeft: function () { return 0; }, paddingRight: function () { return 0; },
                paddingTop: function () { return 0; }, paddingBottom: function () { return 0; }
            }
        };
        return createDefinition(state, profile, [{
            columns: [
                { width: '*', stack: name },
                { width: 178, text: personal.professionalTitle || '', alignment: 'right', color: '#0f766e', bold: true, margin: [8, 7, 0, 0] }
            ],
            columnGap: 10,
            margin: [0, 0, 0, 2]
        }, contactLine(personal, 'left', '#0f766e'), {
            canvas: [{ type: 'line', x1: 0, y1: 0, x2: 547, y2: 0, lineWidth: 0.75, lineColor: '#9fd3ce' }],
            margin: [0, 6, 0, 6]
        }, timelineBody]);
    }

    function buildStudioDefinition(state, profile) {
        const personal = state.personalInformation;
        const sidebarKeys = ['education', 'skills', 'achievementsAndCertifications', 'languages', 'interests', 'references'];
        const mainProfile = templateProfile(profile, { accent: '#6d4c7d', ruleWidth: 338 });
        const sidebarProfile = templateProfile(profile, { accent: '#6d4c7d', headingType: 'compact', headingSize: 7.9 });
        const photo = profilePhotoNode(personal, 72, [0, 0, 0, 0]);
        const identity = identityStack(personal, 'left', '#ffffff');
        const headerCells = photo
            ? [{ stack: [photo], fillColor: '#6d4c7d', margin: [8, 6, 8, 6] }, { stack: identity, fillColor: '#6d4c7d', margin: [14, 17, 10, 12] }]
            : [{ stack: identity, fillColor: '#6d4c7d', margin: [17, 15, 17, 13] }];
        const header = {
            table: { widths: photo ? [92, '*'] : ['*'], body: [headerCells] },
            layout: 'noBorders',
            margin: [0, 0, 0, 5]
        };
        const main = remainingSectionContent(state, mainProfile, sidebarKeys);
        const sidebar = sectionContentFor(state, sidebarProfile, sidebarKeys);
        return createDefinition(state, profile, [header, contactLine(personal, 'center', '#6d4c7d'), {
            canvas: [{ type: 'line', x1: 0, y1: 0, x2: 555, y2: 0, lineWidth: 0.55, lineColor: '#d8cde0' }],
            margin: [0, 5, 0, 4]
        }, twoCellResumeRight(158, main, sidebar, {
            fillColor: '#f5f0f7', color: '#342f38', margin: [11, 7, 10, 7], mainMargin: [3, 4, 16, 7]
        })]);
    }

    function buildDocumentDefinition(value) {
        const state = normalizeState(value);
        if (templateRenderers && templateRenderers.isTemplate(state.templateId)) {
            const fixedOrder = templateRenderers.SECTION_ORDERS[state.templateId] || [];
            const descriptors = new Map(state.sections.map(function (section) { return [section.key, section]; }));
            state.sections = fixedOrder.map(function (key) { return descriptors.get(key); }).filter(Boolean);
            const lockedDefinition = templateRenderers.buildDocumentDefinition(state, {
                createDefinition: createDefinition,
                templateProfile: templateProfile,
                sectionContentFor: sectionContentFor,
                remainingSectionContent: remainingSectionContent,
                contactSection: contactSection,
                contactStack: contactStack,
                contactLine: contactLine,
                identityStack: identityStack,
                profilePhotoNode: profilePhotoNode,
                twoCellResume: twoCellResume,
                twoCellResumeRight: twoCellResumeRight,
                sectionHeading: sectionHeading,
                pdfText: pdfText,
                pdfRichText: pdfRichText,
                safeUrl: parseHttpUrl
            });
            if (lockedDefinition) return lockedDefinition;
        }
        const profile = TEMPLATE_PROFILES[state.templateId] || TEMPLATE_PROFILES[DEFAULT_TEMPLATE_ID];
        switch (state.templateId) {
            case 'emerald': return buildEmeraldDefinition(state, profile);
            case 'modern': return buildModernDefinition(state, profile);
            case 'executive': return buildExecutiveDefinition(state, profile);
            case 'compact': return buildCompactDefinition(state, profile);
            case 'minimal': return buildMinimalDefinition(state, profile);
            case 'corporate': return buildCorporateDefinition(state, profile);
            case 'timeline': return buildTimelineDefinition(state, profile);
            case 'studio': return buildStudioDefinition(state, profile);
            default: return buildClassicDefinition(state, profile);
        }
    }

    function buildFilename(fullName) {
        let normalized = text(fullName, LIMITS.name);
        try { normalized = normalized.normalize('NFKD').replace(/[\u0300-\u036f]/g, ''); } catch (_error) { /* Old browsers. */ }
        const parts = normalized.replace(/[^A-Za-z0-9\s_-]/g, ' ').split(/\s+/).map(function (part) {
            return part.replace(/[^A-Za-z0-9_-]/g, '');
        }).filter(Boolean);
        const selected = parts.length > 1 ? [parts[0], parts[parts.length - 1]] : parts;
        const stem = selected.join('_').slice(0, 70) || 'ApplyWise';
        return stem + '_Resume.pdf';
    }

    function createSampleState() {
        return normalizeState({
            schemaVersion: SCHEMA_VERSION,
            templateSelectionConfirmed: true,
            personalInformation: {
                fullName: 'Jordan Lee',
                professionalTitle: 'Product-Minded Software Engineer',
                phoneNumber: '+1 202 555 0147',
                emailAddress: 'jordan.lee@example.com',
                location: 'Seattle, WA',
                linkedInUrl: 'https://example.com/jordan-linkedin',
                gitHubUrl: 'https://example.com/jordan-github',
                portfolioUrl: 'https://example.com/jordan-portfolio'
            },
            professionalSummary: '[b]Product-minded software engineer[/b] building accessible full-stack applications with React, .NET, and PostgreSQL. Turns ambiguous requirements into reliable features, communicates clearly across teams, and improves delivery through pragmatic testing and automation.',
            education: [
                { id: 'education-northbridge', institutionName: 'Northbridge University', degree: 'Bachelor of Science', fieldOfStudy: 'Computer Science', startDate: '2023-08', endDate: '2027-06', location: 'Seattle, WA', grade: 'GPA: 3.6 / 4.0', descriptionOrCoursework: 'Relevant coursework: [i]Data Structures, Algorithms, Databases, Operating Systems[/i]' },
                { id: 'education-harbor', institutionName: 'Harbor College', degree: 'Intermediate', fieldOfStudy: 'Pre-Engineering', startDate: '2020-08', endDate: '2022-06', location: 'Seattle, WA', grade: '90%', descriptionOrCoursework: 'Mathematics, Physics, and Chemistry' }
            ],
            experience: [
                { id: 'experience-codesprint', companyName: 'CodeSprint - Northbridge University', jobTitle: 'Co-Head', employmentType: 'Leadership', technologySkills: 'Full-Stack Development, Contest Management, Team Coordination', location: 'Seattle, WA', startDate: '2025-03', endDate: '2026-01', bulletPoints: ['Co-led a full-stack competition platform supporting registration, problem access, and [b]real-time results[/b].'] },
                { id: 'experience-workshops', companyName: 'Open Source Society', jobTitle: 'Workshop Lead', employmentType: 'Volunteer', technologySkills: 'Git, GitHub, Public Speaking, Team Leadership', location: 'Seattle, WA', startDate: '2024-08', isCurrentlyWorking: true, bulletPoints: ['Lead practical Git and GitHub workshops for student development teams.'] },
                { id: 'experience-intern', companyName: 'Example Labs', jobTitle: 'Software Engineering Intern', employmentType: 'Internship', technologySkills: '.NET, PostgreSQL, Automated Testing', location: 'Remote', startDate: '2024-06', endDate: '2024-08', bulletPoints: ['Shipped tested API and dashboard improvements that reduced manual reporting work.'] }
            ],
            projects: [
                { id: 'project-campus-collab', projectName: 'Campus Collaboration Hub', projectUrl: 'https://example.com/campus-hub', repositoryUrl: 'https://example.com/campus-hub-source', technologiesUsed: ['Next.js', 'Python', 'PostgreSQL'], startDate: '2025-01', isOngoing: true, bulletPoints: ['Connected university projects with skill-matched contributors across batches.'] },
                { id: 'project-investments', projectName: 'Investment Recommender', repositoryUrl: 'https://example.com/investments-source', technologiesUsed: ['Python', 'Pandas', 'Streamlit'], startDate: '2024-08', endDate: '2024-12', bulletPoints: ['Built recommendation filters and interactive portfolio visualizations.'] },
                { id: 'project-scoreboard', projectName: 'CodeVerse Scoreboard', repositoryUrl: 'https://example.com/scoreboard-source', technologiesUsed: ['React', 'TypeScript', 'WebSockets'], startDate: '2024-03', endDate: '2024-07', bulletPoints: ['Automated live standings and delivered score changes through [u]WebSockets[/u].'] }
            ],
            skills: [
                { id: 'skills-languages', name: 'Languages', skills: [{ id: 'skill-js', name: 'JavaScript', level: 5 }, { id: 'skill-ts', name: 'TypeScript', level: 4 }, { id: 'skill-python', name: 'Python', level: 4 }, { id: 'skill-cpp', name: 'C++', level: 3 }, { id: 'skill-sql', name: 'SQL', level: 4 }] },
                { id: 'skills-frameworks', name: 'Frameworks', skills: [{ id: 'skill-react', name: 'React', level: 5 }, { id: 'skill-next', name: 'Next.js', level: 4 }, { id: 'skill-dotnet', name: '.NET', level: 4 }, { id: 'skill-node', name: 'Node.js', level: 4 }] },
                { id: 'skills-databases', name: 'Databases', skills: [{ id: 'skill-postgres', name: 'PostgreSQL', level: 4 }, { id: 'skill-mongo', name: 'MongoDB', level: 3 }, { id: 'skill-redis', name: 'Redis', level: 3 }] },
                { id: 'skills-tools', name: 'Tools', skills: [{ id: 'skill-git', name: 'Git', level: 5 }, { id: 'skill-linux', name: 'Linux', level: 4 }, { id: 'skill-docker', name: 'Docker', level: 4 }, { id: 'skill-figma', name: 'Figma', level: 3 }] }
            ],
            achievementsAndCertifications: [
                { id: 'achievement-coder-cup', title: 'Coder Cup Runner-up', issuingOrganization: 'Northbridge University', date: '2025-02', description: 'Runner-up in the university programming competition.' },
                { id: 'achievement-reader', title: 'Best Reader', issuingOrganization: 'Harbor College', date: '2021-05', description: 'Recognized for consistent academic contribution.' },
                { id: 'achievement-web', title: 'Responsive Web Design', issuingOrganization: 'freeCodeCamp', date: '2024-01', description: 'Completed accessible HTML and responsive CSS coursework.' }
            ],
            languages: [{ id: 'language-english', name: 'English', proficiency: 'Professional working proficiency', level: 4 }, { id: 'language-urdu', name: 'Urdu', proficiency: 'Native proficiency', level: 5 }],
            volunteerExperience: [],
            references: [
                { id: 'reference-priya', fullName: 'Priya Shah', jobTitle: 'Engineering Manager', company: 'Example Labs', emailAddress: 'priya.shah@example.com', phoneNumber: '+1 202 555 0179' }
            ],
            interests: ['Open-source software', 'Competitive programming', 'System design'],
            customSections: [],
            sections: SECTION_DEFAULTS.map(function (section) { return Object.assign({}, section); })
        });
    }

    // Browser controller is defined below; keeping helpers above DOM-free makes them safe in Node.
    const ALLOWED_PATH_ROOTS = new Set([
        'personalInformation', 'professionalSummary', 'education', 'experience', 'projects', 'skills',
        'achievementsAndCertifications', 'languages', 'volunteerExperience', 'references', 'interests', 'customSections', 'sections'
    ]);

    function pathParts(path) {
        if (typeof path !== 'string' || path.length > 180) return null;
        const parts = path.split('.');
        if (!parts.length || !ALLOWED_PATH_ROOTS.has(parts[0])) return null;
        if (parts.some(function (part) { return !part || part === '__proto__' || part === 'prototype' || part === 'constructor' || !/^[A-Za-z][A-Za-z0-9]*$|^\d+$/.test(part); })) return null;
        return parts;
    }

    function getPath(object, path) {
        const parts = pathParts(path);
        if (!parts) return undefined;
        let cursor = object;
        for (let index = 0; index < parts.length; index += 1) {
            if (cursor === null || cursor === undefined) return undefined;
            cursor = cursor[parts[index]];
        }
        return cursor;
    }

    function setPath(object, path, value) {
        const parts = pathParts(path);
        if (!parts) return false;
        let cursor = object;
        for (let index = 0; index < parts.length - 1; index += 1) {
            if (cursor === null || cursor === undefined || typeof cursor !== 'object') return false;
            cursor = cursor[parts[index]];
        }
        if (cursor === null || cursor === undefined || typeof cursor !== 'object') return false;
        cursor[parts[parts.length - 1]] = value;
        return true;
    }

    function moveItem(items, index, offset) {
        const destination = index + offset;
        if (!Array.isArray(items) || index < 0 || index >= items.length || destination < 0 || destination >= items.length) return false;
        const item = items.splice(index, 1)[0];
        items.splice(destination, 0, item);
        return true;
    }

    function domElement(documentObject, tagName, className, content) {
        const element = documentObject.createElement(tagName);
        if (className) element.className = className;
        if (content !== undefined && content !== null) element.textContent = String(content);
        return element;
    }

    function domButton(documentObject, label, action, className) {
        const button = domElement(documentObject, 'button', className || 'aw-rb-text-button', label);
        button.type = 'button';
        button.dataset.action = action;
        return button;
    }

    function replaceChildren(element) {
        while (element && element.firstChild) element.removeChild(element.firstChild);
        if (!element) return;
        for (let index = 1; index < arguments.length; index += 1) element.appendChild(arguments[index]);
    }

    const FIELD_MAXIMUMS = Object.freeze({
        fullName: LIMITS.name,
        professionalTitle: LIMITS.shortText,
        phoneNumber: 40,
        emailAddress: LIMITS.email,
        location: LIMITS.shortText,
        linkedInUrl: LIMITS.url,
        gitHubUrl: LIMITS.url,
        portfolioUrl: LIMITS.url,
        institutionName: LIMITS.shortText,
        degree: LIMITS.shortText,
        fieldOfStudy: LIMITS.shortText,
        grade: LIMITS.shortText,
        descriptionOrCoursework: LIMITS.longText,
        companyName: LIMITS.shortText,
        company: LIMITS.shortText,
        jobTitle: LIMITS.shortText,
        employmentType: LIMITS.shortText,
        technologySkills: LIMITS.longText,
        projectName: LIMITS.shortText,
        projectUrl: LIMITS.url,
        repositoryUrl: LIMITS.url,
        name: LIMITS.shortText,
        title: LIMITS.shortText,
        issuingOrganization: LIMITS.shortText,
        credentialUrl: LIMITS.url,
        description: LIMITS.longText,
        proficiency: LIMITS.shortText,
        level: 1,
        organizationName: LIMITS.shortText,
        role: LIMITS.shortText,
        heading: LIMITS.shortText,
        subheading: LIMITS.shortText,
        url: LIMITS.url
    });

    const ENTRY_FACTORIES = Object.freeze({
        education: function () { return { id: newId('education'), institutionName: '', degree: '', fieldOfStudy: '', startDate: '', endDate: '', isCurrentlyStudying: false, location: '', grade: '', descriptionOrCoursework: '' }; },
        experience: function () { return { id: newId('experience'), companyName: '', jobTitle: '', employmentType: '', technologySkills: '', location: '', startDate: '', endDate: '', isCurrentlyWorking: false, bulletPoints: [''] }; },
        projects: function () { return { id: newId('project'), projectName: '', projectUrl: '', repositoryUrl: '', technologiesUsed: [], startDate: '', endDate: '', isOngoing: false, bulletPoints: [''] }; },
        skills: function () { return { id: newId('skills'), name: '', skills: [] }; },
        achievementsAndCertifications: function () { return { id: newId('achievement'), title: '', issuingOrganization: '', date: '', credentialUrl: '', description: '' }; },
        languages: function () { return { id: newId('language'), name: '', proficiency: '', level: null }; },
        volunteerExperience: function () { return { id: newId('volunteer'), organizationName: '', role: '', location: '', startDate: '', endDate: '', isCurrentlyVolunteering: false, bulletPoints: [''] }; },
        references: function () { return { id: newId('reference'), fullName: '', jobTitle: '', company: '', emailAddress: '', phoneNumber: '' }; },
        customSections: function () { return { id: newId('custom-section'), title: '', entries: [] }; }
    });

    function customEntryFactory() {
        return { id: newId('custom-entry'), heading: '', subheading: '', location: '', startDate: '', endDate: '', isCurrent: false, url: '', bulletPoints: [''] };
    }

    function createBuilder(root, options) {
        if (!root || !root.ownerDocument || root.dataset.resumeBuilderInitialized === 'true') return null;
        root.dataset.resumeBuilderInitialized = 'true';
        const documentObject = root.ownerDocument;
        const windowObject = documentObject.defaultView;
        const configuration = options || {};
        const storageKey = text(root.dataset.draftKey, 180);
        const initialSection = normalizeBuilderSection(root.dataset.initialSection);
        const form = root.querySelector('[data-resume-form]');
        const dynamicEditor = root.querySelector('[data-section-editor]');
        const sectionManager = root.querySelector('[data-section-manager]');
        const previewFrame = root.querySelector('[data-resume-preview]');
        const previewState = root.querySelector('[data-preview-state]');
        const saveStatus = root.querySelector('[data-save-status]');
        const pdfStatus = root.querySelector('[data-pdf-status]');
        const buildAllowance = root.querySelector('[data-build-allowance]');
        const exportAuthorizationUrl = text(root.dataset.exportAuthorizationUrl, 500);
        const exportAuthorizationForm = root.querySelector('[data-export-authorization]');
        const clearDialog = root.querySelector('[data-clear-dialog]');
        const templateDialog = root.querySelector('[data-template-dialog]');
        const summaryCount = root.querySelector('[data-summary-count]');
        const builderProgress = root.querySelector('[data-builder-progress]');
        const builderProgressBar = root.querySelector('[data-builder-progress-bar]');
        const builderNextStep = root.querySelector('[data-builder-next-step]');
        const nextSectionLabel = root.querySelector('[data-next-section-label]');
        const pageFitStatus = root.querySelector('[data-page-fit-status]');
        const onePageWarning = root.querySelector('[data-one-page-warning]');
        const templatePicker = root.querySelector('[data-template-picker]');
        const templateGallery = root.querySelector('[data-template-gallery]');
        const templateGalleryGrid = root.querySelector('[data-template-gallery-grid]');
        const templateGalleryFrame = root.querySelector('[data-template-gallery-large-preview]');
        const templateGalleryPreviewState = root.querySelector('[data-template-gallery-preview-state]');
        const templatePreviewDialog = root.querySelector('[data-template-preview-dialog]');
        const photoField = root.querySelector('[data-photo-field]');
        const photoInput = root.querySelector('[data-profile-photo-input]');
        const photoPreview = root.querySelector('[data-photo-preview]');
        const photoInitials = root.querySelector('[data-photo-initials]');
        const photoStatus = root.querySelector('[data-photo-status]');
        const dynamicSectionHost = root.querySelector('[data-dynamic-section-host]');
        const touched = new Set();
        let showAllErrors = false;
        let state = createEmptyState();
        let revision = 0;
        let savedRevision = 0;
        let saveTimer = 0;
        let previewTimer = 0;
        let initialFocusTimer = 0;
        let targetHighlightTimer = 0;
        let previewRequest = 0;
        let previewPromise = null;
        let currentPreviewUrl = '';
        let latestBlob = null;
        let latestBlobRevision = -1;
        let latestPageCount = 0;
        let destroyed = false;
        let initialSectionHandled = false;
        let storageEnabled = Boolean(storageKey);
        let activeEditorTab = 'personal';
        let photoRequest = 0;
        let templateCursor = 0;
        let templateDialogOpener = null;
        let galleryOpen = false;
        let galleryGeneration = 0;
        let galleryPreviewObserver = null;
        let galleryPhotoDataUrl = '';
        let galleryPhotoPromise = null;
        let templatePreviewOpener = null;
        let expandedPreviewOpener = null;
        const previewInertRecords = [];
        const galleryBlobs = new Map();
        const galleryUrls = new Map();
        const galleryPromises = new Map();
        const fontScriptPromises = new Map();
        const tabList = root.querySelector('.aw-rb-mobile-tabs');

        function isCompactLayout() {
            if (!tabList || !windowObject || typeof windowObject.getComputedStyle !== 'function') return false;
            return windowObject.getComputedStyle(tabList).display !== 'none';
        }

        function setStatus(element, message) {
            if (element) element.textContent = message;
        }

        function setPreviewMessage(message, kind) {
            if (!previewState) return;
            previewState.hidden = !message;
            previewState.classList.toggle('is-error', kind === 'error');
            previewState.setAttribute('aria-busy', String(kind === 'loading'));
            const textTarget = previewState.querySelector('[data-preview-state-text]') || Array.from(previewState.children).find(function (child) {
                return !child.classList.contains('aw-rb-preview-spinner');
            });
            (textTarget || previewState).textContent = message || '';
        }

        function storageGet() {
            if (!storageEnabled || !windowObject) return null;
            try { return windowObject.localStorage.getItem(storageKey); }
            catch (_error) { storageEnabled = false; return null; }
        }

        function storageSet(serialized) {
            if (!storageEnabled || !windowObject) return false;
            try { windowObject.localStorage.setItem(storageKey, serialized); return true; }
            catch (_error) { storageEnabled = false; return false; }
        }

        function storageRemove() {
            if (!storageKey || !windowObject) return;
            try { windowObject.localStorage.removeItem(storageKey); }
            catch (_error) { storageEnabled = false; }
        }

        function readSample() {
            const source = root.querySelector('[data-sample-resume]');
            if (!source) return createSampleState();
            const serialized = typeof source.value === 'string' ? source.value : source.textContent;
            const parsed = parseDraft(serialized || '');
            return parsed.recovered || parsed.reason === 'empty' ? createSampleState() : parsed.state;
        }

        function loadInitialState() {
            const serialized = storageGet();
            if (serialized === null) {
                setStatus(saveStatus, storageEnabled ? 'Changes save automatically on this device.' : 'Local draft saving is unavailable.');
                return createEmptyState();
            }
            const parsed = parseDraft(serialized);
            if (parsed.recovered) {
                storageRemove();
                setStatus(saveStatus, 'The saved draft was unreadable, so a new resume was started.');
            } else {
                setStatus(saveStatus, 'Draft restored from this device.');
            }
            return parsed.state;
        }

        function showSection(key) {
            const descriptor = state.sections.find(function (item) { return item.key === key; });
            if (descriptor) descriptor.isVisible = true;
        }

        function syncStaticFields() {
            root.querySelectorAll('[data-field]').forEach(function (control) {
                const path = control.dataset.field;
                const value = getPath(state, path);
                if (control.type === 'checkbox') control.checked = Boolean(value);
                else control.value = value === undefined || value === null ? '' : String(value);
                const fieldName = path.split('.').pop();
                if (FIELD_MAXIMUMS[fieldName] && !control.maxLength) control.maxLength = FIELD_MAXIMUMS[fieldName];
                if (path === 'professionalSummary') control.maxLength = LIMITS.summary;
            });
            if (summaryCount) {
                const length = plainRichText(state.professionalSummary, LIMITS.summary).length;
                summaryCount.textContent = length + ' characters - keep the summary to about 3 lines';
            }
        }

        function syncTemplatePicker() {
            const selectedTemplate = templateMetadata(state.templateId);
            if (templatePicker) {
                const mini = templatePicker.querySelector('[data-selected-template-mini]');
                const name = templatePicker.querySelector('[data-selected-template-name]');
                const description = templatePicker.querySelector('[data-selected-template-description]');
                const status = templatePicker.querySelector('[data-template-status]');
                if (mini) mini.className = 'aw-rb-template-mini aw-rb-template-mini-' + selectedTemplate.id;
                if (name) name.textContent = selectedTemplate.name;
                if (description) description.textContent = selectedTemplate.description;
                if (status) status.textContent = selectedTemplate.name + ' selected · ' + (selectedTemplate.hasPhoto ? 'portrait layout' : 'photo-free layout') + '.';
            }
            syncPhotoField(selectedTemplate);
        }

        function templateClassification(template) {
            if (template.classification) return template.classification;
            return template.hasPhoto ? 'Balanced' : 'ATS-optimized';
        }

        function loadGalleryPhotoDataUrl() {
            if (galleryPhotoDataUrl) return Promise.resolve(galleryPhotoDataUrl);
            if (galleryPhotoPromise) return galleryPhotoPromise;
            const source = text(root.dataset.galleryPhotoUrl, LIMITS.url);
            if (!source || !windowObject || !windowObject.Image) return Promise.resolve(GALLERY_PHOTO_DATA_URL);
            galleryPhotoPromise = new Promise(function (resolve) {
                const image = new windowObject.Image();
                image.onload = function () {
                    try {
                        const size = 280;
                        const canvas = documentObject.createElement('canvas');
                        canvas.width = size;
                        canvas.height = size;
                        const context = canvas.getContext('2d');
                        if (!context) throw new Error('Canvas is unavailable.');
                        context.fillStyle = '#f4f8ff';
                        context.fillRect(0, 0, size, size);
                        context.drawImage(image, 0, 0, size, size);
                        const encoded = normalizeProfilePhotoDataUrl(canvas.toDataURL('image/jpeg', 0.84));
                        galleryPhotoDataUrl = encoded || GALLERY_PHOTO_DATA_URL;
                    } catch (_error) {
                        galleryPhotoDataUrl = GALLERY_PHOTO_DATA_URL;
                    }
                    resolve(galleryPhotoDataUrl);
                };
                image.onerror = function () {
                    galleryPhotoDataUrl = GALLERY_PHOTO_DATA_URL;
                    resolve(galleryPhotoDataUrl);
                };
                image.src = source;
            }).finally(function () {
                galleryPhotoPromise = null;
            });
            return galleryPhotoPromise;
        }

        function gallerySampleState(templateId, photoDataUrl) {
            const sample = readSample();
            sample.templateId = templateId;
            sample.templateSelectionConfirmed = true;
            if (templateMetadata(templateId).hasPhoto) {
                sample.personalInformation.profilePhotoDataUrl = photoDataUrl || GALLERY_PHOTO_DATA_URL;
            }
            return sample;
        }

        function revokeGalleryUrls() {
            if (!windowObject || !windowObject.URL) return;
            galleryUrls.forEach(function (url) { windowObject.URL.revokeObjectURL(url); });
            galleryUrls.clear();
            galleryBlobs.clear();
            galleryPromises.clear();
            if (templateGalleryFrame) templateGalleryFrame.removeAttribute('src');
        }

        function galleryPdfUrl(templateId) {
            if (galleryUrls.has(templateId)) return Promise.resolve(galleryUrls.get(templateId));
            if (galleryPromises.has(templateId)) return galleryPromises.get(templateId);
            const photoPromise = templateMetadata(templateId).hasPhoto
                ? loadGalleryPhotoDataUrl()
                : Promise.resolve('');
            const promise = photoPromise.then(function (photoDataUrl) {
                return createPdfBlob(
                    buildDocumentDefinition(gallerySampleState(templateId, photoDataUrl)),
                    templateId);
            }).then(function (blob) {
                if (destroyed || !windowObject || !windowObject.URL) return '';
                galleryBlobs.set(templateId, blob);
                const url = windowObject.URL.createObjectURL(blob);
                galleryUrls.set(templateId, url);
                return url;
            }).finally(function () {
                galleryPromises.delete(templateId);
            });
            galleryPromises.set(templateId, promise);
            return promise;
        }

        function updateGalleryDetail(candidate) {
            if (!templateGallery) return;
            const name = templateGallery.querySelector('[data-gallery-name]');
            const description = templateGallery.querySelector('[data-gallery-description]');
            const classification = templateGallery.querySelector('[data-gallery-classification]');
            const choose = templateGallery.querySelector('[data-action="choose-gallery-template"]');
            const previous = templateGallery.querySelector('[data-action="previous-gallery-template"]');
            const next = templateGallery.querySelector('[data-action="next-gallery-template"]');
            if (name) name.textContent = candidate.name;
            if (description) description.textContent = candidate.description;
            if (classification) {
                classification.textContent = templateClassification(candidate);
                classification.dataset.classification = templateClassification(candidate).toLocaleLowerCase().replace(/[^a-z]+/g, '-');
            }
            if (choose) choose.textContent = candidate.id === state.templateId && state.templateSelectionConfirmed
                ? 'Keep template'
                : 'Use template';
            if (previous) previous.disabled = templateCursor === 0;
            if (next) next.disabled = templateCursor === TEMPLATE_CATALOG.length - 1;
        }

        function isTemplatePreviewOpen() {
            return Boolean(templatePreviewDialog
                && (templatePreviewDialog.open || templatePreviewDialog.hasAttribute('open')));
        }

        function openTemplatePreview(trigger) {
            if (!templatePreviewDialog) return;
            templatePreviewOpener = trigger || null;
            if (typeof templatePreviewDialog.showModal === 'function') templatePreviewDialog.showModal();
            else templatePreviewDialog.setAttribute('open', '');
            selectGalleryTemplate(templateCursor, false);
        }

        function closeTemplatePreview(restoreFocus) {
            if (!templatePreviewDialog) return;
            if (typeof templatePreviewDialog.close === 'function' && templatePreviewDialog.open) templatePreviewDialog.close();
            else templatePreviewDialog.removeAttribute('open');
            if (templateGalleryFrame) templateGalleryFrame.removeAttribute('src');
            if (restoreFocus && templatePreviewOpener && typeof templatePreviewOpener.focus === 'function') {
                templatePreviewOpener.focus();
            }
        }

        function selectGalleryTemplate(index, focusCard) {
            if (!templateGalleryGrid) return;
            templateCursor = Math.max(0, Math.min(TEMPLATE_CATALOG.length - 1, index));
            const candidate = TEMPLATE_CATALOG[templateCursor];
            templateGalleryGrid.querySelectorAll('[data-template-choice]').forEach(function (card, cardIndex) {
                const selected = cardIndex === templateCursor;
                card.classList.toggle('is-selected', selected);
                card.setAttribute('aria-selected', String(selected));
                card.tabIndex = selected ? 0 : -1;
                if (selected && focusCard && typeof card.focus === 'function') card.focus();
            });
            updateGalleryDetail(candidate);
            const cardFrame = templateGalleryGrid.querySelector(
                '[data-gallery-preview-id="' + candidate.id + '"]');
            if (cardFrame) loadGalleryCardPreview(candidate, cardFrame);
            if (!isTemplatePreviewOpen()) return;
            if (templateGalleryPreviewState) {
                templateGalleryPreviewState.hidden = false;
                templateGalleryPreviewState.textContent = 'Preparing ' + candidate.name + ' preview…';
            }
            galleryPdfUrl(candidate.id).then(function (url) {
                if (!galleryOpen || !isTemplatePreviewOpen()
                    || TEMPLATE_CATALOG[templateCursor].id !== candidate.id || !url) return;
                if (templateGalleryFrame) templateGalleryFrame.src = url + '#toolbar=0&navpanes=0&scrollbar=1&view=FitH';
                if (templateGalleryPreviewState) templateGalleryPreviewState.hidden = true;
            }).catch(function () {
                if (templateGalleryPreviewState) {
                    templateGalleryPreviewState.hidden = false;
                    templateGalleryPreviewState.textContent = 'This PDF preview could not be prepared. You can still select the template.';
                }
            });
        }

        function renderTemplateGallery() {
            if (!templateGalleryGrid || templateGalleryGrid.dataset.rendered === 'true') return;
            const cards = TEMPLATE_CATALOG.map(function (candidate, index) {
                const card = domElement(documentObject, 'article', 'aw-rb-template-card');
                card.dataset.templateChoice = candidate.id;
                card.dataset.templateIndex = String(index);
                card.setAttribute('role', 'option');
                card.setAttribute('aria-selected', 'false');
                card.setAttribute('aria-label', candidate.name + '. Open full template preview.');
                card.tabIndex = -1;
                const preview = domElement(documentObject, 'div', 'aw-rb-template-card-preview');
                const frame = domElement(documentObject, 'iframe', 'aw-rb-template-card-frame');
                frame.dataset.galleryPreviewId = candidate.id;
                frame.title = candidate.name + ' generated PDF thumbnail';
                frame.tabIndex = -1;
                frame.loading = 'lazy';
                frame.setAttribute('aria-hidden', 'true');
                preview.appendChild(frame);
                const copy = domElement(documentObject, 'div', 'aw-rb-template-card-copy');
                copy.appendChild(domElement(documentObject, 'strong', '', candidate.name));
                card.append(preview, copy);
                return card;
            });
            replaceChildren.apply(null, [templateGalleryGrid].concat(cards));
            templateGalleryGrid.dataset.rendered = 'true';
        }

        function loadGalleryCardPreview(candidate, frame) {
            if (!candidate || !frame || frame.dataset.previewState === 'loading'
                || frame.dataset.previewState === 'ready') return;
            frame.dataset.previewState = 'loading';
            galleryPdfUrl(candidate.id).then(function (url) {
                if (destroyed || !galleryOpen || !frame.isConnected || !url) return;
                frame.src = url + '#toolbar=0&navpanes=0&scrollbar=0&view=Fit';
                frame.dataset.previewState = 'ready';
            }).catch(function () {
                frame.dataset.previewState = 'error';
                const card = templateGalleryGrid && templateGalleryGrid.querySelector(
                    '[data-template-choice="' + candidate.id + '"]');
                if (card) card.classList.add('has-preview-error');
            });
        }

        function observeGalleryPdfPreviews() {
            if (!templateGalleryGrid) return;
            galleryGeneration += 1;
            if (galleryPreviewObserver) galleryPreviewObserver.disconnect();
            const frames = Array.from(templateGalleryGrid.querySelectorAll('[data-gallery-preview-id]'))
                .filter(function (frame) { return frame.dataset.previewState !== 'ready'; });
            if (!frames.length) return;

            if (windowObject && typeof windowObject.IntersectionObserver === 'function') {
                galleryPreviewObserver = new windowObject.IntersectionObserver(function (entries) {
                    entries.forEach(function (entry) {
                        if (!entry.isIntersecting) return;
                        galleryPreviewObserver.unobserve(entry.target);
                        loadGalleryCardPreview(
                            templateMetadata(entry.target.dataset.galleryPreviewId),
                            entry.target);
                    });
                }, { root: null, rootMargin: '320px 0px', threshold: 0.01 });
                frames.forEach(function (frame) { galleryPreviewObserver.observe(frame); });
                return;
            }

            // Older browsers render only the first visible row instead of
            // eagerly generating every PDF template.
            frames.slice(0, 4).forEach(function (frame) {
                loadGalleryCardPreview(
                    templateMetadata(frame.dataset.galleryPreviewId),
                    frame);
            });
        }

        function openTemplateGallery() {
            if (!templateGallery) return;
            galleryOpen = true;
            templateGallery.hidden = false;
            root.classList.add('is-choosing-template');
            const cancel = templateGallery.querySelector('[data-gallery-cancel]');
            if (cancel) cancel.hidden = !state.templateSelectionConfirmed;
            renderTemplateGallery();
            observeGalleryPdfPreviews();
            templateCursor = Math.max(0, TEMPLATE_CATALOG.findIndex(function (template) { return template.id === state.templateId; }));
            selectGalleryTemplate(templateCursor, false);
            const selected = templateGalleryGrid && templateGalleryGrid.querySelector('[data-template-choice][aria-selected="true"]');
            if (selected && typeof selected.focus === 'function') {
                if (windowObject && typeof windowObject.setTimeout === 'function') {
                    windowObject.setTimeout(function () { if (galleryOpen) selected.focus(); }, 0);
                } else selected.focus();
            }
        }

        function closeTemplateGallery() {
            if (!templateGallery || !state.templateSelectionConfirmed) return;
            closeTemplatePreview(false);
            galleryOpen = false;
            galleryGeneration += 1;
            if (galleryPreviewObserver) {
                galleryPreviewObserver.disconnect();
                galleryPreviewObserver = null;
            }
            templateGallery.hidden = true;
            root.classList.remove('is-choosing-template');
            const changeButton = root.querySelector('[data-action="open-template-picker"]');
            if (changeButton && typeof changeButton.focus === 'function') changeButton.focus();
        }

        function syncGalleryVisibility() {
            if (!state.templateSelectionConfirmed) {
                openTemplateGallery();
                return;
            }
            if (!galleryOpen && templateGallery) {
                templateGallery.hidden = true;
                root.classList.remove('is-choosing-template');
            }
        }

        function renderTemplateDialog() {
            if (!templateDialog) return;
            templateCursor = Math.max(0, Math.min(TEMPLATE_CATALOG.length - 1, templateCursor));
            const candidate = TEMPLATE_CATALOG[templateCursor];
            const preview = templateDialog.querySelector('[data-template-dialog-preview]');
            const name = templateDialog.querySelector('[data-template-dialog-name]');
            const description = templateDialog.querySelector('[data-template-dialog-description]');
            const position = templateDialog.querySelector('[data-template-dialog-position]');
            const photo = templateDialog.querySelector('[data-template-dialog-photo]');
            const font = templateDialog.querySelector('[data-template-dialog-font]');
            const previous = templateDialog.querySelector('[data-action="previous-template"]');
            const next = templateDialog.querySelector('[data-action="next-template"]');
            const confirm = templateDialog.querySelector('[data-action="confirm-template"]');
            templateDialog.dataset.templateId = candidate.id;
            if (preview) preview.className = 'aw-rb-template-showcase aw-rb-template-showcase-' + candidate.id;
            if (name) name.textContent = candidate.name;
            if (description) description.textContent = candidate.description;
            if (position) position.textContent = 'Template ' + (templateCursor + 1) + ' of ' + TEMPLATE_CATALOG.length;
            if (photo) photo.textContent = candidate.hasPhoto ? 'Photo option' : 'No photo';
            if (font) font.textContent = candidate.font + ' font';
            if (previous) previous.disabled = templateCursor === 0;
            if (next) next.disabled = templateCursor === TEMPLATE_CATALOG.length - 1;
            if (confirm) {
                confirm.textContent = candidate.id === state.templateId ? 'Keep template & add data' : 'Use template & add data';
                confirm.setAttribute('aria-label', 'Use ' + candidate.name + ' template and add resume data');
            }
        }

        function openTemplateDialog(trigger) {
            if (!templateDialog) return;
            templateDialogOpener = trigger || null;
            templateCursor = Math.max(0, TEMPLATE_CATALOG.findIndex(function (template) { return template.id === state.templateId; }));
            renderTemplateDialog();
            if (typeof templateDialog.showModal === 'function') templateDialog.showModal();
            else templateDialog.setAttribute('open', '');
        }

        function closeTemplateDialog(restoreFocus) {
            if (!templateDialog) return;
            if (typeof templateDialog.close === 'function' && templateDialog.open) templateDialog.close();
            else templateDialog.removeAttribute('open');
            if (restoreFocus && templateDialogOpener && typeof templateDialogOpener.focus === 'function') {
                templateDialogOpener.focus();
            }
        }

        function moveTemplateCursor(delta) {
            templateCursor = Math.max(0, Math.min(TEMPLATE_CATALOG.length - 1, templateCursor + delta));
            renderTemplateDialog();
        }

        function continueToResumeData() {
            activateTab('form');
            activateEditorTab('personal');
            const firstField = root.querySelector('[data-editor-section="personalInformation"] [data-field]');
            if (firstField && typeof firstField.focus === 'function') {
                if (windowObject && typeof windowObject.setTimeout === 'function') windowObject.setTimeout(function () { firstField.focus(); }, 0);
                else firstField.focus();
            }
        }

        function initialsForName(value) {
            const parts = text(value, LIMITS.name).split(/\s+/).filter(Boolean);
            if (!parts.length) return 'AW';
            return (parts[0][0] + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toLocaleUpperCase();
        }

        function syncPhotoField(selectedTemplate) {
            if (!photoField) return;
            const template = selectedTemplate || templateMetadata(state.templateId);
            photoField.hidden = !template.hasPhoto;
            photoField.setAttribute('aria-hidden', String(!template.hasPhoto));
            const help = photoField.querySelector('[data-photo-template-help]');
            if (help) help.textContent = template.name + ' uses a professional portrait'
                + (template.photoRequired ? ' and requires one before PDF download.' : '.');
            const photo = state.personalInformation.profilePhotoDataUrl;
            if (photoPreview) {
                photoPreview.hidden = !photo;
                if (photo) photoPreview.src = photo;
                else photoPreview.removeAttribute('src');
            }
            if (photoInitials) {
                photoInitials.hidden = Boolean(photo);
                photoInitials.textContent = initialsForName(state.personalInformation.fullName);
            }
            const remove = photoField.querySelector('[data-remove-profile-photo]');
            if (remove) remove.hidden = !photo;
            const pickerLabel = photoField.querySelector('[data-photo-picker-label]');
            if (pickerLabel) pickerLabel.textContent = photo ? 'Change photo' : 'Choose photo';
        }

        function syncGuidedProgress() {
            const personal = state.personalInformation || {};
            const milestones = [
                { done: hasText(personal.fullName), prompt: 'Add your full name' },
                { done: hasText(personal.professionalTitle), prompt: 'Add your professional title' },
                { done: hasText(personal.emailAddress), prompt: 'Add your email address' },
                { done: hasText(state.professionalSummary), prompt: 'Write your profile summary' },
                { done: state.experience.some(function (entry) { return hasText(entry.companyName) || hasText(entry.jobTitle); }), prompt: 'Add your experience' },
                { done: state.education.some(function (entry) { return hasText(entry.institutionName) || hasText(entry.degree); }), prompt: 'Add your education' },
                { done: state.skills.some(function (category) { return category.skills.some(function (skill) { return hasText(skill.name); }); }), prompt: 'Add your strongest skills' },
                { done: state.projects.some(function (entry) { return hasText(entry.projectName); }), prompt: 'Add a relevant project' }
            ];
            const completed = milestones.filter(function (milestone) { return milestone.done; }).length;
            const percentage = Math.round((completed / milestones.length) * 100);
            const next = milestones.find(function (milestone) { return !milestone.done; });
            if (builderProgress) builderProgress.textContent = percentage + '%';
            if (builderProgressBar) {
                builderProgressBar.style.width = percentage + '%';
                const track = builderProgressBar.parentElement;
                if (track) track.setAttribute('aria-valuenow', String(percentage));
            }
            if (builderNextStep) builderNextStep.textContent = next ? next.prompt : 'Your core resume is complete';
        }

        function syncEditorTabs() {
            const group = EDITOR_TAB_GROUPS[activeEditorTab] || EDITOR_TAB_GROUPS.personal;
            root.querySelectorAll('[data-editor-tab]').forEach(function (control) {
                const active = control.dataset.editorTab === activeEditorTab;
                control.classList.toggle('is-active', active);
                control.setAttribute('aria-selected', String(active));
                control.tabIndex = active ? 0 : -1;
            });
            root.querySelectorAll('[data-editor-section]').forEach(function (section) {
                const visible = group.includes(section.dataset.editorSection);
                section.hidden = !visible;
                section.setAttribute('aria-hidden', String(!visible));
            });
            const showsDynamicSection = group.some(function (section) {
                return section !== 'personalInformation' && section !== 'professionalSummary' && section !== 'arrange';
            });
            if (dynamicSectionHost) dynamicSectionHost.hidden = !showsDynamicSection;
            const activeControl = root.querySelector('[data-editor-tab="' + activeEditorTab + '"]');
            if (form && activeControl && activeControl.id) form.setAttribute('aria-labelledby', activeControl.id);
            if (nextSectionLabel) {
                const currentIndex = Math.max(0, EDITOR_TAB_ORDER.indexOf(activeEditorTab));
                const nextTab = EDITOR_TAB_ORDER[currentIndex + 1] || EDITOR_TAB_ORDER[0];
                const nextControl = root.querySelector('[data-editor-tab="' + nextTab + '"]');
                const nextName = nextControl ? nextControl.textContent.trim() : 'Contact';
                nextSectionLabel.textContent = currentIndex === EDITOR_TAB_ORDER.length - 1
                    ? 'Back to Contact'
                    : 'Next: ' + nextName;
            }
        }

        function activateEditorTab(name, focusTab) {
            if (!Object.prototype.hasOwnProperty.call(EDITOR_TAB_GROUPS, name)) return false;
            activeEditorTab = name;
            syncEditorTabs();
            const control = root.querySelector('[data-editor-tab="' + name + '"]');
            if (focusTab && control && typeof control.focus === 'function') control.focus();
            return true;
        }

        function resizeProfilePhoto(file) {
            return new Promise(function (resolve, reject) {
                if (!file || !/^image\/(?:png|jpe?g|webp)$/i.test(file.type || '')) {
                    reject(new Error('Choose a PNG, JPG, or WebP image.'));
                    return;
                }
                if (file.size > LIMITS.photoFileBytes) {
                    reject(new Error('Choose an image smaller than 8 MB.'));
                    return;
                }
                if (!windowObject || !windowObject.FileReader || !windowObject.Image) {
                    reject(new Error('Photo processing is unavailable in this browser.'));
                    return;
                }
                const reader = new windowObject.FileReader();
                reader.onerror = function () { reject(new Error('The selected image could not be read.')); };
                reader.onload = function () {
                    const image = new windowObject.Image();
                    image.onerror = function () { reject(new Error('The selected file is not a readable image.')); };
                    image.onload = function () {
                        const size = 240;
                        const canvas = documentObject.createElement('canvas');
                        canvas.width = size;
                        canvas.height = size;
                        const context = canvas.getContext('2d');
                        if (!context) { reject(new Error('Photo processing is unavailable in this browser.')); return; }
                        const sourceSize = Math.min(image.naturalWidth || image.width, image.naturalHeight || image.height);
                        const sourceX = ((image.naturalWidth || image.width) - sourceSize) / 2;
                        const sourceY = ((image.naturalHeight || image.height) - sourceSize) / 2;
                        context.clearRect(0, 0, size, size);
                        context.beginPath();
                        context.arc(size / 2, size / 2, size / 2 - 1, 0, Math.PI * 2);
                        context.closePath();
                        context.clip();
                        context.drawImage(image, sourceX, sourceY, sourceSize, sourceSize, 0, 0, size, size);
                        const result = normalizeProfilePhotoDataUrl(canvas.toDataURL('image/png'));
                        if (!result) { reject(new Error('The processed photo is too large. Try a simpler or smaller image.')); return; }
                        resolve(result);
                    };
                    image.src = String(reader.result || '');
                };
                reader.readAsDataURL(file);
            });
        }

        async function onPhotoChange() {
            const file = photoInput && photoInput.files ? photoInput.files[0] : null;
            if (!file) return;
            const request = ++photoRequest;
            setStatus(photoStatus, 'Preparing your portrait…');
            try {
                const photo = await resizeProfilePhoto(file);
                if (destroyed || request !== photoRequest) return;
                state.personalInformation.profilePhotoDataUrl = photo;
                if (photoInput) photoInput.value = '';
                changed(false);
                setStatus(photoStatus, 'Portrait added to this resume.');
            } catch (error) {
                if (request !== photoRequest) return;
                if (photoInput) photoInput.value = '';
                setStatus(photoStatus, error && error.message ? error.message : 'The portrait could not be added.');
            }
        }

        function formatToolbar(targetId, label) {
            const toolbar = domElement(documentObject, 'div', 'aw-rb-format-toolbar');
            toolbar.setAttribute('aria-label', (label || 'Description') + ' formatting');
            toolbar.appendChild(domElement(documentObject, 'span', '', 'Select words, then format:'));
            const controls = domElement(documentObject, 'div');
            controls.setAttribute('role', 'group');
            controls.setAttribute('aria-label', 'Text formatting');
            [
                { key: 'bold', label: 'B', title: 'Bold selected text' },
                { key: 'italic', label: 'I', title: 'Italicize selected text' },
                { key: 'underline', label: 'U', title: 'Underline selected text' }
            ].forEach(function (definition) {
                const button = domElement(documentObject, 'button', 'aw-rb-format-' + definition.key, definition.label);
                button.type = 'button';
                button.dataset.format = definition.key;
                button.dataset.formatTarget = targetId;
                button.title = definition.title;
                button.setAttribute('aria-label', definition.title + ' in ' + (label || 'description'));
                controls.appendChild(button);
            });
            toolbar.appendChild(controls);
            return toolbar;
        }

        function fieldControl(path, definition) {
            const wrapper = domElement(documentObject, 'div', 'aw-rb-field' + (definition.wide ? ' aw-rb-field-wide' : ''));
            const id = 'rb-' + path.replace(/[^A-Za-z0-9]+/g, '-');
            if (definition.type === 'checkbox') {
                wrapper.classList.add('aw-rb-checkbox');
                const input = domElement(documentObject, 'input', 'form-check-input');
                input.type = 'checkbox';
                input.id = id;
                input.checked = Boolean(getPath(state, path));
                input.dataset.bindPath = path;
                const label = domElement(documentObject, 'label', 'form-check-label', definition.label);
                label.htmlFor = id;
                wrapper.append(input, label);
                return wrapper;
            }
            const label = domElement(documentObject, 'label', 'form-label', definition.label);
            label.htmlFor = id;
            const controlTag = definition.type === 'textarea' ? 'textarea' : definition.type === 'select' ? 'select' : 'input';
            const control = domElement(documentObject, controlTag, 'form-control');
            control.id = id;
            control.dataset.bindPath = path;
            if (definition.type === 'textarea') control.rows = definition.rows || 3;
            else if (definition.type !== 'select') control.type = definition.type || 'text';
            if (definition.type === 'select') {
                (definition.options || []).forEach(function (optionDefinition) {
                    const option = domElement(documentObject, 'option', '', optionDefinition.label);
                    option.value = optionDefinition.value === null || optionDefinition.value === undefined ? '' : String(optionDefinition.value);
                    control.appendChild(option);
                });
            }
            if (definition.placeholder) control.placeholder = definition.placeholder;
            const fieldName = path.split('.').pop();
            if (definition.type !== 'select') control.maxLength = definition.maximum || FIELD_MAXIMUMS[fieldName] || LIMITS.shortText;
            control.value = String(getPath(state, path) || '');
            const error = domElement(documentObject, 'span', 'aw-rb-error field-validation-error');
            error.dataset.errorFor = path;
            error.id = id + '-error';
            error.setAttribute('aria-live', 'polite');
            control.setAttribute('aria-describedby', error.id);
            wrapper.appendChild(label);
            if (definition.richText) wrapper.appendChild(formatToolbar(id, definition.label));
            wrapper.append(control, error);
            return wrapper;
        }

        function entryActions(section, index, count, label) {
            const actions = domElement(documentObject, 'div', 'aw-rb-entry-actions');
            const up = domButton(documentObject, '↑', 'move-entry-up', 'aw-rb-icon-button');
            up.dataset.section = section; up.dataset.index = String(index); up.disabled = index === 0;
            up.title = 'Move up'; up.setAttribute('aria-label', 'Move ' + label + ' up');
            const down = domButton(documentObject, '↓', 'move-entry-down', 'aw-rb-icon-button');
            down.dataset.section = section; down.dataset.index = String(index); down.disabled = index === count - 1;
            down.title = 'Move down'; down.setAttribute('aria-label', 'Move ' + label + ' down');
            const remove = domButton(documentObject, 'Remove', 'remove-entry', 'aw-rb-text-button');
            remove.dataset.section = section; remove.dataset.index = String(index);
            remove.setAttribute('aria-label', 'Remove ' + label);
            actions.append(up, down, remove);
            return actions;
        }

        function sectionCard(section, title, help, addLabel) {
            const card = domElement(documentObject, 'section', 'aw-rb-section-card aw-rb-section-' + section);
            card.dataset.editorSection = section;
            const header = domElement(documentObject, 'header');
            const headingGroup = domElement(documentObject, 'div');
            headingGroup.appendChild(domElement(documentObject, 'h3', '', title));
            if (help) headingGroup.appendChild(domElement(documentObject, 'p', 'form-text', help));
            header.appendChild(headingGroup);
            if (addLabel) {
                const add = domButton(documentObject, addLabel, 'add-entry', 'aw-rb-text-button');
                add.dataset.section = section;
                header.appendChild(add);
            }
            card.appendChild(header);
            return card;
        }

        function renderEmpty(container, message) {
            container.appendChild(domElement(documentObject, 'p', 'aw-rb-empty', message));
        }

        function renderBullets(container, path, label) {
            const list = domElement(documentObject, 'div', 'aw-rb-bullet-list');
            list.appendChild(domElement(documentObject, 'p', 'form-label', label));
            const bullets = getPath(state, path) || [];
            bullets.forEach(function (bullet, index) {
                const row = domElement(documentObject, 'div', 'aw-rb-bullet-row');
                const input = domElement(documentObject, 'textarea', 'form-control');
                const itemPath = path + '.' + index;
                input.id = 'rb-' + itemPath.replace(/[^A-Za-z0-9]+/g, '-');
                input.dataset.bindPath = itemPath;
                input.value = bullet;
                input.rows = 2;
                input.maxLength = LIMITS.bullet;
                input.placeholder = 'Start with a strong action verb and include a measurable result when possible.';
                input.setAttribute('aria-label', label + ' ' + String(index + 1));
                const remove = domButton(documentObject, 'Remove bullet', 'remove-bullet', 'aw-rb-text-button aw-rb-remove-bullet');
                remove.dataset.listPath = path; remove.dataset.index = String(index);
                remove.setAttribute('aria-label', 'Remove ' + label.toLowerCase() + ' ' + String(index + 1));
                const editor = domElement(documentObject, 'div', 'aw-rb-rich-editor');
                editor.append(formatToolbar(input.id, label + ' ' + String(index + 1)), input);
                row.append(editor, remove);
                list.appendChild(row);
            });
            const add = domButton(documentObject, '+ Add bullet', 'add-bullet', 'aw-rb-text-button');
            add.dataset.listPath = path;
            list.appendChild(add);
            container.appendChild(list);
        }

        function renderTags(container, path, label, placeholder) {
            const group = domElement(documentObject, 'div', 'aw-rb-tag-entry');
            group.appendChild(domElement(documentObject, 'p', 'form-label', label));
            const tags = domElement(documentObject, 'div', 'aw-rb-tags');
            (getPath(state, path) || []).forEach(function (tag, index) {
                const chip = domElement(documentObject, 'span', 'aw-rb-tag');
                chip.appendChild(domElement(documentObject, 'span', '', tag));
                const remove = domButton(documentObject, '×', 'remove-tag', 'aw-rb-icon-button');
                remove.dataset.listPath = path; remove.dataset.index = String(index);
                remove.setAttribute('aria-label', 'Remove ' + tag);
                chip.appendChild(remove);
                tags.appendChild(chip);
            });
            group.appendChild(tags);
            const inputRow = domElement(documentObject, 'div', 'aw-rb-bullet-row');
            const input = domElement(documentObject, 'input', 'form-control');
            input.type = 'text'; input.maxLength = LIMITS.tag;
            input.placeholder = placeholder || 'Type a value, then press Enter';
            input.dataset.tagInput = '';
            input.dataset.listPath = path;
            input.setAttribute('aria-label', 'Add ' + label.toLowerCase());
            const add = domButton(documentObject, 'Add', 'add-tag', 'aw-rb-text-button');
            add.dataset.listPath = path;
            inputRow.append(input, add);
            group.appendChild(inputRow);
            container.appendChild(group);
        }

        function renderSkillItems(container, categoryIndex) {
            const path = 'skills.' + categoryIndex + '.skills';
            const skills = getPath(state, path) || [];
            const group = domElement(documentObject, 'div', 'aw-rb-skill-items');
            const heading = domElement(documentObject, 'div', 'aw-rb-skill-items-heading');
            heading.appendChild(domElement(documentObject, 'p', 'form-label', 'Skills and optional proficiency'));
            const add = domButton(documentObject, '+ Add skill', 'add-skill-item', 'aw-rb-text-button');
            add.dataset.categoryIndex = String(categoryIndex);
            heading.appendChild(add);
            group.appendChild(heading);
            if (!skills.length) group.appendChild(domElement(documentObject, 'p', 'aw-rb-empty', 'No skills in this category yet.'));
            skills.forEach(function (skill, skillIndex) {
                const itemPath = path + '.' + skillIndex;
                const row = domElement(documentObject, 'div', 'aw-rb-skill-item-row');
                row.appendChild(fieldControl(itemPath + '.name', {
                    label: 'Skill ' + String(skillIndex + 1),
                    placeholder: 'e.g. JavaScript'
                }));
                row.appendChild(fieldControl(itemPath + '.level', {
                    label: 'Level',
                    type: 'select',
                    options: PROFICIENCY_LEVEL_OPTIONS
                }));
                const remove = domButton(documentObject, 'Remove', 'remove-skill-item', 'aw-rb-text-button');
                remove.dataset.categoryIndex = String(categoryIndex);
                remove.dataset.skillIndex = String(skillIndex);
                remove.setAttribute('aria-label', 'Remove ' + (skill.name || 'skill ' + String(skillIndex + 1)));
                row.appendChild(remove);
                group.appendChild(row);
            });
            container.appendChild(group);
        }

        function renderCollection(config) {
            const card = sectionCard(config.key, config.title, config.help, config.addLabel || '+ Add entry');
            const entries = state[config.key];
            if (!entries.length) renderEmpty(card, config.empty);
            entries.forEach(function (entry, index) {
                const entryCard = domElement(documentObject, 'article', 'aw-rb-entry-card');
                entryCard.dataset.entrySection = config.key;
                entryCard.dataset.entryIndex = String(index);
                const head = domElement(documentObject, 'div', 'aw-rb-entry-head');
                head.appendChild(domElement(documentObject, 'h4', '', config.entryTitle(entry, index)));
                head.appendChild(entryActions(config.key, index, entries.length, config.singular + ' ' + String(index + 1)));
                entryCard.appendChild(head);
                const grid = domElement(documentObject, 'div', 'aw-rb-dynamic-grid');
                config.fields.forEach(function (definition) {
                    if (definition.when && !definition.when(entry)) return;
                    grid.appendChild(fieldControl(config.key + '.' + index + '.' + definition.property, definition));
                });
                entryCard.appendChild(grid);
                if (config.extra) config.extra(entryCard, entry, index);
                card.appendChild(entryCard);
            });
            dynamicEditor.appendChild(card);
        }

        function renderCustomSections() {
            const card = sectionCard('customSections', 'Custom sections', 'Create flexible sections for publications, memberships, speaking, or anything else.', '+ Add custom section');
            if (!state.customSections.length) renderEmpty(card, 'No custom sections yet.');
            state.customSections.forEach(function (section, sectionIndex) {
                const sectionPath = 'customSections.' + sectionIndex;
                const outer = domElement(documentObject, 'article', 'aw-rb-entry-card');
                const head = domElement(documentObject, 'div', 'aw-rb-entry-head');
                head.appendChild(domElement(documentObject, 'h4', '', section.title || 'Custom section ' + String(sectionIndex + 1)));
                head.appendChild(entryActions('customSections', sectionIndex, state.customSections.length, 'custom section ' + String(sectionIndex + 1)));
                outer.appendChild(head);
                const titleGrid = domElement(documentObject, 'div', 'aw-rb-dynamic-grid');
                titleGrid.appendChild(fieldControl(sectionPath + '.title', { label: 'Section title', placeholder: 'e.g. Publications' }));
                outer.appendChild(titleGrid);
                const entriesHead = domElement(documentObject, 'div', 'aw-rb-entry-head');
                entriesHead.appendChild(domElement(documentObject, 'h5', '', 'Entries'));
                const addEntry = domButton(documentObject, '+ Add entry', 'add-custom-entry', 'aw-rb-text-button');
                addEntry.dataset.sectionIndex = String(sectionIndex);
                entriesHead.appendChild(addEntry);
                outer.appendChild(entriesHead);
                if (!section.entries.length) renderEmpty(outer, 'No entries in this section yet.');
                section.entries.forEach(function (entry, entryIndex) {
                    const entryPath = sectionPath + '.entries.' + entryIndex;
                    const nested = domElement(documentObject, 'div', 'aw-rb-entry-card');
                    const nestedHead = domElement(documentObject, 'div', 'aw-rb-entry-head');
                    nestedHead.appendChild(domElement(documentObject, 'h5', '', entry.heading || 'Entry ' + String(entryIndex + 1)));
                    const actions = domElement(documentObject, 'div', 'aw-rb-entry-actions');
                    ['up', 'down'].forEach(function (direction) {
                        const control = domButton(documentObject, direction === 'up' ? '↑' : '↓', 'move-custom-entry-' + direction, 'aw-rb-icon-button');
                        control.dataset.sectionIndex = String(sectionIndex); control.dataset.entryIndex = String(entryIndex);
                        control.disabled = direction === 'up' ? entryIndex === 0 : entryIndex === section.entries.length - 1;
                        control.setAttribute('aria-label', 'Move custom entry ' + direction);
                        actions.appendChild(control);
                    });
                    const remove = domButton(documentObject, 'Remove', 'remove-custom-entry', 'aw-rb-text-button');
                    remove.dataset.sectionIndex = String(sectionIndex); remove.dataset.entryIndex = String(entryIndex);
                    actions.appendChild(remove);
                    nestedHead.appendChild(actions);
                    nested.appendChild(nestedHead);
                    const grid = domElement(documentObject, 'div', 'aw-rb-dynamic-grid');
                    [
                        { property: 'heading', label: 'Heading' },
                        { property: 'subheading', label: 'Subheading' },
                        { property: 'location', label: 'Location' },
                        { property: 'url', label: 'URL', type: 'url', placeholder: 'https://…' },
                        { property: 'startDate', label: 'Start month', type: 'month' },
                        { property: 'endDate', label: 'End month', type: 'month', when: function () { return !entry.isCurrent; } },
                        { property: 'isCurrent', label: 'Current', type: 'checkbox' }
                    ].forEach(function (definition) {
                        if (!definition.when || definition.when()) grid.appendChild(fieldControl(entryPath + '.' + definition.property, definition));
                    });
                    nested.appendChild(grid);
                    renderBullets(nested, entryPath + '.bulletPoints', 'Highlights');
                    outer.appendChild(nested);
                });
                card.appendChild(outer);
            });
            dynamicEditor.appendChild(card);
        }

        function renderDynamicEditor() {
            if (!dynamicEditor) return;
            replaceChildren(dynamicEditor);
            renderCollection({
                key: 'education', title: 'Education', singular: 'education entry', addLabel: '+ Add education', empty: 'No education entries yet.',
                help: 'Add your most recent or most relevant education first.', entryTitle: function (entry, index) { return entry.institutionName || 'Education ' + String(index + 1); },
                fields: [
                    { property: 'institutionName', label: 'Institution name', placeholder: 'FAST National University' },
                    { property: 'degree', label: 'Degree', placeholder: 'Bachelor of Science' },
                    { property: 'fieldOfStudy', label: 'Field of study', placeholder: 'Computer Science' },
                    { property: 'location', label: 'Location' },
                    { property: 'startDate', label: 'Start month', type: 'month' },
                    { property: 'endDate', label: 'End month', type: 'month', when: function (entry) { return !entry.isCurrentlyStudying; } },
                    { property: 'isCurrentlyStudying', label: 'Currently studying', type: 'checkbox' },
                    { property: 'grade', label: 'CGPA / GPA / grade', placeholder: 'CGPA: 3.5 / 4.0' },
                    { property: 'descriptionOrCoursework', label: 'Description or relevant coursework', type: 'textarea', wide: true, rows: 3, richText: true }
                ]
            });
            renderCollection({
                key: 'experience', title: 'Experience', singular: 'experience entry', addLabel: '+ Add experience', empty: 'No experience entries yet.',
                help: 'Lead with outcomes and quantify impact where you can.', entryTitle: function (entry, index) { return entry.jobTitle || entry.companyName || 'Experience ' + String(index + 1); },
                fields: [
                    { property: 'companyName', label: 'Company name' }, { property: 'jobTitle', label: 'Job title' },
                    { property: 'employmentType', label: 'Employment type', placeholder: 'Full-time, internship, contract…' }, { property: 'location', label: 'Location or organization' },
                    { property: 'technologySkills', label: 'Technology / skills', placeholder: 'React.js, PostgreSQL, Leadership…', wide: true },
                    { property: 'startDate', label: 'Start month', type: 'month' }, { property: 'endDate', label: 'End month', type: 'month', when: function (entry) { return !entry.isCurrentlyWorking; } },
                    { property: 'isCurrentlyWorking', label: 'Currently working', type: 'checkbox' }
                ],
                extra: function (container, _entry, index) { renderBullets(container, 'experience.' + index + '.bulletPoints', 'Responsibilities and achievements'); }
            });
            renderCollection({
                key: 'projects', title: 'Projects', singular: 'project', addLabel: '+ Add project', empty: 'No projects yet.',
                help: 'Show the problem, what you built, and the result.', entryTitle: function (entry, index) { return entry.projectName || 'Project ' + String(index + 1); },
                fields: [
                    { property: 'projectName', label: 'Project name' }, { property: 'projectUrl', label: 'Live project URL', type: 'url', placeholder: 'https://…' },
                    { property: 'repositoryUrl', label: 'Repository URL', type: 'url', placeholder: 'https://…' },
                    { property: 'startDate', label: 'Start month', type: 'month' }, { property: 'endDate', label: 'End month', type: 'month', when: function (entry) { return !entry.isOngoing; } },
                    { property: 'isOngoing', label: 'Ongoing project', type: 'checkbox' }
                ],
                extra: function (container, _entry, index) {
                    renderTags(container, 'projects.' + index + '.technologiesUsed', 'Technologies used', 'Type a technology, then press Enter');
                    renderBullets(container, 'projects.' + index + '.bulletPoints', 'Project highlights');
                }
            });
            renderCollection({
                key: 'skills', title: 'Skills', singular: 'skill category', addLabel: '+ Add category', empty: 'No skill categories yet.',
                help: 'Group related skills. Levels are optional unless the selected design displays proficiency bars.', entryTitle: function (entry, index) { return entry.name || 'Skill category ' + String(index + 1); },
                fields: [{ property: 'name', label: 'Category name', placeholder: 'Languages, Frameworks, Databases…', wide: true }],
                extra: function (container, _entry, index) { renderSkillItems(container, index); }
            });
            renderCollection({
                key: 'achievementsAndCertifications', title: 'Achievements & certifications', singular: 'achievement', addLabel: '+ Add achievement', empty: 'No achievements or certifications yet.',
                help: '', entryTitle: function (entry, index) { return entry.title || 'Achievement ' + String(index + 1); },
                fields: [
                    { property: 'title', label: 'Title' }, { property: 'issuingOrganization', label: 'Issuing organization' },
                    { property: 'date', label: 'Date', type: 'month' }, { property: 'credentialUrl', label: 'Credential URL', type: 'url', placeholder: 'https://…' },
                    { property: 'description', label: 'Short description', type: 'textarea', wide: true, rows: 3, richText: true }
                ]
            });
            renderCollection({
                key: 'languages', title: 'Languages', singular: 'language', addLabel: '+ Add language', empty: 'No languages yet.',
                help: '', entryTitle: function (entry, index) { return entry.name || 'Language ' + String(index + 1); },
                fields: [
                    { property: 'name', label: 'Language' },
                    { property: 'proficiency', label: 'Proficiency', placeholder: 'Native, fluent, conversational…' },
                    { property: 'level', label: 'Level', type: 'select', options: PROFICIENCY_LEVEL_OPTIONS }
                ]
            });
            renderCollection({
                key: 'volunteerExperience', title: 'Volunteer experience', singular: 'volunteer entry', addLabel: '+ Add volunteer role', empty: 'No volunteer experience yet.',
                help: '', entryTitle: function (entry, index) { return entry.role || entry.organizationName || 'Volunteer role ' + String(index + 1); },
                fields: [
                    { property: 'organizationName', label: 'Organization name' }, { property: 'role', label: 'Role' }, { property: 'location', label: 'Location' },
                    { property: 'startDate', label: 'Start month', type: 'month' }, { property: 'endDate', label: 'End month', type: 'month', when: function (entry) { return !entry.isCurrentlyVolunteering; } },
                    { property: 'isCurrentlyVolunteering', label: 'Currently volunteering', type: 'checkbox' }
                ],
                extra: function (container, _entry, index) { renderBullets(container, 'volunteerExperience.' + index + '.bulletPoints', 'Highlights'); }
            });
            renderCollection({
                key: 'references', title: 'References', singular: 'reference', addLabel: '+ Add reference', empty: 'No references yet.',
                help: 'Add only people who have agreed to be contacted.', entryTitle: function (entry, index) { return entry.fullName || 'Reference ' + String(index + 1); },
                fields: [
                    { property: 'fullName', label: 'Full name' }, { property: 'jobTitle', label: 'Job title' },
                    { property: 'company', label: 'Company or organization' }, { property: 'emailAddress', label: 'Email address', type: 'email' },
                    { property: 'phoneNumber', label: 'Phone number', type: 'tel' }
                ]
            });
            const interests = sectionCard('interests', 'Interests', 'Keep this concise and specific.', null);
            renderTags(interests, 'interests', 'Interests', 'Type an interest, then press Enter');
            dynamicEditor.appendChild(interests);
            renderCustomSections();
        }

        function renderSectionManager() {
            if (!sectionManager) return;
            const rows = [];
            const selectedTemplate = templateMetadata(state.templateId);
            const lockedOrder = selectedTemplate.locked && templateRenderers
                ? templateRenderers.SECTION_ORDERS[selectedTemplate.id] || []
                : [];
            const byKey = new Map(state.sections.map(function (section) { return [section.key, section]; }));
            const displaySections = selectedTemplate.locked
                ? lockedOrder.map(function (key) { return byKey.get(key); }).filter(Boolean)
                : state.sections;
            sectionManager.classList.toggle('is-locked', Boolean(selectedTemplate.locked));
            if (selectedTemplate.locked) {
                const notice = domElement(documentObject, 'div', 'aw-rb-manager-lock-note');
                notice.appendChild(domElement(documentObject, 'strong', '', selectedTemplate.name + ' controls section placement.'));
                notice.appendChild(domElement(documentObject, 'span', '', 'You can show or hide sections, but the design keeps its professional order.'));
                rows.push(notice);
            }
            displaySections.forEach(function (section, displayIndex) {
                const index = state.sections.indexOf(section);
                const row = domElement(documentObject, 'div', 'aw-rb-manager-row');
                row.dataset.sectionKey = section.key;
                row.classList.toggle('is-hidden', !section.isVisible);
                const label = domElement(documentObject, 'span', '', section.title);
                const actions = domElement(documentObject, 'div', 'aw-rb-entry-actions');
                const up = domButton(documentObject, '↑', 'move-section-up', 'aw-rb-icon-button');
                up.dataset.index = String(index); up.disabled = selectedTemplate.locked || displayIndex === 0; up.setAttribute('aria-label', 'Move ' + section.title + ' up');
                const down = domButton(documentObject, '↓', 'move-section-down', 'aw-rb-icon-button');
                down.dataset.index = String(index); down.disabled = selectedTemplate.locked || displayIndex === displaySections.length - 1; down.setAttribute('aria-label', 'Move ' + section.title + ' down');
                const toggle = domButton(documentObject, section.isVisible ? 'Hide' : 'Show', 'toggle-section', 'aw-rb-text-button');
                toggle.dataset.index = String(index); toggle.setAttribute('aria-pressed', String(section.isVisible));
                toggle.setAttribute('aria-label', (section.isVisible ? 'Hide ' : 'Show ') + section.title + ' in resume');
                actions.append(up, down, toggle);
                row.append(label, actions);
                rows.push(row);
            });
            replaceChildren.apply(null, [sectionManager].concat(rows));
        }

        function currentErrors() {
            return validateState(state);
        }

        function updateOnePageStatus() {
            const guidance = onePageGuidance(state);
            Object.keys(guidance.metrics).forEach(function (key) {
                const metric = guidance.metrics[key];
                const element = root.querySelector('[data-guide-metric="' + key + '"]');
                if (!element) return;
                const value = element.querySelector('strong');
                const suffix = key === 'professionalSummary' ? ' lines' : '';
                if (value) value.textContent = metric.value + ' / ' + metric.maximum + suffix;
                element.classList.toggle('is-over', metric.value > metric.maximum);
            });

            const pageCountIsCurrent = latestBlobRevision === revision && latestPageCount > 0;
            if (pageFitStatus) {
                pageFitStatus.classList.toggle('is-checking', !pageCountIsCurrent);
                pageFitStatus.classList.toggle('is-overflow', pageCountIsCurrent && latestPageCount !== 1);
                pageFitStatus.textContent = !pageCountIsCurrent
                    ? 'Checking page fit…'
                    : latestPageCount === 1
                        ? 'Fits on 1 A4 page'
                        : latestPageCount + ' pages - shorten content';
            }

            const messages = [];
            if (pageCountIsCurrent && latestPageCount > 1) {
                const largest = largestContentSections(state, 3).map(function (section) { return section.title; });
                messages.push('This resume currently creates ' + latestPageCount + ' pages. Download is blocked until it fits on one page.'
                    + (largest.length ? ' Start by shortening or hiding: ' + largest.join(', ') + '.' : ''));
            }
            if (guidance.exceeded.length) {
                const labels = guidance.exceeded.map(function (key) { return guidance.metrics[key].label; });
                messages.push('Above the one-page guide: ' + labels.join(', ') + '. Keep only the most relevant content or hide a section.');
            }
            if (onePageWarning) {
                onePageWarning.textContent = messages.join(' ');
                onePageWarning.hidden = messages.length === 0;
            }
            return { guidance: guidance, pageCountIsCurrent: pageCountIsCurrent };
        }

        function updateValidation() {
            const errors = currentErrors();
            root.querySelectorAll('[data-error-for]').forEach(function (message) {
                const path = message.dataset.errorFor;
                const focusedTemplateRequirement = state.templateSelectionConfirmed && (
                    path === 'personalInformation.profilePhotoDataUrl'
                    || path.endsWith('.level')
                );
                const visible = showAllErrors || touched.has(path) || focusedTemplateRequirement;
                message.textContent = visible ? (errors[path] || '') : '';
                const control = Array.from(root.querySelectorAll('[data-field], [data-bind-path]')).find(function (item) {
                    return item.dataset.field === path || item.dataset.bindPath === path;
                });
                if (control) {
                    control.classList.toggle('input-validation-error', Boolean(visible && errors[path]));
                    control.setAttribute('aria-invalid', String(Boolean(visible && errors[path])));
                }
            });
            const valid = Object.keys(errors).length === 0;
            const onePage = updateOnePageStatus();
            const canDownload = valid && onePage.pageCountIsCurrent && latestPageCount === 1;
            root.querySelectorAll('[data-action="download"]').forEach(function (button) { button.disabled = !canDownload; });
            return { valid: valid, errors: errors, canDownload: canDownload, pageCount: latestPageCount };
        }

        function renderAll() {
            syncStaticFields();
            syncTemplatePicker();
            renderSectionManager();
            renderDynamicEditor();
            syncEditorTabs();
            syncGuidedProgress();
            updateValidation();
            syncGalleryVisibility();
        }

        function focusInitialSection() {
            if (destroyed || initialSectionHandled || !initialSection) return false;
            activateEditorTab(editorTabForSection(initialSection));
            const selector = builderSectionSelector(initialSection);
            const target = selector ? root.querySelector(selector) : null;
            if (!target) return false;

            initialSectionHandled = true;
            activateTab('form');
            target.classList.add('is-deep-link-target');
            const control = target.querySelector('[data-field], [data-bind-path], input:not([type="hidden"]):not([disabled]), textarea:not([disabled]), select:not([disabled])')
                || target.querySelector('button:not([disabled]), [href]');
            if (control && typeof control.focus === 'function') {
                try { control.focus({ preventScroll: true }); }
                catch (_error) { control.focus(); }
            } else if (typeof target.focus === 'function') {
                target.tabIndex = -1;
                try { target.focus({ preventScroll: true }); }
                catch (_error) { target.focus(); }
            }
            if (typeof target.scrollIntoView === 'function') {
                const reducedMotion = windowObject && typeof windowObject.matchMedia === 'function'
                    && windowObject.matchMedia('(prefers-reduced-motion: reduce)').matches;
                target.scrollIntoView({ behavior: reducedMotion ? 'auto' : 'smooth', block: 'center' });
            }
            if (windowObject) {
                windowObject.clearTimeout(targetHighlightTimer);
                targetHighlightTimer = windowObject.setTimeout(function () {
                    target.classList.remove('is-deep-link-target');
                }, 2400);
            }
            return true;
        }

        function scheduleInitialSectionFocus() {
            if (!initialSection || !windowObject || initialSectionHandled) return;
            windowObject.clearTimeout(initialFocusTimer);
            initialFocusTimer = windowObject.setTimeout(focusInitialSection, 0);
        }

        function saveNow() {
            if (destroyed || revision === savedRevision) return;
            if (!storageEnabled) {
                setStatus(saveStatus, 'Local draft saving is unavailable.');
                return;
            }
            let serialized;
            try { serialized = JSON.stringify(normalizeState(state)); }
            catch (_error) { setStatus(saveStatus, 'This draft could not be saved.'); return; }
            if (serialized.length > LIMITS.draftBytes) {
                setStatus(saveStatus, 'This draft is too large to save locally. Shorten a few entries.');
                return;
            }
            if (storageSet(serialized)) {
                savedRevision = revision;
                const now = new Date();
                setStatus(saveStatus, 'Saved at ' + now.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) + '.');
            } else {
                setStatus(saveStatus, 'This browser could not save the draft locally.');
            }
        }

        function scheduleSave() {
            if (!storageEnabled) return;
            setStatus(saveStatus, 'Saving…');
            windowObject.clearTimeout(saveTimer);
            saveTimer = windowObject.setTimeout(saveNow, 500);
        }

        function pdfMakeInstance() {
            return configuration.pdfMake || (windowObject && windowObject.pdfMake);
        }

        function loadFontScript(key, url) {
            if (!url || !documentObject || !documentObject.head) return Promise.resolve();
            if (fontScriptPromises.has(key)) return fontScriptPromises.get(key);
            const promise = new Promise(function (resolve, reject) {
                const script = documentObject.createElement('script');
                script.src = url;
                script.async = true;
                script.dataset.resumeFont = key;
                script.addEventListener('load', resolve, { once: true });
                script.addEventListener('error', function () {
                    reject(new Error(key + ' resume font could not be loaded.'));
                }, { once: true });
                documentObject.head.appendChild(script);
            });
            fontScriptPromises.set(key, promise);
            return promise;
        }

        function ensureTemplateFonts(templateId) {
            const font = String(templateMetadata(templateId).font || 'Roboto').toLocaleLowerCase();
            const loads = [];
            if (font.includes('roboto')) {
                loads.push(loadFontScript('roboto', root.dataset.robotoFontUrl));
            }
            if (font.includes('poppins')) {
                loads.push(loadFontScript('poppins', root.dataset.poppinsFontUrl));
            }
            if (font.includes('libre')) {
                loads.push(loadFontScript(
                    'libre-baskerville',
                    root.dataset.libreBaskervilleFontUrl));
            }
            if (font.includes('cmu')) {
                loads.push(loadFontScript('cmu-serif', root.dataset.cmuSerifFontUrl));
            }
            return Promise.all(loads);
        }

        function createPdfBlob(definition, templateId) {
            return ensureTemplateFonts(templateId).then(function () {
                const pdfMake = pdfMakeInstance();
                if (!pdfMake || typeof pdfMake.createPdf !== 'function') {
                    throw new Error('PDF generator is unavailable.');
                }
                const result = pdfMake.createPdf(definition);
                if (!result || typeof result.getBlob !== 'function') {
                    throw new Error('PDF generator did not return a document.');
                }
                return result.getBlob();
            });
        }

        function revokePreviewUrl() {
            if (!currentPreviewUrl || !windowObject || !windowObject.URL) return;
            windowObject.URL.revokeObjectURL(currentPreviewUrl);
            currentPreviewUrl = '';
        }

        function onPageHide() {
            saveNow();
            revokePreviewUrl();
        }

        function onPageShow(event) {
            if (event && event.persisted) schedulePreview(0);
        }

        function onStorage(event) {
            if (event.key !== storageKey) return;
            windowObject.clearTimeout(saveTimer);
            if (event.newValue === null) {
                state = createEmptyState();
                revision += 1;
                savedRevision = revision;
                latestBlobRevision = -1;
                latestPageCount = 0;
                touched.clear();
                showAllErrors = false;
                renderAll();
                schedulePreview(0);
                setStatus(saveStatus, 'Resume cleared in another tab.');
                setStatus(pdfStatus, '');
                return;
            }
            if (typeof event.newValue !== 'string') return;
            const parsed = parseDraft(event.newValue);
            if (parsed.recovered) return;
            state = parsed.state;
            revision += 1;
            savedRevision = revision;
            latestBlobRevision = -1;
            latestPageCount = 0;
            touched.clear();
            showAllErrors = false;
            renderAll();
            schedulePreview(0);
            setStatus(saveStatus, 'Draft updated from another tab.');
        }

        function refreshPreview() {
            if (destroyed || !previewFrame) return Promise.resolve(null);
            const request = ++previewRequest;
            const requestedRevision = revision;
            const definition = buildDocumentDefinition(state);
            setPreviewMessage('Updating resume preview…', 'loading');
            previewPromise = createPdfBlob(definition, state.templateId).then(async function (blob) {
                if (destroyed || request !== previewRequest || requestedRevision !== revision) return null;
                if (!blob || typeof blob.size !== 'number') throw new Error('The generated PDF was empty.');
                const pageCount = await countPdfPages(blob);
                if (destroyed || request !== previewRequest || requestedRevision !== revision) return null;
                latestBlob = blob;
                latestBlobRevision = revision;
                latestPageCount = pageCount;
                revokePreviewUrl();
                currentPreviewUrl = windowObject.URL.createObjectURL(blob);
                previewFrame.src = currentPreviewUrl + '#view=FitH&navpanes=0';
                setPreviewMessage('', 'ready');
                updateValidation();
                return blob;
            }).catch(function (_error) {
                if (request === previewRequest) setPreviewMessage('The preview could not be generated. Check the PDF library and try again.', 'error');
                return null;
            }).finally(function () {
                if (request === previewRequest) previewPromise = null;
                if (requestedRevision !== revision && !destroyed) schedulePreview(0);
            });
            return previewPromise;
        }

        function schedulePreview(delay) {
            if (!previewFrame || !windowObject) return;
            windowObject.clearTimeout(previewTimer);
            previewTimer = windowObject.setTimeout(refreshPreview, typeof delay === 'number' ? delay : 650);
        }

        const renderFocusDataKeys = [
            'action', 'field', 'bindPath', 'section', 'index', 'listPath',
            'categoryIndex', 'skillIndex', 'sectionIndex', 'entryIndex', 'editorTab'
        ];

        function captureRenderFocus() {
            const active = documentObject.activeElement;
            if (!active || !root.contains(active)) return null;
            const data = {};
            renderFocusDataKeys.forEach(function (key) {
                if (active.dataset && active.dataset[key] !== undefined) data[key] = active.dataset[key];
            });
            return { id: active.id || '', data: data };
        }

        function restoreRenderFocus(snapshot) {
            if (!snapshot) return;
            let target = snapshot.id ? documentObject.getElementById(snapshot.id) : null;
            const focusable = Array.from(root.querySelectorAll(
                'button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])'));
            const keys = Object.keys(snapshot.data);
            if (!target || !root.contains(target)) {
                target = focusable.find(function (candidate) {
                    return keys.length > 0 && keys.every(function (key) {
                        return candidate.dataset && candidate.dataset[key] === snapshot.data[key];
                    });
                });
            }
            if (!target) {
                const positionalKeys = new Set(['index', 'skillIndex', 'entryIndex']);
                const stableKeys = keys.filter(function (key) { return !positionalKeys.has(key); });
                const candidates = focusable.filter(function (candidate) {
                    return stableKeys.length > 0 && stableKeys.every(function (key) {
                        return candidate.dataset && candidate.dataset[key] === snapshot.data[key];
                    });
                });
                const requestedIndex = Number(snapshot.data.index ?? snapshot.data.skillIndex ?? snapshot.data.entryIndex ?? 0);
                target = candidates[Math.min(Math.max(requestedIndex, 0), candidates.length - 1)];
            }
            if (target && typeof target.focus === 'function') target.focus();
        }

        function changed(rerender) {
            const focusSnapshot = rerender ? captureRenderFocus() : null;
            revision += 1;
            latestBlobRevision = -1;
            latestPageCount = 0;
            if (rerender) {
                renderSectionManager();
                renderDynamicEditor();
            }
            syncStaticFields();
            syncTemplatePicker();
            syncEditorTabs();
            syncGuidedProgress();
            updateValidation();
            scheduleSave();
            schedulePreview();
            if (focusSnapshot && windowObject) {
                const restore = function () { restoreRenderFocus(focusSnapshot); };
                if (typeof windowObject.requestAnimationFrame === 'function') windowObject.requestAnimationFrame(restore);
                else windowObject.setTimeout(restore, 0);
            }
        }

        function setExpandedPreviewIsolation(panel, expanded) {
            if (expanded) {
                previewInertRecords.length = 0;
                let current = panel;
                while (current && current !== documentObject.body) {
                    const parent = current.parentElement;
                    if (!parent) break;
                    Array.from(parent.children).forEach(function (sibling) {
                        if (sibling === current || sibling.matches('[data-preview-backdrop]')) return;
                        previewInertRecords.push([sibling, Boolean(sibling.inert)]);
                        sibling.inert = true;
                    });
                    current = parent;
                }
                panel.setAttribute('role', 'dialog');
                panel.setAttribute('aria-modal', 'true');
                panel.setAttribute('aria-labelledby', 'rb-preview-title');
                panel.setAttribute('aria-hidden', 'false');
                return;
            }

            previewInertRecords.forEach(function (record) { record[0].inert = record[1]; });
            previewInertRecords.length = 0;
            panel.setAttribute('role', 'tabpanel');
            panel.removeAttribute('aria-modal');
            panel.setAttribute('aria-labelledby', 'rb-preview-tab');
            onLayoutChange();
        }

        function notifyLimit(message) {
            setStatus(saveStatus, message);
        }

        function addTag(input) {
            if (!input) return;
            const path = input.dataset.listPath;
            const list = getPath(state, path);
            const value = text(input.value, LIMITS.tag);
            if (!Array.isArray(list) || !value) return;
            if (list.length >= LIMITS.tags) { notifyLimit('That list has reached its ' + LIMITS.tags + '-item limit.'); return; }
            if (list.some(function (item) { return item.toLocaleLowerCase() === value.toLocaleLowerCase(); })) {
                input.value = '';
                notifyLimit('That item is already in the list.');
                return;
            }
            list.push(value);
            input.value = '';
            changed(true);
            const matching = Array.from(root.querySelectorAll('[data-tag-input]')).find(function (candidate) { return candidate.dataset.listPath === path; });
            if (matching) matching.focus();
        }

        function applyTextFormat(button) {
            const markers = {
                bold: ['[b]', '[/b]'],
                italic: ['[i]', '[/i]'],
                underline: ['[u]', '[/u]']
            };
            const pair = markers[button.dataset.format];
            const target = documentObject.getElementById(button.dataset.formatTarget || '');
            if (!pair || !target || !root.contains(target) || typeof target.selectionStart !== 'number') return;
            const start = target.selectionStart;
            const end = target.selectionEnd;
            const selected = target.value.slice(start, end);
            const replacement = pair[0] + selected + pair[1];
            const nextValue = target.value.slice(0, start) + replacement + target.value.slice(end);
            if (target.maxLength > 0 && nextValue.length > target.maxLength) {
                setStatus(saveStatus, 'That field is at its character limit; shorten it before adding formatting.');
                target.focus();
                return;
            }
            target.value = nextValue;
            target.focus();
            const selectionStart = start + pair[0].length;
            target.setSelectionRange(selectionStart, selectionStart + selected.length);
            target.dispatchEvent(new windowObject.Event('input', { bubbles: true }));
        }

        function confirmClear() {
            state = createEmptyState();
            touched.clear();
            showAllErrors = false;
            storageRemove();
            revision += 1;
            savedRevision = revision;
            latestBlobRevision = -1;
            latestPageCount = 0;
            activeEditorTab = 'personal';
            if (clearDialog && typeof clearDialog.close === 'function') clearDialog.close();
            renderAll();
            setStatus(saveStatus, 'Resume cleared.');
            setStatus(pdfStatus, '');
            schedulePreview(0);
            const first = root.querySelector('[data-field="personalInformation.fullName"]');
            if (first) first.focus();
        }

        function requestClear() {
            if (clearDialog && typeof clearDialog.showModal === 'function') {
                if (!clearDialog.open) clearDialog.showModal();
                return;
            }
            if (!windowObject || windowObject.confirm('Clear all resume information? This cannot be undone.')) confirmClear();
        }

        function focusFirstError(errors) {
            const firstPath = Object.keys(errors)[0];
            if (!firstPath) return;
            activateEditorTab(editorTabForSection(firstPath.split('.')[0]));
            const control = Array.from(root.querySelectorAll('[data-field], [data-bind-path]')).find(function (item) {
                return item.dataset.field === firstPath || item.dataset.bindPath === firstPath;
            });
            if (control) control.focus();
        }

        function downloadBlob(blob, filename) {
            const url = windowObject.URL.createObjectURL(blob);
            const anchor = documentObject.createElement('a');
            anchor.href = url;
            anchor.download = filename;
            anchor.hidden = true;
            documentObject.body.appendChild(anchor);
            anchor.click();
            anchor.remove();
            windowObject.setTimeout(function () { windowObject.URL.revokeObjectURL(url); }, 1000);
        }

        async function getCurrentPdfBlob() {
            const maximumAttempts = 3;
            for (let attempt = 0; attempt < maximumAttempts; attempt += 1) {
                const requestedRevision = revision;
                if (latestBlob && latestBlobRevision === requestedRevision) {
                    if (!latestPageCount) latestPageCount = await countPdfPages(latestBlob);
                    return latestBlob;
                }

                if (previewPromise) {
                    await previewPromise;
                    if (latestBlob && latestBlobRevision === revision) return latestBlob;
                    if (requestedRevision !== revision) continue;
                }

                const definition = buildDocumentDefinition(state);
                const generatedBlob = await createPdfBlob(definition, state.templateId);
                const generatedPageCount = await countPdfPages(generatedBlob);
                if (requestedRevision !== revision) continue;

                latestBlob = generatedBlob;
                latestBlobRevision = requestedRevision;
                latestPageCount = generatedPageCount;
                return generatedBlob;
            }

            throw new Error('The resume changed while the PDF was being prepared.');
        }

        async function downloadPdf() {
            showAllErrors = true;
            const validation = updateValidation();
            if (!validation.valid) {
                setStatus(pdfStatus, 'Complete the highlighted fields before downloading.');
                focusFirstError(validation.errors);
                return;
            }
            const buttons = root.querySelectorAll('[data-action="download"]');
            buttons.forEach(function (button) { button.disabled = true; button.setAttribute('aria-busy', 'true'); });
            setStatus(pdfStatus, 'Creating your PDF…');
            try {
                const blob = await getCurrentPdfBlob();
                const currentValidation = updateValidation();
                if (!currentValidation.valid) {
                    setStatus(pdfStatus, 'Complete the highlighted fields before downloading.');
                    focusFirstError(currentValidation.errors);
                    return;
                }
                if (currentValidation.pageCount !== 1) {
                    setStatus(pdfStatus, 'This resume is ' + currentValidation.pageCount + ' pages. Shorten it until the one-page check passes.');
                    return;
                }
                if (exportAuthorizationUrl) {
                    const tokenInput = exportAuthorizationForm
                        ? exportAuthorizationForm.querySelector('input[name="__RequestVerificationToken"]')
                        : null;
                    const body = new URLSearchParams();
                    body.set('templateId', state.templateId);
                    const response = await windowObject.fetch(exportAuthorizationUrl, {
                        method: 'POST',
                        credentials: 'same-origin',
                        headers: tokenInput ? { 'RequestVerificationToken': tokenInput.value } : {},
                        body: body
                    });
                    const payload = await response.json().catch(function () { return {}; });
                    if (!response.ok || !payload.allowed) {
                        throw new Error(payload.message || 'Your resume download allowance is unavailable.');
                    }
                    if (buildAllowance) {
                        buildAllowance.textContent = payload.isPro
                            ? 'Pro · unlimited downloads'
                            : 'Free · ' + payload.remaining + ' downloads left';
                    }
                }
                downloadBlob(blob, buildFilename(state.personalInformation.fullName));
                setStatus(pdfStatus, 'PDF ready. Your download has started.');
            } catch (error) {
                setStatus(pdfStatus, error && error.message
                    ? error.message
                    : 'The PDF could not be created. Please try again.');
            } finally {
                buttons.forEach(function (button) { button.removeAttribute('aria-busy'); });
                updateValidation();
            }
        }

        function handleAction(button) {
            const action = button.dataset.action;
            const section = button.dataset.section;
            const index = Number(button.dataset.index);
            if (action === 'next-editor-tab') {
                const currentIndex = Math.max(0, EDITOR_TAB_ORDER.indexOf(activeEditorTab));
                const nextTab = EDITOR_TAB_ORDER[currentIndex + 1] || EDITOR_TAB_ORDER[0];
                activateEditorTab(nextTab, true);
                return;
            }
            if (action === 'load-sample') {
                const personal = state.personalInformation || {};
                const hasDraftContent = Object.keys(personal).some(function (key) { return hasText(personal[key]); })
                    || hasText(state.professionalSummary)
                    || state.sections.some(function (descriptor) { return hasSectionContent(state, descriptor.key); });
                if (hasDraftContent && windowObject && !windowObject.confirm('Replace your current resume with the sample? Your saved draft will be overwritten.')) return;
                state = readSample(); touched.clear(); showAllErrors = false; revision += 1; latestBlobRevision = -1; latestPageCount = 0;
                renderAll(); scheduleSave(); schedulePreview(0); setStatus(pdfStatus, 'Sample data loaded.'); return;
            }
            if (action === 'clear') { requestClear(); return; }
            if (action === 'confirm-clear') { confirmClear(); return; }
            if (action === 'cancel-clear') { if (clearDialog && typeof clearDialog.close === 'function') clearDialog.close(); return; }
            if (action === 'download') { downloadPdf(); return; }
            if (action === 'remove-profile-photo') {
                photoRequest += 1;
                state.personalInformation.profilePhotoDataUrl = '';
                if (photoInput) photoInput.value = '';
                changed(false);
                setStatus(photoStatus, 'Portrait removed.');
                return;
            }
            if (action === 'open-template-picker') { openTemplateGallery(); return; }
            if (action === 'exit-template-gallery') { closeTemplateGallery(); return; }
            if (action === 'close-template-preview') { closeTemplatePreview(true); return; }
            if (action === 'previous-gallery-template') {
                selectGalleryTemplate(templateCursor - 1, false);
                return;
            }
            if (action === 'next-gallery-template') {
                selectGalleryTemplate(templateCursor + 1, false);
                return;
            }
            if (action === 'choose-gallery-template') {
                const selectedTemplate = TEMPLATE_CATALOG[templateCursor] || TEMPLATE_CATALOG[0];
                const didChange = selectedTemplate.id !== state.templateId || !state.templateSelectionConfirmed;
                state.templateId = selectedTemplate.id;
                state.templateSelectionConfirmed = true;
                galleryOpen = false;
                closeTemplatePreview(false);
                if (didChange) changed(true);
                else {
                    syncTemplatePicker();
                    renderSectionManager();
                    updateValidation();
                }
                if (templateGallery) templateGallery.hidden = true;
                root.classList.remove('is-choosing-template');
                setStatus(pdfStatus, selectedTemplate.name + ' applied. Your details and PDF preview use this fixed design.');
                continueToResumeData();
                return;
            }
            if (action === 'close-template-picker') { closeTemplateDialog(true); return; }
            if (action === 'previous-template') { moveTemplateCursor(-1); return; }
            if (action === 'next-template') { moveTemplateCursor(1); return; }
            if (action === 'confirm-template') {
                const selectedTemplate = TEMPLATE_CATALOG[templateCursor] || TEMPLATE_CATALOG[0];
                const didChange = selectedTemplate.id !== state.templateId;
                state.templateId = selectedTemplate.id;
                state.templateSelectionConfirmed = true;
                if (didChange) changed(false);
                else syncTemplatePicker();
                setStatus(pdfStatus, selectedTemplate.name + ' template applied. Add your details, then download the finished CV.');
                closeTemplateDialog(false);
                continueToResumeData();
                return;
            }
            if (action === 'toggle-preview-size') {
                const panel = root.querySelector('[data-tab-panel="preview"]');
                const backdrop = root.querySelector('[data-preview-backdrop]');
                const expanded = panel ? !panel.classList.contains('is-expanded') : false;
                if (expanded) expandedPreviewOpener = documentObject.activeElement;
                if (panel) panel.classList.toggle('is-expanded', expanded);
                if (backdrop) backdrop.hidden = !expanded;
                root.classList.toggle('has-expanded-preview', expanded);
                root.querySelectorAll('[data-action="toggle-preview-size"]').forEach(function (control) {
                    control.setAttribute('aria-expanded', String(expanded));
                });
                const label = root.querySelector('[data-preview-size-label]');
                if (label) label.textContent = expanded ? 'Exit full screen' : 'Full screen';
                if (panel) setExpandedPreviewIsolation(panel, expanded);
                if (expanded) {
                    schedulePreview(0);
                    panel?.querySelector('[data-action="toggle-preview-size"]')?.focus();
                } else if (expandedPreviewOpener && typeof expandedPreviewOpener.focus === 'function') {
                    expandedPreviewOpener.focus();
                    expandedPreviewOpener = null;
                }
                return;
            }
            if (action === 'add-custom-section') {
                if (state.customSections.length >= LIMITS.customSections) { notifyLimit('Custom sections have reached their ' + LIMITS.customSections + '-section limit.'); return; }
                state.customSections.push(ENTRY_FACTORIES.customSections()); showSection('customSections'); changed(true); return;
            }
            if (action === 'add-entry' && ENTRY_FACTORIES[section]) {
                const list = state[section];
                const maximum = section === 'customSections' ? LIMITS.customSections : LIMITS.entries;
                if (list.length >= maximum) { notifyLimit('That section has reached its ' + maximum + '-entry limit.'); return; }
                list.push(ENTRY_FACTORIES[section]()); showSection(section); changed(true); return;
            }
            if ((action === 'remove-entry' || action === 'move-entry-up' || action === 'move-entry-down') && Array.isArray(state[section])) {
                if (!Number.isInteger(index) || index < 0 || index >= state[section].length) return;
                if (action === 'remove-entry') state[section].splice(index, 1);
                else moveItem(state[section], index, action.endsWith('up') ? -1 : 1);
                changed(true); return;
            }
            if (action === 'add-bullet') {
                const list = getPath(state, button.dataset.listPath);
                if (!Array.isArray(list)) return;
                if (list.length >= LIMITS.bullets) { notifyLimit('That entry has reached its ' + LIMITS.bullets + '-bullet limit.'); return; }
                list.push(''); changed(true); return;
            }
            if (action === 'remove-bullet') {
                const list = getPath(state, button.dataset.listPath);
                if (Array.isArray(list) && Number.isInteger(index) && index >= 0 && index < list.length) { list.splice(index, 1); changed(true); }
                return;
            }
            if (action === 'add-tag') { addTag(button.closest('.aw-rb-tag-entry').querySelector('[data-tag-input]')); return; }
            if (action === 'remove-tag') {
                const list = getPath(state, button.dataset.listPath);
                if (Array.isArray(list) && Number.isInteger(index) && index >= 0 && index < list.length) { list.splice(index, 1); changed(true); }
                return;
            }
            if (action === 'add-skill-item') {
                const categoryIndex = Number(button.dataset.categoryIndex);
                const category = state.skills[categoryIndex];
                if (!category || category.skills.length >= LIMITS.tags) {
                    notifyLimit('That category has reached its ' + LIMITS.tags + '-skill limit.');
                    return;
                }
                category.skills.push({ id: newId('skill'), name: '', level: null });
                showSection('skills');
                changed(true);
                return;
            }
            if (action === 'remove-skill-item') {
                const categoryIndex = Number(button.dataset.categoryIndex);
                const skillIndex = Number(button.dataset.skillIndex);
                const category = state.skills[categoryIndex];
                if (category && Number.isInteger(skillIndex) && skillIndex >= 0 && skillIndex < category.skills.length) {
                    category.skills.splice(skillIndex, 1);
                    changed(true);
                }
                return;
            }
            if (action === 'move-section-up' || action === 'move-section-down') {
                if (templateMetadata(state.templateId).locked) return;
                if (moveItem(state.sections, index, action.endsWith('up') ? -1 : 1)) changed(true);
                return;
            }
            if (action === 'toggle-section' && state.sections[index]) { state.sections[index].isVisible = !state.sections[index].isVisible; changed(true); return; }
            const sectionIndex = Number(button.dataset.sectionIndex);
            const entryIndex = Number(button.dataset.entryIndex);
            const customSection = state.customSections[sectionIndex];
            if (action === 'add-custom-entry' && customSection) {
                if (customSection.entries.length >= LIMITS.customEntries) { notifyLimit('That custom section has reached its entry limit.'); return; }
                customSection.entries.push(customEntryFactory()); showSection('customSections'); changed(true); return;
            }
            if (action === 'remove-custom-entry' && customSection && customSection.entries[entryIndex]) { customSection.entries.splice(entryIndex, 1); changed(true); return; }
            if ((action === 'move-custom-entry-up' || action === 'move-custom-entry-down') && customSection) {
                if (moveItem(customSection.entries, entryIndex, action.endsWith('up') ? -1 : 1)) changed(true);
            }
        }

        function activateTab(name) {
            root.querySelectorAll('[data-tab]').forEach(function (tab) {
                const active = tab.dataset.tab === name;
                tab.classList.toggle('is-active', active);
                tab.setAttribute('aria-selected', String(active));
                tab.tabIndex = active ? 0 : -1;
            });
            root.querySelectorAll('[data-tab-panel]').forEach(function (panel) {
                const active = panel.dataset.tabPanel === name;
                panel.classList.toggle('is-active', active);
                // CSS shows both columns on desktop; only the compact layout behaves as a true tab switcher.
                panel.setAttribute('aria-hidden', String(isCompactLayout() && !active));
            });
            if (name === 'preview') schedulePreview(0);
        }

        function onLayoutChange() {
            const selected = root.querySelector('[data-tab][aria-selected="true"]');
            const selectedName = selected ? selected.dataset.tab : 'form';
            root.querySelectorAll('[data-tab-panel]').forEach(function (panel) {
                panel.setAttribute('aria-hidden', String(isCompactLayout() && panel.dataset.tabPanel !== selectedName));
            });
        }

        function onInput(event) {
            const control = event.target.closest('[data-field], [data-bind-path]');
            if (!control || !root.contains(control)) return;
            const path = control.dataset.field || control.dataset.bindPath;
            let value = control.type === 'checkbox' ? control.checked : control.value;
            const maximum = control.maxLength > 0 ? control.maxLength : LIMITS.longText;
            if (typeof value === 'string' && value.length > maximum) { value = value.slice(0, maximum); control.value = value; }
            if (!setPath(state, path, value)) return;
            if (path === 'professionalSummary' && summaryCount) summaryCount.textContent = plainRichText(value, LIMITS.summary).length + ' characters - keep the summary to about 3 lines';
            if (control.type === 'checkbox') changed(true);
            else changed(false);
        }

        function onBlur(event) {
            const control = event.target.closest('[data-field], [data-bind-path]');
            if (!control) return;
            touched.add(control.dataset.field || control.dataset.bindPath);
            updateValidation();
        }

        function onClick(event) {
            const templateChoice = event.target.closest('[data-template-choice]');
            if (templateChoice && templateGalleryGrid && templateGalleryGrid.contains(templateChoice)) {
                selectGalleryTemplate(Number(templateChoice.dataset.templateIndex), false);
                openTemplatePreview(templateChoice);
                return;
            }
            const editorTab = event.target.closest('[data-editor-tab]');
            if (editorTab && root.contains(editorTab)) { activateEditorTab(editorTab.dataset.editorTab); return; }
            const tab = event.target.closest('[data-tab]');
            if (tab && root.contains(tab)) { activateTab(tab.dataset.tab); return; }
            const format = event.target.closest('[data-format]');
            if (format && root.contains(format)) { event.preventDefault(); applyTextFormat(format); return; }
            const action = event.target.closest('[data-action]');
            if (action && root.contains(action)) { event.preventDefault(); handleAction(action); }
        }

        function onTemplateDialogCancel(event) {
            event.preventDefault();
            closeTemplateDialog(true);
        }

        function onTemplateDialogClick(event) {
            if (event.target === templateDialog) closeTemplateDialog(true);
        }

        function onTemplatePreviewCancel(event) {
            event.preventDefault();
            closeTemplatePreview(true);
        }

        function onTemplatePreviewClick(event) {
            if (event.target === templatePreviewDialog) closeTemplatePreview(true);
        }

        function onKeyDown(event) {
            const expandedPreview = root.querySelector('.aw-rb-preview-panel.is-expanded');
            if (expandedPreview && event.key === 'Tab') {
                const focusable = Array.from(expandedPreview.querySelectorAll(
                    'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), iframe, [tabindex]:not([tabindex="-1"])'))
                    .filter(function (element) { return !element.hidden; });
                if (focusable.length) {
                    const first = focusable[0];
                    const last = focusable[focusable.length - 1];
                    if (event.shiftKey && documentObject.activeElement === first) {
                        event.preventDefault();
                        last.focus();
                    } else if (!event.shiftKey && documentObject.activeElement === last) {
                        event.preventDefault();
                        first.focus();
                    }
                }
            }
            if (event.key === 'Escape') {
                const backdrop = root.querySelector('[data-preview-backdrop]');
                if (expandedPreview && backdrop) {
                    event.preventDefault();
                    handleAction(backdrop);
                    return;
                }
            }
            const galleryChoice = event.target.closest('[data-template-choice]');
            if (galleryChoice && templateGalleryGrid && templateGalleryGrid.contains(galleryChoice)
                && ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'Enter', ' '].includes(event.key)) {
                event.preventDefault();
                if (event.key === 'Enter' || event.key === ' ') {
                    selectGalleryTemplate(Number(galleryChoice.dataset.templateIndex), false);
                    openTemplatePreview(galleryChoice);
                    return;
                }
                let nextIndex = Number(galleryChoice.dataset.templateIndex);
                const renderedColumns = windowObject && typeof windowObject.getComputedStyle === 'function'
                    ? windowObject.getComputedStyle(templateGalleryGrid).gridTemplateColumns.split(/\s+/).filter(Boolean).length
                    : 1;
                if (event.key === 'Home') nextIndex = 0;
                else if (event.key === 'End') nextIndex = TEMPLATE_CATALOG.length - 1;
                else if (event.key === 'ArrowLeft') nextIndex -= 1;
                else if (event.key === 'ArrowRight') nextIndex += 1;
                else if (event.key === 'ArrowUp') nextIndex -= Math.max(1, renderedColumns);
                else if (event.key === 'ArrowDown') nextIndex += Math.max(1, renderedColumns);
                selectGalleryTemplate(nextIndex, true);
                return;
            }
            if (templateDialog && templateDialog.hasAttribute('open') && templateDialog.contains(event.target)
                && (event.key === 'ArrowLeft' || event.key === 'ArrowRight' || event.key === 'Home' || event.key === 'End')) {
                event.preventDefault();
                if (event.key === 'Home') templateCursor = 0;
                else if (event.key === 'End') templateCursor = TEMPLATE_CATALOG.length - 1;
                else moveTemplateCursor(event.key === 'ArrowRight' ? 1 : -1);
                renderTemplateDialog();
                return;
            }
            const tagInput = event.target.closest('[data-tag-input]');
            if (tagInput && (event.key === 'Enter' || event.key === ',')) { event.preventDefault(); addTag(tagInput); return; }
            const editorTab = event.target.closest('[data-editor-tab]');
            if (editorTab && (event.key === 'ArrowLeft' || event.key === 'ArrowRight' || event.key === 'Home' || event.key === 'End')) {
                event.preventDefault();
                const tabs = Array.from(root.querySelectorAll('[data-editor-tab]'));
                const current = tabs.indexOf(editorTab);
                let nextIndex = current;
                if (event.key === 'Home') nextIndex = 0;
                else if (event.key === 'End') nextIndex = tabs.length - 1;
                else nextIndex = (current + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
                activateEditorTab(tabs[nextIndex].dataset.editorTab, true);
                return;
            }
            const tab = event.target.closest('[data-tab]');
            if (tab && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
                event.preventDefault();
                const tabs = Array.from(root.querySelectorAll('[data-tab]'));
                const current = tabs.indexOf(tab);
                const next = tabs[(current + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length];
                activateTab(next.dataset.tab); next.focus();
            }
        }

        state = loadInitialState();
        renderAll();
        root.addEventListener('input', onInput);
        root.addEventListener('change', onInput);
        root.addEventListener('focusout', onBlur);
        root.addEventListener('click', onClick);
        root.addEventListener('keydown', onKeyDown);
        if (photoInput) photoInput.addEventListener('change', onPhotoChange);
        if (form) form.addEventListener('submit', function (event) { event.preventDefault(); downloadPdf(); });
        if (clearDialog) clearDialog.addEventListener('cancel', function (event) { event.preventDefault(); clearDialog.close(); });
        if (templateDialog) {
            templateDialog.addEventListener('cancel', onTemplateDialogCancel);
            templateDialog.addEventListener('click', onTemplateDialogClick);
        }
        if (templatePreviewDialog) {
            templatePreviewDialog.addEventListener('cancel', onTemplatePreviewCancel);
            templatePreviewDialog.addEventListener('click', onTemplatePreviewClick);
        }
        if (windowObject) {
            windowObject.addEventListener('beforeunload', saveNow);
            windowObject.addEventListener('pagehide', onPageHide);
            windowObject.addEventListener('pageshow', onPageShow);
            windowObject.addEventListener('resize', onLayoutChange);
            windowObject.addEventListener('storage', onStorage);
        }
        activateTab((root.querySelector('[data-tab].is-active') || {}).dataset ? root.querySelector('[data-tab].is-active').dataset.tab : 'form');
        scheduleInitialSectionFocus();
        schedulePreview(0);

        return {
            getState: function () { return normalizeState(state); },
            setState: function (value) { state = normalizeState(value); touched.clear(); showAllErrors = false; revision += 1; latestBlobRevision = -1; latestPageCount = 0; renderAll(); scheduleSave(); schedulePreview(0); },
            saveNow: saveNow,
            refreshPreview: refreshPreview,
            downloadPdf: downloadPdf,
            destroy: function () {
                saveNow();
                destroyed = true;
                windowObject.clearTimeout(saveTimer); windowObject.clearTimeout(previewTimer);
                windowObject.clearTimeout(initialFocusTimer); windowObject.clearTimeout(targetHighlightTimer);
                revokePreviewUrl();
                galleryGeneration += 1;
                if (galleryPreviewObserver) galleryPreviewObserver.disconnect();
                revokeGalleryUrls();
                root.removeEventListener('input', onInput); root.removeEventListener('change', onInput);
                root.removeEventListener('focusout', onBlur); root.removeEventListener('click', onClick); root.removeEventListener('keydown', onKeyDown);
                if (photoInput) photoInput.removeEventListener('change', onPhotoChange);
                if (templateDialog) {
                    templateDialog.removeEventListener('cancel', onTemplateDialogCancel);
                    templateDialog.removeEventListener('click', onTemplateDialogClick);
                }
                if (templatePreviewDialog) {
                    templatePreviewDialog.removeEventListener('cancel', onTemplatePreviewCancel);
                    templatePreviewDialog.removeEventListener('click', onTemplatePreviewClick);
                }
                windowObject.removeEventListener('beforeunload', saveNow);
                windowObject.removeEventListener('pagehide', onPageHide);
                windowObject.removeEventListener('pageshow', onPageShow);
                windowObject.removeEventListener('resize', onLayoutChange);
                windowObject.removeEventListener('storage', onStorage);
                delete root.dataset.resumeBuilderInitialized;
            }
        };
    }

    function autoInitialize() {
        if (typeof document === 'undefined') return [];
        return Array.from(document.querySelectorAll('[data-resume-builder]')).map(function (root) { return createBuilder(root); }).filter(Boolean);
    }

    return Object.freeze({
        SCHEMA_VERSION: SCHEMA_VERSION,
        LIMITS: LIMITS,
        ONE_PAGE_RECOMMENDATIONS: ONE_PAGE_RECOMMENDATIONS,
        DEFAULT_TEMPLATE_ID: DEFAULT_TEMPLATE_ID,
        TEMPLATE_CATALOG: TEMPLATE_CATALOG,
        EDITOR_TAB_GROUPS: EDITOR_TAB_GROUPS,
        SECTION_DEFAULTS: SECTION_DEFAULTS,
        normalizeBuilderSection: normalizeBuilderSection,
        builderSectionSelector: builderSectionSelector,
        editorTabForSection: editorTabForSection,
        normalizeTemplateId: normalizeTemplateId,
        normalizeProfilePhotoDataUrl: normalizeProfilePhotoDataUrl,
        normalizeLevel: normalizeLevel,
        createEmptyState: createEmptyState,
        createSampleState: createSampleState,
        normalizeState: normalizeState,
        parseDraft: parseDraft,
        validateState: validateState,
        isEmail: isEmail,
        isPhone: isPhone,
        isHttpUrl: isHttpUrl,
        formatMonth: formatMonth,
        formatDateRange: formatDateRange,
        hasSectionContent: hasSectionContent,
        richTextRuns: richTextRuns,
        plainRichText: plainRichText,
        onePageGuidance: onePageGuidance,
        largestContentSections: largestContentSections,
        countPdfPages: countPdfPages,
        buildFilename: buildFilename,
        buildDocumentDefinition: buildDocumentDefinition,
        createBuilder: createBuilder,
        autoInitialize: autoInitialize
    });
}));
