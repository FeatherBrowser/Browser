(() => {
    if (location.origin !== new URL(__ORIGIN_JSON__).origin) return 'origin-changed';
    const username = __USERNAME_JSON__;
    const password = __PASSWORD_JSON__;
    const inputs = Array.from(document.querySelectorAll('input:not([disabled])'));
    const pass = inputs.find(x => (x.type || '').toLowerCase() === 'password');
    if (!pass) return 'no-password-field';

    const user = inputs.find(x => {
        const type = (x.type || '').toLowerCase();
        const ac = (x.autocomplete || '').toLowerCase();
        const name = ((x.name || '') + ' ' + (x.id || '')).toLowerCase();
        return ac === 'username' || ac === 'email' || type === 'email' ||
               (type === 'text' && /(user|email|login|account)/.test(name));
    });

    const setValue = (el, value) => {
        if (!el || !value) return;
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
        if (setter) setter.call(el, value); else el.value = value;
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
    };

    if (user && !user.value) setValue(user, username);
    if (!pass.value) setValue(pass, password);
    pass.focus();
    return 'filled';
})();