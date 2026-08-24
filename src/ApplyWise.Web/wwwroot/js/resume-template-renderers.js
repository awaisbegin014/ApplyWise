/*
 * ApplyWise fidelity-locked resume templates.
 *
 * This module owns the metadata, fixed section maps, and deterministic
 * document-definition builders for the ApplyWise-original designs.
 * The resume-builder module supplies its shared PDF primitives so the public
 * ApplyWiseResumeBuilder API remains backwards compatible.
 */
(function (globalObject, factory) {
    'use strict';

    const api = factory();
    if (typeof module === 'object' && module.exports) {
        module.exports = api;
        return;
    }

    globalObject.ApplyWiseResumeTemplates = api;
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';

    const CATALOG = Object.freeze([
        Object.freeze({
            id: 'evergreen-professional',
            name: 'Evergreen Professional',
            font: 'Poppins',
            description: 'A balanced deep-green portrait resume with a focused career column.',
            accent: '#0f5b4c',
            layout: 'evergreen-professional',
            hasPhoto: true,
            photoRequired: true,
            locked: true,
            classification: 'Balanced',
            requiresSkillLevels: true,
            requiresLanguageLevels: true,
            contentBudget: 'Best with 2-3 roles, 2 education entries, and 8 rated skills.'
        }),
        Object.freeze({
            id: 'monochrome-timeline',
            name: 'Monochrome Timeline',
            font: 'Roboto',
            description: 'An ATS-focused black-and-white timeline with a compact details rail.',
            accent: '#151515',
            layout: 'monochrome-timeline',
            hasPhoto: true,
            photoRequired: true,
            locked: true,
            classification: 'ATS-optimized',
            requiresSkillLevels: false,
            requiresLanguageLevels: false,
            contentBudget: 'Best with 3 roles, 2 education entries, and concise descriptions.'
        }),
        Object.freeze({
            id: 'mint-horizon',
            name: 'Mint Horizon',
            font: 'Poppins',
            description: 'A fresh mint identity banner with proficiency rails and clear content hierarchy.',
            accent: '#45e2ad',
            layout: 'mint-horizon',
            hasPhoto: true,
            photoRequired: true,
            locked: true,
            classification: 'Balanced',
            requiresSkillLevels: true,
            requiresLanguageLevels: true,
            contentBudget: 'Best with 2-3 roles and up to 10 rated skills.'
        }),
        Object.freeze({
            id: 'amber-academic',
            name: 'Amber Academic',
            font: 'Roboto',
            description: 'A photo-free academic resume with warm headings and a compact credentials column.',
            accent: '#d89a4c',
            layout: 'amber-academic',
            hasPhoto: false,
            photoRequired: false,
            locked: true,
            classification: 'ATS-optimized',
            requiresSkillLevels: false,
            requiresLanguageLevels: false,
            contentBudget: 'Best with detailed education, 2-3 roles, and a short skills list.'
        }),
        Object.freeze({
            id: 'classic-latex',
            name: 'Classic LaTeX',
            font: 'CMU Serif',
            description: 'A faithful Computer Modern single-column resume with compact rules and academic typography.',
            accent: '#111111',
            layout: 'classic-latex-letter',
            hasPhoto: false,
            photoRequired: false,
            locked: true,
            classification: 'ATS-optimized',
            requiresSkillLevels: false,
            requiresLanguageLevels: false,
            contentBudget: 'Best with a two-line summary, 2 education entries, 3 roles, 3 projects, and 3 honors.'
        }),
        Object.freeze({
            id: 'graphite-impact',
            name: 'Graphite Impact',
            font: 'Poppins',
            description: 'A high-impact visual resume with bold labels and a structured expertise panel.',
            accent: '#242424',
            layout: 'graphite-impact',
            hasPhoto: true,
            photoRequired: true,
            locked: true,
            classification: 'Visual',
            atsCaution: 'Use the ATS-focused templates when a job portal warns against columns or graphics.',
            requiresSkillLevels: true,
            requiresLanguageLevels: false,
            contentBudget: 'Best with 2 roles, 2 education entries, and 6-8 rated skills.'
        }),
        Object.freeze({
            id: 'midnight-executive',
            name: 'Midnight Executive',
            font: 'Poppins + Libre Baskerville',
            description: 'A dark executive statement with a refined serif identity and restrained page frame.',
            accent: '#d8c7a3',
            layout: 'midnight-executive',
            hasPhoto: true,
            photoRequired: true,
            locked: true,
            classification: 'Visual',
            atsCaution: 'Best for direct sharing; use an ATS-focused template for strict application portals.',
            requiresSkillLevels: false,
            requiresLanguageLevels: false,
            contentBudget: 'Best with a short profile, 2 roles, and tightly edited supporting sections.'
        })
    ]);

    const IDS = Object.freeze(CATALOG.map(function (template) { return template.id; }));
    const ID_SET = new Set(IDS);
    const ALL_SECTION_KEYS = Object.freeze([
        'professionalSummary',
        'experience',
        'education',
        'skills',
        'projects',
        'achievementsAndCertifications',
        'languages',
        'volunteerExperience',
        'references',
        'interests',
        'customSections'
    ]);
    const SECTION_ORDERS = Object.freeze({
        'evergreen-professional': Object.freeze(['professionalSummary', 'experience', 'projects', 'education', 'achievementsAndCertifications', 'volunteerExperience', 'references', 'skills', 'languages', 'interests', 'customSections']),
        'monochrome-timeline': Object.freeze(['professionalSummary', 'experience', 'education', 'projects', 'achievementsAndCertifications', 'volunteerExperience', 'skills', 'languages', 'references', 'interests', 'customSections']),
        'mint-horizon': Object.freeze(['professionalSummary', 'experience', 'projects', 'education', 'achievementsAndCertifications', 'volunteerExperience', 'references', 'skills', 'languages', 'interests', 'customSections']),
        'amber-academic': Object.freeze(['professionalSummary', 'experience', 'education', 'projects', 'achievementsAndCertifications', 'volunteerExperience', 'customSections', 'skills', 'languages', 'references', 'interests']),
        'classic-latex': Object.freeze(['professionalSummary', 'education', 'skills', 'experience', 'projects', 'achievementsAndCertifications', 'volunteerExperience', 'languages', 'references', 'interests', 'customSections']),
        'graphite-impact': Object.freeze(['professionalSummary', 'experience', 'projects', 'education', 'volunteerExperience', 'achievementsAndCertifications', 'customSections', 'skills', 'references', 'languages', 'interests']),
        'midnight-executive': Object.freeze(['professionalSummary', 'skills', 'experience', 'projects', 'education', 'achievementsAndCertifications', 'volunteerExperience', 'languages', 'references', 'interests', 'customSections'])
    });

    const PROFILES = Object.freeze({
        'evergreen-professional': Object.freeze({
            id: 'evergreen-professional', font: 'Poppins', pageMargins: [16, 16, 16, 16], fontSize: 6.85, lineHeight: 1.015,
            textColor: '#24332f', mutedColor: '#66746f', accent: '#0f5b4c', nameColor: '#ffffff', nameSize: 18.5,
            titleSize: 7.8, headingSize: 8.1, nameSpacing: 0.2, headingSpacing: 0.5, headingType: 'compact',
            sectionTitles: Object.freeze({ professionalSummary: 'Profile', experience: 'Employment History', skills: 'Skills', achievementsAndCertifications: 'Achievements & Certifications' })
        }),
        'monochrome-timeline': Object.freeze({
            id: 'monochrome-timeline', font: 'Roboto', pageMargins: [24, 21, 24, 21], fontSize: 7, lineHeight: 1.02,
            textColor: '#1d1d1d', mutedColor: '#606060', accent: '#151515', nameColor: '#111111', nameSize: 22,
            titleSize: 7.7, headingSize: 7.9, nameSpacing: 1.2, headingSpacing: 1.2, headingType: 'compact', uppercaseHeadings: true,
            sectionTitles: Object.freeze({ professionalSummary: 'Profile', experience: 'Work Experience', skills: 'Skills', achievementsAndCertifications: 'Recognition' })
        }),
        'mint-horizon': Object.freeze({
            id: 'mint-horizon', font: 'Poppins', pageMargins: [17, 17, 17, 17], fontSize: 6.85, lineHeight: 1.015,
            textColor: '#26332f', mutedColor: '#61716b', accent: '#179f75', nameColor: '#102f27', nameSize: 19,
            titleSize: 8, headingSize: 8.1, nameSpacing: 0.1, headingSpacing: 0.4, headingType: 'compact',
            sectionTitles: Object.freeze({ professionalSummary: 'Profile', experience: 'Employment History', skills: 'Skills', achievementsAndCertifications: 'Credentials' })
        }),
        'amber-academic': Object.freeze({
            id: 'amber-academic', font: 'Roboto', pageMargins: [27, 24, 27, 24], fontSize: 7.2, lineHeight: 1.035,
            textColor: '#363636', mutedColor: '#6f6f6f', accent: '#d89a4c', nameColor: '#d18b38', nameSize: 22,
            titleSize: 8, headingSize: 9, nameSpacing: 0.15, headingSpacing: 0.3, headingType: 'compact',
            sectionTitles: Object.freeze({ professionalSummary: 'Profile', experience: 'Employment History', skills: 'Skills', achievementsAndCertifications: 'Achievements & Certifications' })
        }),
        'classic-latex': Object.freeze({
            id: 'classic-latex', font: 'CMU Serif', pageMargins: [32.4, 14.5, 32.4, 22], fontSize: 8.97, lineHeight: 1.08,
            textColor: '#111111', mutedColor: '#111111', accent: '#111111', nameColor: '#111111', nameSize: 24.8,
            titleSize: 8.97, headingSize: 11.96, nameSpacing: 0, headingSpacing: 0, headingType: 'rule',
            sectionTitles: Object.freeze({ professionalSummary: 'Summary', experience: 'Experience', skills: 'Technical Skills', achievementsAndCertifications: 'Honors and Awards' })
        }),
        'graphite-impact': Object.freeze({
            id: 'graphite-impact', font: 'Poppins', pageMargins: [17, 17, 17, 17], fontSize: 6.75, lineHeight: 1.01,
            textColor: '#242424', mutedColor: '#5f5f5f', accent: '#242424', nameColor: '#1d1d1d', nameSize: 25,
            titleSize: 8.3, headingSize: 7.8, nameSpacing: -0.25, headingSpacing: 0.35, headingType: 'bar', uppercaseHeadings: true,
            sectionTitles: Object.freeze({ professionalSummary: 'Profile', experience: 'Employment History', skills: 'Skills', achievementsAndCertifications: 'Credentials' })
        }),
        'midnight-executive': Object.freeze({
            id: 'midnight-executive', font: 'Poppins', pageMargins: [25, 24, 25, 28], fontSize: 6.7, lineHeight: 1.025,
            textColor: '#f3efe7', mutedColor: '#c8c1b7', accent: '#d8c7a3', nameColor: '#ffffff', nameSize: 28,
            titleSize: 7.7, headingSize: 7.8, nameSpacing: 0.35, headingSpacing: 1.1, headingType: 'compact', uppercaseHeadings: true,
            sectionTitles: Object.freeze({ professionalSummary: 'Profile', experience: 'Employment History', skills: 'Skills', achievementsAndCertifications: 'Recognition' })
        })
    });

    function isTemplate(value) {
        return ID_SET.has(value);
    }

    function metadata(value) {
        return CATALOG.find(function (template) { return template.id === value; }) || null;
    }

    function sectionVisible(state, key) {
        const descriptor = state.sections.find(function (section) { return section.key === key; });
        return Boolean(descriptor && descriptor.isVisible);
    }

    function filledSkillItems(state) {
        const result = [];
        state.skills.forEach(function (category) {
            category.skills.forEach(function (skill) {
                if (skill.name) result.push({ category: category.name, name: skill.name, level: skill.level });
            });
        });
        return result;
    }

    function barNode(level, color, background, width) {
        const value = Math.max(0, Math.min(5, Number(level) || 0));
        const totalWidth = width || 64;
        const filled = Math.max(value ? 3 : 0, totalWidth * value / 5);
        return {
            table: {
                widths: [filled, Math.max(0.1, totalWidth - filled)],
                heights: [3],
                body: [[
                    { text: '', fillColor: color },
                    { text: '', fillColor: background || '#d8dfdc' }
                ]]
            },
            layout: {
                hLineWidth: function () { return 0; },
                vLineWidth: function () { return 0; },
                paddingLeft: function () { return 0; },
                paddingRight: function () { return 0; },
                paddingTop: function () { return 0; },
                paddingBottom: function () { return 0; }
            },
            margin: [0, 1, 0, 2]
        };
    }

    function ratedSkillNodes(state, tools, options) {
        if (!sectionVisible(state, 'skills')) return [];
        const settings = options || {};
        const nodes = [];
        state.skills.forEach(function (category) {
            const skills = category.skills.filter(function (skill) { return Boolean(skill.name); });
            if (!skills.length) return;
            if (category.name) nodes.push(tools.pdfText(category.name.toLocaleUpperCase(), {
                bold: true,
                color: settings.headingColor || settings.color,
                fontSize: settings.categorySize || 6.3,
                characterSpacing: 0.45,
                margin: [0, nodes.length ? 2 : 0, 0, 1]
            }));
            skills.forEach(function (skill) {
                const label = settings.showNumeric && skill.level ? skill.name + '  ' + skill.level + '/5' : skill.name;
                nodes.push(tools.pdfText(label, { color: settings.color, fontSize: settings.fontSize || 6.6, margin: [0, 0, 0, 0] }));
                if (settings.showBars && skill.level) {
                    nodes.push(barNode(skill.level, settings.barColor || settings.color, settings.trackColor, settings.barWidth));
                }
            });
        });
        return nodes;
    }

    function ratedLanguageNodes(state, tools, options) {
        if (!sectionVisible(state, 'languages')) return [];
        const settings = options || {};
        const nodes = [];
        state.languages.filter(function (language) { return Boolean(language.name || language.proficiency); }).forEach(function (language) {
            nodes.push(tools.pdfText(language.name || language.proficiency, {
                color: settings.color,
                bold: true,
                fontSize: settings.fontSize || 6.6,
                margin: [0, 0, 0, 0]
            }));
            if (language.proficiency) {
                nodes.push(tools.pdfText(language.proficiency, { color: settings.mutedColor || settings.color, fontSize: (settings.fontSize || 6.6) - 0.5, margin: [0, 0, 0, 0] }));
            }
            if (settings.showBars && language.level) {
                nodes.push(barNode(language.level, settings.barColor || settings.color, settings.trackColor, settings.barWidth));
            }
        });
        return nodes;
    }

    function appendRatedSection(target, title, nodes, profile, tools) {
        if (!nodes.length) return;
        target.push(tools.sectionHeading(title, profile));
        Array.prototype.push.apply(target, nodes);
    }

    function fixedTable(widths, cells, options) {
        const settings = options || {};
        return {
            table: { widths: widths, body: [cells] },
            layout: {
                hLineWidth: function () { return 0; },
                vLineWidth: function (index) { return settings.divider && index === 1 ? settings.dividerWidth || 0.5 : 0; },
                vLineColor: function () { return settings.dividerColor || '#d1d5db'; },
                paddingLeft: function () { return 0; },
                paddingRight: function () { return 0; },
                paddingTop: function () { return 0; },
                paddingBottom: function () { return 0; }
            },
            margin: settings.outerMargin || [0, 0, 0, 0]
        };
    }

    function recolorForDark(value) {
        if (Array.isArray(value)) {
            value.forEach(recolorForDark);
            return;
        }
        if (!value || typeof value !== 'object') return;
        const replacements = {
            '#0000EE': '#9fc5ff',
            '#111827': '#f3efe7',
            '#333333': '#c8c1b7',
            '#475569': '#c8c1b7',
            '#64748b': '#b8b1a7'
        };
        if (typeof value.color === 'string' && replacements[value.color]) value.color = replacements[value.color];
        Object.keys(value).forEach(function (key) { recolorForDark(value[key]); });
    }

    function buildEvergreen(state, profile, tools) {
        const personal = state.personalInformation;
        const sidebarProfile = tools.templateProfile(profile, { accent: '#dff2eb', headingType: 'compact', mutedColor: '#cce2da', headingSize: 7.5 });
        const mainProfile = tools.templateProfile(profile, { accent: '#0f5b4c', ruleWidth: 354 });
        const sidebar = [];
        const photo = tools.profilePhotoNode(personal, 66, [0, 0, 0, 6]);
        if (photo) sidebar.push(photo);
        Array.prototype.push.apply(sidebar, tools.identityStack(personal, 'center', '#ffffff'));
        sidebar.push({ canvas: [{ type: 'line', x1: 11, y1: 0, x2: 112, y2: 0, lineWidth: 0.5, lineColor: '#7da99b' }], margin: [0, 6, 0, 5] });
        Array.prototype.push.apply(sidebar, tools.contactSection(personal, sidebarProfile, '#e7f7f1'));
        appendRatedSection(sidebar, 'Skills', ratedSkillNodes(state, tools, {
            color: '#ffffff', headingColor: '#e7f7f1', showBars: true, barColor: '#ffffff', trackColor: '#5f9382', barWidth: 92
        }), sidebarProfile, tools);
        appendRatedSection(sidebar, 'Languages', ratedLanguageNodes(state, tools, {
            color: '#ffffff', mutedColor: '#d3e8e1', showBars: true, barColor: '#ffffff', trackColor: '#5f9382', barWidth: 92
        }), sidebarProfile, tools);
        Array.prototype.push.apply(sidebar, tools.sectionContentFor(state, sidebarProfile, ['interests']));
        const main = tools.remainingSectionContent(state, mainProfile, ['skills', 'languages', 'interests']);
        return tools.createDefinition(state, profile, [
            tools.twoCellResume(137, sidebar, main, {
                fillColor: '#0f5b4c',
                color: '#ffffff',
                margin: [10, 10, 9, 10],
                mainMargin: [16, 7, 8, 7]
            })
        ]);
    }

    function buildMonochrome(state, profile, tools) {
        const personal = state.personalInformation;
        const detailsProfile = tools.templateProfile(profile, { accent: '#151515', headingType: 'compact', headingSize: 7.2 });
        const bodyProfile = tools.templateProfile(profile, { accent: '#151515', ruleWidth: 346 });
        const header = [];
        const photo = tools.profilePhotoNode(personal, 51, [0, 0, 0, 4]);
        if (photo) header.push(photo);
        Array.prototype.push.apply(header, tools.identityStack(personal, 'center', '#111111'));
        header.push(tools.contactLine(personal, 'center', '#333333'));
        const details = tools.contactSection(personal, detailsProfile, '#222222');
        Array.prototype.push.apply(details, tools.sectionContentFor(state, detailsProfile, ['skills', 'languages', 'references', 'interests']));
        const main = tools.remainingSectionContent(state, bodyProfile, ['skills', 'languages', 'references', 'interests']);
        const body = fixedTable([131, '*'], [
            { stack: details, margin: [0, 2, 13, 0] },
            { stack: main, margin: [15, 2, 0, 0] }
        ], { divider: true, dividerColor: '#b8b8b8', dividerWidth: 0.55 });
        return tools.createDefinition(state, profile, [
            { stack: header, margin: [0, 0, 0, 7] },
            body
        ]);
    }

    function buildMint(state, profile, tools) {
        const personal = state.personalInformation;
        const sidebarProfile = tools.templateProfile(profile, { accent: '#179f75', headingType: 'compact', headingSize: 7.7 });
        const mainProfile = tools.templateProfile(profile, { accent: '#179f75', ruleWidth: 350 });
        const photo = tools.profilePhotoNode(personal, 72, [0, 0, 0, 0]);
        const identity = tools.identityStack(personal, 'left', '#102f27');
        identity.push(tools.contactLine(personal, 'left', '#236c59'));
        const header = fixedTable(photo ? [92, '*'] : ['*'], photo ? [
            { stack: [photo], margin: [0, 0, 10, 0] },
            { stack: identity, fillColor: '#45e2ad', margin: [17, 15, 12, 13] }
        ] : [
            { stack: identity, fillColor: '#45e2ad', margin: [17, 15, 12, 13] }
        ], { outerMargin: [0, 0, 0, 8] });
        const sidebar = [];
        appendRatedSection(sidebar, 'Skills', ratedSkillNodes(state, tools, {
            color: '#243b34', headingColor: '#179f75', showBars: true, barColor: '#243b34', trackColor: '#c8d4d0', barWidth: 103
        }), sidebarProfile, tools);
        appendRatedSection(sidebar, 'Languages', ratedLanguageNodes(state, tools, {
            color: '#243b34', mutedColor: '#61716b', showBars: true, barColor: '#179f75', trackColor: '#c8d4d0', barWidth: 103
        }), sidebarProfile, tools);
        Array.prototype.push.apply(sidebar, tools.sectionContentFor(state, sidebarProfile, ['interests']));
        const main = tools.remainingSectionContent(state, mainProfile, ['skills', 'languages', 'interests']);
        return tools.createDefinition(state, profile, [
            header,
            tools.twoCellResume(135, sidebar, main, {
                fillColor: '#f6faf8',
                color: '#26332f',
                margin: [4, 1, 13, 6],
                mainMargin: [16, 1, 5, 6]
            })
        ]);
    }

    function buildAmber(state, profile, tools) {
        const personal = state.personalInformation;
        const sidebarKeys = ['skills', 'languages', 'references', 'interests'];
        const sidebarProfile = tools.templateProfile(profile, { accent: '#d89a4c', headingType: 'compact', headingSize: 7.8 });
        const mainProfile = tools.templateProfile(profile, { accent: '#d89a4c', ruleWidth: 368 });
        const header = tools.identityStack(personal, 'left', '#d18b38');
        const main = tools.remainingSectionContent(state, mainProfile, sidebarKeys);
        const sidebar = tools.contactSection(personal, sidebarProfile, '#575757');
        Array.prototype.push.apply(sidebar, tools.sectionContentFor(state, sidebarProfile, sidebarKeys));
        return tools.createDefinition(state, profile, [
            { stack: header, margin: [0, 0, 0, 7] },
            tools.twoCellResumeRight(137, main, sidebar, {
                fillColor: '#fffdf9',
                color: '#363636',
                margin: [10, 2, 0, 6],
                mainMargin: [0, 2, 17, 6]
            })
        ]);
    }

    function classicSmallCaps(value, largeSize, smallSize) {
        return [{
            text: String(value || ''),
            fontSize: largeSize,
            fontFeatures: ['smcp'],
            characterSpacing: smallSize < largeSize ? 0.06 : 0
        }];
    }

    function classicHeading(title) {
        return {
            stack: [
                { text: classicSmallCaps(title, 11.96, 9.35), lineHeight: 1, margin: [0, 0, 0, 1.25] },
                { canvas: [{ type: 'line', x1: 0, y1: 0, x2: 547.2, y2: 0, lineWidth: 0.45, lineColor: '#111111' }] }
            ],
            margin: [0, 7.2, 0, 3.4]
        };
    }

    function classicYear(value) {
        const match = /^(\d{4})/.exec(String(value || ''));
        return match ? match[1] : '';
    }

    function classicMonth(value) {
        const match = /^(\d{4})-(\d{2})$/.exec(String(value || ''));
        if (!match) return classicYear(value);
        const months = ['Jan', 'Feb', 'March', 'April', 'May', 'June', 'July', 'Aug', 'Sept', 'Oct', 'Nov', 'Dec'];
        const month = months[Number(match[2]) - 1];
        return month ? month + ' ' + match[1] : match[1];
    }

    function classicRange(entry, currentKey, yearsOnly) {
        const formatter = yearsOnly ? classicYear : classicMonth;
        const start = formatter(entry.startDate);
        const end = entry[currentKey] ? 'Present' : formatter(entry.endDate);
        return [start, end].filter(Boolean).join(' - ');
    }

    function classicHeadingRow(left, right, indent) {
        const columns = [{ width: '*', text: left || '' }];
        if (right) columns.push({ width: 'auto', text: right, alignment: 'right', noWrap: true });
        return { columns: columns, columnGap: 8, margin: [indent || 10.8, 0, 10.8, 0] };
    }

    function classicBullets(values, tools) {
        const nodes = (values || []).filter(Boolean).map(function (value) {
            return {
                columns: [
                    { width: 10, text: '-', alignment: 'right' },
                    tools.pdfRichText(value, { width: '*' })
                ],
                columnGap: 5,
                margin: [12, 0, 0, 0]
            };
        });
        return nodes.length ? { stack: nodes, margin: [0, 0.2, 0, 0.7] } : null;
    }

    function classicContactLine(personal, tools) {
        const runs = [];
        function add(value) {
            if (!value) return;
            if (runs.length) runs.push({ text: ' | ', color: '#111111' });
            runs.push(value);
        }
        add({ text: personal.phoneNumber || '' });
        add({ text: personal.location || '' });
        add({ text: personal.emailAddress || '' });
        [
            ['Portfolio', personal.portfolioUrl],
            ['LinkedIn', personal.linkedInUrl],
            ['GitHub', personal.gitHubUrl]
        ].forEach(function (entry) {
            const safeUrl = tools.safeUrl(entry[1]);
            if (safeUrl) add({ text: entry[0], link: safeUrl, color: '#0000EE', decoration: 'underline' });
        });
        return { text: runs, alignment: 'center', fontSize: 8.97, margin: [0, 0.8, 0, 0] };
    }

    function classicSummary(state, tools) {
        if (!sectionVisible(state, 'professionalSummary') || !state.professionalSummary) return [];
        const nodes = [classicHeading('Summary')];
        state.professionalSummary.split(/\n\s*\n/).filter(Boolean).forEach(function (paragraph) {
            nodes.push(tools.pdfRichText(paragraph, { margin: [0, 0, 0, 0.5] }));
        });
        return nodes;
    }

    function classicEducation(state, tools) {
        if (!sectionVisible(state, 'education')) return [];
        const entries = state.education.filter(function (entry) {
            return Boolean(entry.institutionName || entry.degree || entry.fieldOfStudy || entry.grade || entry.descriptionOrCoursework);
        });
        if (!entries.length) return [];
        const nodes = [classicHeading('Education')];
        entries.forEach(function (entry) {
            const degree = [entry.degree, entry.fieldOfStudy].filter(Boolean).join(' ');
            const title = [degree, entry.institutionName].filter(Boolean).join(', ');
            const stack = [classicHeadingRow([{ text: title, bold: true }], classicRange(entry, 'isCurrentlyStudying', true))];
            if (entry.grade) stack.push(tools.pdfText(entry.grade, { margin: [10.8, 0, 10.8, 0] }));
            if (entry.location) stack.push(tools.pdfText(entry.location, { margin: [10.8, 0, 10.8, 0] }));
            if (entry.descriptionOrCoursework) stack.push(tools.pdfRichText(entry.descriptionOrCoursework, { margin: [10.8, 0, 10.8, 0] }));
            nodes.push({ stack: stack, margin: [0, 0, 0, 0.8] });
        });
        return nodes;
    }

    function classicSkills(state) {
        if (!sectionVisible(state, 'skills')) return [];
        const entries = state.skills.map(function (category) {
            const skills = (category.skills || []).filter(function (skill) { return Boolean(skill.name); });
            return { name: category.name, skills: skills };
        }).filter(function (category) { return Boolean(category.name || category.skills.length); });
        if (!entries.length) return [];
        const nodes = [classicHeading('Technical Skills')];
        entries.forEach(function (category) {
            const runs = [];
            if (category.name) runs.push({ text: category.name + (category.skills.length ? ': ' : ''), bold: true });
            if (category.skills.length) runs.push({ text: category.skills.map(function (skill) { return skill.name; }).join(', ') });
            nodes.push({ text: runs, margin: [10.8, 0, 10.8, 0] });
        });
        return nodes;
    }

    function classicExperience(state, tools) {
        if (!sectionVisible(state, 'experience')) return [];
        const entries = state.experience.filter(function (entry) {
            return Boolean(entry.companyName || entry.jobTitle || entry.location || entry.technologySkills || (entry.bulletPoints || []).some(Boolean));
        });
        if (!entries.length) return [];
        const nodes = [classicHeading('Experience')];
        entries.forEach(function (entry) {
            const title = [entry.jobTitle, entry.companyName].filter(Boolean).join(', ');
            const stack = [classicHeadingRow([{ text: title, bold: true }], classicRange(entry, 'isCurrentlyWorking', false))];
            if (entry.location) stack.push(tools.pdfText(entry.location, { margin: [10.8, 0, 10.8, 0] }));
            const bullets = classicBullets(entry.bulletPoints, tools);
            if (bullets) stack.push(bullets);
            if (entry.technologySkills) {
                stack.push({
                    text: [{ text: 'Technology/Skills: ' }, { text: entry.technologySkills }],
                    margin: [10.8, 0, 10.8, 0]
                });
            }
            nodes.push({ stack: stack, margin: [0, 0, 0, 1.2] });
        });
        return nodes;
    }

    function classicProjects(state, tools) {
        if (!sectionVisible(state, 'projects')) return [];
        const entries = state.projects.filter(function (entry) {
            return Boolean(entry.projectName || entry.projectUrl || entry.repositoryUrl || (entry.technologiesUsed || []).length || (entry.bulletPoints || []).some(Boolean));
        });
        if (!entries.length) return [];
        const nodes = [classicHeading('Projects')];
        entries.forEach(function (entry) {
            const technologies = (entry.technologiesUsed || []).filter(Boolean).join(', ');
            const title = [];
            if (entry.projectName) title.push({ text: entry.projectName, bold: true });
            if (technologies) title.push({ text: (entry.projectName ? ' | ' : '') + technologies, italics: true });
            const stack = [{ text: title, margin: [10.8, 0, 10.8, 0] }];
            const bullets = classicBullets(entry.bulletPoints, tools);
            if (bullets) stack.push(bullets);
            const links = [];
            const liveUrl = tools.safeUrl(entry.projectUrl);
            const repositoryUrl = tools.safeUrl(entry.repositoryUrl);
            if (liveUrl) links.push({ text: 'Live', link: liveUrl, color: '#0000EE', decoration: 'underline' });
            if (repositoryUrl) links.push({ text: 'GitHub', link: repositoryUrl, color: '#0000EE', decoration: 'underline' });
            if (links.length) {
                const line = [{ text: 'Link: ', bold: true }];
                links.forEach(function (link, index) {
                    if (index) line.push({ text: ' | ' });
                    line.push(link);
                });
                stack.push({ text: line, margin: [28, 0, 10.8, 0] });
            }
            nodes.push({ stack: stack, margin: [0, 0, 0, 1.9] });
        });
        return nodes;
    }

    function classicHonors(state, tools) {
        if (!sectionVisible(state, 'achievementsAndCertifications')) return [];
        const entries = state.achievementsAndCertifications.filter(function (entry) {
            return Boolean(entry.title || entry.issuingOrganization || entry.description);
        });
        if (!entries.length) return [];
        const nodes = [classicHeading('Honors and Awards')];
        entries.forEach(function (entry) {
            const line = [];
            if (entry.title) line.push({ text: entry.title, bold: true });
            if (entry.issuingOrganization) line.push({ text: (entry.title ? ' - ' : '') + entry.issuingOrganization });
            nodes.push({ text: line, margin: [10.8, 0, 10.8, 0] });
            if (entry.description) nodes.push(tools.pdfRichText(entry.description, { margin: [10.8, 0, 10.8, 0] }));
        });
        return nodes;
    }

    function buildClassicLatex(state, profile, tools) {
        const personal = state.personalInformation;
        const content = [];
        if (personal.fullName) {
            content.push({
                text: classicSmallCaps(personal.fullName, 24.8, 18.6),
                alignment: 'center',
                lineHeight: 1,
                margin: [0, 0, 0, 0]
            });
        }
        content.push(classicContactLine(personal, tools));
        Array.prototype.push.apply(content, classicSummary(state, tools));
        Array.prototype.push.apply(content, classicEducation(state, tools));
        Array.prototype.push.apply(content, classicSkills(state));
        Array.prototype.push.apply(content, classicExperience(state, tools));
        Array.prototype.push.apply(content, classicProjects(state, tools));
        Array.prototype.push.apply(content, classicHonors(state, tools));
        Array.prototype.push.apply(content, tools.remainingSectionContent(state, profile, [
            'professionalSummary', 'education', 'skills', 'experience', 'projects', 'achievementsAndCertifications'
        ]));
        const definition = tools.createDefinition(state, profile, content);
        definition.pageSize = 'LETTER';
        return definition;
    }

    function buildGraphite(state, profile, tools) {
        const personal = state.personalInformation;
        const mainProfile = tools.templateProfile(profile, { accent: '#242424', headingType: 'bar', headingSize: 7.7 });
        const sidebarProfile = tools.templateProfile(profile, { accent: '#242424', headingType: 'compact', headingSize: 7.6 });
        const photo = tools.profilePhotoNode(personal, 60, [0, 0, 0, 0]);
        const identity = tools.identityStack(personal, 'left', '#1d1d1d');
        identity.push(tools.contactLine(personal, 'left', '#454545'));
        const header = fixedTable(photo ? [72, '*'] : ['*'], photo ? [
            { stack: [photo], margin: [0, 0, 8, 0] },
            { stack: identity, margin: [7, 3, 0, 0] }
        ] : [{ stack: identity }], { outerMargin: [0, 0, 0, 6] });
        const summary = tools.sectionContentFor(state, tools.templateProfile(profile, { accent: '#242424', headingType: 'compact' }), ['professionalSummary']);
        const summaryPanel = summary.length ? {
            table: { widths: ['*'], body: [[{ stack: summary, fillColor: '#eeeeee', margin: [11, 7, 11, 7] }]] },
            layout: 'noBorders',
            margin: [0, 0, 0, 7]
        } : null;
        const sidebar = [];
        appendRatedSection(sidebar, 'Skills', ratedSkillNodes(state, tools, {
            color: '#242424', headingColor: '#242424', showNumeric: true, showBars: true, barColor: '#242424', trackColor: '#c9c9c9', barWidth: 93
        }), sidebarProfile, tools);
        Array.prototype.push.apply(sidebar, tools.sectionContentFor(state, sidebarProfile, ['references', 'languages', 'interests']));
        const main = tools.remainingSectionContent(state, mainProfile, ['professionalSummary', 'skills', 'references', 'languages', 'interests']);
        const content = [header];
        if (summaryPanel) content.push(summaryPanel);
        content.push(tools.twoCellResumeRight(142, main, sidebar, {
            fillColor: '#eeeeee',
            color: '#242424',
            margin: [10, 7, 9, 7],
            mainMargin: [0, 4, 15, 7]
        }));
        return tools.createDefinition(state, profile, content);
    }

    function buildMidnight(state, profile, tools) {
        const personal = state.personalInformation;
        const darkProfile = tools.templateProfile(profile, { accent: '#d8c7a3', headingType: 'compact', mutedColor: '#c8c1b7', ruleWidth: 514 });
        const photo = tools.profilePhotoNode(personal, 66, [0, 0, 0, 0]);
        const identity = tools.identityStack(personal, 'left', '#ffffff');
        identity.push(tools.contactLine(personal, 'left', '#d8c7a3'));
        const header = fixedTable(photo ? [82, '*'] : ['*'], photo ? [
            { stack: [photo], margin: [3, 0, 8, 0] },
            { stack: identity, margin: [10, 5, 0, 0] }
        ] : [{ stack: identity }], { outerMargin: [0, 0, 0, 9] });
        const profileContent = tools.sectionContentFor(state, darkProfile, ['professionalSummary']);
        const skillContent = tools.sectionContentFor(state, darkProfile, ['skills']);
        const introColumns = [];
        if (profileContent.length) introColumns.push({ width: '*', stack: profileContent, margin: [0, 0, skillContent.length ? 13 : 0, 0] });
        if (skillContent.length) introColumns.push({ width: profileContent.length ? 175 : '*', stack: skillContent, margin: [profileContent.length ? 13 : 0, 0, 0, 0] });
        const content = [
            {
                canvas: [{ type: 'rect', x: 0, y: 0, w: 545, h: 785, lineWidth: 0.65, lineColor: '#9b9384' }],
                absolutePosition: { x: 25, y: 24 }
            },
            header,
            { canvas: [{ type: 'line', x1: 0, y1: 0, x2: 512, y2: 0, lineWidth: 0.55, lineColor: '#81796d' }], margin: [0, 0, 0, 6] }
        ];
        if (introColumns.length) {
            content.push({ columns: introColumns, columnGap: 0, margin: [0, 0, 0, 5] });
            content.push({ canvas: [{ type: 'line', x1: 0, y1: 0, x2: 512, y2: 0, lineWidth: 0.45, lineColor: '#81796d' }], margin: [0, 1, 0, 4] });
        }
        Array.prototype.push.apply(content, tools.remainingSectionContent(state, darkProfile, ['professionalSummary', 'skills']));
        const definition = tools.createDefinition(state, profile, content);
        recolorForDark(definition.content);
        definition.background = function (_currentPage, pageSize) {
            return { canvas: [{ type: 'rect', x: 0, y: 0, w: pageSize.width, h: pageSize.height, color: '#10131b' }] };
        };
        definition.footer = function (currentPage) {
            return { text: String(currentPage), alignment: 'right', color: '#9b9384', fontSize: 6, margin: [0, 0, 26, 0] };
        };
        definition.styles.name.font = 'LibreBaskerville';
        return definition;
    }

    function buildDocumentDefinition(state, tools) {
        if (!state || !tools || !isTemplate(state.templateId)) return null;
        const profile = PROFILES[state.templateId];
        switch (state.templateId) {
            case 'evergreen-professional': return buildEvergreen(state, profile, tools);
            case 'monochrome-timeline': return buildMonochrome(state, profile, tools);
            case 'mint-horizon': return buildMint(state, profile, tools);
            case 'amber-academic': return buildAmber(state, profile, tools);
            case 'classic-latex': return buildClassicLatex(state, profile, tools);
            case 'graphite-impact': return buildGraphite(state, profile, tools);
            case 'midnight-executive': return buildMidnight(state, profile, tools);
            default: return null;
        }
    }

    return Object.freeze({
        CATALOG: CATALOG,
        IDS: IDS,
        ALL_SECTION_KEYS: ALL_SECTION_KEYS,
        SECTION_ORDERS: SECTION_ORDERS,
        isTemplate: isTemplate,
        metadata: metadata,
        buildDocumentDefinition: buildDocumentDefinition,
        filledSkillItems: filledSkillItems
    });
}));
