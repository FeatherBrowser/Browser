(() => {
    const expectedOrigin = new URL(__ORIGIN_JSON__).origin;

    if (expectedOrigin === 'null' || location.origin !== expectedOrigin) {
        return 'origin-changed';
    }

    const username = __USERNAME_JSON__;
    const password = __PASSWORD_JSON__;

    const autocompleteTokens = input =>
        (input.getAttribute('autocomplete') ?? '').toLowerCase().split(/\s+/);

    const isEditable = input =>
        input.isConnected &&
        !input.matches(':disabled') &&
        !input.readOnly &&
        !input.closest('[inert]') &&
        input.getClientRects().length > 0 &&
        getComputedStyle(input).visibility === 'visible';

    const inputs = Array.from(document.querySelectorAll('input')).filter(isEditable);
    const passwordFields = inputs.filter(input =>
        input.type === 'password' &&
        !autocompleteTokens(input).includes('new-password')
    );

    if (passwordFields.length === 0) return 'no-password-field';
    if (passwordFields.length > 1) return 'ambiguous-password-field';

    const [passwordField] = passwordFields;

    const usernameRank = input => {
        if (input.form !== passwordField.form) return 0;

        const tokens = autocompleteTokens(input);
        if (tokens.includes('one-time-code')) return 0;
        if (tokens.includes('username')) return 3;
        if (tokens.includes('email') || input.type === 'email') return 2;

        const fieldName = `${input.name} ${input.id}`;
        return /user|email|login|account/i.test(fieldName) ? 1 : 0;
    };

    const usernameCandidates = inputs
        .filter(input => ['text', 'email', 'tel'].includes(input.type))
        .map(input => ({ input, rank: usernameRank(input) }))
        .filter(candidate => candidate.rank > 0)
        .sort((a, b) => b.rank - a.rank);

    const [best, next] = usernameCandidates;
    const usernameField = best && best.rank !== next?.rank ? best.input : null;

    const nativeValueSetter = Object.getOwnPropertyDescriptor(
        HTMLInputElement.prototype,
        'value'
    )?.set;

    const setValue = (input, value) => {
        if (input.value === value) return;

        if (nativeValueSetter) {
            nativeValueSetter.call(input, value);
        } else {
            input.value = value;
        }

        input.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
    };

    if (usernameField && username) {
        setValue(usernameField, username);
    }
    
    if (location.origin !== expectedOrigin) return 'origin-changed';
    if (!isEditable(passwordField) || passwordField.type !== 'password') {
        return 'password-field-changed';
    }

    setValue(passwordField, password);
    passwordField.focus({ preventScroll: true });

    if (!passwordField.isConnected || passwordField.value !== password) {
        return 'password-fill-failed';
    }

    return 'filled';
})();