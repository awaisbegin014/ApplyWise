'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const builder = require(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/js/resume-builder.js'));
const NEW_TEMPLATE_IDS = Object.freeze([
    'evergreen-professional', 'monochrome-timeline', 'mint-horizon',
    'amber-academic', 'classic-latex', 'graphite-impact', 'midnight-executive'
]);
const TEMPLATE_IDS = Object.freeze([
    'classic', 'emerald', 'modern', 'executive', 'compact', 'minimal', 'corporate', 'timeline', 'studio',
    ...NEW_TEMPLATE_IDS
]);
const PHOTO_TEMPLATE_IDS = Object.freeze([
    'emerald', 'modern', 'executive', 'compact', 'studio',
    'evergreen-professional', 'monochrome-timeline', 'mint-horizon', 'graphite-impact', 'midnight-executive'
]);
const EDITOR_TAB_IDS = Object.freeze(['personal', 'summary', 'experience', 'education', 'skills', 'projects', 'more', 'arrange']);
const PROFILE_PHOTO_DATA_URL = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=';

function walk(value, visitor) {
    if (Array.isArray(value)) {
        value.forEach((item) => walk(item, visitor));
        return;
    }
    if (!value || typeof value !== 'object') return;
    visitor(value);
    Object.values(value).forEach((item) => walk(item, visitor));
}

function allText(value) {
    const values = [];
    walk(value, (node) => {
        if (typeof node.text === 'string') values.push(node.text);
        else if (Array.isArray(node.text)) {
            node.text.forEach((part) => {
                if (typeof part === 'string') values.push(part);
                else if (part && typeof part.text === 'string') values.push(part.text);
            });
        }
        if (Array.isArray(node.ul)) values.push(...node.ul.filter((item) => typeof item === 'string'));
    });
    return values.join('\n');
}

function links(value) {
    const values = [];
    walk(value, (node) => {
        if (typeof node.link === 'string') values.push(node.link);
    });
    return values;
}

function images(value) {
    const values = [];
    walk(value, (node) => {
        if (typeof node.image === 'string') values.push(node.image);
    });
    return values;
}

test('empty state implements the complete versioned server contract', () => {
    const state = builder.createEmptyState();
    assert.equal(builder.SCHEMA_VERSION, 5);
    assert.equal(state.schemaVersion, builder.SCHEMA_VERSION);
    assert.equal(state.templateSelectionConfirmed, false);
    assert.deepEqual(Object.keys(state.personalInformation), [
        'fullName', 'professionalTitle', 'phoneNumber', 'emailAddress', 'location',
        'linkedInUrl', 'gitHubUrl', 'portfolioUrl', 'profilePhotoDataUrl'
    ]);
    assert.deepEqual(state.sections.map((section) => section.key), [
        'professionalSummary', 'education', 'skills', 'experience', 'projects',
        'achievementsAndCertifications', 'languages', 'volunteerExperience', 'references', 'interests', 'customSections'
    ]);
    assert.equal(state.sections.length, 11);
    assert.deepEqual(state.references, []);
    assert.ok(state.sections.every((section) => typeof section.title === 'string' && typeof section.isVisible === 'boolean'));
});

test('schema v5 migrates v3 skill strings and language data without inventing ratings', () => {
    const parsed = builder.parseDraft(JSON.stringify({
        schemaVersion: 3,
        templateId: 'modern',
        skills: [{ id: 'frontend', name: 'Frontend', skills: ['React', 'TypeScript', 'react'] }],
        languages: [{ id: 'english', name: 'English', proficiency: 'Fluent' }]
    }));

    assert.equal(parsed.recovered, false);
    assert.equal(parsed.state.schemaVersion, 5);
    assert.equal(parsed.state.templateSelectionConfirmed, true);
    assert.deepEqual(parsed.state.skills[0].skills, [
        { id: 'skill-1-1', name: 'React', level: null },
        { id: 'skill-1-2', name: 'TypeScript', level: null }
    ]);
    assert.equal(parsed.state.languages[0].level, null);
    assert.equal(builder.normalizeLevel(0), null);
    assert.equal(builder.normalizeLevel(6), null);
    assert.equal(builder.normalizeLevel('4'), 4);
});

test('normalization keeps exact nested field names and drops unknown data', () => {
    const normalized = builder.normalizeState({
        schemaVersion: 999,
        personalInformation: { fullName: '  Ada Lovelace  ', emailAddress: 'ada@example.com', profilePhotoDataUrl: PROFILE_PHOTO_DATA_URL, extra: 'drop' },
        education: [{ Id: 'wrong-case', institutionName: 'Analytical Academy', descriptionOrCoursework: 'Math', isCurrentlyStudying: true, evil: 'drop' }],
        experience: [{ companyName: 'Engine Works', technologySkills: 'C++, Mathematics', bulletPoints: ['Designed systems'], isCurrentlyWorking: true }],
        projects: [{ projectName: 'Engine', technologiesUsed: ['Math', 'math', 'Logic'], repositoryUrl: 'https://example.com/repo', isOngoing: true }],
        achievementsAndCertifications: [{ title: 'Fellow', issuingOrganization: 'Society' }],
        volunteerExperience: [{ organizationName: 'Community', isCurrentlyVolunteering: true }],
        references: [{ fullName: 'Charles Babbage', jobTitle: 'Inventor', company: 'Difference Engine', emailAddress: 'charles@example.com', phoneNumber: '+44 20 7946 0958', evil: 'drop' }],
        customSections: [{ title: 'Publications', entries: [{ heading: 'Notes', isCurrent: true, bulletPoints: ['An article'] }] }],
        unknownRoot: 'drop'
    });

    assert.equal(normalized.schemaVersion, builder.SCHEMA_VERSION);
    assert.equal(normalized.personalInformation.fullName, 'Ada Lovelace');
    assert.equal(normalized.education[0].descriptionOrCoursework, 'Math');
    assert.equal(normalized.education[0].isCurrentlyStudying, true);
    assert.equal(normalized.experience[0].isCurrentlyWorking, true);
    assert.equal(normalized.experience[0].technologySkills, 'C++, Mathematics');
    assert.deepEqual(normalized.projects[0].technologiesUsed, ['Math', 'Logic']);
    assert.equal(normalized.achievementsAndCertifications[0].issuingOrganization, 'Society');
    assert.equal(normalized.volunteerExperience[0].organizationName, 'Community');
    assert.equal(normalized.references[0].fullName, 'Charles Babbage');
    assert.equal(normalized.references[0].company, 'Difference Engine');
    assert.equal(normalized.personalInformation.profilePhotoDataUrl, PROFILE_PHOTO_DATA_URL);
    assert.equal(normalized.customSections[0].entries[0].heading, 'Notes');
    assert.equal('unknownRoot' in normalized, false);
    assert.equal('extra' in normalized.personalInformation, false);
    assert.equal('evil' in normalized.education[0], false);
    assert.equal('evil' in normalized.references[0], false);
});

test('profile photo normalization accepts bounded PDF-safe images and rejects unsafe data', () => {
    const jpeg = 'data:image/jpeg;base64,/9j/4AAQSkZJRgABAQ==';
    assert.equal(builder.normalizeProfilePhotoDataUrl(PROFILE_PHOTO_DATA_URL), PROFILE_PHOTO_DATA_URL);
    assert.equal(builder.normalizeProfilePhotoDataUrl(`  ${jpeg}  `), jpeg);

    [
        'https://example.com/photo.png',
        'data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=',
        'data:image/webp;base64,UklGRg==',
        'data:text/html;base64,PGgxPk5vPC9oMT4=',
        'data:image/png;base64,not valid base64',
        `data:image/png;base64,${'A'.repeat(builder.LIMITS.photoDataUrl + 4)}`
    ].forEach((value) => assert.equal(builder.normalizeProfilePhotoDataUrl(value), ''));

    const normalized = builder.normalizeState({ personalInformation: { profilePhotoDataUrl: PROFILE_PHOTO_DATA_URL } });
    assert.equal(normalized.personalInformation.profilePhotoDataUrl, PROFILE_PHOTO_DATA_URL);
    assert.equal(builder.normalizeState({ personalInformation: { profilePhotoDataUrl: 'javascript:alert(1)' } }).personalInformation.profilePhotoDataUrl, '');
});

test('normalization removes control characters and applies defensive caps', () => {
    const tooMany = Array.from({ length: builder.LIMITS.entries + 8 }, (_, index) => ({ institutionName: `School ${index}` }));
    const state = builder.normalizeState({
        personalInformation: { fullName: `A\u0000da${'x'.repeat(500)}` },
        professionalSummary: 's'.repeat(builder.LIMITS.summary + 100),
        education: tooMany,
        interests: Array.from({ length: builder.LIMITS.tags + 10 }, (_, index) => `Interest ${index}`)
    });
    assert.equal(state.personalInformation.fullName.includes('\u0000'), false);
    assert.equal(state.personalInformation.fullName.length, builder.LIMITS.name);
    assert.equal(state.professionalSummary.length, builder.LIMITS.summary);
    assert.equal(state.education.length, builder.LIMITS.entries);
    assert.equal(state.interests.length, builder.LIMITS.tags);
});

test('draft parsing recovers from malformed, oversized, and non-object data', () => {
    assert.equal(builder.parseDraft('{broken').recovered, true);
    assert.equal(builder.parseDraft('[]').reason, 'invalid-shape');
    assert.equal(builder.parseDraft('x'.repeat(builder.LIMITS.draftBytes + 1)).reason, 'too-large');
    const empty = builder.parseDraft('');
    assert.equal(empty.recovered, false);
    assert.equal(empty.reason, 'empty');

    const valid = builder.parseDraft(JSON.stringify({ personalInformation: { fullName: 'Grace Hopper' } }));
    assert.equal(valid.recovered, false);
    assert.equal(valid.state.personalInformation.fullName, 'Grace Hopper');

    const versionTwo = builder.parseDraft(JSON.stringify({
        schemaVersion: 2,
        templateId: 'modern',
        personalInformation: { fullName: 'Legacy User' }
    }));
    assert.equal(versionTwo.recovered, false);
    assert.equal(versionTwo.state.schemaVersion, builder.SCHEMA_VERSION);
    assert.equal(versionTwo.state.templateId, 'modern');
    assert.equal(versionTwo.state.templateSelectionConfirmed, true);
    assert.equal(versionTwo.state.personalInformation.profilePhotoDataUrl, '');
    assert.deepEqual(versionTwo.state.references, []);
});

test('builder deep-link sections normalize to fixed selectors and reject selector input', () => {
    const aliases = {
        personalInformation: 'personalInformation',
        professionalSummary: 'professionalSummary',
        experience: 'experience',
        projects: 'projects',
        education: 'education',
        skills: 'skills',
        references: 'references',
        languages: 'languages',
        volunteerExperience: 'volunteerExperience',
        interests: 'interests',
        customSections: 'customSections',
        achievements: 'achievementsAndCertifications',
        certifications: 'achievementsAndCertifications',
        achievementsAndCertifications: 'achievementsAndCertifications'
    };
    Object.entries(aliases).forEach(([requested, expected]) => {
        assert.equal(builder.normalizeBuilderSection(requested), expected);
        assert.equal(builder.builderSectionSelector(requested), '[data-editor-section="' + expected + '"]');
    });
    assert.equal(builder.normalizeBuilderSection(' CERTIFICATIONS '), 'achievementsAndCertifications');
    assert.equal(builder.normalizeBuilderSection('experience.other'), '');
    assert.equal(builder.builderSectionSelector('experience"] [data-action="clear'), '');
    assert.equal(builder.builderSectionSelector('a'.repeat(65)), '');
});

test('editor sections map to the eight focused top tabs', () => {
    assert.deepEqual(Object.keys(builder.EDITOR_TAB_GROUPS), EDITOR_TAB_IDS);
    const expectedTabs = {
        personalInformation: 'personal',
        professionalSummary: 'summary',
        experience: 'experience',
        education: 'education',
        skills: 'skills',
        projects: 'projects',
        achievementsAndCertifications: 'more',
        languages: 'more',
        volunteerExperience: 'more',
        references: 'more',
        interests: 'more',
        customSections: 'more',
        arrange: 'arrange'
    };
    Object.entries(expectedTabs).forEach(([section, tab]) => {
        assert.equal(builder.editorTabForSection(section), tab, `${section} opens the ${tab} tab`);
    });
    assert.equal(builder.editorTabForSection('experience.other'), 'personal');
});

test('builder view exposes normalized section hooks and deep-link highlighting', () => {
    const view = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/Views/ResumeBuilder/Index.cshtml'), 'utf8');
    assert.match(view, /data-initial-section="@Model\.InitialSection"/);
    assert.match(view, /data-editor-section="personalInformation"/);
    assert.match(view, /data-editor-section="professionalSummary"/);

    const stylesheet = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/css/resume-builder.css'), 'utf8');
    assert.match(stylesheet, /\[data-editor-section\]\.is-deep-link-target/);

    const script = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/js/resume-builder.js'), 'utf8');
    assert.match(script, /classList\.add\('is-deep-link-target'\)/);
    assert.match(script, /focus\(\{ preventScroll: true \}\)/);
    assert.match(script, /scrollIntoView\(\{ behavior:/);
});

test('email, phone, and URL helpers reject unsafe or malformed values', () => {
    assert.equal(builder.isEmail('engineer@example.com'), true);
    assert.equal(builder.isEmail('not-an-email'), false);
    assert.equal(builder.isEmail('a@b.com/foo'), false);
    assert.equal(builder.isEmail('a@example..com'), false);
    assert.equal(builder.isEmail('a..b@example.com'), false);
    assert.equal(builder.isPhone('+1 (202) 555-0147'), true);
    assert.equal(builder.isPhone('call-me-maybe'), false);
    assert.equal(builder.isHttpUrl('https://example.com/path?q=1'), true);
    assert.equal(builder.isHttpUrl('http://localhost:5000/path'), true);
    assert.equal(builder.isHttpUrl('javascript:alert(1)'), false);
    assert.equal(builder.isHttpUrl('https://user:password@example.com'), false);
    assert.equal(builder.isHttpUrl('//example.com'), false);
});

test('validation enforces required personal fields and all supported formats', () => {
    const emptyErrors = builder.validateState(builder.createEmptyState());
    assert.equal(emptyErrors['personalInformation.fullName'], 'Full name is required.');
    assert.equal(emptyErrors['personalInformation.professionalTitle'], 'Professional title is required.');
    assert.equal(emptyErrors['personalInformation.emailAddress'], 'Email address is required.');

    const state = builder.createSampleState();
    state.personalInformation.emailAddress = 'wrong';
    state.personalInformation.phoneNumber = 'abc';
    state.personalInformation.portfolioUrl = 'javascript:alert(1)';
    state.projects[0].projectUrl = 'ftp://example.com/file';
    state.projects[0].repositoryUrl = 'https://user:pass@example.com';
    state.achievementsAndCertifications[0].credentialUrl = 'relative/path';
    state.references[0].emailAddress = 'wrong';
    state.references[0].phoneNumber = 'call-me';
    const errors = builder.validateState(state);
    assert.ok(errors['personalInformation.emailAddress']);
    assert.ok(errors['personalInformation.phoneNumber']);
    assert.ok(errors['personalInformation.portfolioUrl']);
    assert.ok(errors['projects.0.projectUrl']);
    assert.ok(errors['projects.0.repositoryUrl']);
    assert.ok(errors['achievementsAndCertifications.0.credentialUrl']);
    assert.ok(errors['references.0.emailAddress']);
    assert.ok(errors['references.0.phoneNumber']);
});

test('date validation catches reversed ranges but ignores end dates for current roles', () => {
    const state = builder.createSampleState();
    state.education[0].startDate = '2027-01';
    state.education[0].endDate = '2026-12';
    state.experience[0].startDate = 'bad';
    let errors = builder.validateState(state);
    assert.ok(errors['education.0.endDate']);
    assert.ok(errors['experience.0.startDate']);

    state.education[0].isCurrentlyStudying = true;
    state.education[0].endDate = 'not-a-month';
    errors = builder.validateState(state);
    assert.equal(errors['education.0.endDate'], undefined);
    assert.equal(builder.formatMonth('2026-07'), 'Jul 2026');
    assert.equal(builder.formatDateRange('2024-01', '', true), 'Jan 2024 - Present');
});

test('date and URL validation includes nested custom entries', () => {
    const state = builder.createSampleState();
    state.customSections = [{
        id: 'custom', title: 'Publications', entries: [{
            id: 'entry', heading: 'Article', startDate: '2026-02', endDate: '2025-01',
            isCurrent: false, url: 'data:text/html,test', bulletPoints: []
        }]
    }];
    const errors = builder.validateState(state);
    assert.ok(errors['customSections.0.entries.0.endDate']);
    assert.ok(errors['customSections.0.entries.0.url']);
});

test('download filename uses first and last name and blocks path characters', () => {
    assert.equal(builder.buildFilename('Ada Byron Lovelace'), 'Ada_Lovelace_Resume.pdf');
    assert.equal(builder.buildFilename('  René Descartes  '), 'Rene_Descartes_Resume.pdf');
    assert.equal(builder.buildFilename('../../evil\\name'), 'evil_name_Resume.pdf');
    assert.equal(builder.buildFilename(''), 'ApplyWise_Resume.pdf');
    assert.equal(builder.buildFilename('Single'), 'Single_Resume.pdf');
});

test('restricted rich-text markers produce safe selectable PDF runs', () => {
    const source = '[b]Fast [i]and reliable[/i][/b] [u]delivery[/u] <script>alert(1)</script>';
    const runs = builder.richTextRuns(source);
    assert.equal(builder.plainRichText(source), 'Fast and reliable delivery <script>alert(1)</script>');
    assert.ok(runs.some((run) => run.text === 'Fast ' && run.bold === true));
    assert.ok(runs.some((run) => run.text === 'and reliable' && run.bold === true && run.italics === true));
    assert.ok(runs.some((run) => run.text === 'delivery' && run.decoration === 'underline'));

    const state = builder.createSampleState();
    state.professionalSummary = source;
    const output = allText(builder.buildDocumentDefinition(state));
    assert.equal(output.includes('[b]'), false);
    assert.ok(output.includes('<script>alert(1)</script>'));
});

test('one-page guidance tracks the recommended content limits', () => {
    const state = builder.createSampleState();
    const withinGuide = builder.onePageGuidance(state);
    assert.equal(withinGuide.metrics.education.value, 2);
    assert.equal(withinGuide.metrics.experience.value, 3);
    assert.equal(withinGuide.metrics.projects.value, 3);
    assert.equal(withinGuide.metrics.achievementsAndCertifications.value, 3);
    assert.deepEqual(withinGuide.exceeded, []);

    state.sections.find((section) => section.key === 'projects').isVisible = false;
    assert.equal(builder.onePageGuidance(state).metrics.projects.value, 0);
    state.sections.find((section) => section.key === 'projects').isVisible = true;

    state.projects.push({ ...state.projects[0], id: 'fourth-project' });
    state.professionalSummary = 'Long summary sentence. '.repeat(30);
    const overGuide = builder.onePageGuidance(state);
    assert.ok(overGuide.exceeded.includes('projects'));
    assert.ok(overGuide.exceeded.includes('professionalSummary'));
});

test('template catalog exposes sixteen professional immutable choices in deterministic gallery order', () => {
    assert.equal(builder.DEFAULT_TEMPLATE_ID, 'classic');
    assert.equal(Object.isFrozen(builder.TEMPLATE_CATALOG), true);
    assert.deepEqual(builder.TEMPLATE_CATALOG.map((template) => template.id), TEMPLATE_IDS);
    assert.equal(new Set(builder.TEMPLATE_CATALOG.map((template) => template.id)).size, TEMPLATE_IDS.length);
    builder.TEMPLATE_CATALOG.forEach((template) => {
        assert.equal(Object.isFrozen(template), true);
        ['name', 'font', 'description', 'accent', 'layout'].forEach((property) => {
            assert.equal(typeof template[property], 'string');
            assert.ok(template[property].trim().length > 0, `${template.id} has ${property} metadata`);
        });
        assert.equal(typeof template.hasPhoto, 'boolean');
    });
    assert.equal(new Set(builder.TEMPLATE_CATALOG.map((template) => template.layout)).size, TEMPLATE_IDS.length);
    assert.deepEqual(builder.TEMPLATE_CATALOG.filter((template) => template.hasPhoto).map((template) => template.id), PHOTO_TEMPLATE_IDS);
    const newTemplates = builder.TEMPLATE_CATALOG.filter((template) => NEW_TEMPLATE_IDS.includes(template.id));
    assert.equal(newTemplates.length, 7);
    assert.ok(newTemplates.every((template) => template.locked === true));
    assert.deepEqual(newTemplates.map((template) => template.classification), [
        'Balanced', 'ATS-optimized', 'Balanced', 'ATS-optimized', 'ATS-optimized', 'Visual', 'Visual'
    ]);
    assert.deepEqual(newTemplates.filter((template) => template.photoRequired).map((template) => template.id), [
        'evergreen-professional', 'monochrome-timeline', 'mint-horizon', 'graphite-impact', 'midnight-executive'
    ]);
});

test('template selection is normalized and persists with the local draft', () => {
    assert.equal(builder.createEmptyState().templateId, builder.DEFAULT_TEMPLATE_ID);
    assert.equal(builder.normalizeTemplateId(undefined), builder.DEFAULT_TEMPLATE_ID);
    assert.equal(builder.normalizeTemplateId('unknown-template'), builder.DEFAULT_TEMPLATE_ID);
    assert.equal(builder.normalizeTemplateId('../modern'), builder.DEFAULT_TEMPLATE_ID);

    TEMPLATE_IDS.forEach((templateId) => {
        assert.equal(builder.normalizeTemplateId(templateId), templateId);
        const state = builder.normalizeState({ templateId, personalInformation: { profilePhotoDataUrl: PROFILE_PHOTO_DATA_URL } });
        assert.equal(state.templateId, templateId);
        assert.equal(state.personalInformation.profilePhotoDataUrl, PROFILE_PHOTO_DATA_URL);

        const parsed = builder.parseDraft(JSON.stringify(state));
        assert.equal(parsed.recovered, false);
        assert.equal(parsed.state.templateId, templateId);
        assert.equal(parsed.state.personalInformation.profilePhotoDataUrl, PROFILE_PHOTO_DATA_URL);
    });

    const legacyDraft = builder.parseDraft(JSON.stringify({
        personalInformation: { fullName: 'Legacy User' }
    }));
    assert.equal(legacyDraft.state.templateId, builder.DEFAULT_TEMPLATE_ID);
    assert.equal(legacyDraft.state.schemaVersion, builder.SCHEMA_VERSION);
    assert.equal(legacyDraft.state.personalInformation.profilePhotoDataUrl, '');
});

test('builder view offers a gallery-first picker, eight editor tabs, and conditional portrait controls', () => {
    const view = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/Views/ResumeBuilder/Index.cshtml'), 'utf8');
    const stylesheet = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/css/resume-builder.css'), 'utf8');
    const script = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/js/resume-builder.js'), 'utf8');
    const buttons = view.match(/<button\b[^>]*>/g) || [];
    const actionButton = (action) => buttons.find((tag) => tag.includes(`data-action="${action}"`));
    const editorTabButtons = (view.match(/<button\b[^>]*>/g) || []).filter((tag) => tag.includes('data-editor-tab='));
    const photoInput = (view.match(/<input\b[^>]*>/g) || []).find((tag) => tag.includes('data-profile-photo-input'));
    const removePhotoButton = (view.match(/<button\b[^>]*>/g) || []).find((tag) => tag.includes('data-action="remove-profile-photo"'));

    assert.match(view, /data-template-gallery/);
    assert.doesNotMatch(view, /aw-rb-mode-switch|data-action="focus-editor"/);
    assert.match(view, /data-builder-progress/);
    assert.match(view, /data-builder-progress-bar/);
    assert.match(view, /data-builder-next-step/);
    assert.match(view, /data-action="next-editor-tab"/);
    assert.match(script, /function syncGuidedProgress/);
    assert.match(view, /data-template-gallery-grid[^>]*role="listbox"/);
    assert.match(view, /data-template-preview-dialog/);
    assert.match(view, /data-template-gallery-large-preview/);
    assert.match(view, /data-gallery-photo-url="@Url\.Content\("~\/images\/wiso\.jpg"\)"/);
    assert.match(view, /Preparing real PDF previews/);
    assert.doesNotMatch(view, /data-action="select-template"/);
    const openTemplatePicker = actionButton('open-template-picker');
    assert.ok(openTemplatePicker, 'view exposes the template picker trigger');
    assert.match(openTemplatePicker, /type="button"/);
    assert.match(openTemplatePicker, /aria-controls="rb-template-gallery"/);
    ['exit-template-gallery', 'close-template-preview', 'previous-gallery-template', 'next-gallery-template', 'choose-gallery-template'].forEach((action) => {
        const button = actionButton(action);
        assert.ok(button, `template gallery exposes ${action}`);
        assert.match(button, /type="button"/);
    });
    ['name', 'description', 'classification'].forEach((hook) => {
        assert.match(view, new RegExp(`data-gallery-${hook}(?:\\s|=|>)`), `template gallery exposes ${hook} content`);
    });
    assert.doesNotMatch(view, /aw-rb-template-gallery-detail/);
    assert.match(script, /function observeGalleryPdfPreviews/);
    assert.match(script, /function loadGalleryPhotoDataUrl/);
    assert.match(script, /canvas\.toDataURL\('image\/jpeg'/);
    assert.match(script, /function openTemplatePreview/);
    assert.match(script, /galleryPdfUrl\(candidate\.id\)/);
    assert.match(script, /state\.templateSelectionConfirmed = true/);
    assert.match(script, /copy\.appendChild\(domElement\(documentObject, 'strong'/);
    assert.doesNotMatch(script, /copy\.appendChild\(domElement\(documentObject, 'span'/);

    assert.match(view, /data-editor-tabs[^>]*role="tablist"/);
    assert.equal(editorTabButtons.length, EDITOR_TAB_IDS.length);
    EDITOR_TAB_IDS.forEach((tabId) => {
        const button = editorTabButtons.find((tag) => tag.includes(`data-editor-tab="${tabId}"`));
        assert.ok(button, `view exposes the ${tabId} editor tab`);
        assert.match(button, /type="button"/);
        assert.match(button, /role="tab"/);
        assert.match(button, /aria-selected="(?:true|false)"/);
    });

    assert.match(view, /data-photo-field[^>]*hidden/);
    assert.ok(photoInput, 'view exposes the profile photo file input');
    assert.match(photoInput, /type="file"/);
    assert.match(photoInput, /accept="image\/png,image\/jpeg,image\/webp"/);
    assert.doesNotMatch(photoInput, /data-field=/);
    assert.ok(removePhotoButton, 'view exposes a profile photo removal action');
    assert.match(view, /data-photo-preview/);
    assert.match(stylesheet, /\[data-photo-field\]\[hidden\]/);
    assert.match(script, /querySelector\('\[data-profile-photo-input\]'\)/);

    assert.doesNotMatch(script, /READINESS_DEBOUNCE_MS|evaluateDraftReadiness|data-readiness/);
    assert.doesNotMatch(stylesheet, /\.aw-rb-readiness/);
    assert.equal(builder.READINESS_DEBOUNCE_MS, undefined);
    assert.equal(builder.evaluateDraftReadiness, undefined);
    assert.match(view, /data-one-page-guide/);
});

test('templates produce distinct selectable-text document definitions at their intended page size', () => {
    const definitions = builder.TEMPLATE_CATALOG.map((template) => {
        const state = builder.createSampleState();
        state.templateId = template.id;
        const before = JSON.stringify(state);
        const documentDefinition = builder.buildDocumentDefinition(state);

        assert.equal(JSON.stringify(state), before, `${template.id} rendering does not mutate the draft`);
        assert.equal(documentDefinition.pageSize, template.id === 'classic-latex' ? 'LETTER' : 'A4');
        assert.equal(documentDefinition.defaultStyle.font, template.id === 'midnight-executive' ? 'Poppins' : template.font);
        assert.ok(Array.isArray(documentDefinition.content));
        assert.equal(Boolean(documentDefinition.footer), template.id === 'midnight-executive');
        assert.equal(JSON.stringify(documentDefinition).includes('image'), false);
        assert.ok(JSON.stringify(documentDefinition).includes(template.accent), `${template.id} applies its accent`);
        const output = allText(documentDefinition);
        assert.match(output, /Jordan Lee/);
        assert.match(output, /Northbridge University/);
        assert.match(output, /CodeSprint - Northbridge University/);
        assert.match(output, /Campus Collaboration Hub/);
        return documentDefinition;
    });

    assert.equal(new Set(definitions.map((definition) => JSON.stringify(definition))).size, TEMPLATE_IDS.length);
    assert.ok(new Set(definitions.map((definition) => definition.defaultStyle.font)).size >= 2);

    const classicLatexState = builder.createSampleState();
    classicLatexState.templateId = 'classic-latex';
    const classicLatex = builder.buildDocumentDefinition(classicLatexState);
    const classicLatexText = allText(classicLatex);
    assert.equal(classicLatex.defaultStyle.font, 'CMU Serif');
    assert.deepEqual(classicLatex.pageMargins, [32.4, 14.5, 32.4, 22]);
    assert.match(classicLatexText, /TECHNICAL SKILLS/i);
    assert.match(classicLatexText, /HONORS AND AWARDS/i);
    assert.match(classicLatexText, /Technology\/Skills:/);

    const classicState = builder.createSampleState();
    classicState.templateId = 'classic';
    const classic = builder.buildDocumentDefinition(classicState);
    assert.equal(classic.defaultStyle.font, 'Roboto');
    assert.deepEqual(classic.pageMargins, [30, 22, 30, 22]);

    const invalidState = builder.createSampleState();
    invalidState.templateId = 'not-a-template';
    assert.equal(JSON.stringify(builder.buildDocumentDefinition(invalidState)), JSON.stringify(classic));
});

test('portrait templates render a safe profile image while photo-free templates preserve it only in state', () => {
    builder.TEMPLATE_CATALOG.forEach((template) => {
        const state = builder.createSampleState();
        state.templateId = template.id;
        state.personalInformation.profilePhotoDataUrl = PROFILE_PHOTO_DATA_URL;
        const before = JSON.stringify(state);
        const documentDefinition = builder.buildDocumentDefinition(state);

        assert.equal(JSON.stringify(state), before, `${template.id} photo rendering does not mutate the draft`);
        assert.deepEqual(images(documentDefinition), template.hasPhoto ? [PROFILE_PHOTO_DATA_URL] : []);
        assert.match(allText(documentDefinition), /Jordan Lee/);
    });

    const classicState = builder.normalizeState({
        templateId: 'classic',
        personalInformation: { profilePhotoDataUrl: PROFILE_PHOTO_DATA_URL }
    });
    assert.equal(classicState.personalInformation.profilePhotoDataUrl, PROFILE_PHOTO_DATA_URL);
    assert.deepEqual(images(builder.buildDocumentDefinition(classicState)), []);
});

test('photo and proficiency requirements are template-specific and block invalid locked drafts', () => {
    const evergreen = builder.createSampleState();
    evergreen.templateId = 'evergreen-professional';
    evergreen.templateSelectionConfirmed = true;
    evergreen.sections.find((section) => section.key === 'languages').isVisible = true;
    evergreen.personalInformation.profilePhotoDataUrl = '';
    evergreen.skills[0].skills[0].level = null;
    evergreen.languages[0].level = null;
    const evergreenErrors = builder.validateState(evergreen);
    assert.match(evergreenErrors['personalInformation.profilePhotoDataUrl'], /requires/i);
    assert.match(evergreenErrors['skills.0.skills.0.level'], /1-5/);
    assert.match(evergreenErrors['languages.0.level'], /1-5/);

    const amber = builder.createSampleState();
    amber.templateId = 'amber-academic';
    amber.templateSelectionConfirmed = true;
    amber.skills[0].skills[0].level = null;
    assert.equal(builder.validateState(amber)['personalInformation.profilePhotoDataUrl'], undefined);
    assert.equal(builder.validateState(amber)['skills.0.skills.0.level'], undefined);

    const blank = builder.createEmptyState();
    assert.match(builder.validateState(blank).templateSelectionConfirmed, /choose/i);
});

test('PDF omits placeholder identity and suppresses hidden or empty sections', () => {
    const empty = builder.createEmptyState();
    const emptyText = allText(builder.buildDocumentDefinition(empty));
    assert.equal(emptyText.includes('Your Name'), false);
    assert.equal(emptyText.includes('Professional Title'), false);
    assert.equal(emptyText.includes('Education'), false);

    const state = builder.createSampleState();
    state.sections.find((section) => section.key === 'projects').isVisible = false;
    const output = allText(builder.buildDocumentDefinition(state));
    assert.equal(output.includes('Projects'), false);
    assert.equal(output.includes(state.projects[0].projectName), false);
    assert.ok(output.includes('Product-minded software engineer'));
    assert.match(output, /profile summary/i);
});

test('PDF respects user section ordering within a professional template region', () => {
    const state = builder.createSampleState();
    const projects = state.sections.find((section) => section.key === 'projects');
    const experience = state.sections.find((section) => section.key === 'experience');
    state.sections = [projects, experience, ...state.sections.filter((section) => section !== projects && section !== experience)];
    const output = allText(builder.buildDocumentDefinition(state));
    assert.ok(output.indexOf(state.projects[0].projectName) < output.indexOf(state.experience[0].companyName));
});

test('fidelity-locked templates preserve their documented section order', () => {
    NEW_TEMPLATE_IDS.forEach((templateId) => {
        const state = builder.createSampleState();
        state.templateId = templateId;
        const projects = state.sections.find((section) => section.key === 'projects');
        const experience = state.sections.find((section) => section.key === 'experience');
        state.sections = [projects, experience, ...state.sections.filter((section) => section !== projects && section !== experience)];

        const output = allText(builder.buildDocumentDefinition(state));
        assert.ok(
            output.indexOf(state.experience[0].companyName) < output.indexOf(state.projects[0].projectName),
            `${templateId} ignores free-form ordering and keeps experience before projects`
        );
    });
});

test('fidelity-locked templates render every supported optional section in fallback regions', () => {
    NEW_TEMPLATE_IDS.forEach((templateId) => {
        const state = builder.createSampleState();
        state.templateId = templateId;
        state.sections.forEach((section) => { section.isVisible = true; });
        state.volunteerExperience = [{
            id: 'volunteer-marker',
            organizationName: 'Community Systems Guild',
            role: 'Volunteer Mentor',
            location: 'Remote',
            startDate: '2025-01',
            endDate: '',
            isCurrentlyVolunteering: true,
            description: 'Mentored first-generation technologists.',
            bulletPoints: []
        }];
        state.languages = [{
            id: 'language-marker',
            name: 'Urdu',
            proficiency: 'Native',
            level: 5
        }];
        state.interests = ['Accessible product design'];
        state.customSections = [{
            id: 'custom-marker',
            title: 'Selected Writing',
            entries: [{
                id: 'custom-entry-marker',
                heading: 'Systems That Welcome Everyone',
                subheading: 'ApplyWise Journal',
                startDate: '2025-03',
                endDate: '',
                isCurrent: false,
                url: 'https://example.com/writing',
                bulletPoints: ['Explores inclusive career tooling.']
            }]
        }];

        const output = allText(builder.buildDocumentDefinition(state));
        [
            'Community Systems Guild',
            'Urdu',
            'Accessible product design',
            'SELECTED WRITING',
            'Systems That Welcome Everyone',
            'Priya Shah'
        ].forEach((marker) => assert.ok(output.toLowerCase().includes(marker.toLowerCase()), `${templateId} renders ${marker}`));
    });
});

test('PDF links are sanitized and contact details remain visibly clickable', () => {
    const state = builder.createSampleState();
    state.personalInformation.phoneNumber = '+1 (202) 555-0147';
    state.personalInformation.location = 'Seattle, WA';
    state.projects[0].projectUrl = 'javascript:alert(1)';
    const documentDefinition = builder.buildDocumentDefinition(state);
    const documentLinks = links(documentDefinition);
    assert.ok(documentLinks.includes('tel:+12025550147'));
    assert.ok(documentLinks.includes('mailto:jordan.lee@example.com'));
    assert.equal(documentLinks.some((link) => link.startsWith('javascript:')), false);
    let linkedPhoneNode = null;
    walk(documentDefinition, (node) => { if (node.link === 'tel:+12025550147') linkedPhoneNode = node; });
    assert.ok(linkedPhoneNode);
    assert.equal(typeof linkedPhoneNode.color, 'string');
    assert.ok(linkedPhoneNode.color.length > 0);
    assert.equal(linkedPhoneNode.decoration, 'underline');
});

test('PDF renders optional references with safe clickable contact details', () => {
    const state = builder.createSampleState();
    state.sections.find((section) => section.key === 'references').isVisible = true;
    const documentDefinition = builder.buildDocumentDefinition(state);
    const output = allText(documentDefinition);
    const documentLinks = links(documentDefinition);

    assert.ok(output.includes('REFERENCES'));
    assert.ok(output.includes('Priya Shah'));
    assert.ok(output.includes('Engineering Manager - Example Labs'));
    assert.ok(documentLinks.includes('mailto:priya.shah@example.com'));
    assert.ok(documentLinks.includes('tel:+12025550179'));
});

test('PDF renders nested custom sections and suppresses blank custom sections', () => {
    const state = builder.createSampleState();
    state.customSections = [
        { id: 'blank', title: 'Blank section', entries: [] },
        { id: 'publications', title: 'Publications', entries: [{ id: 'paper', heading: 'Reliable Systems', subheading: 'Author', startDate: '2025-01', isCurrent: true, url: 'https://example.com/paper', bulletPoints: ['Explains resilient design.'] }] }
    ];
    const customDescriptor = state.sections.find((section) => section.key === 'customSections');
    customDescriptor.isVisible = true;
    const documentDefinition = builder.buildDocumentDefinition(state);
    const output = allText(documentDefinition);
    assert.doesNotMatch(output, /blank section/i);
    assert.match(output, /publications/i);
    assert.ok(output.includes('Reliable Systems'));
    assert.ok(links(documentDefinition).includes('https://example.com/paper'));
});

test('all generated PDF separator strings use ASCII hyphens', () => {
    const state = builder.createSampleState();
    state.languages[0].proficiency = 'Native';
    const output = allText(builder.buildDocumentDefinition(state));
    assert.equal(/[–—]/.test(output), false);
    assert.match(output, /Jan 2025 - Present/);
});

test('preview and download reuse the same unchanged PDF blob and mobile gallery rules are present', () => {
    const script = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/js/resume-builder.js'), 'utf8');
    const stylesheet = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/css/resume-builder.css'), 'utf8');

    assert.match(script, /latestBlob && latestBlobRevision === revision/);
    assert.match(script, /downloadBlob\(blob,\s*buildFilename/);
    assert.match(script, /previewFrame\.src = currentPreviewUrl \+ '#view=FitH&navpanes=0'/);
    assert.match(stylesheet, /@media\s*\(max-width:\s*760px\)/);
    assert.match(stylesheet, /\.aw-rb-template-preview-dialog\s*\{[\s\S]*width:\s*calc\(100vw - 8px\)/);
    assert.match(stylesheet, /\.aw-rb-template-preview-body\s*\{[\s\S]*display:\s*block/);
    assert.match(stylesheet, /\.aw-rb-template-gallery-grid\s*\{[\s\S]*grid-template-columns:\s*repeat\(2,\s*minmax\(0,\s*1fr\)\)/);
});

test('vendored pdfmake creates one-page text and portrait PDFs for every template and detects overflow', async () => {
    const pdfMake = require(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/lib/pdfmake/pdfmake.min.js'));
    require(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/lib/pdfmake/vfs_fonts.js'));
    require(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/lib/pdfmake/poppins_vfs.js'));
    require(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/lib/pdfmake/libre_baskerville_vfs.js'));
    require(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/lib/pdfmake/cmu_serif_vfs.js'));

    for (const templateId of TEMPLATE_IDS) {
        const sample = builder.createSampleState();
        sample.templateId = templateId;
        const blob = await pdfMake.createPdf(builder.buildDocumentDefinition(sample)).getBlob();
        assert.equal(blob.type, 'application/pdf');
        assert.ok(blob.size > 1000);
        const signature = Buffer.from(await blob.slice(0, 5).arrayBuffer()).toString('ascii');
        assert.equal(signature, '%PDF-');
        assert.equal(await builder.countPdfPages(blob), 1, `${templateId} sample fits one page`);
    }

    for (const templateId of PHOTO_TEMPLATE_IDS) {
        const sample = builder.createSampleState();
        sample.templateId = templateId;
        sample.personalInformation.profilePhotoDataUrl = PROFILE_PHOTO_DATA_URL;
        const blob = await pdfMake.createPdf(builder.buildDocumentDefinition(sample)).getBlob();
        assert.equal(blob.type, 'application/pdf');
        assert.ok(blob.size > 1000);
        const signature = Buffer.from(await blob.slice(0, 5).arrayBuffer()).toString('ascii');
        assert.equal(signature, '%PDF-');
        assert.equal(await builder.countPdfPages(blob), 1, `${templateId} portrait sample fits one page`);
    }

    const sample = builder.createSampleState();
    sample.templateId = 'classic';
    sample.experience = Array.from({ length: 14 }, (_, index) => ({
        ...sample.experience[0],
        id: `overflow-${index}`,
        jobTitle: `Overflow role ${index + 1}`,
        bulletPoints: Array(4).fill('Delivered a substantial production improvement with measurable impact across a cross-functional team.')
    }));
    const overflowBlob = await pdfMake.createPdf(builder.buildDocumentDefinition(sample)).getBlob();
    assert.ok(await builder.countPdfPages(overflowBlob) > 1);
});
