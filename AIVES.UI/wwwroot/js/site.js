(() => {
    const sidebar = document.getElementById('appSidebar');
    const toggle = document.getElementById('menuToggle');
    const backdrop = document.getElementById('sidebarBackdrop');
    const closeMenu = () => { sidebar?.classList.remove('open'); backdrop?.classList.remove('show'); };
    toggle?.addEventListener('click', () => { sidebar?.classList.toggle('open'); backdrop?.classList.toggle('show'); });
    backdrop?.addEventListener('click', closeMenu);
})();
