(() => {
    const header = document.querySelector('[data-home-header]');
    const toggle = header?.querySelector('[data-home-nav-toggle]');
    const navigation = header?.querySelector('[data-home-nav]');
    if (!header || !toggle || !navigation) return;

    const mobileNavigation = window.matchMedia('(max-width: 1080px)');
    const backdrop = document.createElement('button');
    const backgroundRegions = [
        document.querySelector('main.aw-home'),
        document.querySelector('.aw-home-footer')
    ].filter(Boolean);

    backdrop.type = 'button';
    backdrop.className = 'aw-home-nav-backdrop';
    backdrop.setAttribute('aria-label', 'Close navigation');
    backdrop.tabIndex = -1;
    backdrop.hidden = true;
    header.insertAdjacentElement('afterend', backdrop);

    const close = (restoreFocus = false) => {
        header.classList.remove('is-open');
        document.body.classList.remove('home-nav-open');
        toggle.setAttribute('aria-expanded', 'false');
        toggle.setAttribute('aria-label', 'Open navigation');
        backdrop.hidden = true;
        backgroundRegions.forEach((region) => { region.inert = false; });
        if (restoreFocus) toggle.focus();
    };

    const open = () => {
        if (!mobileNavigation.matches) return;
        header.classList.add('is-open');
        document.body.classList.add('home-nav-open');
        toggle.setAttribute('aria-expanded', 'true');
        toggle.setAttribute('aria-label', 'Close navigation');
        backdrop.hidden = false;
        backgroundRegions.forEach((region) => { region.inert = true; });
    };

    toggle.hidden = false;
    header.classList.add('is-nav-ready');
    toggle.addEventListener('click', () => header.classList.contains('is-open') ? close() : open());
    navigation.querySelectorAll('a').forEach((link) => link.addEventListener('click', () => close()));
    backdrop.addEventListener('click', () => close(true));
    document.addEventListener('keydown', (event) => {
        if (!header.classList.contains('is-open')) return;

        if (event.key === 'Escape') {
            close(true);
            return;
        }

        if (event.key !== 'Tab') return;

        const focusable = [toggle, ...navigation.querySelectorAll('a[href]')];
        const first = focusable[0];
        const last = focusable.at(-1);
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    });
    document.addEventListener('click', (event) => {
        if (header.classList.contains('is-open') && !header.contains(event.target)) close();
    });
    const handleViewportChange = (event) => { if (!event.matches) close(); };
    if (mobileNavigation.addEventListener) {
        mobileNavigation.addEventListener('change', handleViewportChange);
    } else {
        mobileNavigation.addListener(handleViewportChange);
    }
    window.addEventListener('pagehide', () => close());
})();
