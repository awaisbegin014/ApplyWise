// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
(() => {
    const sidebar = document.getElementById('appSidebar');
    const menuButton = document.querySelector('.menu-button[data-sidebar-toggle]');
    const toggles = document.querySelectorAll('[data-sidebar-toggle]');
    const mobileQuery = window.matchMedia('(max-width: 800px)');
    const storageKey = 'applywise.sidebar.collapsed';
    if (!sidebar || !menuButton || !toggles.length) return;

    const storeCollapsed = (collapsed) => {
        try { localStorage.setItem(storageKey, String(collapsed)); } catch { }
    };
    const readCollapsed = () => {
        try { return localStorage.getItem(storageKey) === 'true'; } catch { return false; }
    };
    const syncButton = () => {
        const expanded = mobileQuery.matches
            ? document.body.classList.contains('sidebar-open')
            : !document.body.classList.contains('sidebar-collapsed');
        menuButton.setAttribute('aria-expanded', String(expanded));
        menuButton.setAttribute('aria-label', `${expanded ? 'Close' : 'Open'} navigation sidebar`);
        menuButton.title = `${expanded ? 'Close' : 'Open'} navigation sidebar`;
        if (mobileQuery.matches) {
            sidebar.inert = !expanded;
            sidebar.setAttribute('aria-hidden', String(!expanded));
        } else {
            sidebar.inert = false;
            sidebar.removeAttribute('aria-hidden');
        }
    };
    const applyViewportState = () => {
        document.body.classList.remove('sidebar-open');
        document.body.classList.toggle('sidebar-collapsed', !mobileQuery.matches && readCollapsed());
        syncButton();
    };

    toggles.forEach((button) => {
        button.addEventListener('click', () => {
            if (mobileQuery.matches) {
                document.body.classList.toggle('sidebar-open');
            } else {
                const collapsed = document.body.classList.toggle('sidebar-collapsed');
                storeCollapsed(collapsed);
            }
            syncButton();
            if (mobileQuery.matches && document.body.classList.contains('sidebar-open')) {
                window.requestAnimationFrame(() => sidebar.querySelector('a.active, a')?.focus());
            } else if (button !== menuButton) {
                menuButton.focus();
            }
        });
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && document.body.classList.contains('sidebar-open')) {
            document.body.classList.remove('sidebar-open');
            syncButton();
            menuButton.focus();
            return;
        }

        if (event.key === 'Tab' && mobileQuery.matches && document.body.classList.contains('sidebar-open')) {
            const focusable = Array.from(sidebar.querySelectorAll('a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'))
                .filter((element) => !element.hidden && element.getClientRects().length > 0);
            if (!focusable.length) return;

            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        }
    });
    mobileQuery.addEventListener?.('change', applyViewportState);
    applyViewportState();
})();

(() => {
    const greeting = document.querySelector('[data-local-greeting]');
    const dashboard = document.querySelector('[data-display-name]');
    if (greeting && dashboard) {
        const hour = new Date().getHours();
        const salutation = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
        greeting.textContent = `${salutation}, ${dashboard.dataset.displayName}`;
    }
})();

document.querySelectorAll('[data-password-toggle]').forEach((button) => {
    const inputId = button.getAttribute('aria-controls');
    const input = inputId ? document.getElementById(inputId) : null;
    const text = button.querySelector('[data-password-toggle-text]');
    if (!(input instanceof HTMLInputElement)) {
        button.hidden = true;
        return;
    }

    button.addEventListener('click', () => {
        const willShow = input.type === 'password';
        input.type = willShow ? 'text' : 'password';
        button.setAttribute('aria-pressed', String(willShow));
        button.setAttribute('aria-label', `${willShow ? 'Hide' : 'Show'} ${inputId.includes('Confirm') ? 'confirmation password' : 'password'}`);
        if (text) text.textContent = willShow ? 'Hide' : 'Show';
    });
});

document.querySelectorAll('[data-security-code]').forEach((input) => {
    if (!(input instanceof HTMLInputElement)) return;

    const normalize = () => {
        const digits = input.value.replace(/\D/g, '').slice(0, 6);
        if (input.value !== digits) input.value = digits;
    };

    input.addEventListener('input', normalize);
    input.addEventListener('paste', () => window.setTimeout(normalize));
});

(() => {
    const menu = document.querySelector('[data-account-menu]');
    const trigger = menu?.querySelector('[data-account-menu-trigger]');
    const panel = menu?.querySelector('[data-account-menu-panel]');
    if (!menu || !trigger || !panel) return;

    const close = (restoreFocus = false) => {
        panel.hidden = true;
        trigger.setAttribute('aria-expanded', 'false');
        if (restoreFocus) trigger.focus();
    };
    const open = () => {
        panel.hidden = false;
        trigger.setAttribute('aria-expanded', 'true');
    };

    trigger.addEventListener('click', () => panel.hidden ? open() : close());
    document.addEventListener('click', (event) => {
        if (!menu.contains(event.target)) close();
    });
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && !panel.hidden) close(true);
    });
    menu.addEventListener('focusout', () => {
        window.setTimeout(() => {
            if (!menu.contains(document.activeElement)) close();
        });
    });
})();

(() => {
    const container = document.querySelector('[data-custom-fields]');
    const add = document.querySelector('[data-custom-field-add]');
    const list = document.querySelector('[data-custom-fields-list]');
    const template = document.querySelector('[data-custom-field-template]');
    if (!container || !add || !list || !(template instanceof HTMLTemplateElement)) return;

    const reindex = () => {
        list.querySelectorAll('[data-custom-field-row]').forEach((row, index) => {
            row.querySelectorAll('input, label').forEach((item) => {
                ['name', 'id', 'for'].forEach((attribute) => {
                    const value = item.getAttribute(attribute);
                    if (value) item.setAttribute(attribute, value.replace(/CustomFields\[\d+\]|CustomFields_\d+_/g, (match) => match.startsWith('CustomFields[') ? `CustomFields[${index}]` : `CustomFields_${index}_`));
                });
            });
        });
    };

    add.addEventListener('click', () => {
        const index = list.querySelectorAll('[data-custom-field-row]').length;
        list.insertAdjacentHTML('beforeend', template.innerHTML.replaceAll('__index__', String(index)));
        list.querySelector('[data-custom-field-row]:last-child input')?.focus();
    });
    list.addEventListener('click', (event) => {
        const remove = event.target.closest('[data-custom-field-remove]');
        if (!remove) return;
        const row = remove.closest('[data-custom-field-row]');
        const rows = Array.from(list.querySelectorAll('[data-custom-field-row]'));
        const removedIndex = rows.indexOf(row);
        row?.remove();
        reindex();
        const remainingRows = Array.from(list.querySelectorAll('[data-custom-field-row]'));
        const nextRow = remainingRows[Math.min(Math.max(removedIndex, 0), remainingRows.length - 1)];
        (nextRow?.querySelector('input') ?? add).focus();
    });
})();

(() => {
    const key = 'applywise-theme';
    const controls = document.querySelectorAll('[data-theme-toggle]');
    const isAuthenticatedWorkspace = document.documentElement.dataset.authenticatedWorkspace === 'true';
    const syncControls = (theme) => {
        const dark = theme === 'dark';
        controls.forEach((control) => {
            if (control instanceof HTMLInputElement) control.checked = dark;
        });
    };
    const applyTheme = (theme, persist = true) => {
        const nextTheme = isAuthenticatedWorkspace && theme === 'dark' ? 'dark' : 'light';
        document.documentElement.dataset.theme = nextTheme;
        document.documentElement.style.colorScheme = nextTheme;
        if (persist) {
            try { localStorage.setItem(key, nextTheme); } catch { }
        }
        syncControls(nextTheme);
    };

    if (!isAuthenticatedWorkspace) {
        applyTheme('light', false);
        return;
    }

    const initialTheme = document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light';
    syncControls(initialTheme);
    controls.forEach((control) => {
        if (control instanceof HTMLInputElement) {
            control.addEventListener('change', () => applyTheme(control.checked ? 'dark' : 'light'));
        }
    });
})();

(() => {
    const dialog = document.querySelector('[data-avatar-dialog]');
    const openButton = document.querySelector('[data-avatar-open]');
    if (!(dialog instanceof HTMLDialogElement) || !openButton) return;

    const closeButton = dialog.querySelector('[data-avatar-close]');
    const viewport = dialog.querySelector('[data-avatar-viewport]');
    const slides = Array.from(dialog.querySelectorAll('[data-avatar-slide]'));
    const previous = dialog.querySelector('[data-avatar-previous]');
    const next = dialog.querySelector('[data-avatar-next]');
    const position = dialog.querySelector('[data-avatar-position]');
    const category = dialog.querySelector('[data-avatar-category]');
    let currentIndex = Math.max(0, slides.findIndex((slide) => slide.dataset.avatarSelected === 'true'));
    let scrollFrame = 0;

    const updateControls = () => {
        if (position) position.textContent = `${currentIndex + 1} of ${slides.length}`;
        if (previous) previous.disabled = currentIndex === 0;
        if (next) next.disabled = currentIndex === slides.length - 1;
        slides.forEach((slide, index) => slide.setAttribute('aria-hidden', String(index !== currentIndex)));
        const activeCategory = slides[currentIndex]?.dataset.avatarCategoryName ?? '';
        if (category && category.value && category.value !== activeCategory) category.value = '';
    };

    const goTo = (index, behavior = 'smooth') => {
        currentIndex = Math.max(0, Math.min(index, slides.length - 1));
        viewport?.scrollTo({ left: currentIndex * viewport.clientWidth, behavior });
        updateControls();
    };

    openButton.addEventListener('click', () => {
        dialog.showModal();
        window.requestAnimationFrame(() => goTo(currentIndex, 'auto'));
    });
    closeButton?.addEventListener('click', () => dialog.close());
    previous?.addEventListener('click', () => goTo(currentIndex - 1));
    next?.addEventListener('click', () => goTo(currentIndex + 1));
    category?.addEventListener('change', () => {
        if (!category.value) return;
        const targetIndex = slides.findIndex((slide) => slide.dataset.avatarCategoryName === category.value);
        if (targetIndex >= 0) goTo(targetIndex);
    });
    viewport?.addEventListener('scroll', () => {
        window.cancelAnimationFrame(scrollFrame);
        scrollFrame = window.requestAnimationFrame(() => {
            currentIndex = Math.round(viewport.scrollLeft / Math.max(viewport.clientWidth, 1));
            updateControls();
        });
    }, { passive: true });
    dialog.addEventListener('click', (event) => {
        if (event.target === dialog) dialog.close();
    });
    window.addEventListener('resize', () => {
        if (dialog.open) goTo(currentIndex, 'auto');
    });
    updateControls();

    const fileInput = document.querySelector('[data-avatar-upload-input]');
    const fileName = document.querySelector('[data-avatar-file-name]');
    const submit = document.querySelector('[data-avatar-upload-submit]');
    const preview = document.querySelector('[data-avatar-preview]');
    let previewUrl;

    fileInput?.addEventListener('change', () => {
        const file = fileInput.files?.[0];
        if (fileName) fileName.textContent = file?.name ?? 'No photo selected';
        if (submit) submit.disabled = !file;
        if (!file || !preview) return;

        if (previewUrl) URL.revokeObjectURL(previewUrl);
        previewUrl = URL.createObjectURL(file);
        const image = preview.querySelector('img');
        const fallback = preview.querySelector('span');
        if (image) {
            image.src = previewUrl;
            image.hidden = false;
        }
        if (fallback) fallback.hidden = true;
    });
})();
