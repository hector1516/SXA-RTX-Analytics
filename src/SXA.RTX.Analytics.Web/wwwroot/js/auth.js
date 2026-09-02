window.doLogin = async (username, password) => {
    const r = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ Username: username, Password: password })
    });
    return await r.text();
};
